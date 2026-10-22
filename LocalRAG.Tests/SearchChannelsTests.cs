using System.Reflection;
using LocalRAG;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// The keyword channel (BM25 over a stemmed index), rank fusion, the cosine floor, the
/// index migration for older databases, and the memory channel being off by default.
/// SciFact numbers behind these choices: scoring keyword hits with the old word-match
/// scorer gave nDCG@10 0.505 against 0.665 for the index's own BM25 order.
/// Gated on the embedding model like the other integration tests.
/// </summary>
[Trait("Category", "Integration")]
public class SearchChannelsTests : IAsyncLifetime
{
    private string _modelPath = "", _vocabPath = "";
    private readonly List<string> _dbs = new();
    private readonly List<EmbeddingDatabaseNew> _open = new();

    public Task InitializeAsync()
    {
        var defaults = new RAGConfiguration();
        _modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        _vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var db in _open) await db.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var p in _dbs)
            for (int i = 0; i < 3; i++)
            {
                try { foreach (var f in Directory.GetFiles(Path.GetDirectoryName(p)!, Path.GetFileName(p) + "*")) File.Delete(f); break; }
                catch (IOException) { await Task.Delay(100); }
            }
    }

    private RAGConfiguration Config(string path, Action<RAGConfiguration>? tweak = null)
    {
        var c = new RAGConfiguration
        {
            ModelPath = _modelPath, VocabularyPath = _vocabPath, DatabasePath = path,
            IndexChunkEmbeddings = false
        };
        tweak?.Invoke(c);
        return c;
    }

    private async Task<(EmbeddingDatabaseNew db, string path)> NewDbAsync(Action<RAGConfiguration>? tweak = null)
    {
        Skip.IfNot(File.Exists(_modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");
        var path = Path.Combine(Path.GetTempPath(), $"chan_{Guid.NewGuid():N}.db");
        _dbs.Add(path);
        var db = new EmbeddingDatabaseNew(Config(path, tweak));
        _open.Add(db);
        await db.EnsureReadyAsync();
        return (db, path);
    }

    private static async Task<List<FeedbackDatabaseValues>> Keyword(EmbeddingDatabaseNew db, string query, int topK = 10)
    {
        var m = typeof(EmbeddingDatabaseNew).GetMethod("SearchWithFTSAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return await (Task<List<FeedbackDatabaseValues>>)m.Invoke(db, new object[] { query, topK, 1, false })!;
    }

    // ── Keyword channel: BM25 order ─────────────────────────────────────────

    [SkippableFact]
    public async Task Keyword_RanksTheRareTermAboveDocsThatOnlyShareCommonWords()
    {
        var (db, _) = await NewDbAsync();
        // "inhibitor" and "assay" are in ten of eleven documents; "caspase" is in one. A
        // fraction-of-query-words score ranks the ten above it (2/3 vs 1/3); BM25 does not.
        for (int i = 0; i < 10; i++)
            await db.AddRequestToEmbeddingDatabaseAsync($"common{i}", $"inhibitor assay results for sample {i}", embed: false);
        await db.AddRequestToEmbeddingDatabaseAsync("rare", "caspase activity was measured", embed: false);

        var hits = await Keyword(db, "caspase inhibitor assay");

        Assert.Equal("rare", hits.First().RequestID);
    }

    [SkippableFact]
    public async Task Keyword_MatchesWordVariants_ThroughStemming()
    {
        var (db, _) = await NewDbAsync();
        await db.AddRequestToEmbeddingDatabaseAsync("stem", "The drug inhibits kinase activity", embed: false);
        await db.AddRequestToEmbeddingDatabaseAsync("other", "Weather was mild in spring", embed: false);

        var hits = await Keyword(db, "inhibiting kinases");

        Assert.Contains(hits, h => h.RequestID == "stem");
        Assert.DoesNotContain(hits, h => h.RequestID == "other");
    }

    [SkippableFact]
    public async Task Keyword_SurvivesPunctuation_AndStillMatches()
    {
        var (db, _) = await NewDbAsync();
        await db.AddRequestToEmbeddingDatabaseAsync("punct", "IL-6 signalling involves p53 and mTOR", embed: false);
        await db.AddRequestToEmbeddingDatabaseAsync("other", "Unrelated note about gardens", embed: false);

        // Quotes, brackets, hyphens and slashes in a query used to risk breaking the MATCH
        // syntax or gluing words into phrases.
        var hits = await Keyword(db, "what is \"IL-6\" (signalling) with p53/mTOR?");

        Assert.Equal("punct", hits.First().RequestID);
    }

    [SkippableFact]
    public async Task Keyword_QueryOfOnlyStopWordsOrSymbols_ReturnsNothing_WithoutError()
    {
        var (db, _) = await NewDbAsync();
        await db.AddRequestToEmbeddingDatabaseAsync("a", "some text", embed: false);

        Assert.Empty(await Keyword(db, "the of and"));
        Assert.Empty(await Keyword(db, "!!! ??? \"\""));
    }

    // ── Existing databases: the index is rebuilt with stemming ──────────────

    [SkippableFact]
    public async Task OlderDatabase_GetsTheStemmedIndex_RebuiltFromItsRows()
    {
        var (db, path) = await NewDbAsync();
        await db.AddRequestToEmbeddingDatabaseAsync("one", "The drug inhibits kinase activity", embed: false);
        await db.AddRequestToEmbeddingDatabaseAsync("two", "Another note about enzymes", embed: false);
        await db.DisposeAsync();
        _open.Remove(db);
        SqliteConnection.ClearAllPools();

        // Make it look like a database from before stemming: plain default tokenizer.
        using (var c = new SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            foreach (var sql in new[]
            {
                "DROP TABLE embeddings_fts",
                "CREATE VIRTUAL TABLE embeddings_fts USING fts5(RequestID, Request, TextResponse, ToolUseTextResponse, Summary, content='embeddings', content_rowid='Id')",
                "INSERT INTO embeddings_fts(embeddings_fts) VALUES('rebuild')"
            })
                using (var cmd = new SqliteCommand(sql, c)) cmd.ExecuteNonQuery();

            using var before = new SqliteCommand("SELECT count(*) FROM embeddings_fts WHERE embeddings_fts MATCH 'inhibiting'", c);
            Assert.Equal(0L, (long)before.ExecuteScalar()!); // no stemming yet
        }
        SqliteConnection.ClearAllPools();

        // Reopening migrates it.
        var reopened = new EmbeddingDatabaseNew(Config(path));
        _open.Add(reopened);
        await reopened.EnsureReadyAsync();

        var hits = await Keyword(reopened, "inhibiting kinases");
        Assert.Equal(new[] { "one" }, hits.Select(h => h.RequestID));

        // New rows still reach the index through the existing triggers.
        await reopened.AddRequestToEmbeddingDatabaseAsync("three", "More kinases were inhibited", embed: false);
        Assert.Contains(await Keyword(reopened, "inhibiting kinases"), h => h.RequestID == "three");

        using var c2 = new SqliteConnection($"Data Source={path}"); c2.Open();
        using var ddl = new SqliteCommand("SELECT sql FROM sqlite_master WHERE name='embeddings_fts'", c2);
        Assert.Contains("porter", (string)ddl.ExecuteScalar()!);
    }

    // ── Rank fusion ─────────────────────────────────────────────────────────

    [Fact]
    public void RankFusion_AddsReciprocalRanks_KeepsFirstCopy_AndSkipsRowsWithoutAnId()
    {
        var fuse = typeof(EmbeddingDatabaseNew).GetMethod("ReciprocalRankFuse", BindingFlags.NonPublic | BindingFlags.Static)!;
        FeedbackDatabaseValues D(string id) => new() { RequestID = id };
        var a1 = D("a"); var a2 = D("a");
        var vector  = new List<FeedbackDatabaseValues> { a1, D("b"), D("c") };
        var keyword = new List<FeedbackDatabaseValues> { D("c"), a2, D("d"), new() { RequestID = null } };

        var fused = (List<FeedbackDatabaseValues>)fuse.Invoke(null, new object[] { new List<List<FeedbackDatabaseValues>> { vector, keyword } })!;

        // a: 1/61 + 1/62, c: 1/63 + 1/61, b: 1/62, d: 1/63
        Assert.Equal(new[] { "a", "c", "b", "d" }, fused.Select(f => f.RequestID));
        Assert.Same(a1, fused[0]);
    }

    [Fact]
    public void RankFusion_OfOneList_KeepsItsOrder_AndOfNone_IsEmpty()
    {
        var fuse = typeof(EmbeddingDatabaseNew).GetMethod("ReciprocalRankFuse", BindingFlags.NonPublic | BindingFlags.Static)!;
        var list = new List<FeedbackDatabaseValues> { new() { RequestID = "x" }, new() { RequestID = "y" }, new() { RequestID = "z" } };

        var one = (List<FeedbackDatabaseValues>)fuse.Invoke(null, new object[] { new List<List<FeedbackDatabaseValues>> { list } })!;
        var none = (List<FeedbackDatabaseValues>)fuse.Invoke(null, new object[] { new List<List<FeedbackDatabaseValues>>() })!;

        Assert.Equal(new[] { "x", "y", "z" }, one.Select(f => f.RequestID));
        Assert.Empty(none);
    }

    // ── Cosine floor stays a cosine floor ───────────────────────────────────

    [SkippableFact]
    public async Task Floor_AppliesToCosine_ForEveryResult_IncludingKeywordOnlyHits()
    {
        var (db, _) = await NewDbAsync();
        await db.AddRequestToEmbeddingDatabaseAsync("kube", "Kubernetes orchestrates containers across clusters.", embed: true);
        await db.AddRequestToEmbeddingDatabaseAsync("pg", "PostgreSQL is a relational database with indexing.", embed: true);
        // No embedding, so only the keyword channel can find it and its cosine is 0.
        await db.AddRequestToEmbeddingDatabaseAsync("kwonly", "zyxomatrix quarterly figures", embed: false);

        var atZero = await db.SearchEmbeddingsAsync("zyxomatrix figures", topK: 5, minimumSimilarity: 0.0f, searchLevel: 1);
        Assert.Contains(atZero, r => r.RequestID == "kwonly");
        Assert.All(atZero, r => Assert.NotNull(r.Similarity));

        foreach (var floor in new[] { 0.2f, 0.5f, 0.99f })
        {
            var results = await db.SearchEmbeddingsAsync("zyxomatrix figures", topK: 5, minimumSimilarity: floor, searchLevel: 1);
            Assert.All(results, r => Assert.True(r.Similarity >= floor, $"{r.RequestID} {r.Similarity} < {floor}"));
            Assert.DoesNotContain(results, r => r.RequestID == "kwonly");
        }
    }

    [SkippableFact]
    public async Task Search_PutsTheMatchFirst_WhenBothChannelsAgree()
    {
        var (db, _) = await NewDbAsync();
        await db.AddRequestToEmbeddingDatabaseAsync("kube", "Kubernetes orchestrates containers across clusters.", embed: true);
        await db.AddRequestToEmbeddingDatabaseAsync("pg", "PostgreSQL is a relational database with indexing.", embed: true);
        await db.AddRequestToEmbeddingDatabaseAsync("git", "Git tracks source code changes with commits.", embed: true);

        var results = await db.SearchEmbeddingsAsync("container orchestration with kubernetes", topK: 3, minimumSimilarity: 0.0f, searchLevel: 1);

        Assert.Equal("kube", results.First().RequestID);
    }

    // ── Memory channel is opt-in ────────────────────────────────────────────

    [Fact]
    public void MemoryChannel_IsOffByDefault() => Assert.False(new RAGConfiguration().UseMemoryChannel);

    [SkippableFact]
    public async Task MemoryIndex_StaysEmptyWhenTheChannelIsOff_AndFillsWhenOn()
    {
        Skip.IfNot(File.Exists(_modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        using (var off = new EmbedderClassNew(Config("unused")))
        {
            await off.GetEmbeddingsAsync("Kubernetes orchestrates containers.");
            Assert.Empty(await off.SearchSimilarAsync("Kubernetes orchestrates containers."));
        }
        using (var on = new EmbedderClassNew(Config("unused", c => c.UseMemoryChannel = true)))
        {
            await on.GetEmbeddingsAsync("Kubernetes orchestrates containers.");
            Assert.NotEmpty(await on.SearchSimilarAsync("Kubernetes orchestrates containers."));
        }
    }
}

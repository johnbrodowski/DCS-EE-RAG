using LocalRAG.QaDataset;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// The QA dataset is also the "knowledge base" for the RAG-context option, so a duplicate question would hand the
/// model its own answer (G4-5), and a sweep must be able to score every setting on the same questions (G4-9).
/// </summary>
public class QaDatasetImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qa-import-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* sqlite may still hold the file */ }
    }

    private async Task<QaDatasetDatabase> NewDbAsync()
    {
        Directory.CreateDirectory(_dir);
        var db = new QaDatasetDatabase(Path.Combine(_dir, $"qa-{Guid.NewGuid():N}.db"));
        await db.InitializeAsync();
        return db;
    }

    private string WriteJsonl(string name, params string[] lines)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string Line(string q) => $"{{\"question\":\"{q}\",\"answer\":[\"x\"]}}";

    [Fact]
    public async Task SameQuestionFromTheSameSource_IsImportedOnce()
    {
        using var db = await NewDbAsync();
        var first = await db.ImportItemAsync(new QaDatasetItem { Question = "Q1?", DatasetSource = "a.jsonl" });
        var again = await db.ImportItemAsync(new QaDatasetItem { Question = "Q1?", DatasetSource = "a.jsonl" });
        var other = await db.ImportItemAsync(new QaDatasetItem { Question = "Q1?", DatasetSource = "b.jsonl" });

        Assert.True(first > 0);
        Assert.Equal(0, again);
        Assert.True(other > 0);
        Assert.Equal(2, (await db.GetItemCountAsync()).Total);
    }

    [Fact]
    public async Task ImportingAFileTwice_AddsNothingTheSecondTime()
    {
        using var db = await NewDbAsync();
        var path = WriteJsonl("set.jsonl", Line("A?"), Line("B?"), Line("A?"));

        Assert.Equal(2, await QaDatasetLoader.ImportFileAsync(path, db));
        Assert.Equal(0, await QaDatasetLoader.ImportFileAsync(path, db));
        Assert.Equal(2, (await db.GetItemCountAsync()).Total);
    }

    [Fact]
    public async Task ABadLineMidFile_ImportsNothing()
    {
        using var db = await NewDbAsync();
        var path = WriteJsonl("bad.jsonl", Line("A?"), Line("B?"), "{ not json", Line("C?"));

        await Assert.ThrowsAsync<InvalidDataException>(() => QaDatasetLoader.ImportFileAsync(path, db));
        Assert.Equal(0, (await db.GetItemCountAsync()).Total);
    }

    [Fact]
    public async Task ASeed_GivesTheSameSampleEveryTime_AndDifferentSeedsDiffer()
    {
        using var db = await NewDbAsync();
        for (int i = 0; i < 60; i++)
            await db.ImportItemAsync(new QaDatasetItem { Question = $"Question {i}?" });

        async Task<List<int>> Draw(int seed) =>
            (await db.GetItemsForRunAsync(10, onlyEmbedded: false, seed)).Select(x => x.Id).ToList();

        var a1 = await Draw(7);
        var a2 = await Draw(7);
        var b = await Draw(8);

        Assert.Equal(10, a1.Count);
        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
    }

    [Fact]
    public async Task ACopyOfTheQuestionIsNeverShownAsItsOwnExample()
    {
        using var db = await NewDbAsync();
        const string Twin = "What is the capital of France?";
        var a = await db.ImportItemAsync(new QaDatasetItem { Question = Twin, Answers = { "paris" }, DatasetSource = "one.jsonl" });
        var copy = await db.ImportItemAsync(new QaDatasetItem { Question = Twin.ToLowerInvariant(), Answers = { "paris" }, DatasetSource = "two.jsonl" });
        var other = await db.ImportItemAsync(new QaDatasetItem { Question = "Name a French river.", Answers = { "seine" }, DatasetSource = "one.jsonl" });
        await db.UpdateEmbeddingAsync(a, new[] { 1f, 0f });
        await db.UpdateEmbeddingAsync(copy, new[] { 1f, 0f });
        await db.UpdateEmbeddingAsync(other, new[] { 0.9f, 0.43f });

        var prompts = new List<string>();
        await QaBenchmarkRunner.RunAsync(
            new QaBenchmarkOptions { MaxQuestions = 3, UseRagContext = true, SimilarContextCount = 3, Seed = 1 },
            db, p => { prompts.Add(p); return Task.FromResult("I do not know"); });

        var twinPrompts = prompts.Where(p => p.Contains("Question: " + Twin, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(2, twinPrompts.Count);
        Assert.All(twinPrompts, p => Assert.DoesNotContain("Q: " + Twin, p, StringComparison.OrdinalIgnoreCase));
        // ...while a different question may still use them as examples.
        Assert.Contains(prompts, p => p.Contains("Question: Name a French river.") && p.Contains("Q: " + Twin, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EmbeddingsOfDifferentSizes_FailTheQuestionsInsteadOfRankingOnATruncatedVector()
    {
        using var db = await NewDbAsync();
        var a = await db.ImportItemAsync(new QaDatasetItem { Question = "First?" });
        var b = await db.ImportItemAsync(new QaDatasetItem { Question = "Second?" });
        await db.UpdateEmbeddingAsync(a, new[] { 1f, 0f });
        await db.UpdateEmbeddingAsync(b, new[] { 1f, 0f, 0f });

        var report = await QaBenchmarkRunner.RunAsync(
            new QaBenchmarkOptions { MaxQuestions = 2, UseRagContext = true, MaxConsecutiveErrors = 0 },
            db, _ => Task.FromResult("x"));

        Assert.True(report.Errors > 0);
        Assert.Contains(report.Results, r => r.ModelResponse!.Contains("different models"));
        Assert.False(report.Valid);
    }

    [Fact]
    public async Task UnembeddedQuery_SkipsTheIdsThatAlreadyFailed()
    {
        using var db = await NewDbAsync();
        var ids = new List<int>();
        for (int i = 0; i < 3; i++) ids.Add(await db.ImportItemAsync(new QaDatasetItem { Question = $"Q{i}?" }));

        Assert.Equal(3, (await db.GetUnembeddedItemsAsync(10)).Count);
        var rest = await db.GetUnembeddedItemsAsync(10, new HashSet<int> { ids[0], ids[1] });
        Assert.Equal(new[] { ids[2] }, rest.Select(x => x.Id));
    }
}

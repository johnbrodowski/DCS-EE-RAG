using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;

using LocalRAG;
using LocalRAG.Benchmarks;

using System.Diagnostics;
using System.Text;

namespace DemoApp
{
    public partial class Form1 : Form
    {
        private EmbeddingDatabaseNew db;

        private string _currentRequest = string.Empty;

        public Form1()
        {
            InitializeComponent();
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            db = new EmbeddingDatabaseNew(new RAGConfiguration());
        }

        private async Task<string> FormatRelevantFeedback(string query, int topK = 30, int minWordsToTriggerLsh = 1)
        {
            var sb = new StringBuilder();

            //if (CountWords(query) > minWordsToTriggerLsh)
            //{
                List<FeedbackDatabaseValues> results = await db.SearchEmbeddingsAsync(
                searchText: query,
                topK: topK,
              //  minimumSimilarity: 0.05f,
                  minimumSimilarity: 0.00f,
                searchLevel: 2
                );

            void AppendSection(StringBuilder sb, string title, string? content)
            {
                if (!string.IsNullOrEmpty(content))
                {
                    sb.AppendLine($"{title}: {content}");
                }
            }

            var sortedResults = results
                .Where(x => x.Similarity.HasValue)
                .OrderByDescending(x => x.Similarity)
            .ToList();

            sb.AppendLine($"# Here is a list of potentially relevant feedback from the RAG for you to consider when formulating your response.");
            sb.AppendLine(@"Note: Similarity scores are guides, not gospel. Evaluate actual relevance and usefulness of returned content, regardless of numerical scores.");

            foreach (var (result, index) in sortedResults.Select((result, index) => (result, index + 1)))
            {
                sb.AppendLine().AppendLine($"Results: {index} - Similarity {result.Similarity:F5}");

                AppendSection(sb, "Users Request", result.Request);
                AppendSection(sb, "AI text Response", GetTruncatedText(result.TextResponse, 3000));
                AppendSection(sb, "AI Tool Use TextResponse", GetTruncatedText(result.ToolUseTextResponse, 3000));
            }

            sb.AppendLine($"User Request:");
            sb.AppendLine();

            return sb.ToString();
        }

        private async Task LoadConversationHistory(int maxMsgCount = 10, bool showToolInputs = true, bool removeToolMessages = true)
        {
            try
            {
                List<(string request, string textResponse, string toolUseTextResponse, string toolContent, string toolResult, string requestID)> recentHistory =

                await db.GetConversationHistoryAsync(maxMsgCount);

                foreach (var (request, textResponse, toolUseTextResponse, toolContent, toolResult, requestId) in recentHistory)
                {
                    if (!string.IsNullOrEmpty(request) && !string.IsNullOrEmpty(textResponse))
                    {
                        Debug.WriteLine($"Request: {request}");
                        Debug.WriteLine($"Response: {textResponse}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading history: {ex.Message}");
            }
        }

        public int CountWords(string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return 0;

            string[] words = str.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length;
        }

        private string GetTruncatedText(string text, int maxLength, string truncationMessage = "")
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength) + "... " + truncationMessage;
        }

        private async void btnSearch_Click(object sender, EventArgs e)
        {
            var msg = await FormatRelevantFeedback(txtQuery.Text);
            txtResult.Text = msg;
        }

        // ── EE-RAG pipeline ──────────────────────────────────────────────────

        private async void btnAskAI_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtQuery.Text)) return;

            btnAskAI.Enabled = false;
            txtResult.Text = "Running EE-RAG pipeline…\r\n";

            try
            {
                var provider = cmbProvider.Text.Trim();
                var model = txtModel.Text.Trim();

                if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
                {
                    txtResult.Text = "Error: Provider and Model must be specified.";
                    return;
                }


                var config = AiSettings.LoadFromFile("ai-settings.json");
                AiSettings.ApplyToEnvironment(config);


                var session = AiSessionBuilder
                    .WithProvider(provider)
                    .WithModel(model)
                    .WithMaxTokens(2048)
                    .WithSystemMessage(
                        "You are a helpful assistant. " +
                        "When you see a list of candidate context entries under '--- Potentially Relevant Context ---', " +
                        "evaluate them and, if any are relevant to the query, respond ONLY with a RETRIEVE command " +
                        "(e.g. RETRIEVE 7  or  RETRIEVE 3 7 12). " +
                        "If none are relevant, answer the question directly without a RETRIEVE command.")
                    .Build();

                var userMessage = txtQuery.Text;
                var requestId = Guid.NewGuid().ToString();
                var silentMode = chkSilentMode.Checked;

                // Insert the request row so the two-phase audit writes have a row to UPDATE.
                await db.AddRequestToEmbeddingDatabaseAsync(requestId, userMessage, embed: false);

                var result = await EERagPipeline.RunPipelineAsync(
                    userMessage,
                    requestId,
                    session,
                    db,
                    topK: 5,
                    silentMode: silentMode);

                DisplayEERagTrace(userMessage, result, silentMode);
            }
            catch (Exception ex)
            {
                txtResult.Text = $"EE-RAG error: {ex.Message}\r\n{ex.StackTrace}";
            }
            finally
            {
                btnAskAI.Enabled = true;
            }
        }

        private void DisplayEERagTrace(string userMessage, EERagResult result, bool silentMode)
        {
            var sb = new StringBuilder();

            sb.AppendLine("=== EE-RAG Pipeline Trace ===");
            sb.AppendLine();
            sb.AppendLine($"[Query]  {userMessage}");
            sb.AppendLine();

            sb.AppendLine($"[Step 1-2]  Candidate summaries sent to model as transient context ({result.CandidateIdsSurfaced.Count} entries):");
            sb.AppendLine(result.CandidateHeader);

            sb.AppendLine("[Step 4-5]  Model's first response:");
            sb.AppendLine(result.FirstResponse);
            sb.AppendLine();

            if (!result.RetrievalOccurred)
            {
                sb.AppendLine("[Step 6]  No RETRIEVE command issued — first response is final. No extra tokens consumed.");
            }
            else
            {
                var mode = silentMode ? "Layer 5 silent digestion" : "standard ephemeral injection";
                sb.AppendLine($"[Step 6-7]  Model elected IDs: [{string.Join(", ", result.ElectedIds)}] — {mode}");
                sb.AppendLine();
                sb.AppendLine("[Step 7]  Final response (with [RAG:hash] tags):");
            }

            sb.AppendLine(result.FinalResponse);
            sb.AppendLine();

            sb.AppendLine("--- Two-Phase Audit (EE-RAG Section 3.4) ---");
            sb.AppendLine($"rag_candidates_surfaced : [{string.Join(", ", result.CandidateIdsSurfaced)}]  (written pre-inference)");
            sb.AppendLine($"rag_entries_elected     : [{string.Join(", ", result.ElectedHashes)}]  (written post-inference)");
            sb.AppendLine("Both stored in MetaData JSON — zero context tokens consumed.");

            txtResult.Text = sb.ToString().Replace("\n", "\r\n");
        }

        private async void runTestsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            txtResult.Text = "Running tests...\r\n";
            runTestsToolStripMenuItem.Enabled = false;

            try
            {
                var tests = new EmbeddingTests();
                var sb = new StringBuilder();

                // Capture console output
                var originalOut = Console.Out;
                using var writer = new StringWriter();
                Console.SetOut(writer);

                try
                {
                    // Run unit tests (no BERT model required)
                    var results = await tests.RunAllTestsAsync(null);

                    // Try to run integration tests if config is available
                    try
                    {
                        var config = new RAGConfiguration();
                        if (System.IO.File.Exists(config.ModelPath))
                        {
                            sb.AppendLine("\r\n--- Running Integration Tests ---\r\n");
                            results = await tests.RunAllTestsAsync(config);
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"\r\nIntegration tests skipped: {ex.Message}");
                    }
                }
                finally
                {
                    Console.SetOut(originalOut);
                }

                sb.Insert(0, writer.ToString().Replace("\n", "\r\n"));
                txtResult.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                txtResult.Text = $"Test error: {ex.Message}\r\n{ex.StackTrace}";
            }
            finally
            {
                runTestsToolStripMenuItem.Enabled = true;
            }
        }

        private async void benchmarkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            benchmarkToolStripMenuItem.Enabled = false;
            txtResult.Text = "Loading benchmark dataset...\r\n";

            try
            {
                var provider = cmbProvider.Text.Trim();
                var model    = txtModel.Text.Trim();

                if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
                {
                    txtResult.Text = "Error: Provider and Model must be specified.";
                    return;
                }

                var dataset = EERagBenchmark.LoadEmbeddedDataset();
                txtResult.Text += $"Dataset: {dataset.Name} — {dataset.KnowledgeEntries.Count} entries, " +
                                  $"{dataset.Cases.Count} cases.\r\n";

                var options = new BenchmarkOptions
                {
                    TopK              = 5,
                    SilentMode        = chkSilentMode.Checked,
                    SeedFreshDatabase = true
                };

                if (options.SeedFreshDatabase)
                {
                    var confirm = MessageBox.Show(
                        "Run Benchmark will CLEAR all current database records and seed benchmark data.\n\nContinue?",
                        "Confirm Benchmark Run",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (confirm != DialogResult.Yes)
                    {
                        txtResult.Text = "Benchmark cancelled.";
                        return;
                    }

                    txtResult.AppendText("Clearing database...\r\n");
                    await db.ClearAllDataAsync();
                }

                var progress = new Progress<string>(msg =>
                    this.Invoke(() => txtResult.AppendText(msg + "\r\n")));

                txtResult.AppendText("Seeding database with benchmark knowledge...\r\n");
                var slugToId = await EERagBenchmark.SeedDatabaseAsync(
                    dataset, db, generateEmbeddings: true, progress: progress);

                txtResult.AppendText($"Seeded {slugToId.Count} entries.\r\n");

                var config = AiSettings.LoadFromFile("ai-settings.json");
                AiSettings.ApplyToEnvironment(config);

                // Factory so RunAsync creates a fresh session per case, preventing
                // conversation history from one case biasing the next.
                Func<IChatSession> sessionFactory = () => AiSessionBuilder
                    .WithProvider(provider)
                    .WithModel(model)
                    .WithMaxTokens(512)
                    .WithSystemMessage(
                        "You are a helpful knowledge base assistant operating in two modes.\n\n" +
                        "MODE 1 — Candidate Evaluation: When the context begins with '--- Potentially Relevant Context ---' " +
                        "and lists entries with [ID:<number>] labels, your ONLY job is to decide which entries are topically relevant. " +
                        "If ANY entry relates to the query — even if you already know the answer — your ENTIRE response must be " +
                        "EXACTLY: RETRIEVE <id1> [<id2> ...] using the exact numbers shown in [ID:<number>]. " +
                        "No other words. No explanation. No preamble. Just the RETRIEVE command. " +
                        "Only if NONE of the candidates relate to the query at all, answer the question directly.\n\n" +
                        "MODE 2 — Answer Generation: When the context begins with '--- Retrieved Context ---' or " +
                        "'--- Background Knowledge ---', answer the user's question thoroughly using that content. " +
                        "Do NOT issue RETRIEVE commands in this mode.")
                    .Build();

                txtResult.AppendText("Running benchmark cases...\r\n");
                var report = await EERagBenchmark.RunAsync(
                    dataset, slugToId, sessionFactory, db,
                    options: options,
                    progress: progress);

                txtResult.Text = report.FormatSummary().Replace("\n", "\r\n");
            }
            catch (Exception ex)
            {
                txtResult.Text = $"Benchmark error: {ex.Message}\r\n{ex.StackTrace}";
            }
            finally
            {
                benchmarkToolStripMenuItem.Enabled = true;
            }
        }

        private async void generateMockDataToolStripMenuItem_Click(object sender, EventArgs e)
        {
            generateMockDataToolStripMenuItem.Enabled = false;
            txtResult.Text = "Generating mock data...\r\n";

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== Mock Data Generation ===\r\n");

                // Get stats before
                var statsBefore = await db.GetStatsAsync();
                sb.AppendLine($"Before: {statsBefore.TotalRecords} records\r\n");

                // Generate mock data
                int count = await db.PopulateWithMockDataAsync(
                    count: 20,
                    generateEmbeddings: true,
                    progress: (done, total) =>
                    {
                        this.Invoke(() => txtResult.Text = $"Generating: {done}/{total}...");
                    }
                );

                // Get stats after
                var statsAfter = await db.GetStatsAsync();
                sb.AppendLine($"Created {count} mock records\r\n");
                sb.AppendLine($"After: {statsAfter}\r\n");

                txtResult.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                txtResult.Text = $"Error: {ex.Message}\r\n{ex.StackTrace}";
            }
            finally
            {
                generateMockDataToolStripMenuItem.Enabled = true;
            }
        }

        private void qaDatasetBenchmarkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var form = new FormQaBenchmark();
            form.Show(this);
        }

        private async void missingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await RunBackfillAsync("missing");
        }

        private async void allToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await RunBackfillAsync("all");
        }

        private async Task RunBackfillAsync(string mode)
        {
            missingToolStripMenuItem.Enabled = false;
            allToolStripMenuItem.Enabled = false;
            txtResult.Text = $"Starting backfill (mode: {mode})...\r\n";

            try
            {
                var result = await db.BackfillEmbeddingsAsync(
                    mode: mode,
                    batchSize: 10,
                    progress: (done, total, requestId) =>
                    {
                        this.Invoke(() => txtResult.Text = $"Backfill: {done}/{total} - {requestId}");
                    }
                );

                txtResult.Text = $"Backfill Complete!\r\n\r\n{result}";
            }
            catch (Exception ex)
            {
                txtResult.Text = $"Backfill error: {ex.Message}\r\n{ex.StackTrace}";
            }
            finally
            {
                missingToolStripMenuItem.Enabled = true;
                allToolStripMenuItem.Enabled = true;
            }
        }
    }
}

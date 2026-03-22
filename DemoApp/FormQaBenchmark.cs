using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;

using LocalRAG;
using LocalRAG.Benchmarks;
using LocalRAG.QaDataset;

namespace DemoApp
{
    public partial class FormQaBenchmark : Form
    {
        private QaDatasetDatabase? _db;
        private readonly QaEmbeddingBackfiller _backfiller = new();
        private QaBenchmarkReport? _lastReport;
        private BenchmarkReport? _lastDcsReport;
        private BenchmarkSettings _settings = new();

        private CancellationTokenSource? _runCts;
        private CancellationTokenSource? _tuneCts;

        private static string DefaultDbPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                         "Database", "QaDataset", "QaBenchmark.db");

        public FormQaBenchmark()
        {
            InitializeComponent();
        }

        private async void FormQaBenchmark_Load(object sender, EventArgs e)
        {
            LoadSettings();
            await OpenDatabaseAsync(DefaultDbPath);
        }

        // ── Settings ──────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            _settings = BenchmarkSettings.Load();

            // Run controls
            var providerIdx = cmbProvider.Items.IndexOf(_settings.Provider);
            cmbProvider.SelectedIndex = providerIdx >= 0 ? providerIdx : 0;
            txtModel.Text = _settings.Model;
            numQuestions.Value = Clamp(numQuestions.Minimum, numQuestions.Maximum, _settings.MaxQuestions);
            numTemperature.Value = Clamp(0M, 2M, (decimal)_settings.Temperature);
            chkUseRag.Checked = _settings.UseRag;

            // Embedding settings
            chkRemoveStopWords.Checked = _settings.RemoveStopWords;
            numOverlapPct.Value = Clamp(numOverlapPct.Minimum, numOverlapPct.Maximum, (decimal)_settings.OverlapPercentage);
            numWordsPerChunk.Value = Clamp(numWordsPerChunk.Minimum, numWordsPerChunk.Maximum, _settings.WordsPerChunk);
            var seqIdx = cmbMaxSeqLen.Items.IndexOf(_settings.MaxSequenceLength.ToString());
            cmbMaxSeqLen.SelectedIndex = seqIdx >= 0 ? seqIdx : 1; // default to "256"
            chkLowercase.Checked = _settings.LowercaseInput;
            chkOverwriteEmbeddings.Checked = _settings.OverwriteEmbeddings;

            // DCS settings
            chkUseDcs.Checked = _settings.UseDcsPipeline;
            numTopK.Value = Clamp(numTopK.Minimum, numTopK.Maximum, _settings.DcsTopK);
            chkSilentMode.Checked = _settings.DcsSilentMode;
            UpdateDcsControlStates();

            // Thresholds
            numCorrectThresh.Value     = Clamp(0M, 100M, (decimal)_settings.CorrectThreshold);
            numPossCorrectThresh.Value = Clamp(0M, 100M, (decimal)_settings.PossiblyCorrectThreshold);
            numDefWrongThresh.Value    = Clamp(0M, 100M, (decimal)_settings.DefinitelyWrongThreshold);
            numIndetermThresh.Value    = Clamp(0M, 100M, (decimal)_settings.IndeterminateThreshold);

            // Tuning
            txtTuneTemps.Text = _settings.TuneTemperatures;
            txtTuneModels.Text = _settings.TuneModels;
            numTuneQuestions.Value = Clamp(numTuneQuestions.Minimum, numTuneQuestions.Maximum, _settings.TuneQuestionsPerRun);
            var scoreIdx = cmbTuneScore.Items.IndexOf(_settings.TuneScoreMetric);
            if (scoreIdx < 0) scoreIdx = 0;
            cmbTuneScore.SelectedIndex = scoreIdx;
        }

        private void SaveCurrentSettings()
        {
            _settings.Provider    = cmbProvider.Text.Trim();
            _settings.Model       = txtModel.Text.Trim();
            _settings.MaxQuestions = (int)numQuestions.Value;
            _settings.Temperature = (double)numTemperature.Value;
            _settings.UseRag      = chkUseRag.Checked;

            _settings.RemoveStopWords    = chkRemoveStopWords.Checked;
            _settings.OverlapPercentage  = (double)numOverlapPct.Value;
            _settings.WordsPerChunk      = (int)numWordsPerChunk.Value;
            _settings.MaxSequenceLength  = int.TryParse(cmbMaxSeqLen.Text, out var seqLen) ? seqLen : 256;
            _settings.LowercaseInput     = chkLowercase.Checked;
            _settings.OverwriteEmbeddings = chkOverwriteEmbeddings.Checked;

            _settings.UseDcsPipeline = chkUseDcs.Checked;
            _settings.DcsTopK        = (int)numTopK.Value;
            _settings.DcsSilentMode  = chkSilentMode.Checked;

            _settings.CorrectThreshold         = (double)numCorrectThresh.Value;
            _settings.PossiblyCorrectThreshold  = (double)numPossCorrectThresh.Value;
            _settings.DefinitelyWrongThreshold  = (double)numDefWrongThresh.Value;
            _settings.IndeterminateThreshold    = (double)numIndetermThresh.Value;

            _settings.TuneTemperatures   = txtTuneTemps.Text.Trim();
            _settings.TuneModels         = txtTuneModels.Text.Trim();
            _settings.TuneQuestionsPerRun = (int)numTuneQuestions.Value;
            _settings.TuneScoreMetric    = cmbTuneScore.Text;

            _settings.Save();
        }

        private void btnSaveSettings_Click(object sender, EventArgs e)
        {
            SaveCurrentSettings();
            AppendLine("Settings saved.");
        }

        private void chkUseDcs_CheckedChanged(object sender, EventArgs e)
        {
            UpdateDcsControlStates();
        }

        private void UpdateDcsControlStates()
        {
            bool dcs = chkUseDcs.Checked;
            lblTopK.Enabled = dcs;
            numTopK.Enabled = dcs;
            chkSilentMode.Enabled = dcs;
            // Old-style RAG injection is irrelevant when DCS pipeline is active
            chkUseRag.Enabled = !dcs;
        }

        private RAGConfiguration BuildRagConfigFromUI()
        {
            return new RAGConfiguration
            {
                RemoveStopWords   = chkRemoveStopWords.Checked,
                OverlapPercentage = (double)numOverlapPct.Value,
                WordsPerString    = (int)numWordsPerChunk.Value,
                MaxSequenceLength = int.TryParse(cmbMaxSeqLen.Text, out var seqLen) ? seqLen : 256,
                LowercaseInput    = chkLowercase.Checked
            };
        }

        // ── Database ──────────────────────────────────────────────────────────

        private async Task OpenDatabaseAsync(string path)
        {
            _db?.Dispose();
            _db = new QaDatasetDatabase(path);
            await _db.InitializeAsync();
            await RefreshCountsAsync();
        }

        private async Task RefreshCountsAsync()
        {
            if (_db == null) return;
            var (total, embedded) = await _db.GetItemCountAsync();
            lblEmbedStatus.Text   = $"Embedded: {embedded:N0} / {total:N0}";
            lblEmbeddedCount.Text = $"({embedded:N0} embedded)";

            if (total > 0)
            {
                pbarBackfill.Maximum = total;
                pbarBackfill.Value   = Math.Min(embedded, total);
            }
            else
            {
                pbarBackfill.Value = 0;
            }
        }

        // ── Import ────────────────────────────────────────────────────────────

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Select JSONL Dataset File",
                Filter = "JSONL files (*.jsonl)|*.jsonl|JSON files (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                txtFilePath.Text = dlg.FileName;
        }

        private async void btnImport_Click(object sender, EventArgs e)
        {
            var path = txtFilePath.Text.Trim();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                lblImportStatus.Text      = "File not found.";
                lblImportStatus.ForeColor = Color.Red;
                return;
            }

            if (_db == null)
            {
                lblImportStatus.Text      = "Database not ready.";
                lblImportStatus.ForeColor = Color.Red;
                return;
            }

            btnImport.Enabled         = false;
            lblImportStatus.ForeColor = Color.DarkBlue;
            lblImportStatus.Text      = "Importing…";

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    lblImportStatus.Text      = msg;
                    lblImportStatus.ForeColor = Color.DarkBlue;
                });

                int count = await QaDatasetLoader.ImportFileAsync(path, _db, progress);
                lblImportStatus.Text      = $"Imported {count:N0} items from {Path.GetFileName(path)}.";
                lblImportStatus.ForeColor = Color.DarkGreen;
                await RefreshCountsAsync();
            }
            catch (Exception ex)
            {
                lblImportStatus.Text      = $"Error: {ex.Message}";
                lblImportStatus.ForeColor = Color.Red;
            }
            finally
            {
                btnImport.Enabled = true;
            }
        }

        // ── Backfill ──────────────────────────────────────────────────────────

        private void btnStartBackfill_Click(object sender, EventArgs e)
        {
            if (_db == null) return;
            if (_backfiller.IsRunning) return;

            var ragConfig = BuildRagConfigFromUI();
            bool overwrite = chkOverwriteEmbeddings.Checked;

            EmbedderClassNew embedder;
            try { embedder = new EmbedderClassNew(ragConfig); }
            catch (Exception ex)
            {
                AppendLine($"[Backfill] Cannot load BERT model: {ex.Message}");
                return;
            }

            btnStartBackfill.Enabled = false;
            btnStopBackfill.Enabled  = true;
            AppendLine($"[Backfill] Starting…{(overwrite ? " (overwriting existing embeddings)" : "")}");

            var progress = new Progress<(int Done, int Total)>(p =>
            {
                if (p.Total > 0)
                {
                    pbarBackfill.Maximum  = p.Total;
                    pbarBackfill.Value    = Math.Min(p.Done, p.Total);
                    lblEmbedStatus.Text   = $"Embedded: {p.Done:N0} / {p.Total:N0}";
                    lblEmbeddedCount.Text = $"({p.Done:N0} embedded)";
                }
            });

            _backfiller.Start(_db, embedder, progress, errMsg =>
            {
                Invoke(() => AppendLine($"[Backfill] Error: {errMsg}"));
                Invoke(() => ResetBackfillButtons());
            }, overwriteExisting: overwrite);

            _ = Task.Run(async () =>
            {
                await _backfiller.WaitAsync();
                Invoke(() =>
                {
                    AppendLine("[Backfill] Complete.");
                    ResetBackfillButtons();
                    _ = RefreshCountsAsync();
                });
            });
        }

        private void btnStopBackfill_Click(object sender, EventArgs e)
        {
            _backfiller.Stop();
            AppendLine("[Backfill] Stopping after current item…");
            btnStopBackfill.Enabled = false;
        }

        private void ResetBackfillButtons()
        {
            btnStartBackfill.Enabled = true;
            btnStopBackfill.Enabled  = false;
        }

        // ── Benchmark run ─────────────────────────────────────────────────────

        private async void btnRunBenchmark_Click(object sender, EventArgs e)
        {
            var provider = cmbProvider.Text.Trim();
            var model    = txtModel.Text.Trim();
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
            {
                AppendLine("Error: Provider and Model are required.");
                return;
            }

            if (chkUseDcs.Checked)
            {
                await RunDcsBenchmarkAsync(provider, model);
                return;
            }

            if (_db == null) return;

            SaveCurrentSettings();

            btnRunBenchmark.Enabled = false;
            btnSaveReport.Enabled   = false;
            _lastReport             = null;
            ResetLiveStats();
            chartPanel.SetMode(BenchmarkChart.ChartMode.Qa);

            _runCts = new CancellationTokenSource();

            try
            {
                var config = AiSettings.LoadFromFile("ai-settings.json");
                AiSettings.ApplyToEnvironment(config);

                var temperature = (double)numTemperature.Value;

                var session = AiSessionBuilder
                    .WithProvider(provider)
                    .WithModel(model)
                    .WithTemperature(temperature)
                    .WithMaxTokens(256)
                    .WithSystemMessage(
                        "You are a factual Q&A assistant. Answer questions concisely and directly. " +
                        "Give only the answer — do not explain or add qualifications unless they are part of the answer.")
                    .Build();

                var opts = new QaBenchmarkOptions
                {
                    MaxQuestions      = (int)numQuestions.Value,
                    Provider          = provider,
                    Model             = model,
                    Temperature       = temperature,
                    UseRagContext     = chkUseRag.Checked,
                    SimilarContextCount = 3
                };

                var progress = new Progress<BenchmarkProgressUpdate>(OnRunProgress);

                AppendLine($"=== Starting benchmark: {opts.MaxQuestions} questions, model={model}, temp={temperature:F2} ===");

                _lastReport = await QaBenchmarkRunner.RunAsync(opts, _db, session, progress, _runCts.Token);

                _lastReport.Provider                 = provider;
                _lastReport.Model                    = model;
                _lastReport.Temperature              = temperature;
                _lastReport.CorrectThreshold         = (double)numCorrectThresh.Value;
                _lastReport.PossiblyCorrectThreshold = (double)numPossCorrectThresh.Value;
                _lastReport.DefinitelyWrongThreshold = (double)numDefWrongThresh.Value;
                _lastReport.IndeterminateThreshold   = (double)numIndetermThresh.Value;

                rtbResults.AppendText(Environment.NewLine + _lastReport.FormatSummary());
                rtbResults.ScrollToCaret();
                btnSaveReport.Enabled = true;
            }
            catch (OperationCanceledException)
            {
                AppendLine("Run cancelled.");
            }
            catch (Exception ex)
            {
                AppendLine($"Error: {ex.Message}");
            }
            finally
            {
                btnRunBenchmark.Enabled = true;
                _runCts?.Dispose();
                _runCts = null;
            }
        }

        // ── DCS / EE-RAG benchmark run ──────────────────────────────────────

        private static readonly string DcsSystemMessage =
            "You are a helpful knowledge base assistant operating in two modes.\n\n" +
            "MODE 1 — Candidate Evaluation: When the context begins with '--- Potentially Relevant Context ---' " +
            "and lists entries with [ID:<number>] labels, your ONLY job is to decide which entries are topically relevant. " +
            "If ANY entry relates to the query — even if you already know the answer — your ENTIRE response must be " +
            "EXACTLY: RETRIEVE <id1> [<id2> ...] using the exact numbers shown in [ID:<number>]. " +
            "No other words. No explanation. No preamble. Just the RETRIEVE command. " +
            "Only if NONE of the candidates relate to the query at all, answer the question directly.\n\n" +
            "MODE 2 — Answer Generation: When the context begins with '--- Retrieved Context ---' or " +
            "'--- Background Knowledge ---', answer the user's question thoroughly using that content. " +
            "Do NOT issue RETRIEVE commands in this mode.";

        private async Task RunDcsBenchmarkAsync(string provider, string model)
        {
            SaveCurrentSettings();

            btnRunBenchmark.Enabled = false;
            btnSaveReport.Enabled   = false;
            _lastDcsReport          = null;
            _lastReport             = null;
            ResetLiveStats();
            chartPanel.SetMode(BenchmarkChart.ChartMode.Dcs);

            _runCts = new CancellationTokenSource();

            var temperature = (double)numTemperature.Value;
            int topK = (int)numTopK.Value;
            bool silentMode = chkSilentMode.Checked;

            AppendLine($"=== Starting EE-RAG/DCS benchmark: model={model}, temp={temperature:F2}, topK={topK}, silent={silentMode} ===");

            EmbeddingDatabaseNew? dcsDb = null;
            var tempDbPath = Path.Combine(Path.GetTempPath(), $"dcs_bench_{Guid.NewGuid():N}.db");

            try
            {
                var aiConfig = AiSettings.LoadFromFile("ai-settings.json");
                AiSettings.ApplyToEnvironment(aiConfig);

                var ragConfig = new RAGConfiguration { DatabasePath = tempDbPath };
                dcsDb = new EmbeddingDatabaseNew(ragConfig);
                await Task.Delay(500); // Allow DB initialization

                var dataset = EERagBenchmark.LoadEmbeddedDataset();
                AppendLine($"  Dataset: {dataset.KnowledgeEntries.Count} knowledge entries, {dataset.Cases.Count} benchmark cases");

                var seedProgress = new Progress<string>(msg => AppendLine($"  {msg}"));
                var slugToId = await EERagBenchmark.SeedDatabaseAsync(
                    dataset, dcsDb, generateEmbeddings: true,
                    progress: seedProgress, ct: _runCts.Token);
                AppendLine($"  Database seeded: {slugToId.Count} entries");

                Func<IChatSession> sessionFactory = () =>
                    AiSessionBuilder
                        .WithProvider(provider)
                        .WithModel(model)
                        .WithTemperature(temperature)
                        .WithMaxTokens(512)
                        .WithSystemMessage(DcsSystemMessage)
                        .Build();

                var options = new BenchmarkOptions
                {
                    TopK = topK,
                    SilentMode = silentMode
                };

                int casesDone = 0;
                double runF1 = 0, runP = 0, runR = 0, runCR = 0;

                var msgProgress = new Progress<string>(msg =>
                {
                    AppendLine(msg);
                    casesDone++;
                    if (dataset.Cases.Count > 0)
                    {
                        pbarRunProgress.Maximum = dataset.Cases.Count;
                        pbarRunProgress.Value = Math.Min(casesDone, dataset.Cases.Count);
                    }
                });

                var caseResultProgress = new Progress<LocalRAG.Benchmarks.CaseResult>(cr =>
                {
                    // Running averages
                    int n = casesDone;
                    if (n <= 0) n = 1;
                    runF1 = ((runF1 * (n - 1)) + cr.F1 * 100) / n;
                    runP  = ((runP  * (n - 1)) + cr.Precision * 100) / n;
                    runR  = ((runR  * (n - 1)) + cr.Recall * 100) / n;
                    runCR = ((runCR * (n - 1)) + cr.CandidateRecall * 100) / n;

                    lblLiveStats.Text = $"DCS: {n}/{dataset.Cases.Count}  " +
                                        $"F1={runF1:F1}%  P={runP:F1}%  R={runR:F1}%  CR={runCR:F1}%";

                    chartPanel.AddPoint([runF1, runP, runR, runCR]);
                });

                _lastDcsReport = await EERagBenchmark.RunAsync(
                    dataset, slugToId, sessionFactory, dcsDb,
                    options: options, progress: msgProgress,
                    caseProgress: caseResultProgress,
                    ct: _runCts.Token);

                rtbResults.AppendText(Environment.NewLine + _lastDcsReport.FormatSummary());
                rtbResults.ScrollToCaret();
                btnSaveReport.Enabled = true;
            }
            catch (OperationCanceledException)
            {
                AppendLine("DCS run cancelled.");
            }
            catch (Exception ex)
            {
                AppendLine($"DCS Error: {ex.Message}");
            }
            finally
            {
                btnRunBenchmark.Enabled = true;
                _runCts?.Dispose();
                _runCts = null;

                if (dcsDb != null)
                {
                    await dcsDb.DisposeAsync();
                    await Task.Delay(200);
                }
                CleanupTempDb(tempDbPath);
            }
        }

        private async Task<BenchmarkReport?> RunDcsBenchmarkSilentAsync(
            string provider, string model, double temperature,
            int topK, bool silentMode,
            IProgress<BenchmarkProgressUpdate>? progress,
            IProgress<LocalRAG.Benchmarks.CaseResult>? caseProgress = null,
            CancellationToken ct = default)
        {
            EmbeddingDatabaseNew? dcsDb = null;
            var tempDbPath = Path.Combine(Path.GetTempPath(), $"dcs_tune_{Guid.NewGuid():N}.db");

            try
            {
                var ragConfig = new RAGConfiguration { DatabasePath = tempDbPath };
                dcsDb = new EmbeddingDatabaseNew(ragConfig);
                await Task.Delay(500);

                var dataset = EERagBenchmark.LoadEmbeddedDataset();
                var slugToId = await EERagBenchmark.SeedDatabaseAsync(
                    dataset, dcsDb, generateEmbeddings: true, ct: ct);

                Func<IChatSession> sessionFactory = () =>
                    AiSessionBuilder
                        .WithProvider(provider)
                        .WithModel(model)
                        .WithTemperature(temperature)
                        .WithMaxTokens(512)
                        .WithSystemMessage(DcsSystemMessage)
                        .Build();

                var options = new BenchmarkOptions
                {
                    TopK = topK,
                    SilentMode = silentMode
                };

                int casesDone = 0;
                var msgProgress = new Progress<string>(msg =>
                {
                    casesDone++;
                    progress?.Report(new BenchmarkProgressUpdate
                    {
                        Message = msg,
                        Done = casesDone,
                        Total = dataset.Cases.Count
                    });
                });

                return await EERagBenchmark.RunAsync(
                    dataset, slugToId, sessionFactory, dcsDb,
                    options: options, progress: msgProgress,
                    caseProgress: caseProgress, ct: ct);
            }
            finally
            {
                if (dcsDb != null)
                {
                    await dcsDb.DisposeAsync();
                    await Task.Delay(200);
                }
                CleanupTempDb(tempDbPath);
            }
        }

        private static void CleanupTempDb(string path)
        {
            for (int i = 0; i < 3; i++)
            {
                try { if (File.Exists(path)) File.Delete(path); break; }
                catch (IOException) { Thread.Sleep(100); }
            }
        }

        private void OnRunProgress(BenchmarkProgressUpdate upd)
        {
            // Log each question line
            if (!string.IsNullOrEmpty(upd.Message))
                AppendLine(upd.Message);

            // Update live stats bar
            if (upd.Total > 0)
            {
                pbarRunProgress.Maximum = upd.Total;
                pbarRunProgress.Value   = Math.Min(upd.Done, upd.Total);
            }

            if (upd.Done > 0)
            {
                lblLiveStats.Text = $"Q: {upd.Done}/{upd.Total}  " +
                                    $"✓{upd.CorrectRate:F0}%  " +
                                    $"?{upd.PossiblyCorrectRate:F0}%  " +
                                    $"✗{upd.DefinitelyWrongRate:F0}%  " +
                                    $"–{upd.IndeterminateRate:F0}%";

                // Feed chart
                chartPanel.AddPoint([
                    upd.CorrectRate,
                    upd.PossiblyCorrectRate,
                    upd.DefinitelyWrongRate,
                    upd.IndeterminateRate
                ]);
            }
        }

        private void ResetLiveStats()
        {
            pbarRunProgress.Value = 0;
            lblLiveStats.Text     = "Q: 0/0  ✓--%  ?--%  ✗--%  ---%";
        }

        // ── Auto-Tune ─────────────────────────────────────────────────────────

        private async void btnAutoTune_Click(object sender, EventArgs e)
        {
            if (_db == null && !chkUseDcs.Checked) return;

            var provider = cmbProvider.Text.Trim();
            if (string.IsNullOrWhiteSpace(provider))
            {
                AppendLine("Auto-Tune: Provider is required.");
                return;
            }

            // Parse temperatures
            var temps = ParseDoubleList(txtTuneTemps.Text);
            if (temps.Count == 0)
            {
                AppendLine("Auto-Tune: No valid temperatures specified.");
                return;
            }

            // Parse models — blank means use the current model only
            var models = ParseStringList(txtTuneModels.Text);
            if (models.Count == 0)
                models.Add(txtModel.Text.Trim());

            int qPerRun = (int)numTuneQuestions.Value;

            SaveCurrentSettings();

            btnAutoTune.Enabled     = false;
            btnStopTune.Enabled     = true;
            btnRunBenchmark.Enabled = false;

            _tuneCts = new CancellationTokenSource();

            try
            {
                var config = AiSettings.LoadFromFile("ai-settings.json");
                AiSettings.ApplyToEnvironment(config);

                // Build all (model, temperature) combinations
                var combos = (from m in models from t in temps select (Model: m, Temp: t)).ToList();

                bool useDcs = chkUseDcs.Checked;

                chartPanel.SetMode(useDcs ? BenchmarkChart.ChartMode.Dcs : BenchmarkChart.ChartMode.Qa);
                AppendLine($"=== Auto-Tune: {combos.Count} combinations × {qPerRun} questions each ===");
                AppendLine($"    Temperatures : {string.Join(", ", temps.Select(t => t.ToString("F2")))}");
                AppendLine($"    Models       : {string.Join(", ", models)}");
                AppendLine($"    Score metric : {cmbTuneScore.Text}");
                AppendLine("");

                double  bestScore  = double.MinValue;
                string? bestModel  = null;
                double  bestTemp   = 0;
                int comboNum = 0;
                int topK = (int)numTopK.Value;
                bool silentMode = chkSilentMode.Checked;

                foreach (var (model, temp) in combos)
                {
                    _tuneCts.Token.ThrowIfCancellationRequested();
                    comboNum++;

                    AppendLine($"  [{comboNum}/{combos.Count}] model={model}  temp={temp:F2}{(useDcs ? " [DCS]" : "")}");

                    try
                    {
                        if (useDcs)
                        {
                            // DCS / EE-RAG auto-tune path
                            var capturedCombo = comboNum;
                            int dcsCasesDone = 0;
                            double runF1 = 0, runP = 0, runR = 0, runCR = 0;

                            var dcsProgress = new Progress<BenchmarkProgressUpdate>(upd =>
                            {
                                if (upd.Total > 0)
                                {
                                    pbarRunProgress.Maximum = upd.Total;
                                    pbarRunProgress.Value   = Math.Min(upd.Done, upd.Total);
                                }
                                if (upd.Done > 0)
                                {
                                    lblLiveStats.Text = $"[Tune {capturedCombo}/{combos.Count}] " +
                                                        $"DCS: {upd.Done}/{upd.Total}  " +
                                                        $"F1={runF1:F1}%  P={runP:F1}%  R={runR:F1}%  CR={runCR:F1}%";
                                }
                            });

                            var dcsCaseProgress = new Progress<LocalRAG.Benchmarks.CaseResult>(cr =>
                            {
                                dcsCasesDone++;
                                int n = dcsCasesDone > 0 ? dcsCasesDone : 1;
                                runF1 = ((runF1 * (n - 1)) + cr.F1 * 100) / n;
                                runP  = ((runP  * (n - 1)) + cr.Precision * 100) / n;
                                runR  = ((runR  * (n - 1)) + cr.Recall * 100) / n;
                                runCR = ((runCR * (n - 1)) + cr.CandidateRecall * 100) / n;

                                chartPanel.AddPoint([runF1, runP, runR, runCR]);
                            });

                            var dcsReport = await RunDcsBenchmarkSilentAsync(
                                provider, model, temp, topK, silentMode,
                                dcsProgress, dcsCaseProgress, _tuneCts.Token);

                            if (dcsReport != null)
                            {
                                double score = CalculateDcsScore(dcsReport);
                                string flag = score > bestScore ? " ← new best" : "";
                                AppendLine($"         F1={dcsReport.MeanF1:P1}  P={dcsReport.MeanRetrievalPrecision:P1}  " +
                                           $"R={dcsReport.MeanRetrievalRecall:P1}  CandRec={dcsReport.MeanCandidateRecall:P1}  " +
                                           $"score={score:F2}{flag}");

                                if (score > bestScore)
                                {
                                    bestScore = score;
                                    bestModel = model;
                                    bestTemp  = temp;
                                }
                            }
                        }
                        else
                        {
                            // Original QA benchmark auto-tune path
                            var session = AiSessionBuilder
                                .WithProvider(provider)
                                .WithModel(model)
                                .WithTemperature(temp)
                                .WithMaxTokens(256)
                                .WithSystemMessage(
                                    "You are a factual Q&A assistant. Answer questions concisely and directly. " +
                                    "Give only the answer — do not explain or add qualifications unless they are part of the answer.")
                                .Build();

                            var opts = new QaBenchmarkOptions
                            {
                                MaxQuestions        = qPerRun,
                                Provider            = provider,
                                Model               = model,
                                Temperature         = temp,
                                UseRagContext       = chkUseRag.Checked,
                                SimilarContextCount = 3
                            };

                            // Silent progress — only update the live bar, no per-question log spam
                            var capturedCombo = comboNum; // capture by value so the lambda is stable
                            var progress = new Progress<BenchmarkProgressUpdate>(upd =>
                            {
                                if (upd.Total > 0)
                                {
                                    pbarRunProgress.Maximum = upd.Total;
                                    pbarRunProgress.Value   = Math.Min(upd.Done, upd.Total);
                                }
                                if (upd.Done > 0)
                                {
                                    lblLiveStats.Text = $"[Tune {capturedCombo}/{combos.Count}] " +
                                                        $"Q: {upd.Done}/{upd.Total}  " +
                                                        $"✓{upd.CorrectRate:F0}%  " +
                                                        $"?{upd.PossiblyCorrectRate:F0}%  " +
                                                        $"✗{upd.DefinitelyWrongRate:F0}%  " +
                                                        $"–{upd.IndeterminateRate:F0}%";

                                    chartPanel.AddPoint([
                                        upd.CorrectRate,
                                        upd.PossiblyCorrectRate,
                                        upd.DefinitelyWrongRate,
                                        upd.IndeterminateRate
                                    ]);
                                }
                            });

                            var report = await QaBenchmarkRunner.RunAsync(opts, _db, session, progress, _tuneCts.Token);
                            double score = CalculateScore(report);

                            string flag = score > bestScore ? " ← new best" : "";
                            AppendLine($"         ✓{report.CorrectRate:F1}%  ?{report.PossiblyCorrectRate:F1}%  " +
                                       $"✗{report.DefinitelyWrongRate:F1}%  –{report.IndeterminateRate:F1}%  " +
                                       $"score={score:F2}{flag}");

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestModel = model;
                                bestTemp  = temp;
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        AppendLine($"         ERROR: {ex.Message}");
                    }
                }

                AppendLine("");
                AppendLine($"=== Auto-Tune complete ===");
                if (bestModel != null)
                {
                    AppendLine($"    Best config  : model={bestModel}  temp={bestTemp:F2}  score={bestScore:F2}");
                    AppendLine($"    Apply best?  → set Model to '{bestModel}' and Temp to {bestTemp:F2}");

                    // Apply the best settings to the main run controls
                    txtModel.Text         = bestModel;
                    numTemperature.Value  = Clamp(0M, 2M, (decimal)bestTemp);
                }
            }
            catch (OperationCanceledException)
            {
                AppendLine("Auto-Tune cancelled.");
            }
            catch (Exception ex)
            {
                AppendLine($"Auto-Tune error: {ex.Message}");
            }
            finally
            {
                btnAutoTune.Enabled     = true;
                btnStopTune.Enabled     = false;
                btnRunBenchmark.Enabled = true;
                ResetLiveStats();
                _tuneCts?.Dispose();
                _tuneCts = null;
            }
        }

        private void btnStopTune_Click(object sender, EventArgs e)
        {
            _tuneCts?.Cancel();
            AppendLine("Auto-Tune: stopping after current run…");
            btnStopTune.Enabled = false;
        }

        private double CalculateScore(QaBenchmarkReport report)
        {
            return cmbTuneScore.SelectedIndex switch
            {
                1 => report.CorrectRate + report.PossiblyCorrectRate,
                2 => report.CorrectRate + 0.5 * report.PossiblyCorrectRate - 2.0 * report.DefinitelyWrongRate,
                _ => report.CorrectRate
            };
        }

        private double CalculateDcsScore(BenchmarkReport report)
        {
            // For DCS benchmarks, score by Mean F1 (the primary retrieval metric).
            // The tune score selector maps:
            //   0 → F1 only
            //   1 → F1 + Candidate Recall (retrieval breadth)
            //   2 → Composite: F1 + 0.5×CandidateRecall − penalty for missed retrievals
            return cmbTuneScore.SelectedIndex switch
            {
                1 => report.MeanF1 * 100 + report.MeanCandidateRecall * 100,
                2 => report.MeanF1 * 100
                     + 0.5 * report.MeanCandidateRecall * 100
                     - 2.0 * (1.0 - report.RetrievalRateActual) * 100,
                _ => report.MeanF1 * 100
            };
        }

        // ── Save report ───────────────────────────────────────────────────────

        private void btnSaveReport_Click(object sender, EventArgs e)
        {
            string? summary = null;
            string defaultName;

            if (_lastDcsReport != null)
            {
                summary = _lastDcsReport.FormatSummary();
                defaultName = $"dcs_benchmark_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            }
            else if (_lastReport != null)
            {
                summary = _lastReport.FormatSummary();
                defaultName = $"qa_benchmark_{_lastReport.RunId}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            }
            else
            {
                return;
            }

            using var dlg = new SaveFileDialog
            {
                Title    = "Save Benchmark Report",
                Filter   = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = defaultName
            };

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    File.WriteAllText(dlg.FileName, summary);
                    AppendLine($"Report saved to: {dlg.FileName}");
                }
                catch (Exception ex)
                {
                    AppendLine($"Save failed: {ex.Message}");
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AppendLine(string text)
        {
            if (InvokeRequired) { Invoke(() => AppendLine(text)); return; }
            rtbResults.AppendText(text + Environment.NewLine);
            rtbResults.ScrollToCaret();
        }

        private static decimal Clamp(decimal min, decimal max, decimal value) =>
            value < min ? min : value > max ? max : value;

        private static List<double> ParseDoubleList(string csv) =>
            csv.Split(',')
               .Select(s => s.Trim())
               .Where(s => !string.IsNullOrEmpty(s))
               .Select(s => double.TryParse(s, System.Globalization.NumberStyles.Any,
                                             System.Globalization.CultureInfo.InvariantCulture, out var v) ? (double?)v : null)
               .Where(v => v.HasValue)
               .Select(v => v!.Value)
               .ToList();

        private static List<string> ParseStringList(string csv) =>
            csv.Split(',')
               .Select(s => s.Trim())
               .Where(s => !string.IsNullOrEmpty(s))
               .ToList();

        private async void FormQaBenchmark_FormClosing(object sender, FormClosingEventArgs e)
        {
            _runCts?.Cancel();
            _tuneCts?.Cancel();
            _backfiller.Stop();
            await _backfiller.WaitAsync();
            _db?.Dispose();
        }
    }
}

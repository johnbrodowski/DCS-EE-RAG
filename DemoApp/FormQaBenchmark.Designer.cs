namespace DemoApp
{
    partial class FormQaBenchmark
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            grpImport = new GroupBox();
            lblFile = new Label();
            txtFilePath = new TextBox();
            btnBrowse = new Button();
            btnImport = new Button();
            lblImportStatus = new Label();
            grpBackfill = new GroupBox();
            lblEmbedStatus = new Label();
            pbarBackfill = new ProgressBar();
            btnStartBackfill = new Button();
            btnStopBackfill = new Button();
            grpRun = new GroupBox();
            lblProvider = new Label();
            cmbProvider = new ComboBox();
            lblModel = new Label();
            txtModel = new TextBox();
            lblTemperature = new Label();
            numTemperature = new NumericUpDown();
            lblQuestions = new Label();
            numQuestions = new NumericUpDown();
            lblEmbeddedCount = new Label();
            btnRunBenchmark = new Button();
            btnSaveReport = new Button();
            chkUseRag = new CheckBox();
            chkUseDcs = new CheckBox();
            lblTopK = new Label();
            numTopK = new NumericUpDown();
            chkSilentMode = new CheckBox();
            pbarRunProgress = new ProgressBar();
            lblLiveStats = new Label();
            grpThresholds = new GroupBox();
            lblCorrectThresh = new Label();
            numCorrectThresh = new NumericUpDown();
            lblPctCorrect = new Label();
            lblPossCorrectThresh = new Label();
            numPossCorrectThresh = new NumericUpDown();
            lblPctPossCorrect = new Label();
            lblDefWrongThresh = new Label();
            numDefWrongThresh = new NumericUpDown();
            lblPctDefWrong = new Label();
            lblIndetermThresh = new Label();
            numIndetermThresh = new NumericUpDown();
            lblPctIndeterm = new Label();
            btnSaveSettings = new Button();
            grpTuning = new GroupBox();
            lblTuneTemps = new Label();
            txtTuneTemps = new TextBox();
            lblTuneModels = new Label();
            txtTuneModels = new TextBox();
            lblTuneQuestions = new Label();
            numTuneQuestions = new NumericUpDown();
            lblTuneScore = new Label();
            cmbTuneScore = new ComboBox();
            btnAutoTune = new Button();
            btnStopTune = new Button();
            rtbResults = new RichTextBox();
            chartPanel = new BenchmarkChart();
            grpImport.SuspendLayout();
            grpBackfill.SuspendLayout();
            grpRun.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numTemperature).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numQuestions).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numTopK).BeginInit();
            grpThresholds.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numCorrectThresh).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numPossCorrectThresh).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numDefWrongThresh).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numIndetermThresh).BeginInit();
            grpTuning.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numTuneQuestions).BeginInit();
            SuspendLayout();
            // 
            // grpImport
            // 
            grpImport.Controls.Add(lblFile);
            grpImport.Controls.Add(txtFilePath);
            grpImport.Controls.Add(btnBrowse);
            grpImport.Controls.Add(btnImport);
            grpImport.Controls.Add(lblImportStatus);
            grpImport.Location = new Point(8, 8);
            grpImport.Name = "grpImport";
            grpImport.Size = new Size(755, 72);
            grpImport.TabIndex = 0;
            grpImport.TabStop = false;
            grpImport.Text = "Dataset Import";
            // 
            // lblFile
            // 
            lblFile.AutoSize = true;
            lblFile.Location = new Point(8, 26);
            lblFile.Name = "lblFile";
            lblFile.Size = new Size(28, 15);
            lblFile.TabIndex = 0;
            lblFile.Text = "File:";
            // 
            // txtFilePath
            // 
            txtFilePath.BackColor = SystemColors.Window;
            txtFilePath.Location = new Point(40, 22);
            txtFilePath.Name = "txtFilePath";
            txtFilePath.Size = new Size(480, 23);
            txtFilePath.TabIndex = 0;
            // 
            // btnBrowse
            // 
            btnBrowse.Location = new Point(543, 22);
            btnBrowse.Name = "btnBrowse";
            btnBrowse.Size = new Size(80, 25);
            btnBrowse.TabIndex = 1;
            btnBrowse.Text = "Browse…";
            btnBrowse.Click += btnBrowse_Click;
            // 
            // btnImport
            // 
            btnImport.Location = new Point(643, 22);
            btnImport.Name = "btnImport";
            btnImport.Size = new Size(96, 25);
            btnImport.TabIndex = 2;
            btnImport.Text = "Import";
            btnImport.Click += btnImport_Click;
            // 
            // lblImportStatus
            // 
            lblImportStatus.AutoSize = true;
            lblImportStatus.ForeColor = Color.Gray;
            lblImportStatus.Location = new Point(8, 48);
            lblImportStatus.Name = "lblImportStatus";
            lblImportStatus.Size = new Size(106, 15);
            lblImportStatus.TabIndex = 3;
            lblImportStatus.Text = "No dataset loaded.";
            // 
            // grpBackfill
            // 
            grpBackfill.Controls.Add(lblEmbedStatus);
            grpBackfill.Controls.Add(pbarBackfill);
            grpBackfill.Controls.Add(btnStartBackfill);
            grpBackfill.Controls.Add(btnStopBackfill);
            grpBackfill.Location = new Point(8, 88);
            grpBackfill.Name = "grpBackfill";
            grpBackfill.Size = new Size(755, 58);
            grpBackfill.TabIndex = 1;
            grpBackfill.TabStop = false;
            grpBackfill.Text = "Embedding Backfill";
            // 
            // lblEmbedStatus
            // 
            lblEmbedStatus.AutoSize = true;
            lblEmbedStatus.Location = new Point(8, 24);
            lblEmbedStatus.Name = "lblEmbedStatus";
            lblEmbedStatus.Size = new Size(93, 15);
            lblEmbedStatus.TabIndex = 0;
            lblEmbedStatus.Text = "Embedded: 0 / 0";
            // 
            // pbarBackfill
            // 
            pbarBackfill.Location = new Point(130, 22);
            pbarBackfill.Name = "pbarBackfill";
            pbarBackfill.Size = new Size(390, 20);
            pbarBackfill.TabIndex = 0;
            // 
            // btnStartBackfill
            // 
            btnStartBackfill.Location = new Point(531, 20);
            btnStartBackfill.Name = "btnStartBackfill";
            btnStartBackfill.Size = new Size(92, 25);
            btnStartBackfill.TabIndex = 1;
            btnStartBackfill.Text = "Start Backfill";
            btnStartBackfill.Click += btnStartBackfill_Click;
            // 
            // btnStopBackfill
            // 
            btnStopBackfill.Enabled = false;
            btnStopBackfill.Location = new Point(651, 19);
            btnStopBackfill.Name = "btnStopBackfill";
            btnStopBackfill.Size = new Size(88, 25);
            btnStopBackfill.TabIndex = 2;
            btnStopBackfill.Text = "Stop";
            btnStopBackfill.Click += btnStopBackfill_Click;
            // 
            // grpRun
            // 
            grpRun.Controls.Add(lblProvider);
            grpRun.Controls.Add(cmbProvider);
            grpRun.Controls.Add(lblModel);
            grpRun.Controls.Add(txtModel);
            grpRun.Controls.Add(lblTemperature);
            grpRun.Controls.Add(numTemperature);
            grpRun.Controls.Add(lblQuestions);
            grpRun.Controls.Add(numQuestions);
            grpRun.Controls.Add(lblEmbeddedCount);
            grpRun.Controls.Add(btnRunBenchmark);
            grpRun.Controls.Add(btnSaveReport);
            grpRun.Controls.Add(chkUseRag);
            grpRun.Controls.Add(chkUseDcs);
            grpRun.Controls.Add(lblTopK);
            grpRun.Controls.Add(numTopK);
            grpRun.Controls.Add(chkSilentMode);
            grpRun.Controls.Add(pbarRunProgress);
            grpRun.Controls.Add(lblLiveStats);
            grpRun.Location = new Point(8, 154);
            grpRun.Name = "grpRun";
            grpRun.Size = new Size(755, 124);
            grpRun.TabIndex = 2;
            grpRun.TabStop = false;
            grpRun.Text = "Run Benchmark";
            // 
            // lblProvider
            // 
            lblProvider.AutoSize = true;
            lblProvider.Location = new Point(8, 24);
            lblProvider.Name = "lblProvider";
            lblProvider.Size = new Size(54, 15);
            lblProvider.TabIndex = 0;
            lblProvider.Text = "Provider:";
            // 
            // cmbProvider
            // 
            cmbProvider.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbProvider.Items.AddRange(new object[] { "Anthropic", "OpenAI", "Groq", "DeepSeek", "Grok" });
            cmbProvider.Location = new Point(62, 20);
            cmbProvider.Name = "cmbProvider";
            cmbProvider.Size = new Size(100, 23);
            cmbProvider.TabIndex = 0;
            // 
            // lblModel
            // 
            lblModel.AutoSize = true;
            lblModel.Location = new Point(170, 24);
            lblModel.Name = "lblModel";
            lblModel.Size = new Size(44, 15);
            lblModel.TabIndex = 1;
            lblModel.Text = "Model:";
            // 
            // txtModel
            // 
            txtModel.Location = new Point(214, 20);
            txtModel.Name = "txtModel";
            txtModel.Size = new Size(161, 23);
            txtModel.TabIndex = 1;
            txtModel.Text = "claude-haiku-4-5-20251001";
            // 
            // lblTemperature
            // 
            lblTemperature.AutoSize = true;
            lblTemperature.Location = new Point(398, 20);
            lblTemperature.Name = "lblTemperature";
            lblTemperature.Size = new Size(40, 15);
            lblTemperature.TabIndex = 2;
            lblTemperature.Text = "Temp:";
            // 
            // numTemperature
            // 
            numTemperature.DecimalPlaces = 2;
            numTemperature.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            numTemperature.Location = new Point(444, 20);
            numTemperature.Maximum = new decimal(new int[] { 2, 0, 0, 0 });
            numTemperature.Name = "numTemperature";
            numTemperature.Size = new Size(58, 23);
            numTemperature.TabIndex = 2;
            numTemperature.Value = new decimal(new int[] { 7, 0, 0, 65536 });
            // 
            // lblQuestions
            // 
            lblQuestions.AutoSize = true;
            lblQuestions.Location = new Point(508, 22);
            lblQuestions.Name = "lblQuestions";
            lblQuestions.Size = new Size(63, 15);
            lblQuestions.TabIndex = 3;
            lblQuestions.Text = "Questions:";
            // 
            // numQuestions
            // 
            numQuestions.Location = new Point(569, 20);
            numQuestions.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            numQuestions.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numQuestions.Name = "numQuestions";
            numQuestions.Size = new Size(54, 23);
            numQuestions.TabIndex = 3;
            numQuestions.Value = new decimal(new int[] { 50, 0, 0, 0 });
            // 
            // lblEmbeddedCount
            // 
            lblEmbeddedCount.AutoSize = true;
            lblEmbeddedCount.ForeColor = Color.Gray;
            lblEmbeddedCount.Location = new Point(398, 51);
            lblEmbeddedCount.Name = "lblEmbeddedCount";
            lblEmbeddedCount.Size = new Size(81, 15);
            lblEmbeddedCount.TabIndex = 4;
            lblEmbeddedCount.Text = "(0 embedded)";
            // 
            // btnRunBenchmark
            // 
            btnRunBenchmark.Location = new Point(651, 14);
            btnRunBenchmark.Name = "btnRunBenchmark";
            btnRunBenchmark.Size = new Size(88, 25);
            btnRunBenchmark.TabIndex = 5;
            btnRunBenchmark.Text = "Run";
            btnRunBenchmark.Click += btnRunBenchmark_Click;
            // 
            // btnSaveReport
            // 
            btnSaveReport.Enabled = false;
            btnSaveReport.Location = new Point(651, 51);
            btnSaveReport.Name = "btnSaveReport";
            btnSaveReport.Size = new Size(88, 22);
            btnSaveReport.TabIndex = 6;
            btnSaveReport.Text = "Save Report…";
            btnSaveReport.Click += btnSaveReport_Click;
            // 
            // chkUseRag
            // 
            chkUseRag.AutoSize = true;
            chkUseRag.Checked = true;
            chkUseRag.CheckState = CheckState.Checked;
            chkUseRag.Location = new Point(8, 50);
            chkUseRag.Name = "chkUseRag";
            chkUseRag.Size = new Size(319, 19);
            chkUseRag.TabIndex = 4;
            chkUseRag.Text = "Inject similar Q&A pairs as context (requires embeddings)";
            // 
            // chkUseDcs
            // 
            chkUseDcs.AutoSize = true;
            chkUseDcs.Location = new Point(8, 74);
            chkUseDcs.Name = "chkUseDcs";
            chkUseDcs.Size = new Size(166, 19);
            chkUseDcs.TabIndex = 8;
            chkUseDcs.Text = "Use EE-RAG / DCS Pipeline";
            chkUseDcs.CheckedChanged += chkUseDcs_CheckedChanged;
            // 
            // lblTopK
            // 
            lblTopK.AutoSize = true;
            lblTopK.Enabled = false;
            lblTopK.Location = new Point(220, 76);
            lblTopK.Name = "lblTopK";
            lblTopK.Size = new Size(37, 15);
            lblTopK.TabIndex = 9;
            lblTopK.Text = "TopK:";
            // 
            // numTopK
            // 
            numTopK.Enabled = false;
            numTopK.Location = new Point(260, 72);
            numTopK.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            numTopK.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numTopK.Name = "numTopK";
            numTopK.Size = new Size(50, 23);
            numTopK.TabIndex = 9;
            numTopK.Value = new decimal(new int[] { 5, 0, 0, 0 });
            // 
            // chkSilentMode
            // 
            chkSilentMode.AutoSize = true;
            chkSilentMode.Enabled = false;
            chkSilentMode.Location = new Point(320, 74);
            chkSilentMode.Name = "chkSilentMode";
            chkSilentMode.Size = new Size(141, 19);
            chkSilentMode.TabIndex = 10;
            chkSilentMode.Text = "Silent digestion mode";
            // 
            // pbarRunProgress
            // 
            pbarRunProgress.Location = new Point(8, 100);
            pbarRunProgress.Name = "pbarRunProgress";
            pbarRunProgress.Size = new Size(470, 16);
            pbarRunProgress.TabIndex = 5;
            // 
            // lblLiveStats
            // 
            lblLiveStats.AutoSize = true;
            lblLiveStats.Font = new Font("Consolas", 8.25F);
            lblLiveStats.ForeColor = Color.DimGray;
            lblLiveStats.Location = new Point(486, 101);
            lblLiveStats.Name = "lblLiveStats";
            lblLiveStats.Size = new Size(192, 13);
            lblLiveStats.TabIndex = 7;
            lblLiveStats.Text = "Q: 0/0  ✓--%  ?--%  ✗--%  ---%";
            // 
            // grpThresholds
            // 
            grpThresholds.Controls.Add(lblCorrectThresh);
            grpThresholds.Controls.Add(numCorrectThresh);
            grpThresholds.Controls.Add(lblPctCorrect);
            grpThresholds.Controls.Add(lblPossCorrectThresh);
            grpThresholds.Controls.Add(numPossCorrectThresh);
            grpThresholds.Controls.Add(lblPctPossCorrect);
            grpThresholds.Controls.Add(lblDefWrongThresh);
            grpThresholds.Controls.Add(numDefWrongThresh);
            grpThresholds.Controls.Add(lblPctDefWrong);
            grpThresholds.Controls.Add(lblIndetermThresh);
            grpThresholds.Controls.Add(numIndetermThresh);
            grpThresholds.Controls.Add(lblPctIndeterm);
            grpThresholds.Controls.Add(btnSaveSettings);
            grpThresholds.Location = new Point(8, 286);
            grpThresholds.Name = "grpThresholds";
            grpThresholds.Size = new Size(755, 54);
            grpThresholds.TabIndex = 3;
            grpThresholds.TabStop = false;
            grpThresholds.Text = "Score Thresholds";
            // 
            // lblCorrectThresh
            // 
            lblCorrectThresh.AutoSize = true;
            lblCorrectThresh.Location = new Point(8, 24);
            lblCorrectThresh.Name = "lblCorrectThresh";
            lblCorrectThresh.Size = new Size(57, 15);
            lblCorrectThresh.TabIndex = 0;
            lblCorrectThresh.Text = "Correct ≥";
            // 
            // numCorrectThresh
            // 
            numCorrectThresh.DecimalPlaces = 1;
            numCorrectThresh.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            numCorrectThresh.Location = new Point(74, 20);
            numCorrectThresh.Name = "numCorrectThresh";
            numCorrectThresh.Size = new Size(52, 23);
            numCorrectThresh.TabIndex = 0;
            numCorrectThresh.Value = new decimal(new int[] { 50, 0, 0, 0 });
            // 
            // lblPctCorrect
            // 
            lblPctCorrect.AutoSize = true;
            lblPctCorrect.Location = new Point(128, 24);
            lblPctCorrect.Name = "lblPctCorrect";
            lblPctCorrect.Size = new Size(17, 15);
            lblPctCorrect.TabIndex = 1;
            lblPctCorrect.Text = "%";
            // 
            // lblPossCorrectThresh
            // 
            lblPossCorrectThresh.AutoSize = true;
            lblPossCorrectThresh.Location = new Point(148, 24);
            lblPossCorrectThresh.Name = "lblPossCorrectThresh";
            lblPossCorrectThresh.Size = new Size(87, 15);
            lblPossCorrectThresh.TabIndex = 2;
            lblPossCorrectThresh.Text = "Poss. Correct ≥";
            // 
            // numPossCorrectThresh
            // 
            numPossCorrectThresh.DecimalPlaces = 1;
            numPossCorrectThresh.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            numPossCorrectThresh.Location = new Point(248, 20);
            numPossCorrectThresh.Name = "numPossCorrectThresh";
            numPossCorrectThresh.Size = new Size(52, 23);
            numPossCorrectThresh.TabIndex = 1;
            numPossCorrectThresh.Value = new decimal(new int[] { 10, 0, 0, 0 });
            // 
            // lblPctPossCorrect
            // 
            lblPctPossCorrect.AutoSize = true;
            lblPctPossCorrect.Location = new Point(302, 24);
            lblPctPossCorrect.Name = "lblPctPossCorrect";
            lblPctPossCorrect.Size = new Size(17, 15);
            lblPctPossCorrect.TabIndex = 3;
            lblPctPossCorrect.Text = "%";
            // 
            // lblDefWrongThresh
            // 
            lblDefWrongThresh.AutoSize = true;
            lblDefWrongThresh.Location = new Point(322, 24);
            lblDefWrongThresh.Name = "lblDefWrongThresh";
            lblDefWrongThresh.Size = new Size(78, 15);
            lblDefWrongThresh.TabIndex = 4;
            lblDefWrongThresh.Text = "Def. Wrong ≤";
            // 
            // numDefWrongThresh
            // 
            numDefWrongThresh.DecimalPlaces = 1;
            numDefWrongThresh.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            numDefWrongThresh.Location = new Point(408, 20);
            numDefWrongThresh.Name = "numDefWrongThresh";
            numDefWrongThresh.Size = new Size(52, 23);
            numDefWrongThresh.TabIndex = 2;
            numDefWrongThresh.Value = new decimal(new int[] { 10, 0, 0, 0 });
            // 
            // lblPctDefWrong
            // 
            lblPctDefWrong.AutoSize = true;
            lblPctDefWrong.Location = new Point(462, 24);
            lblPctDefWrong.Name = "lblPctDefWrong";
            lblPctDefWrong.Size = new Size(17, 15);
            lblPctDefWrong.TabIndex = 5;
            lblPctDefWrong.Text = "%";
            // 
            // lblIndetermThresh
            // 
            lblIndetermThresh.AutoSize = true;
            lblIndetermThresh.Location = new Point(482, 24);
            lblIndetermThresh.Name = "lblIndetermThresh";
            lblIndetermThresh.Size = new Size(69, 15);
            lblIndetermThresh.TabIndex = 6;
            lblIndetermThresh.Text = "Indeterm. ≤";
            // 
            // numIndetermThresh
            // 
            numIndetermThresh.DecimalPlaces = 1;
            numIndetermThresh.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            numIndetermThresh.Location = new Point(562, 20);
            numIndetermThresh.Name = "numIndetermThresh";
            numIndetermThresh.Size = new Size(52, 23);
            numIndetermThresh.TabIndex = 3;
            numIndetermThresh.Value = new decimal(new int[] { 30, 0, 0, 0 });
            // 
            // lblPctIndeterm
            // 
            lblPctIndeterm.AutoSize = true;
            lblPctIndeterm.Location = new Point(616, 24);
            lblPctIndeterm.Name = "lblPctIndeterm";
            lblPctIndeterm.Size = new Size(17, 15);
            lblPctIndeterm.TabIndex = 7;
            lblPctIndeterm.Text = "%";
            // 
            // btnSaveSettings
            // 
            btnSaveSettings.Location = new Point(651, 18);
            btnSaveSettings.Name = "btnSaveSettings";
            btnSaveSettings.Size = new Size(92, 25);
            btnSaveSettings.TabIndex = 8;
            btnSaveSettings.Text = "Save Settings";
            btnSaveSettings.Click += btnSaveSettings_Click;
            // 
            // grpTuning
            // 
            grpTuning.Controls.Add(lblTuneTemps);
            grpTuning.Controls.Add(txtTuneTemps);
            grpTuning.Controls.Add(lblTuneModels);
            grpTuning.Controls.Add(txtTuneModels);
            grpTuning.Controls.Add(lblTuneQuestions);
            grpTuning.Controls.Add(numTuneQuestions);
            grpTuning.Controls.Add(lblTuneScore);
            grpTuning.Controls.Add(cmbTuneScore);
            grpTuning.Controls.Add(btnAutoTune);
            grpTuning.Controls.Add(btnStopTune);
            grpTuning.Location = new Point(8, 348);
            grpTuning.Name = "grpTuning";
            grpTuning.Size = new Size(755, 76);
            grpTuning.TabIndex = 4;
            grpTuning.TabStop = false;
            grpTuning.Text = "Auto-Tune";
            // 
            // lblTuneTemps
            // 
            lblTuneTemps.AutoSize = true;
            lblTuneTemps.Location = new Point(8, 24);
            lblTuneTemps.Name = "lblTuneTemps";
            lblTuneTemps.Size = new Size(82, 15);
            lblTuneTemps.TabIndex = 0;
            lblTuneTemps.Text = "Temperatures:";
            // 
            // txtTuneTemps
            // 
            txtTuneTemps.Location = new Point(92, 20);
            txtTuneTemps.Name = "txtTuneTemps";
            txtTuneTemps.Size = new Size(210, 23);
            txtTuneTemps.TabIndex = 0;
            txtTuneTemps.Text = "0.0, 0.3, 0.5, 0.7, 1.0, 1.5";
            // 
            // lblTuneModels
            // 
            lblTuneModels.AutoSize = true;
            lblTuneModels.Location = new Point(312, 24);
            lblTuneModels.Name = "lblTuneModels";
            lblTuneModels.Size = new Size(77, 15);
            lblTuneModels.TabIndex = 1;
            lblTuneModels.Text = "Models (csv):";
            // 
            // txtTuneModels
            // 
            txtTuneModels.Location = new Point(398, 20);
            txtTuneModels.Name = "txtTuneModels";
            txtTuneModels.PlaceholderText = "leave blank to use current model";
            txtTuneModels.Size = new Size(294, 23);
            txtTuneModels.TabIndex = 1;
            // 
            // lblTuneQuestions
            // 
            lblTuneQuestions.AutoSize = true;
            lblTuneQuestions.Location = new Point(8, 52);
            lblTuneQuestions.Name = "lblTuneQuestions";
            lblTuneQuestions.Size = new Size(47, 15);
            lblTuneQuestions.TabIndex = 2;
            lblTuneQuestions.Text = "Qs/run:";
            // 
            // numTuneQuestions
            // 
            numTuneQuestions.Location = new Point(58, 48);
            numTuneQuestions.Maximum = new decimal(new int[] { 500, 0, 0, 0 });
            numTuneQuestions.Minimum = new decimal(new int[] { 5, 0, 0, 0 });
            numTuneQuestions.Name = "numTuneQuestions";
            numTuneQuestions.Size = new Size(58, 23);
            numTuneQuestions.TabIndex = 2;
            numTuneQuestions.Value = new decimal(new int[] { 10, 0, 0, 0 });
            // 
            // lblTuneScore
            // 
            lblTuneScore.AutoSize = true;
            lblTuneScore.Location = new Point(126, 52);
            lblTuneScore.Name = "lblTuneScore";
            lblTuneScore.Size = new Size(55, 15);
            lblTuneScore.TabIndex = 3;
            lblTuneScore.Text = "Score by:";
            // 
            // cmbTuneScore
            // 
            cmbTuneScore.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbTuneScore.Items.AddRange(new object[] { "Correct %", "Correct + Possibly Correct %", "Composite (Correct + 0.5×Poss − 2×Wrong)" });
            cmbTuneScore.Location = new Point(186, 48);
            cmbTuneScore.Name = "cmbTuneScore";
            cmbTuneScore.Size = new Size(280, 23);
            cmbTuneScore.TabIndex = 3;
            // 
            // btnAutoTune
            // 
            btnAutoTune.Location = new Point(543, 48);
            btnAutoTune.Name = "btnAutoTune";
            btnAutoTune.Size = new Size(100, 25);
            btnAutoTune.TabIndex = 4;
            btnAutoTune.Text = "Auto-Tune";
            btnAutoTune.Click += btnAutoTune_Click;
            // 
            // btnStopTune
            // 
            btnStopTune.Enabled = false;
            btnStopTune.Location = new Point(657, 47);
            btnStopTune.Name = "btnStopTune";
            btnStopTune.Size = new Size(92, 25);
            btnStopTune.TabIndex = 5;
            btnStopTune.Text = "Stop";
            btnStopTune.Click += btnStopTune_Click;
            // 
            // rtbResults
            // 
            rtbResults.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            rtbResults.BackColor = Color.FromArgb(30, 30, 30);
            rtbResults.Font = new Font("Consolas", 9F);
            rtbResults.ForeColor = Color.LightGray;
            rtbResults.Location = new Point(8, 432);
            rtbResults.Name = "rtbResults";
            rtbResults.ReadOnly = true;
            rtbResults.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtbResults.Size = new Size(1213, 247);
            rtbResults.TabIndex = 5;
            rtbResults.Text = "";
            // 
            // chartPanel
            // 
            chartPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            chartPanel.BackColor = Color.FromArgb(30, 30, 30);
            chartPanel.Location = new Point(769, 8);
            chartPanel.Name = "chartPanel";
            chartPanel.Size = new Size(452, 416);
            chartPanel.TabIndex = 6;
            // 
            // FormQaBenchmark
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1228, 687);
            Controls.Add(chartPanel);
            Controls.Add(grpImport);
            Controls.Add(grpBackfill);
            Controls.Add(grpRun);
            Controls.Add(grpThresholds);
            Controls.Add(grpTuning);
            Controls.Add(rtbResults);
            MinimumSize = new Size(1244, 726);
            Name = "FormQaBenchmark";
            Text = "QA Dataset Benchmark";
            FormClosing += FormQaBenchmark_FormClosing;
            Load += FormQaBenchmark_Load;
            grpImport.ResumeLayout(false);
            grpImport.PerformLayout();
            grpBackfill.ResumeLayout(false);
            grpBackfill.PerformLayout();
            grpRun.ResumeLayout(false);
            grpRun.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numTemperature).EndInit();
            ((System.ComponentModel.ISupportInitialize)numQuestions).EndInit();
            ((System.ComponentModel.ISupportInitialize)numTopK).EndInit();
            grpThresholds.ResumeLayout(false);
            grpThresholds.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numCorrectThresh).EndInit();
            ((System.ComponentModel.ISupportInitialize)numPossCorrectThresh).EndInit();
            ((System.ComponentModel.ISupportInitialize)numDefWrongThresh).EndInit();
            ((System.ComponentModel.ISupportInitialize)numIndetermThresh).EndInit();
            grpTuning.ResumeLayout(false);
            grpTuning.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numTuneQuestions).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private GroupBox grpImport;
        private Label lblFile;
        private TextBox txtFilePath;
        private Button btnBrowse;
        private Button btnImport;
        private Label lblImportStatus;

        private GroupBox grpBackfill;
        private Label lblEmbedStatus;
        private ProgressBar pbarBackfill;
        private Button btnStartBackfill;
        private Button btnStopBackfill;

        private GroupBox grpRun;
        private Label lblProvider;
        private ComboBox cmbProvider;
        private Label lblModel;
        private TextBox txtModel;
        private Label lblTemperature;
        private NumericUpDown numTemperature;
        private Label lblQuestions;
        private NumericUpDown numQuestions;
        private Label lblEmbeddedCount;
        private Button btnRunBenchmark;
        private Button btnSaveReport;
        private CheckBox chkUseRag;
        private CheckBox chkUseDcs;
        private Label lblTopK;
        private NumericUpDown numTopK;
        private CheckBox chkSilentMode;
        private ProgressBar pbarRunProgress;
        private Label lblLiveStats;

        private GroupBox grpThresholds;
        private Label lblCorrectThresh;
        private NumericUpDown numCorrectThresh;
        private Label lblPctCorrect;
        private Label lblPossCorrectThresh;
        private NumericUpDown numPossCorrectThresh;
        private Label lblPctPossCorrect;
        private Label lblDefWrongThresh;
        private NumericUpDown numDefWrongThresh;
        private Label lblPctDefWrong;
        private Label lblIndetermThresh;
        private NumericUpDown numIndetermThresh;
        private Label lblPctIndeterm;
        private Button btnSaveSettings;

        private GroupBox grpTuning;
        private Label lblTuneTemps;
        private TextBox txtTuneTemps;
        private Label lblTuneModels;
        private TextBox txtTuneModels;
        private Label lblTuneQuestions;
        private NumericUpDown numTuneQuestions;
        private Label lblTuneScore;
        private ComboBox cmbTuneScore;
        private Button btnAutoTune;
        private Button btnStopTune;

        private RichTextBox rtbResults;
        private BenchmarkChart chartPanel;
    }
}

namespace DemoApp
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            btnSearch = new Button();
            txtResult = new TextBox();
            txtQuery = new TextBox();
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            ragToolStripMenuItem = new ToolStripMenuItem();
            backfillToolStripMenuItem = new ToolStripMenuItem();
            missingToolStripMenuItem = new ToolStripMenuItem();
            allToolStripMenuItem = new ToolStripMenuItem();
            generateMockDataToolStripMenuItem = new ToolStripMenuItem();
            testToolStripMenuItem = new ToolStripMenuItem();
            runTestsToolStripMenuItem = new ToolStripMenuItem();
            benchmarkToolStripMenuItem = new ToolStripMenuItem();
            qaDatasetBenchmarkToolStripMenuItem = new ToolStripMenuItem();
            lblProvider = new Label();
            cmbProvider = new ComboBox();
            lblModel = new Label();
            txtModel = new TextBox();
            chkSilentMode = new CheckBox();
            btnAskAI = new Button();
            menuStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // btnSearch
            // 
            btnSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSearch.Location = new Point(672, 26);
            btnSearch.Name = "btnSearch";
            btnSearch.Size = new Size(75, 26);
            btnSearch.TabIndex = 0;
            btnSearch.Text = "Search";
            btnSearch.UseVisualStyleBackColor = true;
            btnSearch.Click += btnSearch_Click;
            // 
            // txtResult
            // 
            txtResult.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtResult.BackColor = SystemColors.Window;
            txtResult.Location = new Point(5, 92);
            txtResult.Multiline = true;
            txtResult.Name = "txtResult";
            txtResult.ScrollBars = ScrollBars.Vertical;
            txtResult.Size = new Size(742, 153);
            txtResult.TabIndex = 1;
            // 
            // txtQuery
            // 
            txtQuery.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtQuery.BackColor = SystemColors.Window;
            txtQuery.Font = new Font("Segoe UI", 10F);
            txtQuery.Location = new Point(6, 28);
            txtQuery.Name = "txtQuery";
            txtQuery.Size = new Size(663, 25);
            txtQuery.TabIndex = 2;
            txtQuery.Text = "Simple query that might have some matches in the rag database.";
            // 
            // menuStrip1
            // 
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, ragToolStripMenuItem, testToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(752, 24);
            menuStrip1.TabIndex = 3;
            menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(37, 20);
            fileToolStripMenuItem.Text = "File";
            // 
            // ragToolStripMenuItem
            // 
            ragToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { backfillToolStripMenuItem, generateMockDataToolStripMenuItem });
            ragToolStripMenuItem.Name = "ragToolStripMenuItem";
            ragToolStripMenuItem.Size = new Size(39, 20);
            ragToolStripMenuItem.Text = "Rag";
            // 
            // backfillToolStripMenuItem
            // 
            backfillToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { missingToolStripMenuItem, allToolStripMenuItem });
            backfillToolStripMenuItem.Name = "backfillToolStripMenuItem";
            backfillToolStripMenuItem.Size = new Size(181, 22);
            backfillToolStripMenuItem.Text = "Backfill";
            // 
            // missingToolStripMenuItem
            // 
            missingToolStripMenuItem.Name = "missingToolStripMenuItem";
            missingToolStripMenuItem.Size = new Size(115, 22);
            missingToolStripMenuItem.Text = "Missing";
            missingToolStripMenuItem.Click += missingToolStripMenuItem_Click;
            // 
            // allToolStripMenuItem
            // 
            allToolStripMenuItem.Name = "allToolStripMenuItem";
            allToolStripMenuItem.Size = new Size(115, 22);
            allToolStripMenuItem.Text = "All";
            allToolStripMenuItem.Click += allToolStripMenuItem_Click;
            // 
            // generateMockDataToolStripMenuItem
            // 
            generateMockDataToolStripMenuItem.Name = "generateMockDataToolStripMenuItem";
            generateMockDataToolStripMenuItem.Size = new Size(181, 22);
            generateMockDataToolStripMenuItem.Text = "Generate Mock Data";
            generateMockDataToolStripMenuItem.Click += generateMockDataToolStripMenuItem_Click;
            // 
            // testToolStripMenuItem
            // 
            testToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { runTestsToolStripMenuItem, benchmarkToolStripMenuItem, qaDatasetBenchmarkToolStripMenuItem });
            testToolStripMenuItem.Name = "testToolStripMenuItem";
            testToolStripMenuItem.Size = new Size(40, 20);
            testToolStripMenuItem.Text = "Test";
            // 
            // runTestsToolStripMenuItem
            // 
            runTestsToolStripMenuItem.Name = "runTestsToolStripMenuItem";
            runTestsToolStripMenuItem.Size = new Size(205, 22);
            runTestsToolStripMenuItem.Text = "Run Tests";
            runTestsToolStripMenuItem.Click += runTestsToolStripMenuItem_Click;
            // 
            // benchmarkToolStripMenuItem
            // 
            benchmarkToolStripMenuItem.Name = "benchmarkToolStripMenuItem";
            benchmarkToolStripMenuItem.Size = new Size(205, 22);
            benchmarkToolStripMenuItem.Text = "Run Benchmark";
            benchmarkToolStripMenuItem.Click += benchmarkToolStripMenuItem_Click;
            // 
            // qaDatasetBenchmarkToolStripMenuItem
            // 
            qaDatasetBenchmarkToolStripMenuItem.Name = "qaDatasetBenchmarkToolStripMenuItem";
            qaDatasetBenchmarkToolStripMenuItem.Size = new Size(205, 22);
            qaDatasetBenchmarkToolStripMenuItem.Text = "QA Dataset Benchmark…";
            qaDatasetBenchmarkToolStripMenuItem.Click += qaDatasetBenchmarkToolStripMenuItem_Click;
            // 
            // lblProvider
            // 
            lblProvider.AutoSize = true;
            lblProvider.Location = new Point(6, 63);
            lblProvider.Name = "lblProvider";
            lblProvider.Size = new Size(54, 15);
            lblProvider.TabIndex = 8;
            lblProvider.Text = "Provider:";
            // 
            // cmbProvider
            // 
            cmbProvider.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbProvider.Items.AddRange(new object[] { "Anthropic", "OpenAI", "Groq", "DeepSeek", "Grok" });
            cmbProvider.Location = new Point(62, 59);
            cmbProvider.Name = "cmbProvider";
            cmbProvider.Size = new Size(120, 23);
            cmbProvider.TabIndex = 4;
            // 
            // lblModel
            // 
            lblModel.AutoSize = true;
            lblModel.Location = new Point(189, 63);
            lblModel.Name = "lblModel";
            lblModel.Size = new Size(44, 15);
            lblModel.TabIndex = 9;
            lblModel.Text = "Model:";
            // 
            // txtModel
            // 
            txtModel.BackColor = SystemColors.Window;
            txtModel.Location = new Point(232, 59);
            txtModel.Name = "txtModel";
            txtModel.Size = new Size(220, 23);
            txtModel.TabIndex = 5;
            txtModel.Text = "claude-haiku-4-5";
            // 
            // chkSilentMode
            // 
            chkSilentMode.AutoSize = true;
            chkSilentMode.Location = new Point(462, 62);
            chkSilentMode.Name = "chkSilentMode";
            chkSilentMode.Size = new Size(108, 19);
            chkSilentMode.TabIndex = 6;
            chkSilentMode.Text = "Silent Digestion";
            chkSilentMode.UseVisualStyleBackColor = true;
            // 
            // btnAskAI
            // 
            btnAskAI.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnAskAI.Location = new Point(605, 58);
            btnAskAI.Name = "btnAskAI";
            btnAskAI.Size = new Size(142, 26);
            btnAskAI.TabIndex = 7;
            btnAskAI.Text = "Ask AI (EE-RAG)";
            btnAskAI.UseVisualStyleBackColor = true;
            btnAskAI.Click += btnAskAI_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.Control;
            ClientSize = new Size(752, 254);
            Controls.Add(btnAskAI);
            Controls.Add(chkSilentMode);
            Controls.Add(txtModel);
            Controls.Add(lblModel);
            Controls.Add(cmbProvider);
            Controls.Add(lblProvider);
            Controls.Add(txtQuery);
            Controls.Add(txtResult);
            Controls.Add(btnSearch);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "Form1";
            Text = "LocalRAG — EE-RAG Demo";
            Load += Form1_Load;
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button btnSearch;
        private TextBox txtResult;
        private TextBox txtQuery;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem ragToolStripMenuItem;
        private ToolStripMenuItem backfillToolStripMenuItem;
        private ToolStripMenuItem missingToolStripMenuItem;
        private ToolStripMenuItem allToolStripMenuItem;
        private ToolStripMenuItem generateMockDataToolStripMenuItem;
        private ToolStripMenuItem testToolStripMenuItem;
        private ToolStripMenuItem runTestsToolStripMenuItem;
        private ToolStripMenuItem benchmarkToolStripMenuItem;
        private ToolStripMenuItem qaDatasetBenchmarkToolStripMenuItem;
        private Label lblProvider;
        private ComboBox cmbProvider;
        private Label lblModel;
        private TextBox txtModel;
        private CheckBox chkSilentMode;
        private Button btnAskAI;
    }
}

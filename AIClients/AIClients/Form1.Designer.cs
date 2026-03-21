namespace AIClients
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        private ComboBox cboProvider;
        private TextBox txtModel;
        private TextBox txtSystemMessage;
        private TextBox txtPrompt;
        private RichTextBox rtbOutput;
        private Button btnCreateSession;
        private Button btnSend;
        private Button btnCancel;
        private Label lblProvider;
        private Label lblModel;
        private Label lblSystemMessage;
        private Label lblPrompt;

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
            cboProvider = new ComboBox();
            txtModel = new TextBox();
            txtSystemMessage = new TextBox();
            txtPrompt = new TextBox();
            rtbOutput = new RichTextBox();
            btnCreateSession = new Button();
            btnSend = new Button();
            btnCancel = new Button();
            lblProvider = new Label();
            lblModel = new Label();
            lblSystemMessage = new Label();
            lblPrompt = new Label();
            SuspendLayout();
            //
            // cboProvider
            //
            cboProvider.DropDownStyle = ComboBoxStyle.DropDownList;
            cboProvider.FormattingEnabled = true;
            cboProvider.Location = new Point(97, 14);
            cboProvider.Name = "cboProvider";
            cboProvider.Size = new Size(194, 28);
            cboProvider.TabIndex = 0;
            //
            // txtModel
            //
            txtModel.Location = new Point(372, 14);
            txtModel.Name = "txtModel";
            txtModel.Size = new Size(207, 27);
            txtModel.TabIndex = 1;
            //
            // txtSystemMessage
            //
            txtSystemMessage.Location = new Point(97, 50);
            txtSystemMessage.Name = "txtSystemMessage";
            txtSystemMessage.Size = new Size(579, 27);
            txtSystemMessage.TabIndex = 2;
            //
            // txtPrompt
            //
            txtPrompt.Location = new Point(97, 93);
            txtPrompt.Name = "txtPrompt";
            txtPrompt.Size = new Size(482, 27);
            txtPrompt.TabIndex = 3;
            //
            // rtbOutput
            //
            rtbOutput.Location = new Point(14, 131);
            rtbOutput.Name = "rtbOutput";
            rtbOutput.Size = new Size(774, 337);
            rtbOutput.TabIndex = 4;
            rtbOutput.Text = "";
            //
            // btnCreateSession
            //
            btnCreateSession.Location = new Point(598, 13);
            btnCreateSession.Name = "btnCreateSession";
            btnCreateSession.Size = new Size(90, 29);
            btnCreateSession.TabIndex = 5;
            btnCreateSession.Text = "Create";
            btnCreateSession.UseVisualStyleBackColor = true;
            btnCreateSession.Click += btnCreateSession_Click;
            //
            // btnSend
            //
            btnSend.Location = new Point(598, 92);
            btnSend.Name = "btnSend";
            btnSend.Size = new Size(90, 29);
            btnSend.TabIndex = 6;
            btnSend.Text = "Send";
            btnSend.UseVisualStyleBackColor = true;
            btnSend.Click += btnSend_Click;
            //
            // btnCancel
            //
            btnCancel.Location = new Point(698, 92);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(90, 29);
            btnCancel.TabIndex = 7;
            btnCancel.Text = "Cancel";
            btnCancel.UseVisualStyleBackColor = true;
            btnCancel.Click += btnCancel_Click;
            //
            // lblProvider
            //
            lblProvider.AutoSize = true;
            lblProvider.Location = new Point(14, 17);
            lblProvider.Name = "lblProvider";
            lblProvider.Size = new Size(66, 20);
            lblProvider.TabIndex = 8;
            lblProvider.Text = "Provider";
            //
            // lblModel
            //
            lblModel.AutoSize = true;
            lblModel.Location = new Point(309, 17);
            lblModel.Name = "lblModel";
            lblModel.Size = new Size(50, 20);
            lblModel.TabIndex = 9;
            lblModel.Text = "Model";
            //
            // lblSystemMessage
            //
            lblSystemMessage.AutoSize = true;
            lblSystemMessage.Location = new Point(14, 53);
            lblSystemMessage.Name = "lblSystemMessage";
            lblSystemMessage.Size = new Size(57, 20);
            lblSystemMessage.TabIndex = 10;
            lblSystemMessage.Text = "System";
            //
            // lblPrompt
            //
            lblPrompt.AutoSize = true;
            lblPrompt.Location = new Point(14, 96);
            lblPrompt.Name = "lblPrompt";
            lblPrompt.Size = new Size(56, 20);
            lblPrompt.TabIndex = 11;
            lblPrompt.Text = "Prompt";
            //
            // Form1
            //
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 480);
            Controls.Add(lblPrompt);
            Controls.Add(lblSystemMessage);
            Controls.Add(lblModel);
            Controls.Add(lblProvider);
            Controls.Add(btnCancel);
            Controls.Add(btnSend);
            Controls.Add(btnCreateSession);
            Controls.Add(rtbOutput);
            Controls.Add(txtPrompt);
            Controls.Add(txtSystemMessage);
            Controls.Add(txtModel);
            Controls.Add(cboProvider);
            Name = "Form1";
            Text = "AI Clients Test Harness";
            Load += Form1_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
    }
}

namespace Schnapp.Client.Windows.Forms
{
    partial class ScreenshotEditForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.ActivePanel = new System.Windows.Forms.Panel();
            this.ActiveTimeText = new System.Windows.Forms.Label();
            this.ActiveTargetText = new System.Windows.Forms.Label();
            this.TagsBox = new Schnapp.Client.Windows.Forms.TagsInputControl();
            this.NotesBox = new System.Windows.Forms.TextBox();
            this.ApplyButton = new System.Windows.Forms.Button();
            this.ClearButton = new System.Windows.Forms.Button();
            this.CloseButton = new System.Windows.Forms.Button();
            this.ToolbarPanel = new System.Windows.Forms.Panel();
            this.RefreshBtn = new System.Windows.Forms.Button();
            this.StatusBar = new System.Windows.Forms.Label();
            this.CustomerLabel = new System.Windows.Forms.Label();
            this.ProjectLabel = new System.Windows.Forms.Label();
            this.TaskLabel = new System.Windows.Forms.Label();
            this.RecentLabel = new System.Windows.Forms.Label();
            this.TagsLabel = new System.Windows.Forms.Label();
            this.NotesLabel = new System.Windows.Forms.Label();
            this.TaskBox = new Schnapp.Client.Windows.Forms.ContainsFilterComboBox();
            this.ProjectBox = new Schnapp.Client.Windows.Forms.ContainsFilterComboBox();
            this.CustomerBox = new Schnapp.Client.Windows.Forms.ContainsFilterComboBox();
            this.MruBox = new Schnapp.Client.Windows.Forms.ContainsFilterComboBox();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.ActivePanel.SuspendLayout();
            this.ToolbarPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // ActivePanel
            // 
            this.ActivePanel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ActivePanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(58)))), ((int)(((byte)(95)))));
            this.ActivePanel.Controls.Add(this.ActiveTimeText);
            this.ActivePanel.Controls.Add(this.ActiveTargetText);
            this.ActivePanel.Location = new System.Drawing.Point(10, 10);
            this.ActivePanel.Name = "ActivePanel";
            this.ActivePanel.Padding = new System.Windows.Forms.Padding(8);
            this.ActivePanel.Size = new System.Drawing.Size(341, 56);
            this.ActivePanel.TabIndex = 0;
            // 
            // ActiveTimeText
            // 
            this.ActiveTimeText.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ActiveTimeText.ForeColor = System.Drawing.Color.Gainsboro;
            this.ActiveTimeText.Location = new System.Drawing.Point(8, 32);
            this.ActiveTimeText.Name = "ActiveTimeText";
            this.ActiveTimeText.Size = new System.Drawing.Size(325, 16);
            this.ActiveTimeText.TabIndex = 1;
            this.ActiveTimeText.Text = "—";
            // 
            // ActiveTargetText
            // 
            this.ActiveTargetText.Dock = System.Windows.Forms.DockStyle.Top;
            this.ActiveTargetText.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.ActiveTargetText.ForeColor = System.Drawing.Color.White;
            this.ActiveTargetText.Location = new System.Drawing.Point(8, 8);
            this.ActiveTargetText.Name = "ActiveTargetText";
            this.ActiveTargetText.Size = new System.Drawing.Size(325, 22);
            this.ActiveTargetText.TabIndex = 0;
            this.ActiveTargetText.Text = "Editing screenshots";
            // 
            // TagsBox
            // 
            this.TagsBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TagsBox.Location = new System.Drawing.Point(87, 196);
            this.TagsBox.Name = "TagsBox";
            this.TagsBox.Size = new System.Drawing.Size(264, 22);
            this.TagsBox.TabIndex = 10;
            // 
            // NotesBox
            // 
            this.NotesBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NotesBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.NotesBox.Location = new System.Drawing.Point(87, 224);
            this.NotesBox.Multiline = true;
            this.NotesBox.Name = "NotesBox";
            this.NotesBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.NotesBox.Size = new System.Drawing.Size(264, 89);
            this.NotesBox.TabIndex = 12;
            this.NotesBox.WordWrap = false;
            // 
            // ApplyButton
            // 
            this.ApplyButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.ApplyButton.Location = new System.Drawing.Point(277, 323);
            this.ApplyButton.Name = "ApplyButton";
            this.ApplyButton.Size = new System.Drawing.Size(74, 28);
            this.ApplyButton.TabIndex = 14;
            this.ApplyButton.Text = "Apply";
            this.toolTip1.SetToolTip(this.ApplyButton, "Save customer/project/task, notes, and tags on the selected screenshots");
            this.ApplyButton.UseVisualStyleBackColor = true;
            this.ApplyButton.Click += new System.EventHandler(this.ApplyButton_Click);
            // 
            // ClearButton
            // 
            this.ClearButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.ClearButton.Location = new System.Drawing.Point(197, 323);
            this.ClearButton.Name = "ClearButton";
            this.ClearButton.Size = new System.Drawing.Size(74, 28);
            this.ClearButton.TabIndex = 13;
            this.ClearButton.Text = "Clear";
            this.toolTip1.SetToolTip(this.ClearButton, "Clear journal link, notes, and tags on the selected screenshots");
            this.ClearButton.UseVisualStyleBackColor = true;
            this.ClearButton.Click += new System.EventHandler(this.ClearButton_Click);
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.CloseButton.Location = new System.Drawing.Point(10, 323);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(74, 28);
            this.CloseButton.TabIndex = 15;
            this.CloseButton.Text = "Close";
            this.CloseButton.UseVisualStyleBackColor = true;
            this.CloseButton.Click += new System.EventHandler(this.CloseButton_Click);
            // 
            // ToolbarPanel
            // 
            this.ToolbarPanel.Controls.Add(this.RefreshBtn);
            this.ToolbarPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ToolbarPanel.Location = new System.Drawing.Point(0, 359);
            this.ToolbarPanel.Name = "ToolbarPanel";
            this.ToolbarPanel.Size = new System.Drawing.Size(361, 40);
            this.ToolbarPanel.TabIndex = 16;
            // 
            // RefreshBtn
            // 
            this.RefreshBtn.Location = new System.Drawing.Point(8, 6);
            this.RefreshBtn.Name = "RefreshBtn";
            this.RefreshBtn.Size = new System.Drawing.Size(36, 28);
            this.RefreshBtn.TabIndex = 0;
            this.RefreshBtn.Text = "↻";
            this.toolTip1.SetToolTip(this.RefreshBtn, "Refresh lists from server");
            this.RefreshBtn.UseVisualStyleBackColor = true;
            this.RefreshBtn.Click += new System.EventHandler(this.RefreshButton_Click);
            // 
            // StatusBar
            // 
            this.StatusBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.StatusBar.Location = new System.Drawing.Point(0, 399);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Padding = new System.Windows.Forms.Padding(6, 4, 6, 4);
            this.StatusBar.Size = new System.Drawing.Size(361, 28);
            this.StatusBar.TabIndex = 17;
            this.StatusBar.Text = "Ready";
            // 
            // CustomerLabel
            // 
            this.CustomerLabel.AutoSize = true;
            this.CustomerLabel.Location = new System.Drawing.Point(12, 111);
            this.CustomerLabel.Name = "CustomerLabel";
            this.CustomerLabel.Size = new System.Drawing.Size(54, 13);
            this.CustomerLabel.TabIndex = 3;
            this.CustomerLabel.Text = "Customer:";
            // 
            // ProjectLabel
            // 
            this.ProjectLabel.AutoSize = true;
            this.ProjectLabel.Location = new System.Drawing.Point(12, 139);
            this.ProjectLabel.Name = "ProjectLabel";
            this.ProjectLabel.Size = new System.Drawing.Size(43, 13);
            this.ProjectLabel.TabIndex = 5;
            this.ProjectLabel.Text = "Project:";
            // 
            // TaskLabel
            // 
            this.TaskLabel.AutoSize = true;
            this.TaskLabel.Location = new System.Drawing.Point(12, 167);
            this.TaskLabel.Name = "TaskLabel";
            this.TaskLabel.Size = new System.Drawing.Size(69, 13);
            this.TaskLabel.TabIndex = 7;
            this.TaskLabel.Text = "Task/Ticket:";
            // 
            // RecentLabel
            // 
            this.RecentLabel.AutoSize = true;
            this.RecentLabel.Location = new System.Drawing.Point(12, 79);
            this.RecentLabel.Name = "RecentLabel";
            this.RecentLabel.Size = new System.Drawing.Size(45, 13);
            this.RecentLabel.TabIndex = 1;
            this.RecentLabel.Text = "Recent:";
            // 
            // TagsLabel
            // 
            this.TagsLabel.AutoSize = true;
            this.TagsLabel.Location = new System.Drawing.Point(12, 199);
            this.TagsLabel.Name = "TagsLabel";
            this.TagsLabel.Size = new System.Drawing.Size(34, 13);
            this.TagsLabel.TabIndex = 9;
            this.TagsLabel.Text = "Tags:";
            // 
            // NotesLabel
            // 
            this.NotesLabel.AutoSize = true;
            this.NotesLabel.Location = new System.Drawing.Point(12, 227);
            this.NotesLabel.Name = "NotesLabel";
            this.NotesLabel.Size = new System.Drawing.Size(38, 13);
            this.NotesLabel.TabIndex = 11;
            this.NotesLabel.Text = "Notes:";
            // 
            // TaskBox
            // 
            this.TaskBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TaskBox.FormattingEnabled = true;
            this.TaskBox.Location = new System.Drawing.Point(87, 164);
            this.TaskBox.Name = "TaskBox";
            this.TaskBox.SafeText = "";
            this.TaskBox.Size = new System.Drawing.Size(264, 21);
            this.TaskBox.TabIndex = 8;
            // 
            // ProjectBox
            // 
            this.ProjectBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ProjectBox.FormattingEnabled = true;
            this.ProjectBox.Location = new System.Drawing.Point(87, 136);
            this.ProjectBox.Name = "ProjectBox";
            this.ProjectBox.SafeText = "";
            this.ProjectBox.Size = new System.Drawing.Size(264, 21);
            this.ProjectBox.TabIndex = 6;
            this.ProjectBox.SelectionChangeCommitted += new System.EventHandler(this.ProjectBox_SelectionChangeCommitted);
            this.ProjectBox.Leave += new System.EventHandler(this.ProjectBox_Leave);
            // 
            // CustomerBox
            // 
            this.CustomerBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CustomerBox.FormattingEnabled = true;
            this.CustomerBox.Location = new System.Drawing.Point(87, 108);
            this.CustomerBox.Name = "CustomerBox";
            this.CustomerBox.SafeText = "";
            this.CustomerBox.Size = new System.Drawing.Size(264, 21);
            this.CustomerBox.TabIndex = 4;
            this.CustomerBox.SelectionChangeCommitted += new System.EventHandler(this.CustomerBox_SelectionChangeCommitted);
            this.CustomerBox.Leave += new System.EventHandler(this.CustomerBox_Leave);
            // 
            // MruBox
            // 
            this.MruBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.MruBox.FormattingEnabled = true;
            this.MruBox.Location = new System.Drawing.Point(87, 76);
            this.MruBox.Name = "MruBox";
            this.MruBox.SafeText = "";
            this.MruBox.Size = new System.Drawing.Size(264, 21);
            this.MruBox.TabIndex = 2;
            this.MruBox.SelectionChangeCommitted += new System.EventHandler(this.MruBox_SelectionChangeCommitted);
            // 
            // ScreenshotEditForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(361, 427);
            this.Controls.Add(this.NotesLabel);
            this.Controls.Add(this.TagsLabel);
            this.Controls.Add(this.RecentLabel);
            this.Controls.Add(this.TaskLabel);
            this.Controls.Add(this.ProjectLabel);
            this.Controls.Add(this.CustomerLabel);
            this.Controls.Add(this.ToolbarPanel);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.CloseButton);
            this.Controls.Add(this.ClearButton);
            this.Controls.Add(this.ApplyButton);
            this.Controls.Add(this.NotesBox);
            this.Controls.Add(this.TagsBox);
            this.Controls.Add(this.TaskBox);
            this.Controls.Add(this.ProjectBox);
            this.Controls.Add(this.CustomerBox);
            this.Controls.Add(this.MruBox);
            this.Controls.Add(this.ActivePanel);
            this.MinimumSize = new System.Drawing.Size(260, 360);
            this.Name = "ScreenshotEditForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Schnapp — Edit screenshots";
            this.ActivePanel.ResumeLayout(false);
            this.ToolbarPanel.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Panel ActivePanel;
        private System.Windows.Forms.Label ActiveTimeText;
        private System.Windows.Forms.Label ActiveTargetText;
        private ContainsFilterComboBox MruBox;
        private ContainsFilterComboBox CustomerBox;
        private ContainsFilterComboBox ProjectBox;
        private ContainsFilterComboBox TaskBox;
        private Schnapp.Client.Windows.Forms.TagsInputControl TagsBox;
        private System.Windows.Forms.TextBox NotesBox;
        private System.Windows.Forms.Button ApplyButton;
        private System.Windows.Forms.Button ClearButton;
        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.Panel ToolbarPanel;
        private System.Windows.Forms.Button RefreshBtn;
        private System.Windows.Forms.Label StatusBar;
        private System.Windows.Forms.Label CustomerLabel;
        private System.Windows.Forms.Label ProjectLabel;
        private System.Windows.Forms.Label TaskLabel;
        private System.Windows.Forms.Label RecentLabel;
        private System.Windows.Forms.Label TagsLabel;
        private System.Windows.Forms.Label NotesLabel;
        private System.Windows.Forms.ToolTip toolTip1;
    }
}

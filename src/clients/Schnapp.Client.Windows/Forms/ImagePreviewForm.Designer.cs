namespace Schnapp.Client.Windows.Forms
{
    partial class ImagePreviewForm
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
            this.ToolbarPanel = new System.Windows.Forms.Panel();
            this.CloseButton = new System.Windows.Forms.Button();
            this.ZoomLabel = new System.Windows.Forms.Label();
            this.ZoomFitButton = new System.Windows.Forms.Button();
            this.ZoomActualButton = new System.Windows.Forms.Button();
            this.ZoomOutButton = new System.Windows.Forms.Button();
            this.ZoomInButton = new System.Windows.Forms.Button();
            this.InfoLabel = new System.Windows.Forms.Label();
            this.JournalPanel = new System.Windows.Forms.Panel();
            this.EditorStatusLabel = new System.Windows.Forms.Label();
            this.ApplyButton = new System.Windows.Forms.Button();
            this.ClearButton = new System.Windows.Forms.Button();
            this.RefreshListsButton = new System.Windows.Forms.Button();
            this.NotesBox = new System.Windows.Forms.TextBox();
            this.TagsBox = new Schnapp.Client.Windows.Forms.TagsInputControl();
            this.TaskBox = new Schnapp.Client.Windows.Forms.ContainsFilterComboBox();
            this.ProjectBox = new Schnapp.Client.Windows.Forms.ContainsFilterComboBox();
            this.CustomerBox = new Schnapp.Client.Windows.Forms.ContainsFilterComboBox();
            this.MruBox = new Schnapp.Client.Windows.Forms.ContainsFilterComboBox();
            this.NotesLabel = new System.Windows.Forms.Label();
            this.TagsLabel = new System.Windows.Forms.Label();
            this.TaskLabel = new System.Windows.Forms.Label();
            this.ProjectLabel = new System.Windows.Forms.Label();
            this.CustomerLabel = new System.Windows.Forms.Label();
            this.RecentLabel = new System.Windows.Forms.Label();
            this.JournalTitleLabel = new System.Windows.Forms.Label();
            this.Scroller = new System.Windows.Forms.Panel();
            this.PreviewImage = new System.Windows.Forms.PictureBox();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.ToolbarPanel.SuspendLayout();
            this.JournalPanel.SuspendLayout();
            this.Scroller.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PreviewImage)).BeginInit();
            this.SuspendLayout();
            // 
            // ToolbarPanel
            // 
            this.ToolbarPanel.Controls.Add(this.CloseButton);
            this.ToolbarPanel.Controls.Add(this.ZoomLabel);
            this.ToolbarPanel.Controls.Add(this.ZoomFitButton);
            this.ToolbarPanel.Controls.Add(this.ZoomActualButton);
            this.ToolbarPanel.Controls.Add(this.ZoomOutButton);
            this.ToolbarPanel.Controls.Add(this.ZoomInButton);
            this.ToolbarPanel.Controls.Add(this.InfoLabel);
            this.ToolbarPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.ToolbarPanel.Location = new System.Drawing.Point(0, 0);
            this.ToolbarPanel.Name = "ToolbarPanel";
            this.ToolbarPanel.Size = new System.Drawing.Size(1000, 40);
            this.ToolbarPanel.TabIndex = 0;
            // 
            // ZoomInButton
            // 
            this.ZoomInButton.Location = new System.Drawing.Point(8, 8);
            this.ZoomInButton.Name = "ZoomInButton";
            this.ZoomInButton.Size = new System.Drawing.Size(36, 24);
            this.ZoomInButton.TabIndex = 0;
            this.ZoomInButton.Text = "+";
            this.ZoomInButton.UseVisualStyleBackColor = true;
            this.ZoomInButton.Click += new System.EventHandler(this.ZoomInButton_Click);
            // 
            // ZoomOutButton
            // 
            this.ZoomOutButton.Location = new System.Drawing.Point(50, 8);
            this.ZoomOutButton.Name = "ZoomOutButton";
            this.ZoomOutButton.Size = new System.Drawing.Size(36, 24);
            this.ZoomOutButton.TabIndex = 1;
            this.ZoomOutButton.Text = "-";
            this.ZoomOutButton.UseVisualStyleBackColor = true;
            this.ZoomOutButton.Click += new System.EventHandler(this.ZoomOutButton_Click);
            // 
            // ZoomActualButton
            // 
            this.ZoomActualButton.Location = new System.Drawing.Point(92, 8);
            this.ZoomActualButton.Name = "ZoomActualButton";
            this.ZoomActualButton.Size = new System.Drawing.Size(56, 24);
            this.ZoomActualButton.TabIndex = 2;
            this.ZoomActualButton.Text = "100%";
            this.ZoomActualButton.UseVisualStyleBackColor = true;
            this.ZoomActualButton.Click += new System.EventHandler(this.ZoomActualButton_Click);
            // 
            // ZoomFitButton
            // 
            this.ZoomFitButton.Location = new System.Drawing.Point(154, 8);
            this.ZoomFitButton.Name = "ZoomFitButton";
            this.ZoomFitButton.Size = new System.Drawing.Size(48, 24);
            this.ZoomFitButton.TabIndex = 3;
            this.ZoomFitButton.Text = "Fit";
            this.ZoomFitButton.UseVisualStyleBackColor = true;
            this.ZoomFitButton.Click += new System.EventHandler(this.ZoomFitButton_Click);
            // 
            // ZoomLabel
            // 
            this.ZoomLabel.AutoSize = true;
            this.ZoomLabel.Location = new System.Drawing.Point(212, 13);
            this.ZoomLabel.Name = "ZoomLabel";
            this.ZoomLabel.Size = new System.Drawing.Size(33, 13);
            this.ZoomLabel.TabIndex = 4;
            this.ZoomLabel.Text = "100%";
            // 
            // InfoLabel
            // 
            this.InfoLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.InfoLabel.Location = new System.Drawing.Point(260, 8);
            this.InfoLabel.Name = "InfoLabel";
            this.InfoLabel.Size = new System.Drawing.Size(640, 24);
            this.InfoLabel.TabIndex = 5;
            this.InfoLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.CloseButton.Location = new System.Drawing.Point(917, 8);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(75, 24);
            this.CloseButton.TabIndex = 6;
            this.CloseButton.Text = "Close";
            this.CloseButton.UseVisualStyleBackColor = true;
            this.CloseButton.Click += new System.EventHandler(this.CloseButton_Click);
            // 
            // JournalPanel
            // 
            this.JournalPanel.BackColor = System.Drawing.SystemColors.Control;
            this.JournalPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.JournalPanel.Controls.Add(this.EditorStatusLabel);
            this.JournalPanel.Controls.Add(this.ApplyButton);
            this.JournalPanel.Controls.Add(this.ClearButton);
            this.JournalPanel.Controls.Add(this.RefreshListsButton);
            this.JournalPanel.Controls.Add(this.NotesBox);
            this.JournalPanel.Controls.Add(this.TagsBox);
            this.JournalPanel.Controls.Add(this.TaskBox);
            this.JournalPanel.Controls.Add(this.ProjectBox);
            this.JournalPanel.Controls.Add(this.CustomerBox);
            this.JournalPanel.Controls.Add(this.MruBox);
            this.JournalPanel.Controls.Add(this.NotesLabel);
            this.JournalPanel.Controls.Add(this.TagsLabel);
            this.JournalPanel.Controls.Add(this.TaskLabel);
            this.JournalPanel.Controls.Add(this.ProjectLabel);
            this.JournalPanel.Controls.Add(this.CustomerLabel);
            this.JournalPanel.Controls.Add(this.RecentLabel);
            this.JournalPanel.Controls.Add(this.JournalTitleLabel);
            this.JournalPanel.Dock = System.Windows.Forms.DockStyle.Right;
            this.JournalPanel.Location = new System.Drawing.Point(700, 40);
            this.JournalPanel.Name = "JournalPanel";
            this.JournalPanel.Padding = new System.Windows.Forms.Padding(8);
            this.JournalPanel.Size = new System.Drawing.Size(300, 521);
            this.JournalPanel.TabIndex = 2;
            this.JournalPanel.Visible = false;
            // 
            // JournalTitleLabel
            // 
            this.JournalTitleLabel.AutoSize = true;
            this.JournalTitleLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            this.JournalTitleLabel.Location = new System.Drawing.Point(10, 10);
            this.JournalTitleLabel.Name = "JournalTitleLabel";
            this.JournalTitleLabel.Size = new System.Drawing.Size(99, 17);
            this.JournalTitleLabel.TabIndex = 0;
            this.JournalTitleLabel.Text = "Edit metadata";
            // 
            // RecentLabel
            // 
            this.RecentLabel.AutoSize = true;
            this.RecentLabel.Location = new System.Drawing.Point(10, 40);
            this.RecentLabel.Name = "RecentLabel";
            this.RecentLabel.Size = new System.Drawing.Size(45, 13);
            this.RecentLabel.TabIndex = 1;
            this.RecentLabel.Text = "Recent:";
            // 
            // MruBox
            // 
            this.MruBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.MruBox.FormattingEnabled = true;
            this.MruBox.Location = new System.Drawing.Point(12, 56);
            this.MruBox.Name = "MruBox";
            this.MruBox.SafeText = "";
            this.MruBox.Size = new System.Drawing.Size(272, 21);
            this.MruBox.TabIndex = 2;
            this.MruBox.SelectionChangeCommitted += new System.EventHandler(this.MruBox_SelectionChangeCommitted);
            // 
            // CustomerLabel
            // 
            this.CustomerLabel.AutoSize = true;
            this.CustomerLabel.Location = new System.Drawing.Point(10, 88);
            this.CustomerLabel.Name = "CustomerLabel";
            this.CustomerLabel.Size = new System.Drawing.Size(54, 13);
            this.CustomerLabel.TabIndex = 3;
            this.CustomerLabel.Text = "Customer:";
            // 
            // CustomerBox
            // 
            this.CustomerBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CustomerBox.FormattingEnabled = true;
            this.CustomerBox.Location = new System.Drawing.Point(12, 104);
            this.CustomerBox.Name = "CustomerBox";
            this.CustomerBox.SafeText = "";
            this.CustomerBox.Size = new System.Drawing.Size(272, 21);
            this.CustomerBox.TabIndex = 4;
            this.CustomerBox.SelectionChangeCommitted += new System.EventHandler(this.CustomerBox_SelectionChangeCommitted);
            this.CustomerBox.Leave += new System.EventHandler(this.CustomerBox_Leave);
            // 
            // ProjectLabel
            // 
            this.ProjectLabel.AutoSize = true;
            this.ProjectLabel.Location = new System.Drawing.Point(10, 134);
            this.ProjectLabel.Name = "ProjectLabel";
            this.ProjectLabel.Size = new System.Drawing.Size(43, 13);
            this.ProjectLabel.TabIndex = 5;
            this.ProjectLabel.Text = "Project:";
            // 
            // ProjectBox
            // 
            this.ProjectBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ProjectBox.FormattingEnabled = true;
            this.ProjectBox.Location = new System.Drawing.Point(12, 150);
            this.ProjectBox.Name = "ProjectBox";
            this.ProjectBox.SafeText = "";
            this.ProjectBox.Size = new System.Drawing.Size(272, 21);
            this.ProjectBox.TabIndex = 6;
            this.ProjectBox.SelectionChangeCommitted += new System.EventHandler(this.ProjectBox_SelectionChangeCommitted);
            this.ProjectBox.Leave += new System.EventHandler(this.ProjectBox_Leave);
            // 
            // TaskLabel
            // 
            this.TaskLabel.AutoSize = true;
            this.TaskLabel.Location = new System.Drawing.Point(10, 180);
            this.TaskLabel.Name = "TaskLabel";
            this.TaskLabel.Size = new System.Drawing.Size(69, 13);
            this.TaskLabel.TabIndex = 7;
            this.TaskLabel.Text = "Task/Ticket:";
            // 
            // TaskBox
            // 
            this.TaskBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TaskBox.FormattingEnabled = true;
            this.TaskBox.Location = new System.Drawing.Point(12, 196);
            this.TaskBox.Name = "TaskBox";
            this.TaskBox.SafeText = "";
            this.TaskBox.Size = new System.Drawing.Size(272, 21);
            this.TaskBox.TabIndex = 8;
            // 
            // TagsLabel
            // 
            this.TagsLabel.AutoSize = true;
            this.TagsLabel.Location = new System.Drawing.Point(10, 226);
            this.TagsLabel.Name = "TagsLabel";
            this.TagsLabel.Size = new System.Drawing.Size(34, 13);
            this.TagsLabel.TabIndex = 9;
            this.TagsLabel.Text = "Tags:";
            // 
            // TagsBox
            // 
            this.TagsBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TagsBox.Location = new System.Drawing.Point(12, 242);
            this.TagsBox.Name = "TagsBox";
            this.TagsBox.Size = new System.Drawing.Size(272, 22);
            this.TagsBox.TabIndex = 10;
            // 
            // NotesLabel
            // 
            this.NotesLabel.AutoSize = true;
            this.NotesLabel.Location = new System.Drawing.Point(10, 272);
            this.NotesLabel.Name = "NotesLabel";
            this.NotesLabel.Size = new System.Drawing.Size(38, 13);
            this.NotesLabel.TabIndex = 11;
            this.NotesLabel.Text = "Notes:";
            // 
            // NotesBox
            // 
            this.NotesBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NotesBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.NotesBox.Location = new System.Drawing.Point(12, 288);
            this.NotesBox.Multiline = true;
            this.NotesBox.Name = "NotesBox";
            this.NotesBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.NotesBox.Size = new System.Drawing.Size(272, 160);
            this.NotesBox.TabIndex = 12;
            // 
            // RefreshListsButton
            // 
            this.RefreshListsButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.RefreshListsButton.Location = new System.Drawing.Point(12, 458);
            this.RefreshListsButton.Name = "RefreshListsButton";
            this.RefreshListsButton.Size = new System.Drawing.Size(36, 28);
            this.RefreshListsButton.TabIndex = 13;
            this.RefreshListsButton.Text = "↻";
            this.toolTip1.SetToolTip(this.RefreshListsButton, "Refresh lists from server");
            this.RefreshListsButton.UseVisualStyleBackColor = true;
            this.RefreshListsButton.Click += new System.EventHandler(this.RefreshListsButton_Click);
            // 
            // ClearButton
            // 
            this.ClearButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.ClearButton.Location = new System.Drawing.Point(130, 458);
            this.ClearButton.Name = "ClearButton";
            this.ClearButton.Size = new System.Drawing.Size(72, 28);
            this.ClearButton.TabIndex = 14;
            this.ClearButton.Text = "Clear";
            this.toolTip1.SetToolTip(this.ClearButton, "Clear journal link, notes, and tags on this screenshot");
            this.ClearButton.UseVisualStyleBackColor = true;
            this.ClearButton.Click += new System.EventHandler(this.ClearButton_Click);
            // 
            // ApplyButton
            // 
            this.ApplyButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.ApplyButton.Location = new System.Drawing.Point(208, 458);
            this.ApplyButton.Name = "ApplyButton";
            this.ApplyButton.Size = new System.Drawing.Size(76, 28);
            this.ApplyButton.TabIndex = 15;
            this.ApplyButton.Text = "Apply";
            this.toolTip1.SetToolTip(this.ApplyButton, "Save customer/project/task, notes, and tags on this screenshot");
            this.ApplyButton.UseVisualStyleBackColor = true;
            this.ApplyButton.Click += new System.EventHandler(this.ApplyButton_Click);
            // 
            // EditorStatusLabel
            // 
            this.EditorStatusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.EditorStatusLabel.Location = new System.Drawing.Point(10, 492);
            this.EditorStatusLabel.Name = "EditorStatusLabel";
            this.EditorStatusLabel.Size = new System.Drawing.Size(274, 20);
            this.EditorStatusLabel.TabIndex = 16;
            this.EditorStatusLabel.Text = "Ready";
            // 
            // Scroller
            // 
            this.Scroller.AutoScroll = true;
            this.Scroller.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(40)))), ((int)(((byte)(40)))), ((int)(((byte)(40)))));
            this.Scroller.Controls.Add(this.PreviewImage);
            this.Scroller.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Scroller.Location = new System.Drawing.Point(0, 40);
            this.Scroller.Name = "Scroller";
            this.Scroller.Size = new System.Drawing.Size(700, 521);
            this.Scroller.TabIndex = 1;
            this.Scroller.Resize += new System.EventHandler(this.Scroller_Resize);
            // 
            // PreviewImage
            // 
            this.PreviewImage.Location = new System.Drawing.Point(0, 0);
            this.PreviewImage.Name = "PreviewImage";
            this.PreviewImage.Size = new System.Drawing.Size(100, 100);
            this.PreviewImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.PreviewImage.TabIndex = 0;
            this.PreviewImage.TabStop = false;
            // 
            // ImagePreviewForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1000, 561);
            this.Controls.Add(this.Scroller);
            this.Controls.Add(this.JournalPanel);
            this.Controls.Add(this.ToolbarPanel);
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(720, 420);
            this.Name = "ImagePreviewForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Preview";
            this.ToolbarPanel.ResumeLayout(false);
            this.ToolbarPanel.PerformLayout();
            this.JournalPanel.ResumeLayout(false);
            this.JournalPanel.PerformLayout();
            this.Scroller.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.PreviewImage)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel ToolbarPanel;
        private System.Windows.Forms.Button ZoomInButton;
        private System.Windows.Forms.Button ZoomOutButton;
        private System.Windows.Forms.Button ZoomActualButton;
        private System.Windows.Forms.Button ZoomFitButton;
        private System.Windows.Forms.Label ZoomLabel;
        private System.Windows.Forms.Label InfoLabel;
        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.Panel JournalPanel;
        private System.Windows.Forms.Label JournalTitleLabel;
        private System.Windows.Forms.Label RecentLabel;
        private ContainsFilterComboBox MruBox;
        private System.Windows.Forms.Label CustomerLabel;
        private ContainsFilterComboBox CustomerBox;
        private System.Windows.Forms.Label ProjectLabel;
        private ContainsFilterComboBox ProjectBox;
        private System.Windows.Forms.Label TaskLabel;
        private ContainsFilterComboBox TaskBox;
        private System.Windows.Forms.Label TagsLabel;
        private Schnapp.Client.Windows.Forms.TagsInputControl TagsBox;
        private System.Windows.Forms.Label NotesLabel;
        private System.Windows.Forms.TextBox NotesBox;
        private System.Windows.Forms.Button RefreshListsButton;
        private System.Windows.Forms.Button ClearButton;
        private System.Windows.Forms.Button ApplyButton;
        private System.Windows.Forms.Label EditorStatusLabel;
        private System.Windows.Forms.Panel Scroller;
        private System.Windows.Forms.PictureBox PreviewImage;
        private System.Windows.Forms.ToolTip toolTip1;
    }
}

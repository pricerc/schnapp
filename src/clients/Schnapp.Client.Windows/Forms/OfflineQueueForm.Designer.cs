namespace Schnapp.Client.Windows.Forms
{
    partial class OfflineQueueForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeThumbnails();
                try
                {
                    if (_emptyThumb != null)
                    {
                        _emptyThumb.Dispose();
                        _emptyThumb = null;
                    }
                }
                catch { /* ignore */ }
                if (components != null)
                    components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.TitleText = new System.Windows.Forms.Label();
            this.SummaryText = new System.Windows.Forms.Label();
            this.HelpText = new System.Windows.Forms.Label();
            this.OlderButton = new System.Windows.Forms.Button();
            this.DateFilterText = new System.Windows.Forms.Label();
            this.NewerButton = new System.Windows.Forms.Button();
            this.ShowAllButton = new System.Windows.Forms.Button();
            this.QueueGrid = new System.Windows.Forms.DataGridView();
            this.SelectColumn = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.TimeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.JournalColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.NotesColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.StatusColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SelectAllButton = new System.Windows.Forms.Button();
            this.SelectNoneButton = new System.Windows.Forms.Button();
            this.EditButton = new System.Windows.Forms.Button();
            this.DeleteButton = new System.Windows.Forms.Button();
            this.RefreshButton = new System.Windows.Forms.Button();
            this.PushButton = new System.Windows.Forms.Button();
            this.PreviewButton = new System.Windows.Forms.Button();
            this.CloseButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.QueueGrid)).BeginInit();
            this.SuspendLayout();
            // 
            // TitleText
            // 
            this.TitleText.AutoSize = true;
            this.TitleText.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold);
            this.TitleText.Location = new System.Drawing.Point(12, 12);
            this.TitleText.Name = "TitleText";
            this.TitleText.Size = new System.Drawing.Size(170, 20);
            this.TitleText.TabIndex = 0;
            this.TitleText.Text = "Recent screenshots";
            // 
            // SummaryText
            // 
            this.SummaryText.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SummaryText.Location = new System.Drawing.Point(12, 40);
            this.SummaryText.Name = "SummaryText";
            this.SummaryText.Size = new System.Drawing.Size(860, 32);
            this.SummaryText.TabIndex = 1;
            this.SummaryText.Text = "Loading…";
            // 
            // HelpText
            // 
            this.HelpText.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.HelpText.ForeColor = System.Drawing.SystemColors.GrayText;
            this.HelpText.Location = new System.Drawing.Point(12, 76);
            this.HelpText.Name = "HelpText";
            this.HelpText.Size = new System.Drawing.Size(860, 36);
            this.HelpText.TabIndex = 2;
            this.HelpText.Text = "Each row is one capture batch (same timestamp): pending and cached items. Double-" +
    "click a thumbnail to preview. Deleting a pending row discards it; deleting uploa" +
    "ded cache only clears the local copy.";
            // 
            // OlderButton
            // 
            this.OlderButton.Location = new System.Drawing.Point(12, 118);
            this.OlderButton.Name = "OlderButton";
            this.OlderButton.Size = new System.Drawing.Size(80, 28);
            this.OlderButton.TabIndex = 3;
            this.OlderButton.Text = "◀ Older";
            this.OlderButton.UseVisualStyleBackColor = true;
            this.OlderButton.Click += new System.EventHandler(this.OlderButton_Click);
            // 
            // DateFilterText
            // 
            this.DateFilterText.Location = new System.Drawing.Point(98, 124);
            this.DateFilterText.Name = "DateFilterText";
            this.DateFilterText.Size = new System.Drawing.Size(110, 18);
            this.DateFilterText.TabIndex = 4;
            this.DateFilterText.Text = "All dates";
            this.DateFilterText.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // NewerButton
            // 
            this.NewerButton.Location = new System.Drawing.Point(214, 118);
            this.NewerButton.Name = "NewerButton";
            this.NewerButton.Size = new System.Drawing.Size(80, 28);
            this.NewerButton.TabIndex = 5;
            this.NewerButton.Text = "Newer ▶";
            this.NewerButton.UseVisualStyleBackColor = true;
            this.NewerButton.Click += new System.EventHandler(this.NewerButton_Click);
            // 
            // ShowAllButton
            // 
            this.ShowAllButton.Enabled = false;
            this.ShowAllButton.Location = new System.Drawing.Point(300, 118);
            this.ShowAllButton.Name = "ShowAllButton";
            this.ShowAllButton.Size = new System.Drawing.Size(110, 28);
            this.ShowAllButton.TabIndex = 6;
            this.ShowAllButton.Text = "Show all dates";
            this.ShowAllButton.UseVisualStyleBackColor = true;
            this.ShowAllButton.Click += new System.EventHandler(this.ShowAllButton_Click);
            // 
            // QueueGrid
            // 
            this.QueueGrid.AllowUserToAddRows = false;
            this.QueueGrid.AllowUserToDeleteRows = false;
            this.QueueGrid.AllowUserToResizeRows = false;
            this.QueueGrid.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.QueueGrid.AutoGenerateColumns = false;
            this.QueueGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.QueueGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.SelectColumn,
            this.TimeColumn,
            this.JournalColumn,
            this.NotesColumn,
            this.StatusColumn});
            this.QueueGrid.Location = new System.Drawing.Point(12, 156);
            this.QueueGrid.MultiSelect = true;
            this.QueueGrid.Name = "QueueGrid";
            this.QueueGrid.RowHeadersVisible = false;
            this.QueueGrid.RowTemplate.Height = 72;
            this.QueueGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.QueueGrid.Size = new System.Drawing.Size(860, 340);
            this.QueueGrid.TabIndex = 7;
            this.QueueGrid.VirtualMode = true;
            this.QueueGrid.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.QueueGrid_CellContentClick);
            this.QueueGrid.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.QueueGrid_CellDoubleClick);
            this.QueueGrid.CellMouseDown += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.QueueGrid_CellMouseDown);
            this.QueueGrid.CellToolTipTextNeeded += new System.Windows.Forms.DataGridViewCellToolTipTextNeededEventHandler(this.QueueGrid_CellToolTipTextNeeded);
            this.QueueGrid.CellValueNeeded += new System.Windows.Forms.DataGridViewCellValueEventHandler(this.QueueGrid_CellValueNeeded);
            this.QueueGrid.CellValuePushed += new System.Windows.Forms.DataGridViewCellValueEventHandler(this.QueueGrid_CellValuePushed);
            this.QueueGrid.CurrentCellDirtyStateChanged += new System.EventHandler(this.QueueGrid_CurrentCellDirtyStateChanged);
            this.QueueGrid.SelectionChanged += new System.EventHandler(this.QueueGrid_SelectionChanged);
            // 
            // SelectColumn
            // 
            this.SelectColumn.FalseValue = false;
            this.SelectColumn.HeaderText = "";
            this.SelectColumn.Name = "SelectColumn";
            this.SelectColumn.TrueValue = true;
            this.SelectColumn.Width = 32;
            // 
            // TimeColumn
            // 
            this.TimeColumn.HeaderText = "Time";
            this.TimeColumn.Name = "TimeColumn";
            this.TimeColumn.ReadOnly = true;
            this.TimeColumn.Width = 140;
            // 
            // JournalColumn
            // 
            this.JournalColumn.HeaderText = "Journal";
            this.JournalColumn.Name = "JournalColumn";
            this.JournalColumn.ReadOnly = true;
            this.JournalColumn.Width = 180;
            // 
            // NotesColumn
            // 
            this.NotesColumn.HeaderText = "Notes";
            this.NotesColumn.Name = "NotesColumn";
            this.NotesColumn.ReadOnly = true;
            this.NotesColumn.Width = 160;
            // 
            // StatusColumn
            // 
            this.StatusColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.StatusColumn.HeaderText = "Status";
            this.StatusColumn.MinimumWidth = 120;
            this.StatusColumn.Name = "StatusColumn";
            this.StatusColumn.ReadOnly = true;
            // 
            // SelectAllButton
            // 
            this.SelectAllButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.SelectAllButton.Location = new System.Drawing.Point(12, 510);
            this.SelectAllButton.Name = "SelectAllButton";
            this.SelectAllButton.Size = new System.Drawing.Size(90, 28);
            this.SelectAllButton.TabIndex = 8;
            this.SelectAllButton.Text = "Select all";
            this.SelectAllButton.UseVisualStyleBackColor = true;
            this.SelectAllButton.Click += new System.EventHandler(this.SelectAllButton_Click);
            // 
            // SelectNoneButton
            // 
            this.SelectNoneButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.SelectNoneButton.Location = new System.Drawing.Point(108, 510);
            this.SelectNoneButton.Name = "SelectNoneButton";
            this.SelectNoneButton.Size = new System.Drawing.Size(90, 28);
            this.SelectNoneButton.TabIndex = 9;
            this.SelectNoneButton.Text = "Select none";
            this.SelectNoneButton.UseVisualStyleBackColor = true;
            this.SelectNoneButton.Click += new System.EventHandler(this.SelectNoneButton_Click);
            // 
            // EditButton
            // 
            this.EditButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.EditButton.Enabled = false;
            this.EditButton.Location = new System.Drawing.Point(204, 510);
            this.EditButton.Name = "EditButton";
            this.EditButton.Size = new System.Drawing.Size(100, 28);
            this.EditButton.TabIndex = 10;
            this.EditButton.Text = "Edit selected";
            this.EditButton.UseVisualStyleBackColor = true;
            this.EditButton.Click += new System.EventHandler(this.EditButton_Click);
            // 
            // DeleteButton
            // 
            this.DeleteButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.DeleteButton.Enabled = false;
            this.DeleteButton.Location = new System.Drawing.Point(310, 510);
            this.DeleteButton.Name = "DeleteButton";
            this.DeleteButton.Size = new System.Drawing.Size(120, 28);
            this.DeleteButton.TabIndex = 11;
            this.DeleteButton.Text = "Delete selected";
            this.DeleteButton.UseVisualStyleBackColor = true;
            this.DeleteButton.Click += new System.EventHandler(this.DeleteButton_Click);
            // 
            // RefreshButton
            // 
            this.RefreshButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.RefreshButton.Location = new System.Drawing.Point(436, 510);
            this.RefreshButton.Name = "RefreshButton";
            this.RefreshButton.Size = new System.Drawing.Size(80, 28);
            this.RefreshButton.TabIndex = 12;
            this.RefreshButton.Text = "Refresh";
            this.RefreshButton.UseVisualStyleBackColor = true;
            this.RefreshButton.Click += new System.EventHandler(this.RefreshButton_Click);
            // 
            // PushButton
            // 
            this.PushButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.PushButton.Location = new System.Drawing.Point(522, 510);
            this.PushButton.Name = "PushButton";
            this.PushButton.Size = new System.Drawing.Size(90, 28);
            this.PushButton.TabIndex = 13;
            this.PushButton.Text = "Push all";
            this.PushButton.UseVisualStyleBackColor = true;
            this.PushButton.Click += new System.EventHandler(this.PushButton_Click);
            // 
            // PreviewButton
            // 
            this.PreviewButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.PreviewButton.Location = new System.Drawing.Point(618, 510);
            this.PreviewButton.Name = "PreviewButton";
            this.PreviewButton.Size = new System.Drawing.Size(90, 28);
            this.PreviewButton.TabIndex = 14;
            this.PreviewButton.Text = "Preview";
            this.PreviewButton.UseVisualStyleBackColor = true;
            this.PreviewButton.Click += new System.EventHandler(this.PreviewButton_Click);
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CloseButton.Location = new System.Drawing.Point(797, 510);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(75, 28);
            this.CloseButton.TabIndex = 15;
            this.CloseButton.Text = "Close";
            this.CloseButton.UseVisualStyleBackColor = true;
            this.CloseButton.Click += new System.EventHandler(this.CloseButton_Click);
            // 
            // OfflineQueueForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(884, 550);
            this.Controls.Add(this.CloseButton);
            this.Controls.Add(this.PreviewButton);
            this.Controls.Add(this.PushButton);
            this.Controls.Add(this.RefreshButton);
            this.Controls.Add(this.DeleteButton);
            this.Controls.Add(this.EditButton);
            this.Controls.Add(this.SelectNoneButton);
            this.Controls.Add(this.SelectAllButton);
            this.Controls.Add(this.QueueGrid);
            this.Controls.Add(this.ShowAllButton);
            this.Controls.Add(this.NewerButton);
            this.Controls.Add(this.DateFilterText);
            this.Controls.Add(this.OlderButton);
            this.Controls.Add(this.HelpText);
            this.Controls.Add(this.SummaryText);
            this.Controls.Add(this.TitleText);
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(700, 420);
            this.Name = "OfflineQueueForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "Schnapp — Recent screenshots";
            ((System.ComponentModel.ISupportInitialize)(this.QueueGrid)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label TitleText;
        private System.Windows.Forms.Label SummaryText;
        private System.Windows.Forms.Label HelpText;
        private System.Windows.Forms.Button OlderButton;
        private System.Windows.Forms.Label DateFilterText;
        private System.Windows.Forms.Button NewerButton;
        private System.Windows.Forms.Button ShowAllButton;
        private System.Windows.Forms.DataGridView QueueGrid;
        private System.Windows.Forms.DataGridViewCheckBoxColumn SelectColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn TimeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn JournalColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn NotesColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn StatusColumn;
        private System.Windows.Forms.Button SelectAllButton;
        private System.Windows.Forms.Button SelectNoneButton;
        private System.Windows.Forms.Button EditButton;
        private System.Windows.Forms.Button DeleteButton;
        private System.Windows.Forms.Button RefreshButton;
        private System.Windows.Forms.Button PushButton;
        private System.Windows.Forms.Button PreviewButton;
        private System.Windows.Forms.Button CloseButton;
    }
}

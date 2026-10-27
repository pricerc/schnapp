namespace Schnapp.Client.Windows.Forms
{
    partial class SettingsForm
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
            this.Tabs = new System.Windows.Forms.TabControl();
            this.CaptureTab = new System.Windows.Forms.TabPage();
            this.CaptureLayout = new System.Windows.Forms.TableLayoutPanel();
            this.IntervalLabel = new System.Windows.Forms.Label();
            this.MonitorListPanel = new System.Windows.Forms.Panel();
            this.MonitorPickLabel = new System.Windows.Forms.Label();
            this.IntervalBox = new System.Windows.Forms.NumericUpDown();
            this.CaptureMonitorsBox = new System.Windows.Forms.CheckBox();
            this.FormatLabel = new System.Windows.Forms.Label();
            this.CaptureFormatBox = new System.Windows.Forms.ComboBox();
            this.QualityLabel = new System.Windows.Forms.Label();
            this.QualityBox = new System.Windows.Forms.NumericUpDown();
            this.SkipScreensaverBox = new System.Windows.Forms.CheckBox();
            this.IdlePauseLabel = new System.Windows.Forms.Label();
            this.IdlePauseBox = new System.Windows.Forms.NumericUpDown();
            this.FocusDwellLabel = new System.Windows.Forms.Label();
            this.FocusDwellBox = new System.Windows.Forms.NumericUpDown();
            this.DiscardBlankBox = new System.Windows.Forms.CheckBox();
            this.DiscardIdleBox = new System.Windows.Forms.CheckBox();
            this.IdleDirtyCellsLabel = new System.Windows.Forms.Label();
            this.IdleDirtyCellsBox = new System.Windows.Forms.NumericUpDown();
            this.DesktopScalePanel = new System.Windows.Forms.Panel();
            this.DesktopScaleLabel = new System.Windows.Forms.Label();
            this.CaptureWindowBox = new System.Windows.Forms.CheckBox();
            this.CaptureFullBox = new System.Windows.Forms.CheckBox();
            this.PathsTab = new System.Windows.Forms.TabPage();
            this.InfoText = new System.Windows.Forms.Label();
            this.FileLogLevelBox = new System.Windows.Forms.ComboBox();
            this.LogLevelLabel = new System.Windows.Forms.Label();
            this.MutationsDirBox = new System.Windows.Forms.TextBox();
            this.MutationsLabel = new System.Windows.Forms.Label();
            this.SpoolDirBox = new System.Windows.Forms.TextBox();
            this.SpoolLabel = new System.Windows.Forms.Label();
            this.ApiUrlBox = new System.Windows.Forms.TextBox();
            this.ApiUrlLabel = new System.Windows.Forms.Label();
            this.SaveButton = new System.Windows.Forms.Button();
            this.CloseButton = new System.Windows.Forms.Button();
            this.Tabs.SuspendLayout();
            this.CaptureTab.SuspendLayout();
            this.CaptureLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.IntervalBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.IdlePauseBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.FocusDwellBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.IdleDirtyCellsBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.QualityBox)).BeginInit();
            this.PathsTab.SuspendLayout();
            this.SuspendLayout();
            // 
            // Tabs
            // 
            this.Tabs.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.Tabs.Controls.Add(this.CaptureTab);
            this.Tabs.Controls.Add(this.PathsTab);
            this.Tabs.Location = new System.Drawing.Point(12, 12);
            this.Tabs.Name = "Tabs";
            this.Tabs.SelectedIndex = 0;
            this.Tabs.Size = new System.Drawing.Size(480, 597);
            this.Tabs.TabIndex = 0;
            // 
            // CaptureTab
            // 
            this.CaptureTab.AutoScroll = true;
            this.CaptureTab.Controls.Add(this.CaptureLayout);
            this.CaptureTab.Location = new System.Drawing.Point(4, 22);
            this.CaptureTab.Name = "CaptureTab";
            this.CaptureTab.Padding = new System.Windows.Forms.Padding(8);
            this.CaptureTab.Size = new System.Drawing.Size(472, 571);
            this.CaptureTab.TabIndex = 0;
            this.CaptureTab.Text = "Capture";
            this.CaptureTab.UseVisualStyleBackColor = true;
            // 
            // CaptureLayout
            // 
            this.CaptureLayout.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CaptureLayout.ColumnCount = 3;
            this.CaptureLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 40F));
            this.CaptureLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.CaptureLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 40F));
            this.CaptureLayout.Controls.Add(this.IntervalLabel, 0, 0);
            this.CaptureLayout.Controls.Add(this.MonitorListPanel, 0, 12);
            this.CaptureLayout.Controls.Add(this.MonitorPickLabel, 0, 11);
            this.CaptureLayout.Controls.Add(this.IntervalBox, 1, 0);
            this.CaptureLayout.Controls.Add(this.CaptureMonitorsBox, 0, 10);
            this.CaptureLayout.Controls.Add(this.IdlePauseLabel, 0, 1);
            this.CaptureLayout.Controls.Add(this.IdlePauseBox, 1, 1);
            this.CaptureLayout.Controls.Add(this.FocusDwellLabel, 0, 9);
            this.CaptureLayout.Controls.Add(this.FocusDwellBox, 1, 9);
            this.CaptureLayout.Controls.Add(this.DiscardBlankBox, 2, 1);
            this.CaptureLayout.Controls.Add(this.DiscardIdleBox, 2, 2);
            this.CaptureLayout.Controls.Add(this.IdleDirtyCellsLabel, 0, 2);
            this.CaptureLayout.Controls.Add(this.IdleDirtyCellsBox, 1, 2);
            this.CaptureLayout.Controls.Add(this.FormatLabel, 0, 3);
            this.CaptureLayout.Controls.Add(this.CaptureFormatBox, 1, 3);
            this.CaptureLayout.Controls.Add(this.QualityLabel, 0, 4);
            this.CaptureLayout.Controls.Add(this.QualityBox, 1, 4);
            this.CaptureLayout.Controls.Add(this.SkipScreensaverBox, 2, 0);
            this.CaptureLayout.Controls.Add(this.DesktopScalePanel, 0, 8);
            this.CaptureLayout.Controls.Add(this.DesktopScaleLabel, 0, 7);
            this.CaptureLayout.Controls.Add(this.CaptureWindowBox, 0, 5);
            this.CaptureLayout.Controls.Add(this.CaptureFullBox, 0, 6);
            this.CaptureLayout.Location = new System.Drawing.Point(11, 11);
            this.CaptureLayout.Name = "CaptureLayout";
            this.CaptureLayout.RowCount = 13;
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.CaptureLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.CaptureLayout.Size = new System.Drawing.Size(450, 549);
            this.CaptureLayout.TabIndex = 14;
            // 
            // IntervalLabel
            // 
            this.IntervalLabel.AutoSize = true;
            this.IntervalLabel.Location = new System.Drawing.Point(3, 0);
            this.IntervalLabel.Name = "IntervalLabel";
            this.IntervalLabel.Size = new System.Drawing.Size(130, 13);
            this.IntervalLabel.TabIndex = 0;
            this.IntervalLabel.Text = "Capture interval (seconds)";
            // 
            // MonitorListPanel
            // 
            this.MonitorListPanel.AutoScroll = true;
            this.MonitorListPanel.AutoSize = true;
            this.MonitorListPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.CaptureLayout.SetColumnSpan(this.MonitorListPanel, 3);
            this.MonitorListPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MonitorListPanel.Location = new System.Drawing.Point(3, 283);
            this.MonitorListPanel.MinimumSize = new System.Drawing.Size(0, 20);
            this.MonitorListPanel.Name = "MonitorListPanel";
            this.MonitorListPanel.Size = new System.Drawing.Size(444, 263);
            this.MonitorListPanel.TabIndex = 13;
            // 
            // MonitorPickLabel
            // 
            this.MonitorPickLabel.AutoSize = true;
            this.CaptureLayout.SetColumnSpan(this.MonitorPickLabel, 3);
            this.MonitorPickLabel.Location = new System.Drawing.Point(3, 267);
            this.MonitorPickLabel.Name = "MonitorPickLabel";
            this.MonitorPickLabel.Size = new System.Drawing.Size(315, 13);
            this.MonitorPickLabel.TabIndex = 12;
            this.MonitorPickLabel.Text = "Per-monitor capture, scale, and quality (all checked = all monitors)";
            // 
            // IntervalBox
            // 
            this.IntervalBox.Location = new System.Drawing.Point(183, 3);
            this.IntervalBox.Maximum = new decimal(new int[] {
            3600,
            0,
            0,
            0});
            this.IntervalBox.Minimum = new decimal(new int[] {
            15,
            0,
            0,
            0});
            this.IntervalBox.Name = "IntervalBox";
            this.IntervalBox.Size = new System.Drawing.Size(84, 20);
            this.IntervalBox.TabIndex = 1;
            this.IntervalBox.Value = new decimal(new int[] {
            300,
            0,
            0,
            0});
            // 
            // CaptureMonitorsBox
            // 
            this.CaptureMonitorsBox.AutoSize = true;
            this.CaptureLayout.SetColumnSpan(this.CaptureMonitorsBox, 2);
            this.CaptureMonitorsBox.Location = new System.Drawing.Point(3, 247);
            this.CaptureMonitorsBox.Name = "CaptureMonitorsBox";
            this.CaptureMonitorsBox.Size = new System.Drawing.Size(152, 17);
            this.CaptureMonitorsBox.TabIndex = 11;
            this.CaptureMonitorsBox.Text = "Monitors (separate images)";
            this.CaptureMonitorsBox.UseVisualStyleBackColor = true;
            this.CaptureMonitorsBox.CheckedChanged += new System.EventHandler(this.CaptureMonitorsBox_CheckedChanged);
            // 
            // FormatLabel
            // 
            this.FormatLabel.AutoSize = true;
            this.FormatLabel.Location = new System.Drawing.Point(3, 52);
            this.FormatLabel.Name = "FormatLabel";
            this.FormatLabel.Size = new System.Drawing.Size(68, 13);
            this.FormatLabel.TabIndex = 3;
            this.FormatLabel.Text = "Image format";
            // 
            // CaptureFormatBox
            // 
            this.CaptureLayout.SetColumnSpan(this.CaptureFormatBox, 2);
            this.CaptureFormatBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.CaptureFormatBox.FormattingEnabled = true;
            this.CaptureFormatBox.Location = new System.Drawing.Point(183, 55);
            this.CaptureFormatBox.Name = "CaptureFormatBox";
            this.CaptureFormatBox.Size = new System.Drawing.Size(264, 21);
            this.CaptureFormatBox.TabIndex = 4;
            this.CaptureFormatBox.SelectedIndexChanged += new System.EventHandler(this.CaptureFormatBox_SelectedIndexChanged);
            // 
            // QualityLabel
            // 
            this.QualityLabel.AutoSize = true;
            this.QualityLabel.Location = new System.Drawing.Point(3, 79);
            this.QualityLabel.Name = "QualityLabel";
            this.QualityLabel.Size = new System.Drawing.Size(113, 13);
            this.QualityLabel.TabIndex = 5;
            this.QualityLabel.Text = "Image quality (10-100)";
            // 
            // QualityBox
            // 
            this.QualityBox.Increment = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.QualityBox.Location = new System.Drawing.Point(183, 82);
            this.QualityBox.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.QualityBox.Name = "QualityBox";
            this.QualityBox.Size = new System.Drawing.Size(84, 20);
            this.QualityBox.TabIndex = 6;
            this.QualityBox.Value = new decimal(new int[] {
            80,
            0,
            0,
            0});
            // 
            // SkipScreensaverBox
            // 
            this.SkipScreensaverBox.AutoSize = true;
            this.SkipScreensaverBox.Location = new System.Drawing.Point(273, 3);
            this.SkipScreensaverBox.Name = "SkipScreensaverBox";
            this.SkipScreensaverBox.Size = new System.Drawing.Size(169, 17);
            this.SkipScreensaverBox.TabIndex = 2;
            this.SkipScreensaverBox.Text = "Skip when screensaver active";
            this.SkipScreensaverBox.UseVisualStyleBackColor = true;
            // 
            // IdlePauseLabel
            // 
            this.IdlePauseLabel.AutoSize = true;
            this.IdlePauseLabel.Location = new System.Drawing.Point(3, 26);
            this.IdlePauseLabel.Name = "IdlePauseLabel";
            this.IdlePauseLabel.Size = new System.Drawing.Size(174, 13);
            this.IdlePauseLabel.TabIndex = 14;
            this.IdlePauseLabel.Text = "Pause after idle (minutes; 0 = off)";
            // 
            // IdlePauseBox
            // 
            this.IdlePauseBox.Location = new System.Drawing.Point(183, 29);
            this.IdlePauseBox.Maximum = new decimal(new int[] {
            1440,
            0,
            0,
            0});
            this.IdlePauseBox.Name = "IdlePauseBox";
            this.IdlePauseBox.Size = new System.Drawing.Size(84, 20);
            this.IdlePauseBox.TabIndex = 15;
            // 
            // FocusDwellLabel
            // 
            this.FocusDwellLabel.AutoSize = true;
            this.FocusDwellLabel.Location = new System.Drawing.Point(3, 200);
            this.FocusDwellLabel.Name = "FocusDwellLabel";
            this.FocusDwellLabel.Size = new System.Drawing.Size(168, 13);
            this.FocusDwellLabel.TabIndex = 16;
            this.FocusDwellLabel.Text = "Focus snapshot (seconds; 0 = off)";
            // 
            // FocusDwellBox
            // 
            this.FocusDwellBox.Location = new System.Drawing.Point(183, 200);
            this.FocusDwellBox.Maximum = new decimal(new int[] {
            3600,
            0,
            0,
            0});
            this.FocusDwellBox.Name = "FocusDwellBox";
            this.FocusDwellBox.Size = new System.Drawing.Size(84, 20);
            this.FocusDwellBox.TabIndex = 17;
            // 
            // DiscardBlankBox
            // 
            this.DiscardBlankBox.AutoSize = true;
            this.DiscardBlankBox.Location = new System.Drawing.Point(273, 29);
            this.DiscardBlankBox.Name = "DiscardBlankBox";
            this.DiscardBlankBox.Size = new System.Drawing.Size(160, 17);
            this.DiscardBlankBox.TabIndex = 16;
            this.DiscardBlankBox.Text = "Discard blank (all-black)";
            this.DiscardBlankBox.UseVisualStyleBackColor = true;
            // 
            // IdleDirtyCellsLabel
            // 
            this.IdleDirtyCellsLabel.AutoSize = true;
            this.IdleDirtyCellsLabel.Location = new System.Drawing.Point(3, 52);
            this.IdleDirtyCellsLabel.Name = "IdleDirtyCellsLabel";
            this.IdleDirtyCellsLabel.Size = new System.Drawing.Size(170, 13);
            this.IdleDirtyCellsLabel.TabIndex = 17;
            this.IdleDirtyCellsLabel.Text = "Idle tolerance (dirty cells)";
            // 
            // IdleDirtyCellsBox
            // 
            this.IdleDirtyCellsBox.Location = new System.Drawing.Point(183, 55);
            this.IdleDirtyCellsBox.Maximum = new decimal(new int[] {
            20,
            0,
            0,
            0});
            this.IdleDirtyCellsBox.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.IdleDirtyCellsBox.Name = "IdleDirtyCellsBox";
            this.IdleDirtyCellsBox.Size = new System.Drawing.Size(84, 20);
            this.IdleDirtyCellsBox.TabIndex = 18;
            this.IdleDirtyCellsBox.Value = new decimal(new int[] {
            2,
            0,
            0,
            0});
            // 
            // DiscardIdleBox
            // 
            this.DiscardIdleBox.AutoSize = true;
            this.DiscardIdleBox.Location = new System.Drawing.Point(273, 55);
            this.DiscardIdleBox.Name = "DiscardIdleBox";
            this.DiscardIdleBox.Size = new System.Drawing.Size(155, 17);
            this.DiscardIdleBox.TabIndex = 19;
            this.DiscardIdleBox.Text = "Discard idle duplicates";
            this.DiscardIdleBox.UseVisualStyleBackColor = true;
            // 
            // DesktopScalePanel
            // 
            this.DesktopScalePanel.AutoScroll = true;
            this.DesktopScalePanel.AutoSize = true;
            this.DesktopScalePanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.CaptureLayout.SetColumnSpan(this.DesktopScalePanel, 3);
            this.DesktopScalePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DesktopScalePanel.Location = new System.Drawing.Point(3, 141);
            this.DesktopScalePanel.MinimumSize = new System.Drawing.Size(0, 100);
            this.DesktopScalePanel.Name = "DesktopScalePanel";
            this.DesktopScalePanel.Size = new System.Drawing.Size(444, 100);
            this.DesktopScalePanel.TabIndex = 9;
            // 
            // DesktopScaleLabel
            // 
            this.DesktopScaleLabel.AutoSize = true;
            this.CaptureLayout.SetColumnSpan(this.DesktopScaleLabel, 2);
            this.DesktopScaleLabel.Location = new System.Drawing.Point(3, 125);
            this.DesktopScaleLabel.Name = "DesktopScaleLabel";
            this.DesktopScaleLabel.Size = new System.Drawing.Size(133, 13);
            this.DesktopScaleLabel.TabIndex = 8;
            this.DesktopScaleLabel.Text = "Full desktop scale / quality";
            // 
            // CaptureWindowBox
            // 
            this.CaptureWindowBox.AutoSize = true;
            this.CaptureLayout.SetColumnSpan(this.CaptureWindowBox, 2);
            this.CaptureWindowBox.Location = new System.Drawing.Point(3, 82);
            this.CaptureWindowBox.Name = "CaptureWindowBox";
            this.CaptureWindowBox.Size = new System.Drawing.Size(95, 17);
            this.CaptureWindowBox.TabIndex = 10;
            this.CaptureWindowBox.Text = "Active window";
            this.CaptureWindowBox.UseVisualStyleBackColor = true;
            // 
            // CaptureFullBox
            // 
            this.CaptureFullBox.AutoSize = true;
            this.CaptureLayout.SetColumnSpan(this.CaptureFullBox, 2);
            this.CaptureFullBox.Location = new System.Drawing.Point(3, 105);
            this.CaptureFullBox.Name = "CaptureFullBox";
            this.CaptureFullBox.Size = new System.Drawing.Size(220, 17);
            this.CaptureFullBox.TabIndex = 7;
            this.CaptureFullBox.Text = "Full desktop (single image for all monitors)";
            this.CaptureFullBox.UseVisualStyleBackColor = true;
            this.CaptureFullBox.CheckedChanged += new System.EventHandler(this.CaptureFullBox_CheckedChanged);
            // 
            // PathsTab
            // 
            this.PathsTab.Controls.Add(this.InfoText);
            this.PathsTab.Controls.Add(this.FileLogLevelBox);
            this.PathsTab.Controls.Add(this.LogLevelLabel);
            this.PathsTab.Controls.Add(this.MutationsDirBox);
            this.PathsTab.Controls.Add(this.MutationsLabel);
            this.PathsTab.Controls.Add(this.SpoolDirBox);
            this.PathsTab.Controls.Add(this.SpoolLabel);
            this.PathsTab.Controls.Add(this.ApiUrlBox);
            this.PathsTab.Controls.Add(this.ApiUrlLabel);
            this.PathsTab.Location = new System.Drawing.Point(4, 22);
            this.PathsTab.Name = "PathsTab";
            this.PathsTab.Padding = new System.Windows.Forms.Padding(8);
            this.PathsTab.Size = new System.Drawing.Size(472, 571);
            this.PathsTab.TabIndex = 1;
            this.PathsTab.Text = "Paths";
            this.PathsTab.UseVisualStyleBackColor = true;
            // 
            // InfoText
            // 
            this.InfoText.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.InfoText.Location = new System.Drawing.Point(11, 232);
            this.InfoText.Name = "InfoText";
            this.InfoText.Size = new System.Drawing.Size(440, 40);
            this.InfoText.TabIndex = 8;
            // 
            // FileLogLevelBox
            // 
            this.FileLogLevelBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.FileLogLevelBox.FormattingEnabled = true;
            this.FileLogLevelBox.Location = new System.Drawing.Point(14, 188);
            this.FileLogLevelBox.Name = "FileLogLevelBox";
            this.FileLogLevelBox.Size = new System.Drawing.Size(160, 21);
            this.FileLogLevelBox.TabIndex = 7;
            // 
            // LogLevelLabel
            // 
            this.LogLevelLabel.AutoSize = true;
            this.LogLevelLabel.Location = new System.Drawing.Point(11, 172);
            this.LogLevelLabel.Name = "LogLevelLabel";
            this.LogLevelLabel.Size = new System.Drawing.Size(65, 13);
            this.LogLevelLabel.TabIndex = 6;
            this.LogLevelLabel.Text = "File log level";
            // 
            // MutationsDirBox
            // 
            this.MutationsDirBox.Location = new System.Drawing.Point(14, 136);
            this.MutationsDirBox.Name = "MutationsDirBox";
            this.MutationsDirBox.Size = new System.Drawing.Size(261, 20);
            this.MutationsDirBox.TabIndex = 5;
            // 
            // MutationsLabel
            // 
            this.MutationsLabel.AutoSize = true;
            this.MutationsLabel.Location = new System.Drawing.Point(11, 120);
            this.MutationsLabel.Name = "MutationsLabel";
            this.MutationsLabel.Size = new System.Drawing.Size(170, 13);
            this.MutationsLabel.TabIndex = 4;
            this.MutationsLabel.Text = "Mutation queue directory (optional)";
            // 
            // SpoolDirBox
            // 
            this.SpoolDirBox.Location = new System.Drawing.Point(14, 84);
            this.SpoolDirBox.Name = "SpoolDirBox";
            this.SpoolDirBox.Size = new System.Drawing.Size(261, 20);
            this.SpoolDirBox.TabIndex = 3;
            // 
            // SpoolLabel
            // 
            this.SpoolLabel.AutoSize = true;
            this.SpoolLabel.Location = new System.Drawing.Point(11, 68);
            this.SpoolLabel.Name = "SpoolLabel";
            this.SpoolLabel.Size = new System.Drawing.Size(178, 13);
            this.SpoolLabel.TabIndex = 2;
            this.SpoolLabel.Text = "Screenshot spool directory (optional)";
            // 
            // ApiUrlBox
            // 
            this.ApiUrlBox.Location = new System.Drawing.Point(14, 32);
            this.ApiUrlBox.Name = "ApiUrlBox";
            this.ApiUrlBox.Size = new System.Drawing.Size(261, 20);
            this.ApiUrlBox.TabIndex = 1;
            // 
            // ApiUrlLabel
            // 
            this.ApiUrlLabel.AutoSize = true;
            this.ApiUrlLabel.Location = new System.Drawing.Point(11, 14);
            this.ApiUrlLabel.Name = "ApiUrlLabel";
            this.ApiUrlLabel.Size = new System.Drawing.Size(75, 13);
            this.ApiUrlLabel.TabIndex = 0;
            this.ApiUrlLabel.Text = "API base URL";
            // 
            // SaveButton
            // 
            this.SaveButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.SaveButton.Location = new System.Drawing.Point(336, 621);
            this.SaveButton.Name = "SaveButton";
            this.SaveButton.Size = new System.Drawing.Size(75, 28);
            this.SaveButton.TabIndex = 1;
            this.SaveButton.Text = "Save";
            this.SaveButton.UseVisualStyleBackColor = true;
            this.SaveButton.Click += new System.EventHandler(this.SaveButton_Click);
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CloseButton.Location = new System.Drawing.Point(417, 621);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(75, 28);
            this.CloseButton.TabIndex = 2;
            this.CloseButton.Text = "Close";
            this.CloseButton.UseVisualStyleBackColor = true;
            this.CloseButton.Click += new System.EventHandler(this.CloseButton_Click);
            // 
            // SettingsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(504, 661);
            this.Controls.Add(this.CloseButton);
            this.Controls.Add(this.SaveButton);
            this.Controls.Add(this.Tabs);
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(520, 600);
            this.Name = "SettingsForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "Schnapp settings";
            this.Tabs.ResumeLayout(false);
            this.CaptureTab.ResumeLayout(false);
            this.CaptureLayout.ResumeLayout(false);
            this.CaptureLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.IntervalBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.IdlePauseBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.FocusDwellBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.IdleDirtyCellsBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.QualityBox)).EndInit();
            this.PathsTab.ResumeLayout(false);
            this.PathsTab.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TabControl Tabs;
        private System.Windows.Forms.TabPage CaptureTab;
        private System.Windows.Forms.TabPage PathsTab;
        private System.Windows.Forms.Label IntervalLabel;
        private System.Windows.Forms.NumericUpDown IntervalBox;
        private System.Windows.Forms.CheckBox SkipScreensaverBox;
        private System.Windows.Forms.Label IdlePauseLabel;
        private System.Windows.Forms.NumericUpDown IdlePauseBox;
        private System.Windows.Forms.Label FocusDwellLabel;
        private System.Windows.Forms.NumericUpDown FocusDwellBox;
        private System.Windows.Forms.CheckBox DiscardBlankBox;
        private System.Windows.Forms.CheckBox DiscardIdleBox;
        private System.Windows.Forms.Label IdleDirtyCellsLabel;
        private System.Windows.Forms.NumericUpDown IdleDirtyCellsBox;
        private System.Windows.Forms.Label FormatLabel;
        private System.Windows.Forms.ComboBox CaptureFormatBox;
        private System.Windows.Forms.Label QualityLabel;
        private System.Windows.Forms.NumericUpDown QualityBox;
        private System.Windows.Forms.CheckBox CaptureFullBox;
        private System.Windows.Forms.Label DesktopScaleLabel;
        private System.Windows.Forms.Panel DesktopScalePanel;
        private System.Windows.Forms.CheckBox CaptureWindowBox;
        private System.Windows.Forms.CheckBox CaptureMonitorsBox;
        private System.Windows.Forms.Label MonitorPickLabel;
        private System.Windows.Forms.Panel MonitorListPanel;
        private System.Windows.Forms.Label ApiUrlLabel;
        private System.Windows.Forms.TextBox ApiUrlBox;
        private System.Windows.Forms.Label SpoolLabel;
        private System.Windows.Forms.TextBox SpoolDirBox;
        private System.Windows.Forms.Label MutationsLabel;
        private System.Windows.Forms.TextBox MutationsDirBox;
        private System.Windows.Forms.Label LogLevelLabel;
        private System.Windows.Forms.ComboBox FileLogLevelBox;
        private System.Windows.Forms.Label InfoText;
        private System.Windows.Forms.Button SaveButton;
        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.TableLayoutPanel CaptureLayout;
    }
}

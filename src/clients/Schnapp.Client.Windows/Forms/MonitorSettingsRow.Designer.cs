namespace Schnapp.Client.Windows.Forms
{
    partial class MonitorSettingsRow
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
            this.CaptureCheck = new System.Windows.Forms.CheckBox();
            this.MonitorDescriptionLabel = new System.Windows.Forms.Label();
            this.ScaleHintLabel = new System.Windows.Forms.Label();
            this.OutputSlider = new System.Windows.Forms.TrackBar();
            this.OutputLabel = new System.Windows.Forms.Label();
            this.QualityOverrideCheck = new System.Windows.Forms.CheckBox();
            this.QualityBox = new System.Windows.Forms.NumericUpDown();
            this.RowLayout = new System.Windows.Forms.FlowLayoutPanel();
            ((System.ComponentModel.ISupportInitialize)(this.OutputSlider)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.QualityBox)).BeginInit();
            this.RowLayout.SuspendLayout();
            this.SuspendLayout();
            // 
            // CaptureCheck
            // 
            this.CaptureCheck.AutoSize = true;
            this.RowLayout.SetFlowBreak(this.CaptureCheck, true);
            this.CaptureCheck.Location = new System.Drawing.Point(3, 3);
            this.CaptureCheck.Name = "CaptureCheck";
            this.CaptureCheck.Size = new System.Drawing.Size(61, 17);
            this.CaptureCheck.TabIndex = 0;
            this.CaptureCheck.Text = "Monitor";
            this.CaptureCheck.UseVisualStyleBackColor = true;
            // 
            // MonitorDescriptionLabel
            // 
            this.MonitorDescriptionLabel.AutoSize = true;
            this.RowLayout.SetFlowBreak(this.MonitorDescriptionLabel, true);
            this.MonitorDescriptionLabel.Location = new System.Drawing.Point(3, 26);
            this.MonitorDescriptionLabel.Margin = new System.Windows.Forms.Padding(3);
            this.MonitorDescriptionLabel.Name = "MonitorDescriptionLabel";
            this.MonitorDescriptionLabel.Size = new System.Drawing.Size(58, 13);
            this.MonitorDescriptionLabel.TabIndex = 1;
            this.MonitorDescriptionLabel.Text = "description";
            // 
            // ScaleHintLabel
            // 
            this.ScaleHintLabel.AutoSize = true;
            this.ScaleHintLabel.Location = new System.Drawing.Point(3, 45);
            this.ScaleHintLabel.Margin = new System.Windows.Forms.Padding(3, 3, 3, 0);
            this.ScaleHintLabel.Name = "ScaleHintLabel";
            this.ScaleHintLabel.Size = new System.Drawing.Size(72, 13);
            this.ScaleHintLabel.TabIndex = 3;
            this.ScaleHintLabel.Text = "Capture scale";
            this.ScaleHintLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // OutputSlider
            // 
            this.OutputSlider.AutoSize = false;
            this.RowLayout.SetFlowBreak(this.OutputSlider, true);
            this.OutputSlider.LargeChange = 1;
            this.OutputSlider.Location = new System.Drawing.Point(3, 64);
            this.OutputSlider.Margin = new System.Windows.Forms.Padding(3, 3, 3, 0);
            this.OutputSlider.Name = "OutputSlider";
            this.OutputSlider.Size = new System.Drawing.Size(360, 28);
            this.OutputSlider.TabIndex = 4;
            this.OutputSlider.ValueChanged += new System.EventHandler(this.OutputSlider_ValueChanged);
            // 
            // OutputLabel
            // 
            this.OutputLabel.AutoSize = true;
            this.RowLayout.SetFlowBreak(this.OutputLabel, true);
            this.OutputLabel.Location = new System.Drawing.Point(81, 45);
            this.OutputLabel.Margin = new System.Windows.Forms.Padding(3);
            this.OutputLabel.Name = "OutputLabel";
            this.OutputLabel.Size = new System.Drawing.Size(110, 13);
            this.OutputLabel.TabIndex = 5;
            this.OutputLabel.Text = "100% → 1920 x 1080";
            this.OutputLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // QualityOverrideCheck
            // 
            this.QualityOverrideCheck.AutoSize = true;
            this.QualityOverrideCheck.Location = new System.Drawing.Point(3, 95);
            this.QualityOverrideCheck.Name = "QualityOverrideCheck";
            this.QualityOverrideCheck.Size = new System.Drawing.Size(99, 17);
            this.QualityOverrideCheck.TabIndex = 6;
            this.QualityOverrideCheck.Text = "Quality override";
            this.QualityOverrideCheck.UseVisualStyleBackColor = true;
            this.QualityOverrideCheck.CheckedChanged += new System.EventHandler(this.QualityOverrideCheck_CheckedChanged);
            // 
            // QualityBox
            // 
            this.QualityBox.Enabled = false;
            this.QualityBox.Location = new System.Drawing.Point(108, 95);
            this.QualityBox.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.QualityBox.Name = "QualityBox";
            this.QualityBox.Size = new System.Drawing.Size(56, 20);
            this.QualityBox.TabIndex = 7;
            this.QualityBox.Value = new decimal(new int[] {
            80,
            0,
            0,
            0});
            // 
            // RowLayout
            // 
            this.RowLayout.AutoSize = true;
            this.RowLayout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.RowLayout.Controls.Add(this.CaptureCheck);
            this.RowLayout.Controls.Add(this.MonitorDescriptionLabel);
            this.RowLayout.Controls.Add(this.ScaleHintLabel);
            this.RowLayout.Controls.Add(this.OutputLabel);
            this.RowLayout.Controls.Add(this.OutputSlider);
            this.RowLayout.Controls.Add(this.QualityOverrideCheck);
            this.RowLayout.Controls.Add(this.QualityBox);
            this.RowLayout.Location = new System.Drawing.Point(0, 0);
            this.RowLayout.MaximumSize = new System.Drawing.Size(400, 0);
            this.RowLayout.Name = "RowLayout";
            this.RowLayout.Size = new System.Drawing.Size(366, 118);
            this.RowLayout.TabIndex = 0;
            // 
            // MonitorSettingsRow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Controls.Add(this.RowLayout);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "MonitorSettingsRow";
            this.Size = new System.Drawing.Size(369, 121);
            ((System.ComponentModel.ISupportInitialize)(this.OutputSlider)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.QualityBox)).EndInit();
            this.RowLayout.ResumeLayout(false);
            this.RowLayout.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox CaptureCheck;
        private System.Windows.Forms.Label MonitorDescriptionLabel;
        private System.Windows.Forms.Label ScaleHintLabel;
        private System.Windows.Forms.TrackBar OutputSlider;
        private System.Windows.Forms.Label OutputLabel;
        private System.Windows.Forms.CheckBox QualityOverrideCheck;
        private System.Windows.Forms.NumericUpDown QualityBox;
        private System.Windows.Forms.FlowLayoutPanel RowLayout;
    }
}

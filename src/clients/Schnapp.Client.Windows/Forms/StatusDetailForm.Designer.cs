namespace Schnapp.Client.Windows.Forms
{
    partial class StatusDetailForm
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
            this.IconPicture = new System.Windows.Forms.PictureBox();
            this.ProductTitle = new System.Windows.Forms.Label();
            this.VersionText = new System.Windows.Forms.Label();
            this.AboutText = new System.Windows.Forms.TextBox();
            this.StatusHeading = new System.Windows.Forms.Label();
            this.StatusLevelDot = new System.Windows.Forms.Panel();
            this.StatusTitle = new System.Windows.Forms.Label();
            this.StatusTime = new System.Windows.Forms.Label();
            this.DetailText = new System.Windows.Forms.TextBox();
            this.CloseButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.IconPicture)).BeginInit();
            this.SuspendLayout();
            // 
            // IconPicture
            // 
            this.IconPicture.Location = new System.Drawing.Point(19, 18);
            this.IconPicture.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.IconPicture.Name = "IconPicture";
            this.IconPicture.Size = new System.Drawing.Size(56, 55);
            this.IconPicture.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.IconPicture.TabIndex = 0;
            this.IconPicture.TabStop = false;
            // 
            // ProductTitle
            // 
            this.ProductTitle.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ProductTitle.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.ProductTitle.Location = new System.Drawing.Point(89, 18);
            this.ProductTitle.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.ProductTitle.Name = "ProductTitle";
            this.ProductTitle.Size = new System.Drawing.Size(415, 28);
            this.ProductTitle.TabIndex = 1;
            this.ProductTitle.Text = "Schnapp Client for Windows";
            // 
            // VersionText
            // 
            this.VersionText.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.VersionText.ForeColor = System.Drawing.SystemColors.GrayText;
            this.VersionText.Location = new System.Drawing.Point(89, 48);
            this.VersionText.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.VersionText.Name = "VersionText";
            this.VersionText.Size = new System.Drawing.Size(415, 21);
            this.VersionText.TabIndex = 2;
            this.VersionText.Text = "Version";
            // 
            // AboutText
            // 
            this.AboutText.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.AboutText.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.AboutText.Cursor = System.Windows.Forms.Cursors.Default;
            this.AboutText.Location = new System.Drawing.Point(19, 88);
            this.AboutText.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.AboutText.Multiline = true;
            this.AboutText.Name = "AboutText";
            this.AboutText.ReadOnly = true;
            this.AboutText.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.AboutText.Size = new System.Drawing.Size(485, 109);
            this.AboutText.TabIndex = 3;
            this.AboutText.Text = "About";
            // 
            // StatusHeading
            // 
            this.StatusHeading.AutoSize = true;
            this.StatusHeading.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.StatusHeading.Location = new System.Drawing.Point(19, 226);
            this.StatusHeading.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.StatusHeading.Name = "StatusHeading";
            this.StatusHeading.Size = new System.Drawing.Size(86, 15);
            this.StatusHeading.TabIndex = 4;
            this.StatusHeading.Text = "Current status";
            // 
            // StatusLevelDot
            // 
            this.StatusLevelDot.Location = new System.Drawing.Point(19, 254);
            this.StatusLevelDot.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.StatusLevelDot.Name = "StatusLevelDot";
            this.StatusLevelDot.Size = new System.Drawing.Size(14, 14);
            this.StatusLevelDot.TabIndex = 5;
            // 
            // StatusTitle
            // 
            this.StatusTitle.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StatusTitle.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            this.StatusTitle.Location = new System.Drawing.Point(42, 249);
            this.StatusTitle.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.StatusTitle.Name = "StatusTitle";
            this.StatusTitle.Size = new System.Drawing.Size(462, 23);
            this.StatusTitle.TabIndex = 6;
            this.StatusTitle.Text = "Status";
            // 
            // StatusTime
            // 
            this.StatusTime.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StatusTime.ForeColor = System.Drawing.SystemColors.GrayText;
            this.StatusTime.Location = new System.Drawing.Point(42, 272);
            this.StatusTime.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.StatusTime.Name = "StatusTime";
            this.StatusTime.Size = new System.Drawing.Size(462, 18);
            this.StatusTime.TabIndex = 7;
            this.StatusTime.Text = "Time";
            // 
            // DetailText
            // 
            this.DetailText.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DetailText.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.DetailText.Cursor = System.Windows.Forms.Cursors.Default;
            this.DetailText.Location = new System.Drawing.Point(19, 301);
            this.DetailText.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.DetailText.Multiline = true;
            this.DetailText.Name = "DetailText";
            this.DetailText.ReadOnly = true;
            this.DetailText.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.DetailText.Size = new System.Drawing.Size(485, 127);
            this.DetailText.TabIndex = 8;
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CloseButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.CloseButton.Location = new System.Drawing.Point(416, 434);
            this.CloseButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(88, 32);
            this.CloseButton.TabIndex = 0;
            this.CloseButton.Text = "Close";
            this.CloseButton.UseVisualStyleBackColor = true;
            this.CloseButton.Click += new System.EventHandler(this.CloseButton_Click);
            // 
            // StatusDetailForm
            // 
            this.AcceptButton = this.CloseButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(523, 480);
            this.Controls.Add(this.CloseButton);
            this.Controls.Add(this.DetailText);
            this.Controls.Add(this.StatusTime);
            this.Controls.Add(this.StatusTitle);
            this.Controls.Add(this.StatusLevelDot);
            this.Controls.Add(this.StatusHeading);
            this.Controls.Add(this.AboutText);
            this.Controls.Add(this.VersionText);
            this.Controls.Add(this.ProductTitle);
            this.Controls.Add(this.IconPicture);
            this.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(487, 409);
            this.Name = "StatusDetailForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "About Schnapp";
            ((System.ComponentModel.ISupportInitialize)(this.IconPicture)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox IconPicture;
        private System.Windows.Forms.Label ProductTitle;
        private System.Windows.Forms.Label VersionText;
        private System.Windows.Forms.TextBox AboutText;
        private System.Windows.Forms.Label StatusHeading;
        private System.Windows.Forms.Panel StatusLevelDot;
        private System.Windows.Forms.Label StatusTitle;
        private System.Windows.Forms.Label StatusTime;
        private System.Windows.Forms.TextBox DetailText;
        private System.Windows.Forms.Button CloseButton;
    }
}

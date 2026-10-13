namespace Schnapp.Client.Windows.Forms
{
    partial class MachinesForm
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
            this.DefaultText = new System.Windows.Forms.Label();
            this.IdentityText = new System.Windows.Forms.Label();
            this.NameLabel = new System.Windows.Forms.Label();
            this.NameBox = new System.Windows.Forms.TextBox();
            this.MachineList = new System.Windows.Forms.ListBox();
            this.RefreshButton = new System.Windows.Forms.Button();
            this.RegisterButton = new System.Windows.Forms.Button();
            this.FindButton = new System.Windows.Forms.Button();
            this.UseButton = new System.Windows.Forms.Button();
            this.CloseButton = new System.Windows.Forms.Button();
            this.StatusText = new System.Windows.Forms.Label();
            this.ErrorText = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // DefaultText
            // 
            this.DefaultText.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DefaultText.Location = new System.Drawing.Point(12, 12);
            this.DefaultText.Name = "DefaultText";
            this.DefaultText.Size = new System.Drawing.Size(410, 20);
            this.DefaultText.TabIndex = 0;
            this.DefaultText.Text = "In use: (none)";
            // 
            // IdentityText
            // 
            this.IdentityText.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.IdentityText.Location = new System.Drawing.Point(12, 36);
            this.IdentityText.Name = "IdentityText";
            this.IdentityText.Size = new System.Drawing.Size(410, 36);
            this.IdentityText.TabIndex = 1;
            // 
            // NameLabel
            // 
            this.NameLabel.AutoSize = true;
            this.NameLabel.Location = new System.Drawing.Point(12, 80);
            this.NameLabel.Name = "NameLabel";
            this.NameLabel.Size = new System.Drawing.Size(77, 13);
            this.NameLabel.TabIndex = 2;
            this.NameLabel.Text = "Machine name";
            // 
            // NameBox
            // 
            this.NameBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NameBox.Location = new System.Drawing.Point(12, 96);
            this.NameBox.Name = "NameBox";
            this.NameBox.Size = new System.Drawing.Size(410, 20);
            this.NameBox.TabIndex = 3;
            // 
            // MachineList
            // 
            this.MachineList.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.MachineList.FormattingEnabled = true;
            this.MachineList.IntegralHeight = false;
            this.MachineList.Location = new System.Drawing.Point(12, 128);
            this.MachineList.Name = "MachineList";
            this.MachineList.Size = new System.Drawing.Size(410, 185);
            this.MachineList.TabIndex = 4;
            // 
            // RefreshButton
            // 
            this.RefreshButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.RefreshButton.Location = new System.Drawing.Point(12, 370);
            this.RefreshButton.Name = "RefreshButton";
            this.RefreshButton.Size = new System.Drawing.Size(75, 28);
            this.RefreshButton.TabIndex = 5;
            this.RefreshButton.Text = "Refresh";
            this.RefreshButton.UseVisualStyleBackColor = true;
            this.RefreshButton.Click += new System.EventHandler(this.RefreshButton_Click);
            // 
            // RegisterButton
            // 
            this.RegisterButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.RegisterButton.Location = new System.Drawing.Point(93, 370);
            this.RegisterButton.Name = "RegisterButton";
            this.RegisterButton.Size = new System.Drawing.Size(75, 28);
            this.RegisterButton.TabIndex = 6;
            this.RegisterButton.Text = "Register";
            this.RegisterButton.UseVisualStyleBackColor = true;
            this.RegisterButton.Click += new System.EventHandler(this.RegisterButton_Click);
            // 
            // FindButton
            // 
            this.FindButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.FindButton.Location = new System.Drawing.Point(174, 370);
            this.FindButton.Name = "FindButton";
            this.FindButton.Size = new System.Drawing.Size(75, 28);
            this.FindButton.TabIndex = 7;
            this.FindButton.Text = "Find";
            this.FindButton.UseVisualStyleBackColor = true;
            this.FindButton.Click += new System.EventHandler(this.FindButton_Click);
            // 
            // UseButton
            // 
            this.UseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.UseButton.Location = new System.Drawing.Point(255, 370);
            this.UseButton.Name = "UseButton";
            this.UseButton.Size = new System.Drawing.Size(75, 28);
            this.UseButton.TabIndex = 8;
            this.UseButton.Text = "Use";
            this.UseButton.UseVisualStyleBackColor = true;
            this.UseButton.Click += new System.EventHandler(this.UseButton_Click);
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CloseButton.Location = new System.Drawing.Point(347, 370);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(75, 28);
            this.CloseButton.TabIndex = 9;
            this.CloseButton.Text = "Close";
            this.CloseButton.UseVisualStyleBackColor = true;
            this.CloseButton.Click += new System.EventHandler(this.CloseButton_Click);
            // 
            // StatusText
            // 
            this.StatusText.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StatusText.Location = new System.Drawing.Point(12, 340);
            this.StatusText.Name = "StatusText";
            this.StatusText.Size = new System.Drawing.Size(410, 20);
            this.StatusText.TabIndex = 10;
            // 
            // ErrorText
            // 
            this.ErrorText.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ErrorText.ForeColor = System.Drawing.Color.Firebrick;
            this.ErrorText.Location = new System.Drawing.Point(12, 316);
            this.ErrorText.Name = "ErrorText";
            this.ErrorText.Size = new System.Drawing.Size(410, 20);
            this.ErrorText.TabIndex = 11;
            this.ErrorText.Visible = false;
            // 
            // MachinesForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(434, 411);
            this.Controls.Add(this.ErrorText);
            this.Controls.Add(this.StatusText);
            this.Controls.Add(this.CloseButton);
            this.Controls.Add(this.UseButton);
            this.Controls.Add(this.FindButton);
            this.Controls.Add(this.RegisterButton);
            this.Controls.Add(this.RefreshButton);
            this.Controls.Add(this.MachineList);
            this.Controls.Add(this.NameBox);
            this.Controls.Add(this.NameLabel);
            this.Controls.Add(this.IdentityText);
            this.Controls.Add(this.DefaultText);
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(450, 450);
            this.Name = "MachinesForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "Schnapp machines";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label DefaultText;
        private System.Windows.Forms.Label IdentityText;
        private System.Windows.Forms.Label NameLabel;
        private System.Windows.Forms.TextBox NameBox;
        private System.Windows.Forms.ListBox MachineList;
        private System.Windows.Forms.Button RefreshButton;
        private System.Windows.Forms.Button RegisterButton;
        private System.Windows.Forms.Button FindButton;
        private System.Windows.Forms.Button UseButton;
        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.Label StatusText;
        private System.Windows.Forms.Label ErrorText;
    }
}

namespace Schnapp.Client.Windows.Forms
{
    partial class TagsInputControl
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.Box = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            // 
            // Box
            // 
            this.Box.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Box.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Box.Location = new System.Drawing.Point(0, 0);
            this.Box.Name = "Box";
            this.Box.Size = new System.Drawing.Size(200, 20);
            this.Box.TabIndex = 0;
            // 
            // TagsInputControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Box);
            this.MinimumSize = new System.Drawing.Size(40, 22);
            this.Name = "TagsInputControl";
            this.Size = new System.Drawing.Size(200, 22);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        /// <summary>Primary tags editor (designer surface).</summary>
        private System.Windows.Forms.TextBox Box;
    }
}

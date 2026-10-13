namespace Schnapp.Client.Windows.Forms
{
    partial class TrayShellForm
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
            this.notifyIcon = new System.Windows.Forms.NotifyIcon(this.components);
            this.TrayMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.StatusItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.LoginItem = new System.Windows.Forms.ToolStripMenuItem();
            this.SettingsItem = new System.Windows.Forms.ToolStripMenuItem();
            this.MachinesItem = new System.Windows.Forms.ToolStripMenuItem();
            this.QuitItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.ViewCacheButton = new System.Windows.Forms.ToolStripMenuItem();
            this.UploadsPauseItem = new System.Windows.Forms.ToolStripMenuItem();
            this.PushItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.NoteItem = new System.Windows.Forms.ToolStripMenuItem();
            this.TagsItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator5 = new System.Windows.Forms.ToolStripSeparator();
            this.CustomerItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ProjectItem = new System.Windows.Forms.ToolStripMenuItem();
            this.TaskItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator6 = new System.Windows.Forms.ToolStripSeparator();
            this.OpenMainItem = new System.Windows.Forms.ToolStripMenuItem();
            this.JournalTargetItem = new System.Windows.Forms.ToolStripMenuItem();
            this.JournalNoneItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator7 = new System.Windows.Forms.ToolStripSeparator();
            this.PauseCaptureItem = new System.Windows.Forms.ToolStripMenuItem();
            this.CaptureNowItem = new System.Windows.Forms.ToolStripMenuItem();
            this.TrayMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // notifyIcon
            // 
            this.notifyIcon.ContextMenuStrip = this.TrayMenu;
            this.notifyIcon.Text = "Schnapp";
            this.notifyIcon.Visible = true;
            this.notifyIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(this.NotifyIcon_MouseClick);
            this.notifyIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.NotifyIcon_MouseDoubleClick);
            // 
            // TrayMenu
            // 
            this.TrayMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.StatusItem,
            this.toolStripSeparator1,
            this.LoginItem,
            this.SettingsItem,
            this.MachinesItem,
            this.QuitItem,
            this.toolStripSeparator2,
            this.ViewCacheButton,
            this.UploadsPauseItem,
            this.PushItem,
            this.toolStripSeparator3,
            this.NoteItem,
            this.TagsItem,
            this.toolStripSeparator5,
            this.CustomerItem,
            this.ProjectItem,
            this.TaskItem,
            this.toolStripSeparator6,
            this.OpenMainItem,
            this.JournalTargetItem,
            this.toolStripSeparator7,
            this.PauseCaptureItem,
            this.CaptureNowItem});
            this.TrayMenu.Name = "TrayMenu";
            this.TrayMenu.Size = new System.Drawing.Size(156, 414);
            // 
            // StatusItem
            // 
            this.StatusItem.Name = "StatusItem";
            this.StatusItem.Size = new System.Drawing.Size(155, 22);
            this.StatusItem.Text = "⚪  Starting…";
            this.StatusItem.Click += new System.EventHandler(this.StatusItem_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(152, 6);
            // 
            // LoginItem
            // 
            this.LoginItem.Name = "LoginItem";
            this.LoginItem.Size = new System.Drawing.Size(155, 22);
            this.LoginItem.Text = "Login…";
            this.LoginItem.Click += new System.EventHandler(this.LoginItem_Click);
            // 
            // SettingsItem
            // 
            this.SettingsItem.Name = "SettingsItem";
            this.SettingsItem.Size = new System.Drawing.Size(155, 22);
            this.SettingsItem.Text = "Settings…";
            this.SettingsItem.Click += new System.EventHandler(this.SettingsItem_Click);
            // 
            // MachinesItem
            // 
            this.MachinesItem.Name = "MachinesItem";
            this.MachinesItem.Size = new System.Drawing.Size(155, 22);
            this.MachinesItem.Text = "Machines…";
            this.MachinesItem.Click += new System.EventHandler(this.MachinesItem_Click);
            // 
            // QuitItem
            // 
            this.QuitItem.Name = "QuitItem";
            this.QuitItem.Size = new System.Drawing.Size(155, 22);
            this.QuitItem.Text = "Quit";
            this.QuitItem.Click += new System.EventHandler(this.QuitItem_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(152, 6);
            // 
            // ViewCacheButton
            // 
            this.ViewCacheButton.Name = "ViewCacheButton";
            this.ViewCacheButton.Size = new System.Drawing.Size(155, 22);
            this.ViewCacheButton.Text = "View ...";
            this.ViewCacheButton.Click += new System.EventHandler(this.ViewCache_Click);
            // 
            // UploadsPauseItem
            // 
            this.UploadsPauseItem.Name = "UploadsPauseItem";
            this.UploadsPauseItem.Size = new System.Drawing.Size(155, 22);
            this.UploadsPauseItem.Text = "Pause uploads";
            this.UploadsPauseItem.Click += new System.EventHandler(this.UploadsPauseItem_Click);
            // 
            // PushItem
            // 
            this.PushItem.Name = "PushItem";
            this.PushItem.Size = new System.Drawing.Size(155, 22);
            this.PushItem.Text = "Start Sync";
            this.PushItem.Click += new System.EventHandler(this.PushItem_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new System.Drawing.Size(152, 6);
            // 
            // NoteItem
            // 
            this.NoteItem.Name = "NoteItem";
            this.NoteItem.Size = new System.Drawing.Size(155, 22);
            this.NoteItem.Text = "Note";
            this.NoteItem.Click += new System.EventHandler(this.NoteItem_Click);
            // 
            // TagsItem
            // 
            this.TagsItem.Name = "TagsItem";
            this.TagsItem.Size = new System.Drawing.Size(155, 22);
            this.TagsItem.Text = "Tags";
            this.TagsItem.Click += new System.EventHandler(this.TagsItem_Click);
            // 
            // toolStripSeparator5
            // 
            this.toolStripSeparator5.Name = "toolStripSeparator5";
            this.toolStripSeparator5.Size = new System.Drawing.Size(152, 6);
            // 
            // CustomerItem
            // 
            this.CustomerItem.Name = "CustomerItem";
            this.CustomerItem.Size = new System.Drawing.Size(155, 22);
            this.CustomerItem.Text = "Customer";
            // 
            // ProjectItem
            // 
            this.ProjectItem.Name = "ProjectItem";
            this.ProjectItem.Size = new System.Drawing.Size(155, 22);
            this.ProjectItem.Text = "Project";
            // 
            // TaskItem
            // 
            this.TaskItem.Name = "TaskItem";
            this.TaskItem.Size = new System.Drawing.Size(155, 22);
            this.TaskItem.Text = "Task";
            // 
            // toolStripSeparator6
            // 
            this.toolStripSeparator6.Name = "toolStripSeparator6";
            this.toolStripSeparator6.Size = new System.Drawing.Size(152, 6);
            // 
            // OpenMainItem
            // 
            this.OpenMainItem.Name = "OpenMainItem";
            this.OpenMainItem.Size = new System.Drawing.Size(155, 22);
            this.OpenMainItem.Text = "Journal target…";
            this.OpenMainItem.Click += new System.EventHandler(this.OpenMainItem_Click);
            // 
            // JournalTargetItem
            // 
            this.JournalTargetItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.JournalNoneItem});
            this.JournalTargetItem.Name = "JournalTargetItem";
            this.JournalTargetItem.Size = new System.Drawing.Size(155, 22);
            this.JournalTargetItem.Text = "Journal target";
            // 
            // JournalNoneItem
            // 
            this.JournalNoneItem.Name = "JournalNoneItem";
            this.JournalNoneItem.Size = new System.Drawing.Size(103, 22);
            this.JournalNoneItem.Text = "None";
            this.JournalNoneItem.Click += new System.EventHandler(this.JournalNoneItem_Click);
            // 
            // toolStripSeparator7
            // 
            this.toolStripSeparator7.Name = "toolStripSeparator7";
            this.toolStripSeparator7.Size = new System.Drawing.Size(152, 6);
            // 
            // PauseCaptureItem
            // 
            this.PauseCaptureItem.Name = "PauseCaptureItem";
            this.PauseCaptureItem.Size = new System.Drawing.Size(155, 22);
            this.PauseCaptureItem.Text = "Pause capture";
            this.PauseCaptureItem.Click += new System.EventHandler(this.PauseCaptureItem_Click);
            // 
            // CaptureNowItem
            // 
            this.CaptureNowItem.Name = "CaptureNowItem";
            this.CaptureNowItem.Size = new System.Drawing.Size(155, 22);
            this.CaptureNowItem.Text = "Capture now";
            this.CaptureNowItem.Click += new System.EventHandler(this.CaptureNowItem_Click);
            // 
            // TrayShellForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(218, 107);
            this.Name = "TrayShellForm";
            this.Text = "Schnapp";
            this.TrayMenu.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.NotifyIcon notifyIcon;
        private System.Windows.Forms.ContextMenuStrip TrayMenu;
        private System.Windows.Forms.ToolStripMenuItem StatusItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem LoginItem;
        private System.Windows.Forms.ToolStripMenuItem SettingsItem;
        private System.Windows.Forms.ToolStripMenuItem MachinesItem;
        private System.Windows.Forms.ToolStripMenuItem QuitItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem ViewCacheButton;
        private System.Windows.Forms.ToolStripMenuItem UploadsPauseItem;
        private System.Windows.Forms.ToolStripMenuItem PushItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripMenuItem PauseCaptureItem;
        private System.Windows.Forms.ToolStripMenuItem OpenMainItem;
        private System.Windows.Forms.ToolStripMenuItem NoteItem;
        private System.Windows.Forms.ToolStripMenuItem TagsItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator5;
        private System.Windows.Forms.ToolStripMenuItem CustomerItem;
        private System.Windows.Forms.ToolStripMenuItem ProjectItem;
        private System.Windows.Forms.ToolStripMenuItem TaskItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator6;
        private System.Windows.Forms.ToolStripMenuItem JournalTargetItem;
        private System.Windows.Forms.ToolStripMenuItem JournalNoneItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator7;
        private System.Windows.Forms.ToolStripMenuItem CaptureNowItem;
    }
}

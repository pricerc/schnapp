namespace Schnapp.Client.Windows.Forms;

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

using Schnapp.Client.Core.Config;
using Schnapp.Client.Windows.Services;

/// <summary>
/// About dialog for the Windows client: product information first, then current tray status.
/// </summary>
/// <remarks>
/// Opened from the tray status menu item. Keeps the historical type name
/// <see cref="StatusDetailForm"/> so existing call sites need not change.
/// </remarks>
public partial class StatusDetailForm : Form
{
    /// <summary>
    /// Creates an About dialog showing program identity and the given status snapshot.
    /// </summary>
    /// <param name="status">Current tray status (may be null for product info only).</param>
    public StatusDetailForm(TrayStatus? status)
    {
        InitializeComponent();
        PopulateAbout();
        PopulateStatus(status);
        CloseButton.Focus();
    }

    /// <summary>
    /// Fills product name, version, paths, and runtime details from assembly metadata.
    /// </summary>
    private void PopulateAbout()
    {
        var asm = typeof(StatusDetailForm).Assembly;
        var asmName = asm.GetName();
        var version = asmName.Version != null
            ? asmName.Version.ToString()
            : "unknown";

        string product = "Schnapp Client for Windows";
        string description = "Desktop capture client for Schnapp.";
        try
        {
            // Because FileVersionInfo reads the PE resources the user sees in Explorer properties.
            var path = asm.Location;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var fvi = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(fvi.ProductName))
                {
                    product = fvi.ProductName;
                }

                if (!string.IsNullOrWhiteSpace(fvi.FileDescription))
                {
                    description = fvi.FileDescription;
                }

                // Because ProductVersion often includes the marketing/semver string; FileVersion is fallback.
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                {
                    version = fvi.ProductVersion;
                }
                else if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                {
                    version = fvi.FileVersion;
                }
            }
        }
        catch
        {
            // Assembly metadata is optional; keep defaults.
        }

        Text = "About Schnapp";
        ProductTitle.Text = product;
        VersionText.Text = "Version " + version;

        // Because path helpers can throw if AppData is unavailable; still show the dialog.
        string configDir;
        string dataDir;
        try { configDir = ClientPaths.GetConfigDirectory(); }
        catch { configDir = "(unavailable)"; }
        try { dataDir = ClientPaths.GetDataDirectory(); }
        catch { dataDir = "(unavailable)"; }

        AboutText.Text =
            $"{description}{Environment.NewLine}{Environment.NewLine}Runtime: .NET Framework {Environment.Version}{Environment.NewLine}OS: {Environment.OSVersion}{Environment.NewLine}Config: {configDir}{Environment.NewLine}Data: {dataDir}";
        // Because assigning Text can select all; clear selection so the user sees the top.
        AboutText.SelectionStart = 0;
        AboutText.SelectionLength = 0;

        try
        {
            // Because the form icon is loaded from Assets at runtime, not embedded as a designer resource.
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app-icon.ico");
            if (File.Exists(iconPath))
            {
                using (var fs = File.OpenRead(iconPath))
                using (var ico = new Icon(fs, 48, 48))
                {
                    IconPicture.Image = ico.ToBitmap();
                }
                Icon = new Icon(iconPath);
            }
            else if (Icon != null)
            {
                IconPicture.Image = Icon.ToBitmap();
            }
        }
        catch
        {
            // Icon is decorative only.
        }
    }

    /// <summary>
    /// Fills the status section (severity of the About block) from a tray snapshot.
    /// </summary>
    /// <param name="status">Tray status to display, or null for an empty status section.</param>
    private void PopulateStatus(TrayStatus? status)
    {
        if (status == null)
        {
            StatusTitle.Text = "No status";
            StatusTime.Text = "";
            DetailText.Text = "";
            StatusLevelDot.BackColor = Color.Gray;
            return;
        }

        StatusTitle.Text = status.ShortLabel ?? "Status";
        StatusTime.Text = status.At.ToLocalTime().ToString("g");
        // Because detail may be blank for simple OK states; fall back to the short label.
        DetailText.Text = string.IsNullOrWhiteSpace(status.Detail)
            ? (status.ShortLabel ?? "")
            : status.Detail;
        DetailText.SelectionStart = 0;
        DetailText.SelectionLength = 0;

        StatusLevelDot.BackColor = LevelColor(status.Level);
    }

    /// <summary>Maps tray severity to a simple solid colour for the status dot.</summary>
    /// <param name="level">Tray severity level.</param>
    /// <returns>RGB colour used for the status indicator.</returns>
    private static Color LevelColor(TrayStatusLevel level)
    {
        // Because the palette mirrors common “traffic light” status hues used in the tray icons.
        return level switch
        {
            TrayStatusLevel.Ok => Color.FromArgb(0x98, 0xc3, 0x79),
            TrayStatusLevel.Warning => Color.FromArgb(0xe5, 0xc0, 0x7b),
            TrayStatusLevel.Error => Color.FromArgb(0xe0, 0x6c, 0x75),
            _ => Color.FromArgb(0x9a, 0xa8, 0xbc),
        };
    }

    /// <summary>Closes the dialog when the Close button is clicked.</summary>
    private void CloseButton_Click(object sender, EventArgs e) => Close();
}

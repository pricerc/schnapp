namespace Schnapp.Client.Linux.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

using Schnapp.Client.Linux.Services;

/// <summary>
/// Small dialog showing the full tray status detail (opened by clicking the status menu item).
/// </summary>
public partial class StatusDetailWindow : Window
{
    public StatusDetailWindow() => InitializeComponent();

    /// <summary>
    /// Creates a dialog bound to the given status.
    /// </summary>
    /// <param name="status">Current tray status.</param>
    public StatusDetailWindow(TrayStatus status) : this()
    {
        TitleText.Text = status.ShortLabel;
        TimeText.Text = status.At.ToLocalTime().ToString("G");
        DetailText.Text = string.IsNullOrWhiteSpace(status.Detail)
            ? status.ShortLabel
            : status.Detail;

        var color = status.Level switch
        {
            TrayStatusLevel.Ok => Color.Parse("#98c379"),
            TrayStatusLevel.Warning => Color.Parse("#e5c07b"),
            TrayStatusLevel.Error => Color.Parse("#e06c75"),
            _ => Color.Parse("#9aa8bc")
        };
        LevelDot.Background = new SolidColorBrush(color);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

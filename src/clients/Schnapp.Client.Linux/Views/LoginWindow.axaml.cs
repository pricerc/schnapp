namespace Schnapp.Client.Linux.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Linux.Services;
using System;
using Schnapp.Contracts;

/// <summary>
/// Login dialog: identity (email/code/name) + optional password against the API.
/// </summary>
/// <remarks>
/// Saves token and login identity to shared Client.Core config on success.
/// Opened at startup when no token is present, or from the tray menu.
/// </remarks>
public partial class LoginWindow : Window
{
    private readonly ClientSettingsStore _store = new();

    /// <summary>True if login succeeded and a token was stored.</summary>
    public bool Succeeded { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        var s = _store.Load();
        ServerBox.Text = s.ApiBaseUrl;
        LoginBox.Text = s.LoginIdentity ?? "";
        Opened += PositionNearTray;
    }

    private void PositionNearTray(object? sender, EventArgs e)
    {
        Opened -= PositionNearTray;
        var screens = Screens.All;
        if (screens.Count == 0)
        {
            return;
        }


        var screen = screens
            .OrderByDescending(s => s.Bounds.Right)
            .ThenByDescending(s => s.Bounds.Bottom)
            .First();

        var wa = screen.WorkingArea;
        double scale = screen.Scaling;
        double w = Width > 0 ? Width : 440;
        double h = Height > 0 ? Height : 420;
        int x = wa.Right - (int)(w * scale) - (int)(16 * scale);
        int y = wa.Bottom - (int)(h * scale) - (int)(48 * scale);
        x = Math.Max(wa.X, x);
        y = Math.Max(wa.Y, y);
        Position = new PixelPoint(x, y);
    }

    private async void OnLogin(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        var identity = LoginBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(identity))
        {
            ShowError("Enter email, code, or name.");
            return;
        }

        var server = ServerBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(server))
        {
            ShowError("API server URL is required.");
            return;
        }

        AppLog.Info($"Login attempt: identity='{identity}' server={server}");
        LoginButton.IsEnabled = false;
        StatusText.Text = "Signing in…";
        try
        {
            var settings = _store.Load();
            settings.ApiBaseUrl = server;
            _store.Save(settings);

            using var api = new SchnappApiClient(server);
            var password = string.IsNullOrEmpty(PasswordBox.Text) ? null : PasswordBox.Text;
            var login = await api.LoginAsync(identity, password).ConfigureAwait(true);

            _store.SaveToken(login.Token);
            settings.LoginIdentity = identity;
            _store.Save(settings);

            Succeeded = true;
            AppLog.Info($"Login success: user={login.User.Name} id={Uuid7Text.Format(login.User.Id)}");
            StatusText.Text = $"Signed in as {login.User.Name}";
            Close(true);
        }
        catch (ApiException ex)
        {
            AppLog.Warn($"Login ApiException {ex.StatusCode}: {ex.Message}");
            ShowError(ex.StatusCode == 401
                ? "Invalid login or password."
                : $"API error {ex.StatusCode}: {Truncate(ex.ResponseBody, 200)}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Login failed: " + ex.Message);
            ShowError("Could not reach API: " + ex.Message);
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.IsVisible = true;
        StatusText.Text = "";
    }

    private static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : string.Concat(s.AsSpan(0, n), "…"));
}

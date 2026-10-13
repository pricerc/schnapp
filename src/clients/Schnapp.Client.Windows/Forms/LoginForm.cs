namespace Schnapp.Client.Windows.Forms;

using System;
using System.Windows.Forms;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Windows.Services;

/// <summary>
/// Login dialog: identity (email/code/name) + optional password against the API.
/// </summary>
/// <remarks>
/// Saves token and login identity to shared Client.Core config on success.
/// Designer layout lives in <c>LoginForm.Designer.cs</c>.
/// </remarks>
public partial class LoginForm : Form
{
    private readonly ClientSettingsStore _store = new();

    /// <summary>True if login succeeded and a token was stored.</summary>
    public bool Succeeded { get; private set; }

    /// <summary>Creates the login dialog and loads saved identity/server.</summary>
    public LoginForm()
    {
        InitializeComponent();
        var s = _store.Load();
        ServerBox.Text = s.ApiBaseUrl;
        LoginBox.Text = s.LoginIdentity ?? "";
    }

    /// <inheritdoc />
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        WindowPlacement.RestoreOrPlaceNearTray(this, null);
    }

    private async void LoginButton_Click(object sender, EventArgs e)
    {
        ErrorText.Visible = false;
        var identity = (LoginBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(identity))
        {
            ShowError("Enter email, code, or name.");
            return;
        }

        var server = (ServerBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(server))
        {
            ShowError("API server URL is required.");
            return;
        }

        AppLog.Info("Login attempt: identity='" + identity + "' server=" + server);
        LoginButton.Enabled = false;
        StatusText.Text = "Signing in…";
        try
        {
            var settings = _store.Load();
            settings.ApiBaseUrl = server;
            _store.Save(settings);

            using (var api = new SchnappApiClient(server))
            {
                var password = string.IsNullOrEmpty(PasswordBox.Text) ? null : PasswordBox.Text;
                var login = await api.LoginAsync(identity, password).ConfigureAwait(true);

                _store.SaveToken(login.Token);
                settings.LoginIdentity = identity;
                _store.Save(settings);

                Succeeded = true;
                AppLog.Info("Login success: user=" + login.User.Name);
                StatusText.Text = "Signed in as " + login.User.Name;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (ApiException ex)
        {
            AppLog.Error("Login failed: " + ex.Message);
            ShowError(ex.Message);
            StatusText.Text = "";
        }
        catch (Exception ex)
        {
            AppLog.Error("Login failed: " + ex.Message);
            ShowError(ex.Message);
            StatusText.Text = "";
        }
        finally
        {
            LoginButton.Enabled = true;
        }
    }

    private void CancelBtn_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visible = true;
    }
}

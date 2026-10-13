namespace Schnapp.Client.Linux.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Machine;
using Schnapp.Client.Linux.Services;
using Schnapp.Contracts;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using Medo;

/// <summary>
/// List, register, find, and use a machine for capture/upload.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><b>Register</b> — create a new machine for this PC and select it.</description></item>
/// <item><description><b>Find</b> — search existing machines that match this PC's fingerprint/FQDN.</description></item>
/// <item><description><b>Use</b> — make the selected machine the default (links the account if needed).</description></item>
/// </list>
/// Shared Client.Core config with the CLI. Opened when no default machine is set, or from the tray.
/// </remarks>
public partial class MachinesWindow : Window
{
    private readonly ClientSettingsStore _store = new();
    private readonly DefaultMachineIdentity _identity = new();
    private readonly ObservableCollection<MachineRow> _rows = [];

    public MachinesWindow()
    {
        InitializeComponent();
        MachineList.ItemsSource = _rows;
        NameBox.Text = _identity.GetSuggestedName();
        IdentityText.Text =
            $"Detected FQDN: {_identity.GetFqdn()}\nFingerprint: {_identity.GetHardwareFingerprint()}";
        RefreshDefaultLabel();
        Opened += PositionNearTray;
        _ = LoadMachinesAsync();
    }

    private void PositionNearTray(object? sender, EventArgs e)
    {
        Opened -= PositionNearTray;
        WindowPlacement.RestoreOrPlaceNearTray(this, saved: null);
    }

    private void RefreshDefaultLabel()
    {
        var s = _store.Load();
        DefaultText.Text = s.MachineId.HasValue
            ? $"In use: {s.MachineName ?? s.MachineCode} ({s.MachineId})"
            : "In use: (none — register this PC, or Find and Use a match)";
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e) => await LoadMachinesAsync();

    private async Task LoadMachinesAsync()
    {
        ClearError();
        StatusText.Text = "Loading…";
        _rows.Clear();
        try
        {
            using var api = CreateApi();
            var list = await api.ListMachinesAsync();
            foreach (var m in list)
            {
                _rows.Add(new MachineRow
                {
                    Id = m.Id,
                    Title = m.Name,
                    Detail = $"code={m.Code}  fp={m.HardwareFingerprint}  fqdn={m.Fqdn}"
                });
            }
            StatusText.Text = list.Count == 0
                ? "No machines yet. Register this PC, or Find matches from a previous install."
                : $"{list.Count} machine(s). Select one and Use, or Register / Find.";
            RefreshDefaultLabel();
        }
        catch (Exception ex)
        {
            ShowError(FormatError(ex));
            StatusText.Text = "";
        }
    }

    private async void OnRegister(object? sender, RoutedEventArgs e)
    {
        ClearError();
        StatusText.Text = "Registering…";
        try
        {
            using var api = CreateApi();
            var name = string.IsNullOrWhiteSpace(NameBox.Text)
                ? _identity.GetSuggestedName()
                : NameBox.Text.Trim();
            AppLog.Info($"Register machine: name='{name}'");
            var m = await api.RegisterMachineAsync(new CreateMachineRequest
            {
                Name = name,
                Fqdn = _identity.GetFqdn(),
                HardwareFingerprint = _identity.GetHardwareFingerprint()
            });
            SelectDefault(m);
            AppLog.Info($"Registered machine id={Uuid7Text.Format(m.Id)} code={m.Code}");
            StatusText.Text = $"Registered and using {m.Name}";
            await LoadMachinesAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Register failed: " + ex.Message);
            ShowError(FormatError(ex));
            StatusText.Text = "";
        }
    }

    /// <summary>
    /// Find existing machines that look like this PC (fingerprint / FQDN).
    /// </summary>
    private async void OnFind(object? sender, RoutedEventArgs e)
    {
        ClearError();
        StatusText.Text = "Searching…";
        _rows.Clear();
        try
        {
            using var api = CreateApi();
            AppLog.Info("Find machines");
            var result = await api.FindMachinesAsync(new FindMachinesRequest
            {
                Fqdn = _identity.GetFqdn(),
                HardwareFingerprint = _identity.GetHardwareFingerprint()
            });
            var candidates = result.Candidates?.ToList() ?? [];
            foreach (var c in candidates)
            {
                _rows.Add(new MachineRow
                {
                    Id = c.Id,
                    Title = $"{c.Name} ({c.Confidence})",
                    Detail = $"match={c.Reason}  fp={c.HardwareFingerprint}  fqdn={c.Fqdn}"
                });
            }
            AppLog.Debug($"Find returned {candidates.Count} candidates");
            StatusText.Text = candidates.Count == 0
                ? "No matches. Try Register this PC."
                : "Select a match and Use.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Find failed: " + ex.Message);
            ShowError(FormatError(ex));
            StatusText.Text = "";
        }
    }

    /// <summary>
    /// Use the selected machine: ensure account is linked, refresh device identity, set as default.
    /// </summary>
    private async void OnUse(object? sender, RoutedEventArgs e)
    {
        ClearError();
        if (MachineList.SelectedItem is not MachineRow row)
        {
            ShowError("Select a machine in the list.");
            return;
        }

        AppLog.Info($"Use machine id={Uuid7Text.Format(row.Id)}");
        StatusText.Text = "Selecting…";
        try
        {
            using var api = CreateApi();
            // Link upserts user_machine and refreshes FQDN/fingerprint — works for list rows and Find results.
            var m = await api.LinkMachineAsync(row.Id, new CreateMachineRequest
            {
                Fqdn = _identity.GetFqdn(),
                HardwareFingerprint = _identity.GetHardwareFingerprint()
            });
            SelectDefault(m);
            AppLog.Info($"Using machine id={Uuid7Text.Format(m.Id)} name={m.Name}");
            StatusText.Text = $"Using {m.Name}";
            await LoadMachinesAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Use machine failed: " + ex.Message);
            ShowError(FormatError(ex));
            StatusText.Text = "";
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void SelectDefault(MachineResponse m)
    {
        var s = _store.Load();
        s.MachineId = m.Id;
        s.MachineCode = m.Code;
        s.MachineName = m.Name;
        _store.Save(s);
        RefreshDefaultLabel();
    }

    private SchnappApiClient CreateApi()
    {
        var s = _store.Load();
        var token = Environment.GetEnvironmentVariable("SCHNAPP_TOKEN") ?? _store.LoadToken();
        if (string.IsNullOrEmpty(token))
        {

            throw new InvalidOperationException("Not logged in. Open Login from the tray menu.");
        }


        return new SchnappApiClient(s.ApiBaseUrl, token);
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.IsVisible = true;
    }

    private void ClearError()
    {
        ErrorText.IsVisible = false;
        ErrorText.Text = "";
    }

    private static string FormatError(Exception ex) =>
        ex is ApiException ae
            ? $"API {ae.StatusCode}: {(ae.ResponseBody.Length > 180 ? ae.ResponseBody[..180] + "…" : ae.ResponseBody)}"
            : ex.Message;

    /// <summary>List row for a machine (from your list or Find results).</summary>
    public sealed class MachineRow
    {
        public Uuid7 Id { get; set; }
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
    }
}

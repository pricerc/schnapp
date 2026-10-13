namespace Schnapp.Client.Windows.Forms;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

using Medo;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Machine;
using Schnapp.Client.Windows.Machine;
using Schnapp.Client.Windows.Services;
using Schnapp.Contracts;

/// <summary>
/// List, register, find, and use a machine for capture/upload.
/// </summary>
/// <remarks>
/// Designer layout in <c>MachinesForm.Designer.cs</c>. Shared Client.Core config with the CLI.
/// </remarks>
public partial class MachinesForm : Form
{
    private readonly ClientSettingsStore _store = new();
    private readonly IMachineIdentity _identity = new WindowsMachineIdentity();
    private readonly List<MachineRow> _rows = [];

    /// <summary>Creates the machines dialog.</summary>
    public MachinesForm()
    {
        InitializeComponent();
        NameBox.Text = _identity.GetSuggestedName();
        IdentityText.Text =
            "Detected FQDN: " + _identity.GetFqdn() + Environment.NewLine
            + "Fingerprint: " + _identity.GetHardwareFingerprint();
        RefreshDefaultLabel();
    }

    /// <inheritdoc />
    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        WindowPlacement.RestoreOrPlaceNearTray(this, null);
        await LoadMachinesAsync();
    }

    private void RefreshDefaultLabel()
    {
        var s = _store.Load();
        DefaultText.Text = s.MachineId.HasValue
            ? "In use: " + (s.MachineName ?? s.MachineCode) + " (" + s.MachineId + ")"
            : "In use: (none — register this PC, or Find and Use a match)";
    }

    private async void RefreshButton_Click(object sender, EventArgs e) => await LoadMachinesAsync();

    private async Task LoadMachinesAsync()
    {
        ClearError();
        StatusText.Text = "Loading…";
        _rows.Clear();
        MachineList.Items.Clear();
        try
        {
            using (var api = CreateApi())
            {
                var list = await api.ListMachinesAsync();
                foreach (var m in list)
                {
                    var row = new MachineRow
                    {
                        Id = m.Id,
                        Title = m.Name,
                        Detail = "code=" + m.Code + "  fp=" + m.HardwareFingerprint + "  fqdn=" + m.Fqdn
                    };
                    _rows.Add(row);
                    MachineList.Items.Add(row);
                }
                StatusText.Text = list.Count == 0
                    ? "No machines yet. Register this PC, or Find matches from a previous install."
                    : list.Count + " machine(s). Select one and Use, or Register / Find.";
                RefreshDefaultLabel();
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            StatusText.Text = "";
        }
    }

    private async void RegisterButton_Click(object sender, EventArgs e)
    {
        ClearError();
        StatusText.Text = "Registering…";
        try
        {
            using (var api = CreateApi())
            {
                var name = string.IsNullOrWhiteSpace(NameBox.Text)
                    ? _identity.GetSuggestedName()
                    : NameBox.Text.Trim();
                AppLog.Info("Register machine: name='" + name + "'");
                var m = await api.RegisterMachineAsync(new CreateMachineRequest
                {
                    Name = name,
                    Fqdn = _identity.GetFqdn(),
                    HardwareFingerprint = _identity.GetHardwareFingerprint()
                });
                SelectDefault(m);
                AppLog.Info("Registered machine id=" + m.Id + " code=" + m.Code);
                StatusText.Text = "Registered and using " + m.Name;
                await LoadMachinesAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Register failed: " + ex.Message);
            ShowError(ex.Message);
            StatusText.Text = "";
        }
    }

    private async void FindButton_Click(object sender, EventArgs e)
    {
        ClearError();
        StatusText.Text = "Finding…";
        _rows.Clear();
        MachineList.Items.Clear();
        try
        {
            using (var api = CreateApi())
            {
                var result = await api.FindMachinesAsync(new FindMachinesRequest
                {
                    Name = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim(),
                    Fqdn = _identity.GetFqdn(),
                    HardwareFingerprint = _identity.GetHardwareFingerprint()
                });
                var candidates = result.Candidates ?? [];
                foreach (var c in candidates)
                {
                    var row = new MachineRow
                    {
                        Id = c.Id,
                        Title = c.Name + " [" + c.Confidence + "]",
                        Detail = "match=" + c.Reason + "  fp=" + c.HardwareFingerprint + "  fqdn=" + c.Fqdn
                    };
                    _rows.Add(row);
                    MachineList.Items.Add(row);
                }
                StatusText.Text = candidates.Count == 0
                    ? "No matches. Register this PC as a new machine."
                    : candidates.Count + " match(es). Select one and Use.";
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            StatusText.Text = "";
        }
    }

    private async void UseButton_Click(object sender, EventArgs e)
    {
        ClearError();
        if (MachineList.SelectedItem is not MachineRow row)
        {
            ShowError("Select a machine first.");
            return;
        }

        StatusText.Text = "Linking…";
        try
        {
            using (var api = CreateApi())
            {
                var m = await api.LinkMachineAsync(row.Id, new CreateMachineRequest
                {
                    Fqdn = _identity.GetFqdn(),
                    HardwareFingerprint = _identity.GetHardwareFingerprint()
                });
                SelectDefault(m);
                StatusText.Text = "Now using " + m.Name;
                RefreshDefaultLabel();
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            StatusText.Text = "";
        }
    }

    private void CloseButton_Click(object sender, EventArgs e) => Close();

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
            throw new InvalidOperationException("Not logged in.");
        }

        return new SchnappApiClient(s.ApiBaseUrl, token);
    }

    private void ClearError()
    {
        ErrorText.Visible = false;
        ErrorText.Text = "";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visible = true;
    }

    private sealed class MachineRow
    {
        public Uuid7 Id { get; set; }
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public override string ToString() => Title + "  —  " + Detail;
    }
}

namespace Schnapp.Client.Linux.Services;

using Avalonia.Controls;

using Schnapp.Client.Linux.Views;
using Schnapp.Contracts;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Fills dynamic tray journal menu content: MRU under Journal target; C/P/T + notes/tags on main menu.
/// Target selection (MRU / C / P / T) applies immediately.
/// </summary>
public sealed class JournalQuickMenu
{
    private readonly JournalSessionService _journal;
    private readonly Func<Window> _getOwner;
    private readonly Action _openMain;
    private readonly Action<string> _reportStatus;

    private string? _pendingCustomer;
    private string? _pendingProject;
    private string? _pendingTask;
    private string? _pendingNotes;
    private string? _pendingTags;

    private NativeMenu? _mruMenu;
    private NativeMenuItem? _noneItem;
    private NativeMenuItem? _customerRoot;
    private NativeMenuItem? _projectRoot;
    private NativeMenuItem? _taskRoot;
    private NativeMenuItem? _notesItem;
    private NativeMenuItem? _tagsItem;

    public JournalQuickMenu(
        JournalSessionService journal,
        Func<Window> getOwner,
        Action openMain,
        Action<string> reportStatus)
    {
        _journal = journal;
        _getOwner = getOwner;
        _openMain = openMain;
        _reportStatus = reportStatus;
    }

    /// <summary>
    /// Binds to tray menu structure from App.axaml:
    /// Journal target = MRU only; Note/Tags/Customer/Project/Task on the main menu.
    /// </summary>
    public void Attach(NativeMenu mainMenu, NativeMenuItem journalTargetItem)
    {
        _mruMenu = journalTargetItem.Menu
            ?? throw new InvalidOperationException("Journal target menu missing from XAML.");

        MenuIcons.Apply(journalTargetItem, "journal");

        foreach (var item in _mruMenu.Items)
        {
            if (item is NativeMenuItem nmi && nmi.Header is "None" or "(none)")
            {
                _noneItem = nmi;
                MenuIcons.Apply(nmi, "none");
            }
        }

        foreach (var item in mainMenu.Items)
        {
            if (item is not NativeMenuItem nmi || nmi.Header is not string h)
            {
                continue;
            }


            if (h.StartsWith("Customer", StringComparison.Ordinal))
            {
                _customerRoot = nmi;
                MenuIcons.Apply(nmi, "customer");
                ApplyListShellIcons(nmi.Menu);
            }
            else if (h.StartsWith("Project", StringComparison.Ordinal))
            {
                _projectRoot = nmi;
                MenuIcons.Apply(nmi, "project");
                ApplyListShellIcons(nmi.Menu);
            }
            else if (h.StartsWith("Task", StringComparison.Ordinal))
            {
                _taskRoot = nmi;
                MenuIcons.Apply(nmi, "task");
                ApplyListShellIcons(nmi.Menu);
            }
            else if (h.StartsWith("Note", StringComparison.Ordinal))
            {
                _notesItem = nmi;
                MenuIcons.Apply(nmi, "note");
            }
            else if (h.StartsWith("Tags", StringComparison.Ordinal))
            {
                _tagsItem = nmi;
                MenuIcons.Apply(nmi, "tags");
            }
        }
    }

    private static void ApplyListShellIcons(NativeMenu? menu)
    {
        if (menu == null)
        {
            return;
        }


        foreach (var item in menu.Items)
        {
            if (item is not NativeMenuItem nmi || nmi.Header is not string h)
            {
                continue;
            }


            if (h is "(none)" or "None")
            {
                MenuIcons.Apply(nmi, "none");
            }
            else if (h.StartsWith("Add", StringComparison.Ordinal))
            {
                MenuIcons.Apply(nmi, "add");
            }

        }
    }

    /// <summary>
    /// Refreshes MRU + list submenus and pending headers.
    /// </summary>
    public void RefreshDynamicItems()
    {
        if (_mruMenu == null)
        {
            return;
        }


        SyncPendingFromOpen();
        RebuildMru();
        RebuildListSubmenu(_customerRoot, "Customer", () => _pendingCustomer, v => _pendingCustomer = v, _journal.Customers);
        RebuildListSubmenu(_projectRoot, "Project", () => _pendingProject, v => _pendingProject = v, _journal.ProjectsFor(_pendingCustomer));
        RebuildListSubmenu(_taskRoot, "Task", () => _pendingTask, v => _pendingTask = v, _journal.TasksFor(_pendingCustomer, _pendingProject));
        UpdateStaticHeaders();
    }

    public async Task OnCustomerNoneAsync()
    {
        _pendingCustomer = null;
        // Customer drives the cascade — clear dependents so we never keep a stale project/task.
        _pendingProject = null;
        _pendingTask = null;
        RebuildDependentListMenus();
        await ApplyPendingAsync();
    }

    public async Task OnProjectNoneAsync()
    {
        _pendingProject = null;
        _pendingTask = null;
        RebuildDependentListMenus();
        await ApplyPendingAsync();
    }

    public async Task OnTaskNoneAsync()
    {
        _pendingTask = null;
        await ApplyPendingAsync();
    }

    public async Task OnCustomerAddAsync() => await AddListValueAsync("Customer", v =>
    {
        _pendingCustomer = v;
        _pendingProject = null;
        _pendingTask = null;
    });
    public async Task OnProjectAddAsync() => await AddListValueAsync("Project", v =>
    {
        _pendingProject = v;
        _pendingTask = null;
    });
    public async Task OnTaskAddAsync() => await AddListValueAsync("Task", v => _pendingTask = v);

    public async Task OnNoteAsync()
    {
        var text = await TextPromptWindow.PromptAsync(_getOwner(), "Journal note", "One-line note:", _pendingNotes);
        if (text == null)
        {
            return;
        }


        _pendingNotes = text;
        if (_journal.OpenJournal != null)
        {
            _reportStatus(await _journal.SaveFieldsAsync(text, _pendingTags, saveNotes: true, saveTags: false));
        }
        else
        {
            _reportStatus("Note set — pick a target to start journal.");
        }


        UpdateStaticHeaders();
    }

    public async Task OnTagsAsync()
    {
        var text = await TextPromptWindow.PromptTagsAsync(
            _getOwner(), "Journal tags", "Tags:", _pendingTags, _journal.ReferenceData);
        if (text == null)
        {
            return;
        }


        _pendingTags = text;
        if (_journal.OpenJournal != null)
        {
            _reportStatus(await _journal.SaveFieldsAsync(_pendingNotes, text, saveNotes: false, saveTags: true));
        }
        else
        {
            _reportStatus("Tags set — pick a target to start journal.");
        }


        UpdateStaticHeaders();
    }

    public async Task OnClearAsync()
    {
        _reportStatus(await _journal.ClearAsync());
        _pendingCustomer = _pendingProject = _pendingTask = null;
        _pendingNotes = _pendingTags = null;
        UpdateStaticHeaders();
    }

    public void OnOpenFull() => _openMain();

    private async Task AddListValueAsync(string label, Action<string?> set)
    {
        var name = await TextPromptWindow.PromptAsync(_getOwner(), "Add " + label, label + " name:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }


        set(name.Trim());
        RebuildDependentListMenus();
        await ApplyPendingAsync();
    }

    private void RebuildMru()
    {
        if (_mruMenu == null)
        {
            return;
        }


        _mruMenu.Items.Clear();

        var none = new NativeMenuItem("None");
        MenuIcons.Apply(none, "none");
        none.Click += async (_, _) => await OnClearAsync();
        _mruMenu.Items.Add(none);
        _noneItem = none;

        foreach (var entry in _journal.BuildMru().Take(12))
        {
            var item = new NativeMenuItem(entry.Display);
            MenuIcons.Apply(item, "mru");
            var target = entry.Target;
            item.Click += async (_, _) => await ApplyMruAsync(target);
            _mruMenu.Items.Add(item);
        }
    }

    private void RebuildListSubmenu(
        NativeMenuItem? root,
        string label,
        Func<string?> get,
        Action<string?> set,
        IReadOnlyList<ListItemResponse> items)
    {
        if (root?.Menu is not { } menu)
        {
            return;
        }


        root.Header = HeaderWithValue(label, get());

        // Keep first 3 static entries: (none), Add new…, separator
        const int staticCount = 3;
        while (menu.Items.Count > staticCount)
        {
            menu.Items.RemoveAt(menu.Items.Count - 1);
        }


        foreach (var name in items.Select(i => i.Name)
                     .Where(n => !string.IsNullOrWhiteSpace(n))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(n => n)
                     .Take(40))
        {
            var item = new NativeMenuItem(name);
            var captured = name;
            item.Click += async (_, _) =>
            {
                set(captured);
                // Always cascade-clear: new customer resets project+task; new project resets task.
                // (Keeping a same-named project under a different customer caused invalid C/P/T triples.)
                if (label == "Customer")
                {
                    _pendingProject = null;
                    _pendingTask = null;
                }
                else if (label == "Project")
                {
                    _pendingTask = null;
                }

                RebuildDependentListMenus();
                root.Header = HeaderWithValue(label, captured);
                await ApplyPendingAsync();
            };
            menu.Items.Add(item);
        }
    }

    /// <summary>
    /// Rebuild project/task submenus from the current pending customer/project (without
    /// re-syncing from the open journal, which would undo cascade clears).
    /// </summary>
    private void RebuildDependentListMenus()
    {
        RebuildListSubmenu(_projectRoot, "Project", () => _pendingProject, v => _pendingProject = v,
            _journal.ProjectsFor(_pendingCustomer));
        RebuildListSubmenu(_taskRoot, "Task", () => _pendingTask, v => _pendingTask = v,
            _journal.TasksFor(_pendingCustomer, _pendingProject));
        UpdateStaticHeaders();
    }

    private void UpdateStaticHeaders()
    {
        _customerRoot?.Header = HeaderWithValue("Customer", _pendingCustomer);

        _projectRoot?.Header = HeaderWithValue("Project", _pendingProject);

        _taskRoot?.Header = HeaderWithValue("Task", _pendingTask);

        _notesItem?.Header = HeaderWithValue("Note", _pendingNotes);

        _tagsItem?.Header = HeaderWithValue("Tags", _pendingTags);

    }

    private void SyncPendingFromOpen()
    {
        var open = _journal.OpenJournal;
        if (open == null)
        {
            return;
        }


        _pendingNotes = open.Notes;
        _pendingTags = open.Tags;
        var t = _journal.Targets.FirstOrDefault(x => x.Id == open.JournalTargetId);
        if (t == null)
        {
            return;
        }


        var names = _journal.NamesFor(t);
        _pendingCustomer = string.IsNullOrEmpty(names.Customer) ? null : names.Customer;
        _pendingProject = string.IsNullOrEmpty(names.Project) ? null : names.Project;
        _pendingTask = string.IsNullOrEmpty(names.Task) ? null : names.Task;
    }

    private static string HeaderWithValue(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? label : $"{label}: {Trim(value!, 28)}";

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private async Task ApplyMruAsync(JournalTargetResponse target)
    {
        try
        {
            var names = _journal.NamesFor(target);
            _pendingCustomer = string.IsNullOrEmpty(names.Customer) ? null : names.Customer;
            _pendingProject = string.IsNullOrEmpty(names.Project) ? null : names.Project;
            _pendingTask = string.IsNullOrEmpty(names.Task) ? null : names.Task;
            var msg = await _journal.ApplyMruAsync(target, _pendingNotes, _pendingTags);
            _reportStatus(msg);
            UpdateStaticHeaders();
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message);
        }
    }

    private async Task ApplyPendingAsync()
    {
        try
        {
            var msg = await _journal.ApplyFromNamesAsync(
                _pendingCustomer,
                _pendingProject,
                _pendingTask,
                _pendingNotes,
                _pendingTags);
            _reportStatus(msg);
            UpdateStaticHeaders();
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message);
        }
    }
}

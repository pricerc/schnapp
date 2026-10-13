using Medo;
using Schnapp.Contracts;

namespace Schnapp.Web.Models;

/// <summary>Shared data for journal + journal-target editor dialogs.</summary>
public sealed class JournalEditorsViewModel
{
    public string PagePath { get; init; } = "/Index";
    public string? ProfileTimeZoneId { get; init; }
    public List<JournalOptionItem> Journals { get; init; } = new();
    public List<ListOptionItem> Targets { get; init; } = new();
    public List<ListOptionItem> Customers { get; init; } = new();
    public List<ListOptionItem> Projects { get; init; } = new();
    public List<ListOptionItem> Tasks { get; init; } = new();
    public List<ListOptionItem> Machines { get; init; } = new();
    /// <summary>JSON blob for client-side field population.</summary>
    public string DataJson { get; init; } = "{}";
    public Dictionary<string, string?> HiddenFields { get; init; } = new();
}

public sealed class JournalOptionItem
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
}

public sealed class ListOptionItem
{
    public string Id { get; init; } = "";
    /// <summary>Select text — codes preferred; C/P/T use "code - name".</summary>
    public string Label { get; init; } = "";
    /// <summary>Name shown under the selector.</summary>
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public string? ParentId { get; init; }
    public bool IsNone { get; init; }
}

/// <summary>Builds journal display labels: Machine - Started - Finished - Target.</summary>
public static class JournalLabelFormatter
{
    public static string FormatCodeName(string? code, string? name)
    {
        code = (code ?? "").Trim();
        name = (name ?? "").Trim();
        if (code.Length == 0 && name.Length == 0) return "(none)";
        if (code.Length == 0) return name;
        if (name.Length == 0) return code;
        if (string.Equals(code, name, StringComparison.OrdinalIgnoreCase)) return code;
        return $"{code} - {name}";
    }

    public static string Format(
        JournalEntryResponse j,
        IReadOnlyDictionary<Uuid7, string> machineNames,
        IReadOnlyDictionary<Uuid7, JournalTargetResponse> targets,
        IReadOnlyDictionary<Uuid7, ListItemResponse> customers,
        IReadOnlyDictionary<Uuid7, ListItemResponse> projects,
        IReadOnlyDictionary<Uuid7, ListItemResponse> tasks,
        Func<DateTimeOffset, string> formatDateTime)
    {
        var target = FormatTarget(j.JournalTargetId, targets, customers, projects, tasks);
        var machine = machineNames.TryGetValue(j.MachineId, out var mn) ? mn : "machine";
        var start = formatDateTime(j.StartUtc);
        var stop = j.StopUtc is { } s ? formatDateTime(s) : "open";
        // Machine first (scan/filter by device), then span, then work target.
        return $"{machine} · {start} - {stop} · {target}";
    }

    /// <summary>Journal target list/select label — prefer target code, else C/P/T codes.</summary>
    public static string FormatTarget(
        Uuid7 targetId,
        IReadOnlyDictionary<Uuid7, JournalTargetResponse> targets,
        IReadOnlyDictionary<Uuid7, ListItemResponse> customers,
        IReadOnlyDictionary<Uuid7, ListItemResponse> projects,
        IReadOnlyDictionary<Uuid7, ListItemResponse> tasks)
    {
        if (!targets.TryGetValue(targetId, out var t))
            return "—";

        var code = (t.Code ?? "").Trim();
        if (code.Length > 0 && !string.Equals(code, "-", StringComparison.OrdinalIgnoreCase))
            return code;

        var c = CodeOrNone(t.CustomerId, customers);
        var p = CodeOrNone(t.ProjectId, projects);
        var task = CodeOrNone(t.TaskId, tasks);
        return $"{c}/{p}/{task}";
    }

    /// <summary>Display name under a target selector (target name or C/P/T names).</summary>
    public static string FormatTargetName(
        Uuid7 targetId,
        IReadOnlyDictionary<Uuid7, JournalTargetResponse> targets,
        IReadOnlyDictionary<Uuid7, ListItemResponse> customers,
        IReadOnlyDictionary<Uuid7, ListItemResponse> projects,
        IReadOnlyDictionary<Uuid7, ListItemResponse> tasks)
    {
        if (!targets.TryGetValue(targetId, out var t))
            return "";

        var name = (t.Name ?? "").Trim();
        if (name.Length > 0 && !string.Equals(name, "(none)", StringComparison.OrdinalIgnoreCase))
            return name;

        var c = NameOrNone(t.CustomerId, customers);
        var p = NameOrNone(t.ProjectId, projects);
        var task = NameOrNone(t.TaskId, tasks);
        return $"{c} / {p} / {task}";
    }

    public static ListOptionItem FromListItem(ListItemResponse item, string? parentId = null) =>
        new()
        {
            Id = Uuid7Text.Format(item.Id),
            Code = item.Code ?? "",
            Name = item.Name ?? "",
            Label = FormatCodeName(item.Code, item.Name),
            ParentId = parentId,
            IsNone = item.Id == Uuid7.Empty
                || string.Equals(item.Code, "-", StringComparison.OrdinalIgnoreCase)
        };

    public static ListOptionItem NoneOption(string? parentId = null) =>
        new()
        {
            Id = Uuid7Text.Format(Uuid7.Empty),
            Code = "-",
            Name = "(none)",
            Label = "(none)",
            ParentId = parentId,
            IsNone = true
        };

    private static string CodeOrNone(Uuid7 id, IReadOnlyDictionary<Uuid7, ListItemResponse> items)
    {
        if (id == Uuid7.Empty) return "-";
        if (!items.TryGetValue(id, out var item)) return "?";
        var code = (item.Code ?? "").Trim();
        return code.Length > 0 ? code : (item.Name ?? "?").Trim();
    }

    private static string NameOrNone(Uuid7 id, IReadOnlyDictionary<Uuid7, ListItemResponse> items)
    {
        if (id == Uuid7.Empty) return "(none)";
        if (!items.TryGetValue(id, out var item)) return "?";
        var name = (item.Name ?? "").Trim();
        return name.Length > 0 ? name : (item.Code ?? "?").Trim();
    }
}

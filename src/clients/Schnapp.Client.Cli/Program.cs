namespace Schnapp.Cli;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Machine;
using Schnapp.Client.Core.Spool;
using Schnapp.Client.Core.Tags;
using Schnapp.Client.Core.Time;
using Schnapp.Client.Core.Upload;
using Schnapp.Contracts;

using Medo;

/// <summary>
/// Schnapp CLI entry point (Phase 8b). Uses Client.Core for all API work.
/// </summary>
internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 1;
    private const int ExitAuth = 2;
    private const int ExitApi = 3;
    private const int ExitLocal = 4;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return args.Length == 0 ? ExitUsage : ExitOk;
        }

        try
        {
            var verb = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToArray();

            switch (verb)
            {
                case "login":
                    return await CmdLogin(rest);
                case "logout":
                    return CmdLogout();
                case "whoami":
                    return await CmdWhoAmI();
                case "config":
                    return CmdConfig(rest);
                case "machine":
                    return await CmdMachine(rest);
                case "journal":
                    return await CmdJournal(rest);
                case "list":
                    return await CmdList(rest);
                case "upload":
                    return await CmdUpload(rest);
                case "spool":
                    return await CmdSpool(rest);
                case "sync":
                    return await CmdSync(rest);
                case "fingerprints":
                    return await CmdFingerprints(rest);
                case "help":
                case "-h":
                case "--help":
                    PrintHelp();
                    return ExitOk;
                default:
                    Console.Error.WriteLine("Unknown command: " + verb);
                    PrintHelp();
                    return ExitUsage;
            }
        }
        catch (ApiException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ex.StatusCode == 401 ? ExitAuth : ExitApi;
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitUsage;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitLocal;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitLocal;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return ExitApi;
        }
    }

    private static async Task<int> CmdLogin(string[] args)
    {
        var p = ArgParser.Parse(args);
        if (p.GetFlag("help")) { PrintHelpLogin(); return ExitOk; }

        var identity = p.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(identity))
        {
            Console.Error.WriteLine("Usage: schnapp login <email|code|name> [--password …]");
            return ExitUsage;
        }

        var store = new ClientSettingsStore();
        var settings = store.Load();

        var baseUrl = p.Get("server");
        if (baseUrl != null)
        {
            settings.ApiBaseUrl = baseUrl;
        }
        else
        {
            baseUrl = Environment.GetEnvironmentVariable("SCHNAPP_SERVER");
            if (baseUrl != null)
            {
                settings.ApiBaseUrl = baseUrl;
            }
        } 

        if (string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
        {
            Console.Error.WriteLine("No server URL configured. Use: schnapp config set server <url>");
            return ExitLocal;
        }


        var password = p.Get("password")
            ?? Environment.GetEnvironmentVariable("SCHNAPP_PASSWORD");

        if (password == null && !Console.IsInputRedirected)
        {
            Console.Write("Password (empty if none): ");
            password = ReadPassword();
            Console.WriteLine();
        }

        using var api = new SchnappApiClient(settings.ApiBaseUrl);
        var login = await api.LoginAsync(identity, password);
        store.SaveToken(login.Token);
        settings.LoginIdentity = identity;
        if (string.IsNullOrWhiteSpace(settings.TimeZoneId) && !string.IsNullOrWhiteSpace(login.User.TimeZoneId))
        {
            // Adopt the server profile's timezone so "at HH:mm" parsing and upload
            // metadata match the user's configured zone instead of silently using
            // whatever timezone this machine's OS happens to be set to.
            settings.TimeZoneId = login.User.TimeZoneId;
        }
        store.Save(settings);

        Console.WriteLine("Logged in as {0} ({1}) [{2}]", login.User.Name, login.User.Email, login.User.Role);
        Console.WriteLine("Token saved to {0}", ClientPaths.TokenFile);
        return ExitOk;
    }

    private static int CmdLogout()
    {
        new ClientSettingsStore().ClearToken();
        Console.WriteLine("Logged out.");
        return ExitOk;
    }

    private static async Task<int> CmdWhoAmI()
    {
        using var api = CreateAuthedClient(out _);
        var me = await api.GetMeAsync();
        Console.WriteLine("{0} <{1}> code={2} role={3} tz={4}", me.Name, me.Email, me.Code, me.Role, me.TimeZoneId);
        return ExitOk;
    }

    private static int CmdConfig(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: schnapp config show | set server <url> | set timezone <iana-id>");
            return ExitUsage;
        }

        var store = new ClientSettingsStore();
        var settings = store.Load();
        var sub = args[0].ToLowerInvariant();
        if (sub == "show")
        {
            Console.WriteLine("config dir:    {0}", ClientPaths.GetConfigDirectory());
            Console.WriteLine("data dir:      {0}", ClientPaths.GetDataDirectory());
            Console.WriteLine("database:      {0}", ClientPaths.GetDatabasePath());
            Console.WriteLine("spool dir:     {0}", ClientPaths.GetSpoolDirectory(settings.SpoolDirectory));
            Console.WriteLine("database:      {0}", ClientPaths.GetDatabasePath());
            Console.WriteLine("mutations:     SQLite table 'mutation' (legacy dir {0})",
                ClientPaths.GetMutationsDirectory(settings.MutationsDirectory));
            Console.WriteLine("api:           {0}", settings.ApiBaseUrl);
            Console.WriteLine("login:         {0}", settings.LoginIdentity ?? "(none)");
            Console.WriteLine("machine:       {0} {1}", settings.MachineId, settings.MachineName);
            Console.WriteLine("timezone:      {0}", settings.TimeZoneId ?? "(local)");
            Console.WriteLine("token:         {0}", store.LoadToken() != null ? "(set)" : "(none)");
            return ExitOk;
        }

        if (sub == "set" && args.Length >= 3 && args[1].Equals("server", StringComparison.OrdinalIgnoreCase))
        {
            settings.ApiBaseUrl = args[2];
            store.Save(settings);
            Console.WriteLine("Server set to {0}", settings.ApiBaseUrl);
            return ExitOk;
        }

        if (sub == "set" && args.Length >= 3 && args[1].Equals("timezone", StringComparison.OrdinalIgnoreCase))
        {
            var tz = args[2];
            if (!IsValidTimeZoneId(tz))
            {
                Console.Error.WriteLine($"Unknown time zone id '{tz}'. Use an IANA id, e.g. Pacific/Auckland.");
                return ExitUsage;
            }
            settings.TimeZoneId = tz;
            store.Save(settings);
            Console.WriteLine("Timezone set to {0}", tz);
            return ExitOk;
        }

        Console.Error.WriteLine("Usage: schnapp config show | set server <url> | set timezone <iana-id>");
        return ExitUsage;
    }

    private static async Task<int> CmdMachine(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelpMachine();
            return ExitUsage;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        var p = ArgParser.Parse(rest);
        var identity = new DefaultMachineIdentity();
        using var api = CreateAuthedClient(out var store);
        var settings = store.Load();

        switch (sub)
        {
            case "list":
                {
                    var list = await api.ListMachinesAsync();
                    foreach (var m in list)
                    {
                        Console.WriteLine("{0}  {1}  code={2}  fp={3}  fqdn={4}",
                            m.Id, m.Name, m.Code, m.HardwareFingerprint, m.Fqdn);
                    }

                    if (list.Count == 0)
                    {
                        Console.WriteLine("(no machines)");
                    }

                    return ExitOk;
                }
            case "show":
                {
                    Console.WriteLine("default id:   {0}", settings.MachineId);
                    Console.WriteLine("default name: {0}", settings.MachineName);
                    Console.WriteLine("detected fqdn:{0}", identity.GetFqdn());
                    Console.WriteLine("detected fp:  {0}", identity.GetHardwareFingerprint());
                    return ExitOk;
                }
            case "register":
                {
                    var name = p.Get("name") ?? identity.GetSuggestedName();
                    var body = new CreateMachineRequest
                    {
                        Name = name,
                        Code = p.Get("code"),
                        Fqdn = identity.GetFqdn(),
                        HardwareFingerprint = identity.GetHardwareFingerprint()
                    };
                    var m = await api.RegisterMachineAsync(body);
                    settings.MachineId = m.Id;
                    settings.MachineCode = m.Code;
                    settings.MachineName = m.Name;
                    store.Save(settings);
                    Console.WriteLine("Registered and selected machine {0} ({1})", m.Name, m.Id);
                    return ExitOk;
                }
            case "find":
                {
                    var result = await api.FindMachinesAsync(new FindMachinesRequest
                    {
                        Fqdn = identity.GetFqdn(),
                        HardwareFingerprint = identity.GetHardwareFingerprint()
                    });
                    foreach (var c in result.Candidates)
                    {
                        Console.WriteLine("{0}  {1}  conf={2}  reason={3}", c.Id, c.Name, c.Confidence, c.Reason);
                    }

                    if (result.Candidates.Count == 0)
                    {
                        Console.WriteLine("(no matches — try machine register)");
                    }
                    else
                    {
                        Console.WriteLine("Use: schnapp machine use <id>");
                    }

                    return ExitOk;
                }
            case "use":
                {
                    if (p.Positionals.Count == 0 || !Uuid7Text.TryParse(p.Positionals[0], out var id))
                    {
                        Console.Error.WriteLine("Usage: schnapp machine use <id>");
                        return ExitUsage;
                    }
                    // Link account + refresh FQDN/fingerprint + set as default
                    var m = await api.LinkMachineAsync(id, new CreateMachineRequest
                    {
                        Fqdn = identity.GetFqdn(),
                        HardwareFingerprint = identity.GetHardwareFingerprint()
                    });
                    settings.MachineId = m.Id;
                    settings.MachineCode = m.Code;
                    settings.MachineName = m.Name;
                    store.Save(settings);
                    Console.WriteLine("Using machine {0} ({1})", m.Name, m.Id);
                    return ExitOk;
                }
            default:
                PrintHelpMachine();
                return ExitUsage;
        }
    }

    private static async Task<int> CmdJournal(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelpJournal();
            return ExitUsage;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        var p = ArgParser.Parse(rest);
        using var api = CreateAuthedClient(out var store);
        var settings = store.Load();

        if (sub == "list")
        {
            var list = await api.ListJournalsAsync();
            foreach (var j in list)
            {
                Console.WriteLine("{0}  {1} → {2}  {3}",
                    j.Id, j.StartUtc.ToString("u"), j.StopUtc?.ToString("u") ?? "open", j.Notes);
            }

            return ExitOk;
        }

        if (sub == "stop")
        {
            var list = await api.ListJournalsAsync();
            var open = list.FirstOrDefault(j => j.IsOpen &&
                (!settings.MachineId.HasValue || j.MachineId == settings.MachineId.Value));
            if (open == null)
            {
                Console.Error.WriteLine("No open journal for default machine.");
                return ExitLocal;
            }
            var stopped = await api.StopJournalAsync(open.Id);
            Console.WriteLine("Stopped {0} at {1}", stopped.Id, stopped.StopUtc);
            return ExitOk;
        }

        if (sub == "start")
        {
            var machineId = ResolveMachineId(settings, p);
            DateTimeOffset? start = null;
            if (p.GetFlag("now"))
            {
                start = DateTimeOffset.UtcNow;
            }
            else if (p.Get("at") != null)
            {
                start = TimestampParser.Parse(p.Get("at"), settings.TimeZoneId);
            }

            Uuid7? journalTargetId = null;
            Uuid7? customerId = null;
            Uuid7? projectId = null;
            Uuid7? taskId = null;
            var customerName = p.Get("customer");
            var projectName = p.Get("project");
            var taskName = p.Get("task");
            if (!string.IsNullOrWhiteSpace(customerName) || !string.IsNullOrWhiteSpace(projectName) || !string.IsNullOrWhiteSpace(taskName))
            {
                if (!string.IsNullOrWhiteSpace(customerName))
                {
                    customerId = await ResolveOrCreateListItemAsync(api, "customers", customerName, null, null);
                }

                if (!string.IsNullOrWhiteSpace(projectName))
                {
                    projectId = await ResolveOrCreateListItemAsync(api, "projects", projectName, customerId, null);
                }

                if (!string.IsNullOrWhiteSpace(taskName))
                {
                    taskId = await ResolveOrCreateListItemAsync(api, "tasks", taskName, customerId, projectId);
                }

                var target = await api.GetOrCreateJournalTargetAsync(new JournalTargetRequest
                {
                    CustomerId = customerId ?? Uuid7.Empty,
                    ProjectId = projectId ?? Uuid7.Empty,
                    TaskId = taskId ?? Uuid7.Empty
                });
                journalTargetId = target.Id;

                // Keep the local reference-data cache warm (same cache the tray/`list` command reads).
                var uploads = new UploadService(settingsMutationsDirectory: settings.MutationsDirectory, referenceDataDbPath: ClientPaths.GetDatabasePath());
                try { await uploads.RefreshReferenceDataAsync(api); }
                catch { /* best-effort; journal start already succeeded/will succeed regardless */ }
            }

            var entry = await api.StartJournalAsync(new StartJournalRequest
            {
                MachineId = machineId,
                JournalTargetId = journalTargetId,
                CustomerId = customerId,
                ProjectId = projectId,
                TaskId = taskId,
                Notes = p.Get("notes"),
                Tags = TagNormalizer.Normalize(p.Get("tags") ?? ""),
                StartUtc = start
            });
            Console.WriteLine("Started journal {0} on machine {1}{2}", entry.Id, entry.MachineId,
                journalTargetId.HasValue ? " target " + journalTargetId : "");
            return ExitOk;
        }

        PrintHelpJournal();
        return ExitUsage;
    }

    private static async Task<Uuid7> ResolveOrCreateListItemAsync(
        SchnappApiClient api, string kind, string name, Uuid7? customerId, Uuid7? projectId)
    {
        var items = await api.ListItemsAsync(kind);
        var match = items.FirstOrDefault(i =>
            string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(i.Code, name, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            return match.Id;
        }

        var created = await api.CreateListItemAsync(kind, new ListItemRequest
        {
            Code = ListCodeGenerator.Allocate(name, items.Select(i => i.Code)),
            Name = name,
            CustomerId = customerId,
            ProjectId = projectId
        });
        return created.Id;
    }

    /// <summary>
    /// Inspects the local reference-data cache (customers/projects/tasks/journal-targets) — the
    /// same cache the tray's cascading Customer/Project/Task menus read. Scripted-testing surface
    /// for the offline cache behavior the GUI otherwise drives silently.
    /// </summary>
    private static async Task<int> CmdList(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelpList();
            return ExitUsage;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        var p = ArgParser.Parse(rest);
        var store = new ClientSettingsStore();
        var settings = store.Load();
        var uploads = new UploadService(settingsMutationsDirectory: settings.MutationsDirectory, referenceDataDbPath: ClientPaths.GetDatabasePath());

        if (p.GetFlag("refresh"))
        {
            using var api = CreateAuthedClient(out _);
            await uploads.RefreshReferenceDataAsync(api);
        }

        var cache = uploads.ReferenceData;
        var customerFilter = p.Get("customer");
        var projectFilter = p.Get("project");

        switch (sub)
        {
            case "customers":
                foreach (var c in cache.GetCustomers().OrderBy(i => i.Name))
                {
                    Console.WriteLine("{0}  {1,-24} {2}", c.Id, c.Name, c.Code);
                }

                return ExitOk;

            case "projects":
            {
                var customerId = ResolveFilterId(cache.GetCustomers(), customerFilter);
                foreach (var proj in cache.GetProjects()
                             .Where(x => customerId == null || x.CustomerId == customerId)
                             .OrderBy(i => i.Name))
                    {
                        Console.WriteLine("{0}  {1,-24} {2}", proj.Id, proj.Name, proj.Code);
                    }

                    return ExitOk;
            }

            case "tasks":
            {
                var customerId = ResolveFilterId(cache.GetCustomers(), customerFilter);
                var projectId = ResolveFilterId(cache.GetProjects(), projectFilter);
                foreach (var t in cache.GetTasks()
                             .Where(x => (customerId == null || x.CustomerId == customerId)
                                         && (projectId == null || x.ProjectId == projectId))
                             .OrderBy(i => i.Name))
                    {
                        Console.WriteLine("{0}  {1,-24} {2}", t.Id, t.Name, t.Code);
                    }

                    return ExitOk;
            }

            case "targets":
            {
                var customers = cache.GetCustomers();
                var projects = cache.GetProjects();
                var tasks = cache.GetTasks();
                foreach (var t in cache.GetJournalTargets().OrderBy(i => i.Name))
                {
                    var c = customers.FirstOrDefault(x => x.Id == t.CustomerId)?.Name ?? "?";
                    var pr = projects.FirstOrDefault(x => x.Id == t.ProjectId)?.Name ?? "?";
                    var tk = tasks.FirstOrDefault(x => x.Id == t.TaskId)?.Name ?? "?";
                    Console.WriteLine("{0}  {1} / {2} / {3}", t.Id, c, pr, tk);
                }
                return ExitOk;
            }

            default:
                PrintHelpList();
                return ExitUsage;
        }
    }

    private static Uuid7? ResolveFilterId(List<ListItemResponse> items, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return items.FirstOrDefault(i =>
            string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(i.Code, name, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static async Task<int> CmdUpload(string[] args)
    {
        var p = ArgParser.Parse(args);
        if (p.GetFlag("help") || p.Positionals.Count == 0)
        {
            PrintHelpUpload();
            return p.Positionals.Count == 0 ? ExitUsage : ExitOk;
        }

        var path = p.Positionals[0];
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("File not found: " + path, path);
        }

        var onlineOnly = p.GetFlag("online-only") || p.GetFlag("onlineonly");
        using var api = CreateAuthedClient(out var store);
        var settings = store.Load();
        var machineId = ResolveMachineId(settings, p);

        DateTimeOffset timestamp;
        if (p.GetFlag("now"))
        {
            timestamp = DateTimeOffset.UtcNow;
        }
        else if (p.Get("at") != null)
        {
            timestamp = TimestampParser.Parse(p.Get("at"), settings.TimeZoneId);
        }
        else
        {
            timestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }

        var contentType = GuessContentType(path);
        Uuid7? journalId = null;
        try
        {
            var journals = await api.ListJournalsAsync();
            var open = journals.FirstOrDefault(j => j.IsOpen && j.MachineId == machineId);
            if (open != null)
            {
                journalId = open.Id;
            }
        }
        catch
        {
            // offline — continue without journal stamp
        }

        var meta = new ScreenshotUploadMetadata
        {
            MachineId = machineId,
            Timestamp = timestamp,
            ClientTimeZoneId = settings.TimeZoneId ?? TimeZoneInfo.Local.Id,
            MonitorName = p.Get("monitor"),
            Notes = p.Get("notes"),
            Tags = TagNormalizer.Normalize(p.Get("tags") ?? ""),
            JournalEntryId = journalId
        };

        try
        {
            using (var fs = File.OpenRead(path))
            {
                var shot = await api.UploadScreenshotAsync(fs, Path.GetFileName(path), contentType, meta);
                Console.WriteLine("Uploaded {0} timestamp={1:u} monitor={2}",
                    shot.Id, shot.TimestampUtc, shot.MonitorName);
            }
            return ExitOk;
        }
        catch (Exception ex) when (!onlineOnly)
        {
            var bytes = File.ReadAllBytes(path);
            var spool = new ScreenshotSpool(settingsSpoolDirectory: settings.SpoolDirectory, databasePath: ClientPaths.GetDatabasePath());
            var id = spool.Enqueue(bytes, Path.GetFileName(path), contentType, meta);
            Console.WriteLine("API unavailable ({0}); queued offline as {1}",
                ex is ApiException ae ? "HTTP " + ae.StatusCode : ex.GetType().Name,
                id);
            Console.WriteLine("Run: schnapp spool push");
            return ExitOk;
        }
    }

    private static async Task<int> CmdSpool(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelpSpool();
            return ExitUsage;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        var p = ArgParser.Parse(rest);
        var store = new ClientSettingsStore();
        var settings = store.Load();
        var spool = new ScreenshotSpool(settingsSpoolDirectory: settings.SpoolDirectory, databasePath: ClientPaths.GetDatabasePath());
        var mutations = new UploadService(settingsMutationsDirectory: settings.MutationsDirectory, referenceDataDbPath: ClientPaths.GetDatabasePath());

        if (sub == "status")
        {
            var s = spool.GetStatus();
            Console.WriteLine("spool:      {0}", s.RootPath);
            Console.WriteLine("database:   {0}", mutations.DatabasePath);
            Console.WriteLine("mutations:  SQLite table 'mutation'");
            Console.WriteLine("pending: {0}", s.PendingCount);
            Console.WriteLine("failed:  {0}", s.FailedCount);
            Console.WriteLine("bytes:   {0}", s.TotalBytes);
            Console.WriteLine("oldest:  {0}", s.OldestEnqueuedUtc?.ToString("u") ?? "-");
            Console.WriteLine("newest:  {0}", s.NewestEnqueuedUtc?.ToString("u") ?? "-");
            Console.WriteLine("mutations pending: {0}", mutations.PendingCount);
            Console.WriteLine("mutations dead:    {0} (permanently failed — not retried; see spool clear-dead)", mutations.DeadCount);
            if (p.GetFlag("verbose") || p.GetFlag("v"))
            {
                foreach (var item in spool.ListItems())
                {
                    Console.WriteLine("  {0}  enq={1:u}  attempts={2}  err={3}",
                        item.Id, item.EnqueuedUtc, item.Attempts, item.LastError ?? "-");
                }

                foreach (var item in mutations.ListDeadItems())
                {
                    Console.WriteLine("  DEAD {0}  kind={1}  attempts={2}  err={3}",
                        item.Id, item.Kind, item.Attempts, item.LastError ?? "-");
                }
            }
            return ExitOk;
        }

        if (sub == "clear-dead")
        {
            var n = mutations.ClearDeadItems();
            Console.WriteLine("Cleared {0} permanently-failed mutation(s).", n);
            return ExitOk;
        }

        if (sub == "push")
        {
            using var api = CreateAuthedClient(out _);
            var (mok, mfail) = await mutations.PushAsync(api);
            var (ok, fail) = await spool.PushAsync(api);
            Console.WriteLine("Mutations: {0} ok, {1} failed (remaining {2}).",
                mok, mfail, mutations.PendingCount);
            Console.WriteLine("Screenshots: {0} ok, {1} failed (remaining {2}).",
                ok, fail, spool.GetStatus().PendingCount);
            return (fail + mfail) > 0 && (ok + mok) == 0 ? ExitApi : ExitOk;
        }

        PrintHelpSpool();
        return ExitUsage;
    }

    private static async Task<int> CmdFingerprints(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelpFingerprints();
            return args.Length == 0 ? ExitUsage : ExitOk;
        }

        var sub = args[0].ToLowerInvariant();
        if (sub != "backfill")
        {
            PrintHelpFingerprints();
            return ExitUsage;
        }

        var p = ArgParser.Parse(args.Skip(1).ToArray());
        if (p.GetFlag("help"))
        {
            PrintHelpFingerprints();
            return ExitOk;
        }

        var body = new ScreenshotFingerprintBackfillRequest();
        var date = p.Get("date");
        if (!string.IsNullOrWhiteSpace(date))
        {
            body.Date = date.Trim();
        }

        if (p.Get("from") is string fromRaw && DateTimeOffset.TryParse(fromRaw, out var from))
        {
            body.From = from;
        }

        if (p.Get("to") is string toRaw && DateTimeOffset.TryParse(toRaw, out var to))
        {
            body.To = to;
        }

        if (p.GetFlag("all") && body.Date is null && body.From is null)
        {
            body.From = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            body.To = DateTimeOffset.UtcNow.AddDays(1);
        }

        if (int.TryParse(p.Get("limit"), out var limit) && limit > 0)
        {
            body.Limit = limit;
        }
        else
        {
            body.Limit = 100;
        }

        using var api = CreateAuthedClient(out _);
        var totalOk = 0;
        var totalFail = 0;
        while (true)
        {
            var r = await api.BackfillScreenshotFingerprintsAsync(body);
            totalOk += r.Processed;
            totalFail += r.Failed;
            Console.WriteLine(
                "processed={0} failed={1} remaining={2}",
                r.Processed, r.Failed, r.Remaining);
            if (r.Remaining <= 0 || (r.Processed == 0 && r.Failed == 0))
            {
                break;
            }

            if (r.Processed == 0 && r.Failed > 0)
            {
                Console.Error.WriteLine("Stopped: remaining rows failed to decode.");
                return ExitApi;
            }
        }

        Console.WriteLine("Done. total processed={0} failed={1}", totalOk, totalFail);
        return totalFail > 0 && totalOk == 0 ? ExitApi : ExitOk;
    }

    private static async Task<int> CmdSync(string[] args)
    {
        _ = args;
        using var api = CreateAuthedClient(out _);
        var store = new ClientSettingsStore();
        var settings = store.Load();
        var mutations = new UploadService(settingsMutationsDirectory: settings.MutationsDirectory, referenceDataDbPath: ClientPaths.GetDatabasePath());
        var spool = new ScreenshotSpool(settingsSpoolDirectory: settings.SpoolDirectory, databasePath: ClientPaths.GetDatabasePath());
        var (mok, mfail) = await mutations.PushAsync(api);
        var (ok, fail) = await spool.PushAsync(api);
        Console.WriteLine("Sync mutations: {0} ok, {1} failed; remaining {2}",
            mok, mfail, mutations.PendingCount);
        Console.WriteLine("Sync screenshots: {0} ok, {1} failed; remaining {2}",
            ok, fail, spool.GetStatus().PendingCount);
        try
        {
            var me = await api.GetMeAsync();
            Console.WriteLine("API ok as {0}", me.Email);
        }
        catch (Exception ex)
        {
            Console.WriteLine("API check failed: {0}", ex.Message);
            return ExitApi;
        }
        return (fail + mfail) > 0 && (ok + mok) == 0 ? ExitApi : ExitOk;
    }

    private static Uuid7 ResolveMachineId(ClientSettings settings, ParsedArgs p)
    {
        if (p.Get("machine") != null && Uuid7Text.TryParse(p.Get("machine"), out var mid))
        {
            return mid;
        }

        if (settings.MachineId.HasValue)
        {
            return settings.MachineId.Value;
        }

        throw new InvalidOperationException(
            "No default machine. Run: schnapp machine list|register|use <id>");
    }

    /// <summary>Checks whether <paramref name="timeZoneId"/> is a known IANA/system time zone id.</summary>
    private static bool IsValidTimeZoneId(string timeZoneId)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static SchnappApiClient CreateAuthedClient(out ClientSettingsStore store)
    {
        store = new ClientSettingsStore();
        var settings = store.Load();
        var token = Environment.GetEnvironmentVariable("SCHNAPP_TOKEN") ?? store.LoadToken();
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Not logged in. Run: schnapp login <identity>");
        }

        return new SchnappApiClient(settings.ApiBaseUrl, token);
    }

    private static string GuessContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".png")
        {
            return "image/png";
        }

        if (ext == ".webp")
        {
            return "image/webp";
        }

        if (ext == ".jpg" || ext == ".jpeg")
        {
            return "image/jpeg";
        }

        return "application/octet-stream";
    }

    private static string ReadPassword()
    {
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Length--;
                }

                continue;
            }
            sb.Append(key.KeyChar);
        }
        return sb.ToString();
    }

    private static bool IsHelp(string a) =>
        a == "-h" || a == "--help" || a == "/?" || a == "help";

    private static void PrintHelp()
    {
        Console.WriteLine(@"schnapp — Schnapp CLI (Client.Core)

  schnapp login <email|code|name> [--password …] [--server url]
  schnapp logout
  schnapp whoami
  schnapp config show | set server <url> | set timezone <iana-id>
  schnapp machine list|show|register|find|use <id>
  schnapp journal start|stop|list [--now|--at TIME] [--notes …] [--tags …]
  schnapp journal start [--customer NAME] [--project NAME] [--task NAME]
  schnapp list customers|projects|tasks|targets [--refresh] [--customer NAME] [--project NAME]
  schnapp upload <file> [--at TIME|--now] [--tags …] [--monitor …] [--notes …] [--online-only]
  schnapp spool status [--verbose] | push
  schnapp fingerprints backfill [--date yyyy-MM-dd] [--from ISO] [--to ISO] [--limit N] [--all]
  schnapp sync

Options accept -name, --name, /name, or bare:  at 13:15 tags a,b
See docs/cli.md for details.");
    }

    private static void PrintHelpLogin() =>
        Console.WriteLine("schnapp login <email|code|name> [--password …] [--server url]");

    private static void PrintHelpMachine() =>
        Console.WriteLine("schnapp machine list|show|register|find|use <id>");

    private static void PrintHelpJournal() =>
        Console.WriteLine("schnapp journal start|stop|list [--now|--at TIME] [--notes …] [--tags a,b] [--customer NAME] [--project NAME] [--task NAME]");

    private static void PrintHelpList() =>
        Console.WriteLine("schnapp list customers|projects|tasks|targets [--refresh] [--customer NAME] [--project NAME]");

    private static void PrintHelpUpload() =>
        Console.WriteLine("schnapp upload <file> [--at TIME|--now] [--tags a,b] [--monitor name] [--notes …] [--online-only]");

    private static void PrintHelpSpool() =>
        Console.WriteLine("schnapp spool status [--verbose] | push | clear-dead");

    private static void PrintHelpFingerprints() =>
        Console.WriteLine(
            "schnapp fingerprints backfill [--date yyyy-MM-dd] [--from ISO] [--to ISO] [--limit 100] [--all]\n"
            + "  Computes visual fingerprints for screenshots that lack one.\n"
            + "  Default range is today (profile timezone). --all covers 2000-01-01 through tomorrow UTC.\n"
            + "  Repeats until remaining=0. Requires: schnapp login");
}

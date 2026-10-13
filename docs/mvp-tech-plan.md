# Schnapp MVP — Technology & Implementation Plan

> Editable workspace copy. Product intent: [`goals.md`](../goals.md).
> This plan freezes **MVP tech + scope only**.

## Goal

Multi-user screenshot capture for a single-org team: tray clients capture monitors + journal context, upload to a C# API (PostgreSQL + filesystem blobs); users review **their own** data in a Razor+HTMX UI with day URLs.

---

## Locked decisions


| Area           | Choice                                                                                                                 |
| -------------- | ---------------------------------------------------------------------------------------------------------------------- |
| API            | C# ASP.NET Core (net10)                                                                   |
| Web UI         | Separate process: Razor Pages + HTMX; talks to API over**HTTP only** (BFF)                                             |
| DB             | **PostgreSQL** (metadata only; image bytes on filesystem)                                                              |
| Data access    | **Dapper** + thin repository/query types (no EF Core)                                                                  |
| Migrations     | **DbUp** + numbered `.sql` scripts (applied on API startup and/or CLI)                                                 |
| Blobs          | Filesystem via`IImageBlobStore` (MinIO-capable later)                                                                  |
| Formats        | Client chooses webp/jpeg/png; server accepts all; app metadata in**JSON + DB**                                         |
| Thumbs         | Server generates on upload (stored)                                                                                    |
| Linux client   | Avalonia + modern .NET + ImageSharp encode (**implement before Windows**)                                              |
| Windows client | WinForms**net48**, xcopy                                                                                               |
| Auth           | Admin-created users; browser session via Web BFF; clients:**user/pass once → long-lived API token**                   |
| Tenancy        | Single org per server                                                                                                  |
| Visibility     | Private by default; sharing later (keep`UserId` owner column)                                                          |
| Machine        | Device metadata;**many users ↔ one machine**; user token is principal                                                 |
| Machine id     | Server-assigned; client stores in config; **find** matches via fingerprint/FQDN, then **use** (link + select)          |
| Offline        | Capture **always enqueues** to local spool; background push with retain-as-cache; client id = spool id on upload (server idempotent on true duplicates); list/journal **mutation** queue + ref cache |
| Day browser    | **`User.TimeZoneId`** (IANA); URL day = that zone’s local midnight–midnight                                          |
| Lists          | **Any user CRUD** customers/projects/tasks; **Code and Name both UNIQUE** per table; seeded **NONE** rows (nil UUID); Project/Task/JournalTarget FKs **NOT NULL** |
| Org settings   | **Setting** key-value table in Postgres (e.g. `journal.recent_last_seen_minutes`); admin write, app read |
| Journal link   | Optional`activeJournalEntryId` on screenshot only                                                                      |
| Open journals  | **At most one open entry per (UserId, MachineId)**                                                                     |
| Capture MVP    | Interval + selected monitors + manual + pause/resume                                                                   |
| Multi-monitor  | **One screenshot per selected monitor** per tick (not stitched)                                                        |
| Retention      | Manual delete own shots (DB + files); no auto-purge; document disk monitoring                                          |
| Tests          | Automated test suite builds and runs with the implementation (see Testing)                                             |
| Deploy         | Native processes first; Podman later                                                                                   |

---

## MVP in / out

### In

- API: auth/tokens, machines (register/find/list), screenshots CRUD-lite, journals, lists CRUD, admin users, health
- Web: login, user timezone settings, day gallery + filters + zoom, edit notes/tags, delete shot, journals, list CRUD, admin users
- Clients: tray, settings, capture selected monitors on interval, screenshot upload queue, **offline cache for lists + journals**, offline create of customers/projects/tasks (sync later; **conflict resolution TBD**), journal start/stop/pause, token bootstrap
- Tests: unit + integration coverage for server and Client.Core; CI-friendly `dotnet test`

### Out

Reports, sharing, dedup/diff/activity, exclusions, watermarks, OIDC, Podman, ParentShotId, full M2M journal↔shot, multi-tenant, active-window-only capture (unless free later), **final offline conflict policy for lists** (document TBD; implement optimistic local ids + sync queue only)

---

## Architecture

```
[Avalonia Linux] ──┐
                   ├── HTTP ──► [Schnapp.Api] ──► PostgreSQL
[WinForms net48] ──┘              │
                                  └──► filesystem blobs (orig + thumb)
[Browser] ──► [Schnapp.Web BFF] ──HTTP──► [Schnapp.Api]
```

Dev ports (example): API `:5080`, Web `:5081`. Independently restartable.

**Web auth (BFF):** login posts to API; Web stores API token/session in **encrypted cookie**; server-side HttpClient attaches `Authorization: Bearer` to API. Browser does not hold long-lived client tokens.

---

## Repo layout

```
docs/
  mvp-tech-plan.md           # this file (edit here)
goals.md
src/Schnapp.slnx
  Schnapp.Api/
  Schnapp.Web/
  Schnapp.Domain/            # entities/POCOs, interfaces (no ORM)
  Schnapp.Data/              # Dapper repos, Npgsql, DbUp runner
  Schnapp.Data/Migrations/   # 001_*.sql, 002_*.sql, …
  Schnapp.Contracts/         # API DTOs
clients/
  Schnapp.Client.Core/       # netstandard2.0: queue, cache, HTTP, settings models
  Schnapp.Client.Linux/      # Avalonia (first client)
  Schnapp.Client.Windows/    # net48 WinForms
tests/
  Schnapp.Data.Tests/
  Schnapp.Api.Tests/         # integration (WebApplicationFactory + Testcontainers Postgres optional)
  Schnapp.Client.Core.Tests/
  Schnapp.Domain.Tests/      # pure unit if needed
data/                        # gitignored blob root
```

`Client.Core` must stay net48-safe (no ImageSharp in Core).

## Code style

- All classes, methods, and public properties must be documented with appropriate comments. For C# code, use XML documentation comments (`///`).
- Comments should incorporate the **reasoning** behind a method, and **from where it is expected to be called**.
- All parameters should be described, using `cref` / `seealso` tags where possible.
- Use of `<remarks>`, lists, and tables is encouraged so a plain-English reader can confirm intent and call sites without reverse-engineering the body.

## Data access conventions

- **Npgsql** connection factory; one connection (or transaction) per request/unit-of-work on write paths
- **Dapper** for all SQL; explicit parameterized queries only (no string-concat SQL)
- Map to POCOs/DTOs; no change-tracker, no lazy loading
- Day gallery / list endpoints: SQL projects only needed columns (`SELECT` list matches response DTO)
- Screenshot insert: single transaction for DB row; blob files written with compensation delete on failure (as already planned)
- Repositories or query services by aggregate area (`Users`, `Machines`, `Screenshots`, `Journals`, `Lists`) — keep thin

## Migrations (DbUp)

- Scripts live in `Schnapp.Data/Migrations/` as embedded resources or content files
- Naming: `001_init.sql`, `002_….sql` (zero-padded, ordered)
- Journal table: DbUp default `schemaversions` (or `schnapp_schema_versions`)
- Run: **API startup** applies pending scripts; optional `dotnet run --project Schnapp.Api -- migrate` for ops
- Never edit applied scripts; only add new ones
- Init script includes: all MVP tables (incl. Setting), indexes, partial unique open journals, Code+Name uniques on lists, seed NONE sentinels + default settings; admin user bootstrap remains in app code

---

## Data model (MVP)

### General

`Id` fields are UUIDv7, unique, and immutable — **except** well-known sentinel rows that use the nil UUID (`00000000-0000-0000-0000-000000000000`).

`Code` fields are 'short' text, **unique within their table**. Can be changed. Indexed. Defaults defined in client code where helpful. Reserved: `NONE` for sentinels.

`Name` fields are 'longer' text, **also unique within their table** (same uniqueness scope as Code). Code exists because names can be lengthy; both must be unique so either can identify a row in pickers/search.

`Notes` and `Tags` are free-text fields. `Tags` is expected to only contain keywords to assist searching.

**List uniqueness (Customer, Project, Task each table):** `UNIQUE (code)` and `UNIQUE (name)`. User/Machine may follow the same Code/Name uniqueness pattern where those fields exist.

### Well-known “none” sentinels

To avoid nullable FKs on list parents and JournalTarget triples, each org list has a seeded **NONE** row:

| Entity | Id | Code | Name (display) |
|---|---|---|---|
| Customer | `00000000-0000-0000-0000-000000000000` | `NONE` | `(none)` |
| Project | same nil UUID | `NONE` | `(none)` |
| Task | same nil UUID | `NONE` | `(none)` |

Rules:

- Shared constant in code: e.g. `WellKnownIds.None` (= `Guid.Empty`). Same value is valid in three tables (FKs are per-table).
- Seeded in `001_init.sql` (or bootstrap); **immutable id**; not deletable via API; Code/Name may be display-only protected.
- App must never assign nil UUID to normal user-created entities (only seed / hard-coded client cache).
- **Project.CustomerId** and **Task.CustomerId / Task.ProjectId** are **NOT NULL**; default / “unscoped” = nil → sentinel parents.
- Project NONE: `CustomerId = nil`. Task NONE: `CustomerId = nil`, `ProjectId = nil`.
- Reports later: exclude `id = nil` or `code = 'NONE'` from totals.
- Clients always have a pickable “(none)” row offline without sync (hard-coded seed in local SQLite too).

### User

- Id, Code, Name, Email (unique), PasswordHash, Role (`User`|`Admin`)
- **TimeZoneId** (IANA, required after first settings save; default at create/login)
- Tags, Notes, CreatedAt, LastLogin, LastSeen

### ApiToken

- Id, UserId, TokenHash, Name, CreatedAt, LastUsedAt, RevokedAt, Notes

### Machine

- Id, Code, Name
- **Fqdn** (nullable)
- **HardwareFingerprint** (nullable string; client-computed stable-ish id)
- **LastIpAddresses** (jsonb array, updated on register/upload)
- OptionalPasswordHash (nullable; unused for auth in MVP)
- Notes, Tags, CreatedAt, UpdatedAt, LastSeen

### UserMachine

- UserId, MachineId, RegisteredAt, DisplayName, Notes, Tags, LastSeen, LastUpdated
- Client Settings (JSON) — per user-machine client prefs (not server org settings)

### Setting (org/server key-value)

Simple table for server-side configuration that may grow over time (not client tray settings).

| Column | Type | Notes |
|---|---|---|
| Key | text PK | Stable dotted or snake name, e.g. `journal.recent_last_seen_minutes` |
| Value | text | String storage; parse in app (int, bool, duration, JSON if needed) |
| Notes | text nullable | Optional human description |
| UpdatedAt | timestamptz | |

- Any authenticated user may **read** settings used by clients (or subset via API); **admin-only write** for MVP unless noted
- Seed known keys in `001_init.sql` with defaults
- Initial keys (extend as needed):
  - `journal.recent_last_seen_minutes` — default e.g. `30` — window for journal auto-close rule (recent vs aged LastSeen)
- Prefer this table over new columns/env vars for tunable org behavior

### Customer / Project / Task

- Org-global; any authenticated user CRUD (except delete/mutate sentinel NONE rows)
- **Client offline creates** use locally assigned UUIDv7. On sync, server accepts client id (**duplicate/conflict resolution TBD**)
- **Per table:** `UNIQUE (code)` and `UNIQUE (name)`; `NONE` / `(none)` reserved for sentinels

#### Customer

- Id, Code (unique), Name (unique), Notes, Tags
- Seeded NONE row (nil Id)

#### Project

- Id, Code (unique), **CustomerId** (NOT NULL FK → Customer; default nil = Customer NONE), Name (unique), Notes, Tags
- Seeded NONE row (nil Id, CustomerId = nil)

#### Task

- Id, Code (unique), **CustomerId** (NOT NULL FK → Customer; default nil), **ProjectId** (NOT NULL FK → Project; default nil), Name (unique), Notes, Tags
- Seeded NONE row (nil Id, CustomerId = nil, ProjectId = nil)

#### JournalTarget

- Id, **CustomerId** (NOT NULL), **ProjectId** (NOT NULL), **TaskId** (NOT NULL), Code, Name, Notes, Tags
- All three FKs always set; “empty” target = `(nil, nil, nil)` → Customer/Project/Task NONE
- **UNIQUE (customer_id, project_id, task_id)** — plain unique, no null tricks
- Seed optional row for `(nil, nil, nil)` or create on first use via get-or-create
- Lookup/get-or-create uses ordinary `=` equality

#### Tag

- Tag, Notes, **CustomerId?** (null = org-global)
- Dictionary for auto-completing free-text Tags fields; rows are collated from free-text writes and optional explicit `POST /lists/tags`.
- Uniqueness: one global row per tag text; one row per (tag, customer) when scoped.
- No separate Id column.

### Screenshot

- Id, UserId, MachineId
- TimestampUtc, **ClientTimeZoneId**, **ReceivedAtUtc**
- MonitorId?/MonitorName?, window/program fields optional
- ContentType, ByteSize, Width, Height, Sha256
- StorageKey, ThumbStorageKey
- Notes, Tags
- **ActiveJournalEntryId?** (nullable FK)
- CreatedAt

### JournalEntry

- Id, UserId, **MachineId**
- StartUtc, StopUtc? (null = open)
- JournalTarget (mutable)
- Notes, Tags, CreatedAt, UpdatedAt
- **Partial unique index:** one open row per (UserId, MachineId) where `StopUtc IS NULL`

### Indexes

- Screenshot `(UserId, TimestampUtc)`
- Journal `(UserId, MachineId, StartUtc)`
- Machine lookup helpers on Fqdn, HardwareFingerprint

### Visibility

Every non-admin data query: `UserId = currentUser`. Lists are org-global (not user-scoped).

---

## Day queries

Given user timezone `TZ` and path date `D`:

```
startUtc = D 00:00:00 in TZ → UTC
endUtc   = (D+1) 00:00:00 in TZ → UTC
filter: UserId = me AND TimestampUtc >= startUtc AND TimestampUtc < endUtc
```

Store `TimestampUtc` from client instant; keep `ClientTimeZoneId` for display only. Changing profile TZ reinterprets which local day a shot falls into (absolute time unchanged).

---

## Machine register / find / link

1. Client has token + local `machineId` → use it; refresh FQDN/fingerprint/IP on startup.
2. No local id → `POST /api/v1/machines/find` with `{ name, fqdn, hardwareFingerprint }`.
3. API (scoped to current user):
   - Strong single match (fingerprint and/or FQDN on a machine already linked or linkable to user) → return candidates with `confidence`
   - Client **confirms** one id or `POST /machines` to create new
4. Multiple candidates → client picker (name, FQDN, last IPs, last seen).
5. On confirm/create: upsert `UserMachine`; update Machine FQDN/fingerprint/IPs.

Fingerprint algorithm: document per OS (e.g. machine-id / product UUID); must not require admin rights if possible. Weak fingerprint → prefer confirm UI over silent bind.

---

## Blob storage

- Root: `Schnapp:BlobRoot`
- Keys: `{userId}/{yyyy}/{MM}/{dd}/{id}.ext` + `{id}.thumb.webp` (or .jpg)
- `IImageBlobStore`: Put / OpenRead / Delete
- Upload: write orig → thumb (ImageSharp) → DB insert; compensate files on failure
- Delete shot: DB row + both files

---

## API (`/api/v1`)


| Method         | Path                      | Purpose                                          |
| -------------- | ------------------------- | ------------------------------------------------ |
| POST           | `/auth/login`             | email/password → token (and/or session for BFF) |
| POST           | `/auth/tokens`            | create named token                               |
| DELETE         | `/auth/tokens/{id}`       | revoke                                           |
| GET            | `/me`                     | profile incl. TimeZoneId                         |
| PATCH          | `/me`                     | update TimeZoneId, name, …                      |
| GET/POST       | `/machines`               | list mine / create                               |
| POST           | `/machines/find`          | candidates matching fingerprint/FQDN             |
| POST           | `/machines/{id}/link`     | link user to machine + refresh identity          |
| CRUD           | `/lists/customers`        | customers                                        |
| CRUD           | `/lists/projects`         | projects                                         |
| CRUD           | `/lists/tasks`            | tasks                                            |
| POST           | `/screenshots`            | multipart file + metadata JSON                   |
| GET            | `/screenshots`            | `date=yyyy-MM-dd` or from/to UTC; monitor filter |
| GET            | `/screenshots/{id}`       | metadata                                         |
| PATCH          | `/screenshots/{id}`       | notes/tags                                       |
| DELETE         | `/screenshots/{id}`       | row + blobs                                      |
| GET            | `/screenshots/{id}/image` | image                                            |
| GET            | `/screenshots/{id}/thumb` | thumb                                            |
| GET/POST/PATCH | `/journals`               | enforce one open per user+machine                |
| GET            | `/settings`               | org key-value settings (read)                    |
| GET/PATCH      | `/settings/{key}`         | get one; admin patch value                       |
| POST           | `/admin/users`            | admin create/reset password                      |
| GET            | `/health`                 |                                                  |

**Upload metadata:** timestamp (ISO offset), clientTimeZoneId, machineId, monitor*, window/program*, notes, tags, activeJournalEntryId?, width/height?

**Auth:** `Authorization: Bearer <token>`. Tokens hashed at rest.

**Limits:** multipart max ~25–50MB; sniff image magic bytes.

---

## Web UI routes


| Route                |                                                   |
| -------------------- | ------------------------------------------------- |
| `/login`             | BFF login                                         |
| `/settings`          | timezone (IANA picker), password change optional  |
| `/`                  | redirect today in user TZ                         |
| `/{yyyy}/{MM}/{dd}/` | day gallery                                       |
| zoom partial         | full image + prev/next + edit notes/tags + delete |
| `/journals`          | list/edit                                         |
| `/lists/...`         | CRUD                                              |
| `/admin/users`       | admin                                             |
| `/admin/settings`    | optional: edit org Setting key-values             |

Filters: time-within-day, monitor. HTMX partials for dialogs/grid refresh.

---

## Tags

- **Stored in the database as space-separated** text on entity `tags` fields (screenshots, journals, lists, etc.).
- **API (and Core helper) normalize on write:** split input on a set of punctuation delimiters (and whitespace), trim, drop empties, re-join with single spaces.
- **Initial split delimiters:** comma `,`, semicolon `;`, tab, whitespace. Full delimiter list **TBD** (may add pipe, etc.).
- Example: client sends `cli,demo;foo` → stored `cli demo foo`.
- Display/edit UIs may show commas for convenience; persistence remains space-separated.
- **Tag dictionary table** (`tag`): collated on every free-text tags write; source for autocomplete.
  - Optional **`customer_id`** (null = org-global). **If a global row exists for a tag text, no customer-specific row is allowed** for that text (collation and create both enforce this).
  - Customer / project / task / journal / journal-target writes scope dictionary rows to the relevant customer only when no global exists; user/machine/screenshot (without customer context) write global rows (and purge customer duplicates).
  - `GET /api/v1/lists/tags?customerId=&scope=global|customer` — default autocomplete is global+customer; editors use `scope=global` or `scope=customer`.
  - Reassign: `POST …/assign-customer` (global → customer), `POST …/make-global` (customer → global); `PATCH` notes; `DELETE`.
  - Web: `/Tags` general editor; customer page collapsible **Customer tags**; token-aware autocomplete via `/Api/Tags`.

## Clients

### Layout

```
clients/
  Schnapp.Client.Core/     # netstandard2.0 — API, config, machine identity, spool (8c)
  Schnapp.Cli/             # net10.0 — hybrid CLI (8b)
  Schnapp.Client.Linux/    # net10.0 Avalonia (9)
  Schnapp.Client.Windows/  # net48 WinForms (10)
docs/cli.md                # CLI user documentation (8b deliverable)
```

### Core (netstandard2.0) — Phase 8 / 8c

**Phase 8 (online):**

- Settings + token under user config dir (`~/.config/schnapp` / `%AppData%\Schnapp`)
- HTTP API client (login, me, machines, journals, screenshot upload)
- Machine fingerprint/FQDN detection interface + default machine id in settings
- Tag normalize helper; timestamp parse helper
- Relink flow helpers

**Phase 8c (offline):**

- **Screenshot spool:** durable upload queue; exponential backoff; caps (defaults: 2000 items / 2GB / 14 days; drop oldest)
- **Local cache (offline-first for tray UX):**
  - Customers / Projects / Tasks / JournalTarget
  - Journal entries for this user+machine (open + recent)
  - MRU JournalTarget
  - SQLite under user data dir; Dapper for consistency with server style
- **Offline mutations queue** (separate from screenshot spool):
  - Create/update journal segments while offline
  - Create Customer / Project / Task while offline (local temp ids)
  - On reconnect: push mutation queue, then pull lists/journals
  - **Conflict resolution: TBD** — structure + error flags only in MVP

**While offline (tray / CLI after 8c):**

- Pick list items from cache; add lists offline; journal start/stop offline; screenshots into spool

**While online:**

- Push spool + mutations; pull lists/journals; rewrite temp ids after server create

**Conflict resolution:** see [Offline conflict resolution](#offline-conflict-resolution).

### Token bootstrap

Settings: server URL + email + optional password → login → store token → **discard password**.  
CLI/scripts may use env `SCHNAPP_TOKEN` or `SCHNAPP_PASSWORD`. Initial deploy is a **trusted environment**; stronger secret handling if the project escapes to the wild is deferred.

### CLI (Phase 8b) — no capture

- Conversational hybrid argv (see Phase 8b)
- Verbs: login/logout/whoami, config, machine list/register/find/link/use/show, journal start/stop/list, upload file
- After 8c: `spool status|push`, `sync`
- Docs: `docs/cli.md`

### Capture (Phases 9–10 tray only)

- Interval default **120s**, minimum floor **15s**
- Selected monitors only; manual capture; pause/resume
- **Per tick:** one image **per selected monitor** (not stitched)
- Encode off UI thread; Windows net48: JPEG/PNG required, WebP best-effort

### Journal (all clients)

- Start opens entry for **default/current machine**; API enforces one open per user+machine
- Pause: stop segment + local paused flag; Resume: new segment
- Stop: set StopUtc
- While open, stamp `activeJournalEntryId` on uploads

### Linux tray (Phase 9)

- Avalonia TrayIcon; X11 capture; ImageSharp encode
- Monitor geometry overrides in settings

### Windows tray (Phase 10)

- NotifyIcon; DXGI preferred, GDI fallback
- System.Drawing encode path; xcopy install

---

## Journal concurrency rule

API on create/start:

- If another row exists with same UserId+MachineId and `StopUtc IS NULL`
  - Read `journal.recent_last_seen_minutes` from **Setting** table (default 30 if missing)
  - If `JournalTarget` is the same, and `UserMachine.LastSeen` is **recent** (within that many minutes), auto-close previous at `now`
  - Otherwise (different target, or LastSeen **aged** beyond the window), auto-close previous at `UserMachine.LastSeen`

---

## Testing

Tests are **first-class**: they live in-repo, build with the solution, and gate changes.

### Projects


| Project                     | Focus                                                                                  |
| --------------------------- | -------------------------------------------------------------------------------------- |
| `Schnapp.Domain.Tests`      | Pure logic if any (time/day bounds helpers, tag parse)                                 |
| `Schnapp.Data.Tests`        | SQL/repos against real Postgres (Testcontainers) or disposable DB                      |
| `Schnapp.Api.Tests`         | HTTP integration: auth, scope isolation, upload, journals, lists, one-open journal     |
| `Schnapp.Client.Core.Tests` | Queue, offline cache, mutation queue, id rewrite, retry/backoff (filesystem temp dirs) |

### Conventions

- `dotnet test` from repo root runs all
- Prefer deterministic unit tests without network
- Integration tests may use **Testcontainers.PostgreSql** when Docker/Podman available; document skip/env flag if not
- Cover at least: user-scoped screenshot access denied, day range in user TZ, journal one-open constraint, multipart upload happy path, client spool survives restart, offline list create lands in mutation queue
- UI (Avalonia/WinForms) automated UI tests **out of MVP**; manual checklist remains in Validation

### When to write tests

- Alongside each implementation step below (not a final bolt-on)
- New API endpoint or repo method → at least one integration or data test
- Client.Core offline/sync paths → unit tests with fake clock/filesystem

---

## Security bar

- Password hashing (Identity hasher); token hash only
- User-scoped queries; admin for user provisioning only
- Server-generated blob keys; path traversal impossible
- File perms on blob root; client config file user-only perms
- No cross-user screenshot access in MVP

---

## Failure modes


| Case                    | Behavior                                                          |
| ----------------------- | ----------------------------------------------------------------- |
| API down                | Client uses cache + queues screenshots/mutations; Web errors      |
| Client disk full        | Stop capture + tray warning                                       |
| Server disk full        | Upload fails; client retries; log                                 |
| 401                     | Tray prompts re-login                                             |
| Relink ambiguous       | Picker, never silent wrong bind                                   |
| Two RDP users same host | Separate configs/tokens; shared Machine allowed after each links |
| Clock skew              | Store client TimestampUtc + ReceivedAtUtc                         |
| List sync conflict      | Queue item flagged; local cache keeps working;**resolver TBD**    |

---

## Implementation order

Phases match the numbered list below. Each phase ends when its **done when** checklist is true, **automated tests are green**, and **manual Web (or mock-client) smoke** works against a running API.

### Principle: API + Web together for manual testing

- Build **API capability first** within a phase, then a matching **Web BFF slice** so you can click through without curl-only workflows.
- Web is a pure HTTP client of the API (`Schnapp:ApiBaseUrl`).
- **Temporary / diagnostic UI is allowed** (e.g. show bearer token, relink simulator fields). Mark with a visible **“dev / temporary”** label; gate or strip in Phase 11.
- **Product manual screenshot upload (not temporary):** browser upload of an image with **user-chosen timestamp** (and machine/monitor/notes). Use cases: un-monitored system, manual crop/region grab, backfill. Refine UX later; keep as a first-class feature alongside tray capture.
- **Mock client forms** still OK for tray-only behaviours until Phases 8–10 (e.g. relink fingerprint, journal start/stop from Web until clients exist).

| Phase | Focus | Manual / consumer |
|---|---|---|
| 1–7 | Server + Web (done / in progress) | Browser smoke |
| **8** | **Client.Core** (netstandard2.0): online API client, config, token, machine identity | Unit tests |
| **8b** | **Schnapp.Cli** (net10): hybrid argv parser; login/machine/journal/upload | CLI docs + smoke |
| **8c** | Offline spool + mutation queue in Core; CLI `spool` / `sync` verbs | Offline then flush |
| 9 | Linux Avalonia tray (net10) | Capture + Core |
| 10 | Windows WinForms (net48) | Capture + Core |
| 11 | Docs / validation | Full checklist |

### Phase 1 — Solution skeleton, schema, health ✅

1. Create `src/Schnapp.slnx` and projects: `Schnapp.Api`, `Schnapp.Web` (empty host ok), `Schnapp.Domain`, `Schnapp.Data`, `Schnapp.Contracts`
2. Test projects; packages (Npgsql, Dapper, DbUp, xunit, FluentAssertions)
3. Domain POCOs + `001_init.sql` + DbUp on API startup
4. `GET /api/v1/health`
5. **Web (optional):** stub host or simple “API health” page
6. **Done when:** migrate + health 200; `dotnet test` green; optional browser health check

### Phase 2 — Auth + Web login (manual test) ✅ / in progress

**API**

1. Password hashing, user/token repos, login + named tokens, bearer middleware
2. `GET/PATCH /me`, admin bootstrap, `POST /admin/users` (+ reset password)
3. Automated API tests

**Web (BFF)**

1. Razor Pages + layout; config `Schnapp:ApiBaseUrl` (default `http://127.0.0.1:5080`)
2. Login form → `POST /api/v1/auth/login` → store token in **HTTP-only auth cookie**
3. Home/dashboard after login: profile from `/me`
4. **Temporary:** display bearer token (copy for curl/client testing); clear warning it is dev-only
5. Logout clears cookie
6. Profile edit (timezone/name/code) via `PATCH /me`
7. Admin: create user form (if role Admin)
8. **Manual smoke:** bootstrap admin → browser login → see profile + token → create second user → logout/login as user B
9. **Done when:** API tests green **and** browser login path works without curl

### Phase 3 — Machines + Web machine UI

**API:** register/list/find/link as previously planned; tests  
**Web:** list my machines; register; relink candidate picker; **dev mock “client machine” form** (fingerprint/FQDN fields)  
**Done when:** browser can complete find/link against API

### Phase 4 — Lists + settings + Web CRUD

**API:** customers/projects/tasks/JournalTarget/tags/settings; tests  
**Web:** list maintenance pages; admin settings editor  
**Done when:** browser CRUD works for lists; settings readable/writable as designed

### Phase 5 — Screenshots + Web gallery + manual upload ✅

**API:** blob store, thumbs, upload/list/get/patch/delete; metadata includes optional `timestamp`  
**Web:** day gallery + zoom/edit/delete; **manual upload** (product feature — inject image from un-monitored system / manual grab)  
**Done when:** upload appears in day gallery; tests cover isolation/delete

### Phase 5b / ongoing — Manual upload productization

Promote browser upload from “mock client” to a **documented product feature**:

- User selects image file, **machine**, optional monitor label, notes/tags, optional **active journal**
- User sets **capture timestamp** (local datetime; server stores as `TimestampUtc` + optional `ClientTimeZoneId`)
- `ReceivedAtUtc` remains server receive time
- Refinements later: timezone picker, multi-file, drag-drop — not required for MVP

### Phase 6 — Journals + Web journal UI

**API**

1. JournalEntry repo; FK to JournalTarget; one open per (UserId, MachineId)
2. Start/stop/pause: auto-close previous open entry (same target + recent LastSeen → close at now; else close at LastSeen); window from `journal.recent_last_seen_minutes`
3. `GET/POST/PATCH /journals`; list by user
4. Tests: second start closes first; scoped to user

**Web**

1. Journal list + edit (times, notes, tags, target)
2. Start / Stop / Pause controls (Web stands in for tray until clients)
3. Manual screenshot upload can stamp `activeJournalEntryId` when a journal is open

**Done when:** browser drives journal lifecycle; API tests green

### Phase 7 — Web day-browser polish

1. Day URLs: `/{yyyy}/{MM}/{dd}/` (or `/Screenshots/{yyyy}/{MM}/{dd}`) aligned with user timezone
2. Prev/next day navigation; default today
3. Filters: monitor name (and time-within-day if easy)
4. Manual upload form: explicit timestamp field (datetime-local), clearer product copy (not “mock”)
5. Zoom page: notes/tags edit if not already; layout polish
6. Optional: keep token display behind `ShowDevTools` only

**Done when:** gallery UX is navigable by day URL + filters; manual upload with custom timestamp works end-to-end

### Phase 8 — Client.Core (online foundation, netstandard2.0)

**Goal:** shared library for CLI, Avalonia, and later WinForms. **No capture** in Core. Target **netstandard2.0** so Phase 10 (net48) can reference it without rewriting.

**net48 / netstandard2.0 constraints (do not break):**

- No C# features or BCL APIs that require net5+ only in public Core surface (avoid raw `DateOnly`/`TimeOnly` in public API if awkward; prefer `DateTime`/`DateTimeOffset`)
- Prefer `HttpClient` + `System.Text.Json` (or Newtonsoft if forced); no ImageSharp in Core
- File/config paths via abstractions; no Linux-only APIs without `#if` or interface
- Avoid `required` members, default interface methods that net48 consumers choke on

**Core responsibilities (online MVP):**

1. **Config store** under user config dir:
   - Linux: `~/.config/schnapp/`
   - Windows: `%AppData%\Schnapp\`
   - Files: `settings.json` (api base URL, email, default machine id/code, default timezone, …), `token` (file mode 600 / restrictive ACL)
2. **Token bootstrap:** login → store token; optional password (prompt, flag, or env); discard password after login
3. **HTTP API client:** login, whoami/me, machines list/register/find/link, journals start/stop/list, screenshot multipart upload
4. **Machine identity helpers:**
   - Detect candidate **hardware fingerprint** and **FQDN** (platform-specific implementations behind an interface; Core defines the interface + portable fallbacks)
   - Suggest default machine for relink/register; **persist chosen machine id** in settings after user list → register/link
   - Do **not** invent a machine silently without user action
5. **Tags helper:** normalize client-side to API-ready form (see [Tags](#tags))
6. **Timestamp helper:** parse `13:15` (today), full local datetime, `--now`; optional EXIF/mtime for upload default (CLI can call into helper)

**Not in Phase 8 (defer to 8c):** offline screenshot spool, mutation queue, SQLite list cache.

**Tests:** Core unit tests (config paths, tag split, timestamp parse, fake HttpMessageHandler for API).

**Done when:** Core builds as netstandard2.0; tests green; can be referenced by a net10 console app.

### Phase 8b — Schnapp.Cli (net10.0)

**Goal:** conversational CLI over Client.Core. **Does not capture screenshots**; uploads from files only.

**Project:** `clients/Schnapp.Cli` (or `src/Schnapp.Cli`), targets **net10.0** only. References Client.Core.

**Documentation:** `docs/cli.md` (and README pointer) — install/run, config dir layout, command reference, scripting notes, machine setup flow.

#### Parser (hybrid)

Accept options in any of:

| Form | Example |
|---|---|
| Long flag | `--at 13:15` |
| Short flag | `-at 13:15` |
| Windows slash | `/at 13:15` |
| Bare keyword | `at 13:15` |

Rules:

1. First tokens: **verb** (+ optional subverb): `login`, `journal start`, `upload`, …
2. **One primary positional** per command when needed: email, filename
3. Option keys are a **whitelist** (`password`, `at`, `now`, `notes`, `tags`, `monitor`, `machine`, `server`, …); unknown keys → usage error
4. Values with spaces require quotes (`--notes "how's this for a note"`)
5. Do **not** support free prose without keys (`upload shot.png this is a note` is invalid)
6. Implementation: small custom `ArgLexer` / argv walk (flexible enough for hybrid); System.CommandLine optional later if it fits

#### Commands (MVP)

```text
# Auth
schnapp login <email>                 # prompt for password if omitted
schnapp login <email> --password …    # scripted (history risk; also SCHNAPP_PASSWORD env)
schnapp logout
schnapp whoami

# Config / server
schnapp config show
schnapp config set server <url>

# Machine (required before journal/upload)
schnapp machine list
schnapp machine register [--name …] [--code …]   # uses detected FQDN/fingerprint by default
schnapp machine find                              # search matches (fingerprint/FQDN)
schnapp machine use <id>                          # link account if needed + set as default
schnapp machine show                              # default + detected identity

# Journal (uses default machine unless --machine)
schnapp journal start --now --notes "…" --tags cli,demo
schnapp journal start --at 13:15 --notes "…" --tags cli,demo
schnapp journal stop
schnapp journal list

# Upload from file (no capture)
schnapp upload <file>                             # timestamp: EXIF → mtime → now
schnapp upload <file> --at 13:15 --tags cli,demo --monitor monitor1
schnapp upload <file> at 13:15 tags cli,demo monitor monitor1
```

**Password:** optional in trusted/tame deployments (empty password login if server allows — today API requires password; if empty password accounts are created, CLI may send empty). Prefer prompt over `--password` when interactive. Env `SCHNAPP_PASSWORD` / `SCHNAPP_TOKEN` for scripts. Security hardening if project leaves trusted LAN is acknowledged, not blocking MVP.

**Exit codes:** 0 OK; 1 usage; 2 auth; 3 API; 4 local file/config error.

**Done when:** documented CLI can login, select machine, start/stop journal, upload file with timestamp; parser unit tests; smoke against running API.

### Phase 8c — Offline spool + CLI flush verbs

**Goal:** durable offline behaviour shared by tray clients and CLI.

**Core:**

- Screenshot spool (filesystem meta + bytes); caps; backoff
- Optional mutation queue + SQLite list/journal cache (as in [Clients](#clients) / offline sections)
- Sync coordinator: push spool → push mutations → pull

**CLI (synchronous nature):**

Offline spool is useless without explicit flush verbs:

```text
schnapp spool status          # counts, oldest, errors, disk use
schnapp spool push            # attempt upload all pending (or until failure)
schnapp spool clear --failed  # optional later
schnapp sync                  # push spool + mutations, then pull lists/journals if cache exists
```

`upload` when API unreachable: **enqueue** to spool and exit 0 with message “queued offline” (or exit 3 if user passed `--online-only`).  
`journal start` offline: enqueue mutation if 8c mutation queue exists; else fail clearly until implemented.

**Done when:** kill API → CLI upload queues → `spool push` drains; tests for spool survive process restart.

### Phase 9 — Linux Avalonia client (net10)

Tray capture client on Client.Core; X11 capture; ImageSharp encode; uses same config dir / machine / token as CLI when possible.

**Done when:** Linux tray captures and uploads; can share token/machine with CLI on same box.

### Phase 10 — Windows WinForms net48 client

net48 WinForms on Client.Core (netstandard2.0); DXGI/GDI capture; System.Drawing encode; xcopy layout.

**Done when:** Windows parity for capture/upload/journal.

### Phase 11 — Docs, polish, validation pass

Root README, **CLI docs**, strip/gate pure-dev Web chrome (`ShowDevTools`), full validation checklist.

**Done when:** success criteria met on a clean machine.

### Cross-cutting rules for all phases

- Ship automated tests with the phase that introduces the behaviour
- End API/Web phases with **manual Web smoke**; end 8b with **CLI smoke**
- Temporary/dev UI: label clearly
- No EF Core packages
- Keep API user-scoping on every screenshot/journal query
- Do not block phases on offline **conflict policy**
- Prefer small commits per phase subsection
- **Client.Core must remain net48-consumable** (netstandard2.0) from Phase 8 onward

---

## Validation (manual)

1. Bootstrap admin; create user B
2. User B login; set timezone; day URL uses that zone
3. **CLI (8b):** login → machine list/register/use → journal start → upload file with `--at` → appears in gallery
4. Linux tray (9): login→token, register machine, manual+interval capture, gallery shows thumbs
5. Kill API → tray/CLI spool; restore → `spool push` / sync drains (8c+)
6. Reinstall client without machineId → find matches → confirm → same MachineId
7. User A cannot open user B screenshot by id
8. Second Start on same machine auto-closes previous journal; uploads stamp new id
9. Edit notes/tags; delete removes files; tags stored space-separated
10. Non-admin CRUD lists; admin creates users
11. Windows client (10) parity upload appears only for that user
12. `dotnet test` green on clean machine with Postgres

---

## Bootstrap / config

- `ConnectionStrings:Main` (Npgsql connection string)
- `Schnapp:BlobRoot`
- `Schnapp:ApiBaseUrl` (Web → API)
- First run: `SCHNAPP_ADMIN_EMAIL` / `SCHNAPP_ADMIN_PASSWORD`
- Thumb: max edge 640px
- DbUp runs against `ConnectionStrings:Main` before accepting traffic

### Suggested packages (server)

- `Npgsql`, `Dapper`, `dbup-postgresql`
- `SixLabors.ImageSharp` (API thumbs only)
- `xunit`, `Microsoft.NET.Test.Sdk`, `FluentAssertions` (or similar), `Testcontainers.PostgreSql` (optional for integration)
- No `Microsoft.EntityFrameworkCore*` packages

### SQL type notes (init migration)

- Timestamps: `timestamptz` for UTC instants (`timestamp_utc`, `received_at_utc`, journal start/stop)
- Tags / last IPs: prefer **`jsonb`**
- Ids: **`uuid`** app-generated UUIDv7 for normal rows; offline client may mint UUIDv7; **nil UUID reserved** for Customer/Project/Task NONE sentinels
- Project/Task/JournalTarget parent FKs: `NOT NULL` with `DEFAULT '00000000-0000-0000-0000-000000000000'`
- Customer/Project/Task: `UNIQUE (code)`, `UNIQUE (name)` each
- Setting table: `key text PRIMARY KEY`, `value text NOT NULL`, optional notes/updated_at
- Seed in `001_init.sql` after tables exist:
  ```sql
  INSERT INTO customer (id, code, name) VALUES
    ('00000000-0000-0000-0000-000000000000', 'NONE', '(none)');
  INSERT INTO project (id, code, customer_id, name) VALUES
    ('00000000-0000-0000-0000-000000000000', 'NONE',
     '00000000-0000-0000-0000-000000000000', '(none)');
  INSERT INTO task (id, code, customer_id, project_id, name) VALUES
    ('00000000-0000-0000-0000-000000000000', 'NONE',
     '00000000-0000-0000-0000-000000000000',
     '00000000-0000-0000-0000-000000000000', '(none)');
  INSERT INTO setting (key, value, notes) VALUES
    ('journal.recent_last_seen_minutes', '30',
     'Minutes: UserMachine.LastSeen is recent for journal auto-close');
  ```
- JournalTarget uniqueness (no nulls):
  ```sql
  UNIQUE (customer_id, project_id, task_id)
  ```
- Partial unique open journal:
  ```sql
  CREATE UNIQUE INDEX ux_journal_one_open_per_user_machine
    ON journal_entry (user_id, machine_id)
    WHERE stop_utc IS NULL;
  ```

---

## Offline conflict resolution

This is the remaining open design item. **MVP implements the machinery** (local UUIDv7, mutation queue, error flags, enough metadata to resolve later) but **does not auto-merge** competing list/journal edits. Screenshots are simpler (append-only upload spool) and are mostly out of scope here.

### What can conflict?

| Entity | Offline action | Server uniqueness / rule | Typical conflict |
|---|---|---|---|
| Customer / Project / Task | Create | `UNIQUE (code)`, `UNIQUE (name)` per table | Two clients (or client + Web) create same Code or same Name |
| Customer / Project / Task | Update (later) | Same uniques + concurrent edits | Two edits to same row; offline rename collides |
| JournalTarget | get-or-create | `UNIQUE (customer_id, project_id, task_id)` | Usually **idempotent** if both use same triple + accepted client/server ids |
| JournalEntry | Create/start/stop/patch | One open per (UserId, MachineId); times/notes/target | Two machines for same user: **no open-journal conflict** (key includes MachineId). Same machine, two client copies / reinstall: possible double-open or overlapping segments |
| Screenshot | Upload | User-scoped insert; client UUIDv7 id optional | Duplicate upload of same id → treat as idempotent upsert or 409; rare if spool is single-writer |

**Highest-probability MVP conflict:** offline **list create** with a Code or Name another user (or the Web UI) already took while this client was offline.

### What does *not* usually conflict (by design)

- **Screenshot spool:** one client process owns the spool; retries are the same bytes/id. Server accepts client UUIDv7 → re-upload is idempotent if implemented as insert-or-ignore / upsert by id.
- **NONE sentinels:** hard-coded nil ids; never created offline.
- **Journal open rule across machines:** user@laptop and user@desktop each have their own open journal; no cross-machine unique violation.
- **JournalTarget triples:** deterministic get-or-create; two clients creating the same triple should converge on one row (prefer client-supplied JournalTarget UUIDv7 or server returns existing id and client rewrites).

### Identity model (already locked)

1. Client mints **UUIDv7** for new Customer/Project/Task/Journal/Screenshot **before** offline use.
2. Server **accepts client id** on create when valid and not nil.
3. If create succeeds, local id **is** server id — no rewrite needed.
4. Id rewrite only when server refuses client id or returns a different canonical row (e.g. “merge into existing”).

So conflicts are mostly **business-key** collisions (Code/Name), not random id collisions.

### Mutation queue record (minimum fields)

Each offline mutation should store enough to resolve later without guessing:

| Field | Purpose |
|---|---|
| `MutationId` | Local queue pk |
| `Type` | `ListCreate` / `ListUpdate` / `JournalUpsert` / … |
| `EntityType` | Customer, Project, Task, JournalEntry, … |
| `EntityId` | Client UUIDv7 |
| `Payload` | Full create/update body (code, name, parent FKs, journal times, target ids, …) |
| `ClientTimeUtc` | When user did it offline |
| `DependsOn` | Optional entity ids that must sync first (e.g. journal after new customer) |
| `Status` | `Pending` / `InFlight` / `Done` / `Error` |
| `ErrorCode` / `ErrorMessage` | e.g. `DuplicateCode`, `DuplicateName`, `409` body |
| `ServerConflictHint` | Optional: existing server id/code/name if API returns them |

### Sync order (reduces avoidable conflicts)

1. Push **list creates** (respect `DependsOn`: Customer before Project before Task)
2. Push **list updates** (if any)
3. Push **journal** mutations (after list ids they reference are `Done` or already on server)
4. Push **screenshot** spool (stamp `activeJournalEntryId` only after journal id is known good)
5. Pull lists + journals + settings into SQLite cache
6. Leave `Error` mutations in queue; do not block unrelated Pending items forever (skip or retry with backoff)

### Candidate policies (choose later)

#### A. Server-wins on unique key (simplest auto)

- On `409 DuplicateCode` / `DuplicateName`: drop or park local create; pull server row; **optionally** rewrite local references from local id → server’s existing id if API returns that row.
- **Pros:** no user prompt; cache converges to server truth.  
- **Cons:** user’s offline-created display name/code may vanish; journal may point at “wrong” semantic entity if we map by name alone.

#### B. Rename-local-and-retry (keep both)

- On duplicate Code: append suffix (`ACME` → `ACME-2` or `ACME-offline`) and retry create with **same UUIDv7** (or new id — prefer keep UUIDv7 if server never stored it).
- Same for Name.
- **Pros:** no data loss; both entities exist.  
- **Cons:** clutter; user must clean up; need stable suffix rules.

#### C. User prompt (safest product-wise)

- Tray/UI: “Customer code `ACME` already exists as ‘Acme Corp’. [Use existing] [Keep mine as ACME-2] [Edit…]”
- **Pros:** correct intent.  
- **Cons:** needs UI; blocks fully silent sync; more work (post-MVP friendly).

#### D. Last-write-wins on updates (journals / list edits)

- Compare `ClientTimeUtc` or `UpdatedAt`; apply newer patch.
- **Pros:** common for notes/times.  
- **Cons:** silent loss of the other edit; bad for Code/Name without care.

#### E. Hybrid (recommended direction when TBD is closed)

| Case | Policy |
|---|---|
| List **create**, same UUIDv7 already on server | Idempotent success (already created) |
| List **create**, new UUIDv7, Code or Name taken | **User prompt** or default **rename-and-retry**; never silently delete local work |
| List **update** conflict | Last-write-wins on Notes/Tags; Code/Name change that 409s → prompt or rename |
| Journal segment overlap same machine | Prefer server open-entry rule on push (auto-close); accept server’s close timestamps; surface Error only if API rejects |
| Journal references missing list id | Hold journal mutation until list create `Done` or Error |

### MVP behavior (locked until policy chosen)

1. Attempt create/update on server with client UUIDv7 + payload.
2. Success → mark mutation `Done`; ensure cache row matches server.
3. Failure unique constraint / 409 → mark `Error`, keep local cache row **usable** for journal/pickers, show tray badge (“sync issue”).
4. Do **not** auto-delete local entities; do **not** auto-rename in MVP unless you explicitly implement B.
5. Pull still runs so server-only rows appear in cache alongside conflicted local rows (local row may be “orphan” until resolved).
6. API should return structured errors when possible:
   ```json
   { "error": "DuplicateCode", "field": "code", "value": "ACME",
     "existingId": "…", "existingName": "Acme Corp" }
   ```
   so a future resolver can offer “use existing”.

### Journal-specific notes

- Open journal is unique per **(UserId, MachineId)** — not per user globally.
- Offline start while another open exists **locally**: client should apply same auto-close rule as server before enqueue, using cached LastSeen + `journal.recent_last_seen_minutes` (cached setting).
- On push, server re-applies rule; client accepts returned Start/Stop as truth.
- If two offline journal creates for same machine both link “open”, server keeps one open and closes the other per rule; client reconciles on pull.

### Screenshot + journal id stamping

- Offline capture may stamp `activeJournalEntryId` = local journal UUIDv7.
- Upload after journal sync: if journal create is `Error`, either hold screenshots or upload with null journal link + notes — **prefer hold** screenshot spool items that depend on an `Error` journal until resolved or user overrides.

### Decision checklist (when closing TBD)

Answer these and implement one hybrid:

1. Silent sync vs allow tray prompts?
2. Prefer **keep both** (rename) or **merge into existing** (server-wins) for duplicate Code/Name?
3. Are Code/Name conflicts case-sensitive? (DB collation — decide in `001_init.sql`)
4. Who may resolve: only the offline user, or admin in Web?
5. Max age of `Error` mutations before archive/drop?

**Suggested default when you pick a policy:** Hybrid **E** with **rename-and-retry** for unattended sync and optional prompt when tray is interactive.

---

## Open design items


| Topic                                               | Status                                                                   |
| --------------------------------------------------- | ------------------------------------------------------------------------ |
| **Offline list/journal conflict resolution**        | **TBD policy** — see [Offline conflict resolution](#offline-conflict-resolution); MVP = queue + UUIDv7 + Error flags + structured 409s |
| List Code/Name uniqueness                           | **Locked: both Code and Name UNIQUE per list table**; `NONE`/`(none)` reserved |
| List parent / JournalTarget null FKs                | **Locked: NOT NULL + seeded nil-UUID NONE rows** (see Well-known sentinels) |
| Org/server settings                                 | **Locked: Setting key-value table**; seed keys include `journal.recent_last_seen_minutes`, `screenshot.max_upload_bytes`, `screenshot.thumb_max_edge` (see `SettingKeys`) |
| Client cache storage                                | **Locked: local SQLite + Dapper** (not JSON files); seed NONE rows locally too |

---

## Open non-blockers (defaults)


| Topic                     | Default                                              |
| ------------------------- | ---------------------------------------------------- |
| net version API/Web/Linux | net10     |
| WebP on net48             | best-effort; default JPEG on Windows if missing      |
| Monitor entity table      | skip; store name on screenshot                       |
| WindowPrograms table      | skip or optional upsert later                        |
| Token storage Windows     | DPAPI if easy, else restrictive ACL on settings file |
| Token storage Linux       | chmod 600 config; libsecret optional later           |
| Id type                   | uuid (app-generated)                                 |
| DbUp journal table        | `schemaversions`                                     |

---

## Success criteria

Colleague gets admin-created account, runs Linux Avalonia (then Windows xcopy) client, authenticates once to a token, captures selected monitors with offline screenshot queue **and offline list/journal UX**, and reviews/edits/deletes own day gallery in the Web UI without seeing others’ screenshots. `dotnet test` passes as part of normal development.

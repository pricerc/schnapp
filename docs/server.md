# Server (API + Web)

## Schnapp.Api

ASP.NET Core REST API (`/api/v1/...`).

### Startup

- Configure Dapper Uuid7 handlers, DI for repositories, blob store, screenshot image processor
- Optional **DbUp** migrate on startup (`Schnapp:MigrateOnStartup`)
- Seed **admin** user if no users exist and admin credentials are configured
- Map endpoint modules: auth, machines, screenshots, journals, lists, health

### Auth

- Login with identity (email / code / name) + optional password → long-lived opaque token
- Bearer middleware attaches user principal
- Admin-only routes for user management, org machines, settings

### Screenshots

| Concern | Behavior |
|---------|----------|
| Upload | Multipart `file` + JSON `metadata` (jpeg/png/webp). Stored original is always JPEG. |
| Client id | Optional `metadata.id` (Uuid7); see [architecture.md](architecture.md) |
| Timestamp | Truncated to whole seconds (batch alignment) |
| EXIF / XMP | Ingest re-encode writes date taken, machine (`Make`), monitor (`Model`), title; XMP under `https://schnapp.price.org.nz/ns/1` for import |
| Thumbs | Generated on upload from the same decode; stored beside original |
| List | By profile-local calendar day or explicit UTC range; optional exact `monitor` name filter |
| Patch | Notes/tags: `null` leave unchanged, empty string clears; journal link has explicit set flag |
| Delete | Row + original + thumb blobs |

**Idempotent upload:** same client id + same user + matching machine, second, SHA-256 of the **stored JPEG**, size (and monitor when set) returns existing row without inserting a duplicate. SHA-256 is of the re-encoded file (EXIF/XMP included; receive time is not written into the file so retries match).

### Backfill existing blobs

Older originals were stored as WebP (`{id}.webp` + `{id}.thumb.jpg`). `schnapp-utils reencode` walks the catalog, re-encodes each original with the same JPEG EXIF+XMP path as upload, updates `storage_key` / hash / size / dimensions, then deletes leftover `.webp`. Already-tagged JPEGs are skipped unless `--force`. Default root is `/srv/schnapp` (maps kit `BlobRoot` `/opt/schnapp/...` onto that mount). Needs a catalog connection (`--connection`, `SCHNAPP_CONNECTION`, or `{root}/api/appsettings.json`).

Gallery thumbnails are stored as five tiers under `thumbs/{128,256,384,512,640}/` (see architecture). Shots uploaded before tiers existed only have `{id}.thumb.jpg`; run `schnapp-utils thumbs` (same connection/root flags; `--dry-run`, `--force`, `--jobs N`, `--progress`) to generate the tiers, point `thumb_storage_key` at the 640 tier, and remove the legacy file. It only creates missing tiers, so it is safe to re-run.

Idle detection can run on those blobs with `schnapp-utils --blobs` (same connection/root flags). `--monitor *` lists every monitor.

See [development.md](development.md) for command examples.

### Journals & lists

- Journal start/stop/list/patch; one open journal per user+machine
- Customers, projects, tasks CRUD; unique code and name; seeded NONE (empty Uuid7)
- Journal targets: get-or-create by customer/project/task triple

### Machines

- Register (new machine + user link), find by fingerprint/FQDN, link existing, list own, admin org-wide

### Storage config

| Setting | Purpose |
|---------|---------|
| Connection string `Main` | Catalog connection string (PostgreSQL or Firebird) |
| `Schnapp:DatabaseProvider` | `PostgreSQL` (default) or `Firebird` |
| `Schnapp:BlobRoot` | Image/thumb filesystem root |
| `Setting` table | Org knobs (max upload bytes, JPEG quality, thumb max edge, journal MRU window, …) |

## Schnapp.Web

Razor Pages BFF. All data access is HTTP to the API via **`ApiClient`** (no direct DB).

### Auth cookie

Login posts to API; token stored in **encrypted cookie**. Pages redirect to `/Login` when missing.

### Main surfaces

| Area | Notes |
|------|--------|
| **Home** | Day timeline, filters (day, monitor column, time, journal text, tags), bulk row edit/delete, shot viewer dialog |
| **Journals** | Grid + start dialog + full edit page (`/Journals/Edit/{id}`) |
| **Customers / projects / tasks** | Hierarchy editors |
| **Machines / profile / admin** | Machine edit, user profile timezone, admin users/machines/settings |

### Timezones

Profile timezone drives “today”, day bounds for lists (via API), and parsing of datetime-local fields on journal/start forms. Always load profile TZ **before** parsing local datetimes on POST.

### Viewer

- Keyboard: ←/→ image, ↑/↓ batch, PageUp/PageDown ±10 batches, zoom, Esc
- Save / save-and-close / delete (confirm) / close
- Linked journal vs covering-at-capture hint

## Domain & data projects

- **Domain:** pure entities and constants (no IO)
- **Data:** Dapper SQL, migrations under `Migrations/*.sql`, `FileSystemImageBlobStore` (or configured `IImageBlobStore`)

## Related

- [architecture.md](architecture.md)
- [development.md](development.md)
- [mvp-tech-plan.md](mvp-tech-plan.md)

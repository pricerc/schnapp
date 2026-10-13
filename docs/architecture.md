# Architecture

Schnapp is a **single-org** multi-user system: clients capture screenshots and journal context, the API stores metadata and image files, and the web UI reviews a user’s own data.

## High-level diagram

```
┌──────────────────────┐     ┌─────────────────────┐
│ Linux tray (Avalonia)│     │ CLI (no capture)    │
│  capture → spool     │     │  upload / journals  │
│  background push     │     │  spool push         │
└──────────┬───────────┘     └──────────┬──────────┘
           │                            │
┌──────────┴───────────┐                │
│ Windows tray (net48) │                │
│  GDI + WinForms      │                │
└──────────┬───────────┘                │
           │  HTTPS / HTTP + Bearer     │
           └────────────┬───────────────┘
                        ▼
                 ┌──────────────┐
                 │ Schnapp.Api  │
                 │  auth, CRUD  │
                 └───┬──────┬───┘
                     │      │
     PostgreSQL or Firebird 5 │      │ filesystem blobs
     (metadata, UTC instants) │      │ (JPEG original + thumb)
                     │      │
           ┌─────────▼──────▼─────────┐
           │ Browser → Schnapp.Web    │
           │ (Razor BFF, cookie→token)│
           └──────────────────────────┘
```

Dev ports (typical): API **5080**, Web **5081**.

## Processes and projects

| Project | Responsibility |
|---------|----------------|
| `Schnapp.Api` | REST surface, auth, validation, thumbs on upload |
| `Schnapp.Web` | Cookie session BFF; never holds client long-lived tokens in the browser |
| `Schnapp.Domain` | Entities, roles, well-known ids, setting keys |
| `Schnapp.Data` | Dapper repositories, DbUp SQL migrations, `IImageBlobStore` |
| `Schnapp.Contracts` | DTOs shared by API, Web, and clients |
| `Schnapp.Client.Core` | Settings, token, API client, screenshot spool, mutation queue, list/journal ref cache |
| `Schnapp.Client.Linux` | Tray UI, X11 capture, encode, journal session, calls Core |
| `Schnapp.Client.Windows` | WinForms net48 tray, GDI capture, System.Drawing + Imazen.WebP encode, calls Core |
| `Schnapp.Client.Cli` | Scriptable login/machine/journal/upload/spool |

## Screenshot identity and offline model

### Spool-first capture (tray)

1. Capture tick encodes one or more frames (per monitor / desktop / window).
2. Each frame is **`Enqueue`d** into the local spool (filesystem folder + SQLite `spool_item`).
3. A **background upload loop** (and a kick after capture) runs `PushAsync` with **`retainUploaded: true`**.
4. Successful uploads mark the row **Uploaded** and set **`server_screenshot_id`** from the API response; files stay for the recent-cache until size housekeeping evicts them.

There is **no** “upload first, cache second” path on the tray: the spool is both the offline queue and the local cache source of truth for pending work.

### Client id vs server id

| Field | Meaning |
|-------|---------|
| `spool_item.id` | Local PK: folder name, index key, UI offline-queue cell id. On enqueue, written into `upload.json` as `metadata.Id`. |
| `metadata.Id` (upload) | Preferred **screenshot** primary key on the server. |
| `server_screenshot_id` | Id returned by the API after upload (usually equals `spool_item.id`; may differ if the server reassigned). |

**Server upload rules** (when `metadata.Id` is present):

- If a row with that id exists for the same user and looks like the **same** shot (machine, capture second, SHA-256, size, monitor when present) → **idempotent success** (return existing).
- If the id exists but is **not** a true duplicate → mint a **new** id and insert.
- If no client id → server mints a new Uuid7.

Local UI and files always key off **`spool_item.id`**. Use `server_screenshot_id` for cross-checking against the server.

### Mutations (offline journals / lists)

Separate SQLite **`mutation`** queue (same `schnapp.db` as the spool index and `ref_*` cache): journal start/stop/patch and offline list creates. Upload loop pushes mutations **before** screenshots so journal links can resolve.

## Journals

- **Covering journal:** time range that contains a capture instant (work context for timeline group headings).
- **Linked journal:** explicit `screenshot.journal_entry_id` set at capture from the active open journal (or edited later).
- At most **one open journal** per `(user, machine)` on the server.
- **Journal target:** get-or-create triple (customer, project, task), including seeded NONE rows.

## Day browser (Web)

- Calendar day is interpreted in the user’s **profile IANA timezone** (not UTC).
- Home timeline groups shots into **batches** (same capture second).
- Columns: Window / Desktop / Monitor N (derived display buckets, not raw `monitor_name` alone).
- Viewer: left/right image, up/down batch (±10 with Page Up/Down), edit notes/tags/journal, delete with confirm.

## Auth

| Client | Mechanism |
|--------|-----------|
| Tray / CLI | Login once → long-lived **API bearer token** on disk |
| Browser | Login via Web → encrypted cookie holding token; Web `ApiClient` attaches `Authorization` |

Users are admin-created; roles include admin vs normal user.

## Storage

- **Catalog (PostgreSQL or Firebird 5 SuperServer):** users, machines, screenshots metadata, journals, lists, settings, tokens. Chosen with `Schnapp:DatabaseProvider`. Instants are UTC; IANA zones are stored separately.
- **Blobs:** originals at `{userId}/{yyyy}/{MM}/{dd}/{screenshotId}.jpg`; gallery thumbnails at `thumbs/{128|256|384|512|640}/{userId}/{yyyy}/{MM}/{dd}/{screenshotId}.jpg` (one file per tier; the Home thumbnail slider maps one step to one tier), all under `Schnapp:BlobRoot`. `GET /screenshots/{id}/thumb?size=N` snaps up to the next tier and falls back to a larger tier or the legacy `{id}.thumb.jpg` if a tier is missing. Upload accepts jpeg/png/webp; ingest re-encodes a JPEG original (EXIF for viewers, XMP under `https://schnapp.price.org.nz/ns/1` for import). SHA-256 is of that stored JPEG. Historical WebP originals are rewritten in place by `schnapp-utils reencode` (catalog `storage_key` becomes `{id}.jpg`); `schnapp-utils thumbs` back-fills thumbnail tiers.
- **Client SQLite (`schnapp.db`):** `spool_item`, mutation queue, reference cache for offline lists/targets/journals.

## Related docs

- [client.md](client.md) — paths, tray behavior, CLI, capture scale steps, Windows tray details
- [server.md](server.md) — API/Web details
- [development.md](development.md) — running and testing
- [mvp-tech-plan.md](mvp-tech-plan.md) — locked MVP choices

> Product intent (living notes). Implementation overview: [architecture.md](architecture.md), [README](../README.md).

I want to take regular screen shots of my system and save them to a database on my server.

I'd like to make it available to my colleagues to do the same.

I'd like to keep it fairly simple.

# Server

## Interfaces

### REST API

* REST API for the client programs to talk to
* image + metadata upload.
* machine/user state upload/download.

### Admin

* Administrative UI.
* DB maintenance.
* Maintain users.
* Maintain shared lists.

### User

* User must log in
  - initially simple user/password
  - allow for more sophistcated authentication later.

* User Review/Reporting UI

* Navigable, filterable, view of users' own data, with filters and reporting functions.
  - user should be able to navigate by url (i.e. put a date into the url path like http://server:54321/2026/06/14/ and see the images for that day)
  - user should be able to preview large thumbnails for a day at a time on a single page.
  - filter by time/monitor/program/window/customer/project, etc.
  - first/previous/item #/next/last navigation buttons for images.
  - popup "zoom" for individual image, with navigation buttons.
  - metadata editing.
  - journal editing.

* Reports
  - time active
  - customer time
  - project time
  - task time
  - time by tags

## Entities

The following entities/lists need to be saved in the database:

### Machine

* MachineId#
* Name
* Owner
* Optional Password
* Optional Tags
* Optional Notes
* Monitors (to be configured through UI)
  - MonitorId#
  - Resolution

### User

* UserId#
* Name
* Email
* Optional Password
* Optional Tags
* Optional Notes

### Lists

* Customers
  - CustomerId
  - Name
  - Optional Contact details
  - Optional Notes
  - Optional Tags

* Projects
  - ProjectId
  - Name
  - Optional CustomerId
  - Optional Notes
  - Optional Tags

* Tasks
  - TaskId
  - Name
  - Optional CustomerId
  - Optional ProjectId
  - Optional Notes
  - Optional Tags
* WindowsPrograms
  A self-building/de-duplicating list of Windows/Programs

  - WindowId#
  - Program name / command line
  - Optional Notes
  - Optional Tags

### ScreenShot

* ScreenShotId#
* ParentShotId# - (future) this image is a difference from the image whose ScreenShotId# = this ParentShotId#.
* Timestamp - date/time/timezone to nearest second.
* Image - format TBD
* MachineId#
* UserId#
* Optional MonitorId#
* Optional WindowId#
* Optional Notes
* Optional Tags
* Link to Client Journal

### Client Journal

* JournalEntryId#
* Start / Stop
* Optional Customer
* Optional Project
* Optional Task
* Optional Notes
* Optional Tags

## Storage

* Metadata, including image metadata, in the database
* Images: To be decided - in DB or in file system.

# Client

* The "client" should run in the system tray and allow for timed and manual screen shots.
* It should be lightweight with a minimalist UI.

## Client Platforms

* Xlibre on Linux
* Windows

## Client UX

* Clients should be minimalist

  - Chrome and controls should be minimal.
* Clients should look "native"

  - On Windows, it should look like a simple Windows app.
  - On Linux, it should like a simple X11 app.
* There should be a simple "settings" dialog for static information.

  - Server address.
  - Machine registration.
  - User registration.
  - Screen / Monitor configuration.
    - with per-monitor scaling/compression/manual resolution.
  - Screen shot frequency.
  - (Future) Exclusion patterns.
* Dynamic user interaction for "Client Journal" should be via popup menu from the system tray.

  - Select Customer / Project / Task.
  - Specify Tags.
  - Edit notes.
  - Start/Pause/Resume/Stop screen capture.
* Client Journals and screenshots may overlap.

  - i.e. many-to-many relationship

## What needs to be recorded?

1. One or more of the following, as determined by user settings:

   * Selected monitors
   * Currently active window
     - optional scaling/compression/manual resolution - ideally automatic depending on the monitor.
   * Entire desktop
     - optional scaling/compression/manual resolution - ideally automatic based on the individual monitor settings.
   * *Note: because X11 may report resolutions different from the physical resolutions to aid scaling, these need to be configurable by the user.*

2. Metadata per screenshot.

   * Stored as JSON in image metadata and uploaded with image to server.
   * MachineId# (derived from environment or specified by user)
   * UserId# (derived from environment or specified by user)
   * Date/Time/Timezone
   * MonitorId#/Name, Window/Program name/command line.
   * Optional:
     - Customer
     - Project
     - Task
     - Notes
     - 'active' Client JournalEntryId#

3. Client Journal

   * Record when changed
   * Stop/Pause on system inactivity/screen saver activation

# Technology requirements

## Languages

* Languages must be relatively mature and stable.
* Languages must be compiled, not interpreted.

## Frameworks

* Frameworks must be cross-platform and open source.

## Image formats

* Must enable arbitrary length metadata for later decoding.

## Platforms

* The server will be running Linux (Fedora, probably).
* I want to use a Postgresql database.
* The service should be able to be hosted in a Podman pod.*

# Future ideas/considerations

* Exclusions - Hide/do not screenshot some Windows based on "rules" - e.g. banking, email, specific programs.

* Detect if there has been activity before taking another screenshot
  - maybe exclude clock rectangle?
  - maybe record diff from previous image

* Image Deduplication
  - detect if there have been no/few changes over time.
  - using perceptual hashing or structural similarity ?

* Security/Encryption
  - Watermarks

* Housekeeping
    - merge adjacent journal entries for same customer/project/task within a day
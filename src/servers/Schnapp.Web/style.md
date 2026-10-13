# Schnapp.Web UI style notes

Working notes for dialogs, editors, and shared field layouts.  
**Status:** guide decided (2026-07-23). Implementation started on branch `web/entity-editor-layout`  
(entity-fields CSS, session dialog geometry, C/P/T + machine Status/Colour, notes textarea, one dialog size).

---

## Style guide (decided)

### Consistency principle

A field with a given **name** should appear in **more-or-less the same place** on every entity page, card, and dialog. Prefer one shared layout pattern (“entity editor”) over one-off grids.

### Shells

| Kind | Class | Use when |
|------|--------|----------|
| Page section | `.card` | Full-page editors, lists, non-modal forms |
| Entity editor modal | `.form-dialog` (+ `.form-dialog-body`) | Create/edit in a `<dialog>` |
| Image viewer | `.viewer-dialog` | Screenshot lightbox **only** (special case) |

**Retired / fold in**

- `.password-dialog` → use `.form-dialog` (keep password-field affordances).
- `.form-dialog-wide` as a second editor size → **drop**; one editor popup size.
- Tiny vs large create dialogs → same shell as editors.

**Open/close:** `data-open-dialog="id"` / `data-close-dialog` (`wwwroot/js/site.js`).  
**Submit safety:** `data-disable-on-submit` on forms.

**Resize & session geometry**

- **All** editor dialogs are **resizable**.
- Remember **size and position for the browser session** (e.g. `sessionStorage` by dialog `id`). Not DB-persisted.
- Image viewer stays its own shell; may keep its own geometry key.

**One editor size**

- Single default width/height band for entity editors (create and edit).
- Exact CSS values TBD once; stop mixing 28rem default vs wide 48rem+.

### Dialog anatomy

```
<dialog id="…" class="form-dialog form-dialog-resizable">
  <form class="form-dialog-body" data-disable-on-submit>
    <h2>…</h2>
    <p class="muted">optional help</p>
    … entity-fields …
    <div class="form-dialog-actions">
      primary Save/Create · secondary Cancel (data-close-dialog)
    </div>
    <!-- entity id footer: see Identity -->
  </form>
</dialog>
```

Page-level editors: same field block inside `.card`, actions via `.form-actions`  
(Save · Save and close · Close).

### Entity field layout (generalised “meta fields”)

Expand beyond Code/Name/Tags/Notes into a **two-column entity editor**.

**Desktop (wide) — short right column** (e.g. Customer / Project / Task: only Status + Colour on the right):

```
 LEFT                         RIGHT
 ──────────────────────       ──────────────────────────
 Code                         Status (dropdown)
 Name                         Colour code (picker)
 Tags
 Notes-label | Notes textarea spanning full table width (3 lines)
```

Notes is the **only control on the last row**. Label stays in the **first column** (same width as other labels). The editor spans **all remaining columns** of the entity-fields table (not only the left half).

**Desktop (wide) — tall left column** (e.g. Machine: extra left fields):

```
 LEFT                         RIGHT
 ──────────────────────       ──────────────────────────
 Code                         Status
 Name                         Colour
 FQDN / fingerprint / …       [category attrs…]
 Tags                         Notes  (label above; stretches
                                      to bottom of right col)
```

**Narrow:** single column, top → bottom:

1. Code  
2. Name  
3. Entity lookups (C → P → T, etc.)  
4. Other text fields (email, FQDN, …)  
5. Tags  
6. Status  
7. Colour code  
8. Category attributes (timezone, role, …)  
9. Notes (full width)

#### Placement rules (must)

| Field / group | Placement |
|---------------|-----------|
| **Code** | Always **top-left** |
| **Name** | Directly **below Code** (left) |
| **Entity lookups** (customer, project, task, machine, journal, …) | Left column, **below Name** |
| **Additional text** (email, FQDN, fingerprint, display name, …) | Left column, **below Name** (with/after lookups as fits domain) |
| **Tags** | Left column, after lookups/extra text (before Notes when Notes is on the left) |
| **Status** | Always **top-right**; **dropdown** (not free numeric) |
| **Colour code** | Right column, **below Status**; colour picker → named or HTML colour string |
| **Category / attribute selects** (timezone, role, …) | Right column, **below colour** |
| **Notes** | See **Notes placement** below; multiline (**≥ 2 rows**, prefer 3) |

#### Notes placement

| Situation | Where | Label | Editor |
|-----------|--------|--------|--------|
| Short right column (C/P/T, create dialogs, UserEdit with notes not on right) | **Last row of the whole entity-fields table** | **First column** (same label width as Code/Name) | **Spans remaining columns** to full table width; `rows="3"` |
| Tall left column (Machine extras, etc.) | **Right column**, last | **Above** textarea | Stretches to bottom of right column |
| Tags+notes-only subset | Tags left / Notes right (or stack on narrow) | By column rules | Never single-line `<input>` |

Do **not** leave Notes orphaned in only the left half (or only the right half under a short Status/Colour stack) when it is the sole field on the last row — use the full-width last row instead.

#### Markup / CSS

- Use **`.entity-fields`** / **`.entity-col-left`** / **`.entity-col-right`**.
- Full-width Notes last row (sibling of the two columns, not nested in left col):

```html
<div class="entity-fields">
  <div class="entity-col entity-col-left">…</div>
  <div class="entity-col entity-col-right">…</div>
  <div class="field-row field-notes-row entity-notes-span">
    <label>Notes</label>
    <textarea rows="3"></textarea>
  </div>
</div>
```

- `.entity-notes-span` → `grid-column: 1 / -1`; label column + editor fills the rest.
- Notes on right: `.field-notes` as last child of `.entity-col-right` (flex-grow).
- Domain blocks compose inside left/right columns; full-width Notes sits under both.

#### Labels

- Default: **label to the left of** the control (`.field-row`).
- **Full-width Notes row:** label in first column, left of the textarea.
- **Notes on the right column only:** label **above** the textarea is OK.

### Identity + actions footer

- Wrap actions + id in **`.entity-footer`**:
  - **Left:** `.form-actions` / `.form-dialog-actions` (Save · Save and close · Close)
  - **Right:** `.entity-id` — Id26, muted mono
- Id sits at the **very bottom** of the card/dialog; **bottom-aligned** with the action buttons (not above them).
- Create dialogs may omit id until the entity exists.

### Actions

| Context | Class | Buttons |
|---------|--------|---------|
| Dialog | `.form-dialog-actions` | Primary + Cancel |
| Page card | `.form-actions` | Save · Save and close · Close |
| Grid row | `.icon-btn` | Edit / trash / stop |

Prefer `btn-with-icon` + `_Icon`. Unify password actions into `.form-dialog-actions`.

### Selectors & list display

- Options: prefer **`code - name`**; **name under select** (`.select-caption`) when the value is code-forward.
- Journal targets in lists: **codes preferred**.
- Cascaded project/task: **NONE / (none)** always available.
- **Status**: always a **`<select>`** of known statuses for that entity type (not raw int).

### Colour code

- Control: colour picker supporting **named** and **HTML** colours; stored string on entity (`ColourCode`).
- “For future use” in product behaviour is fine; **field still present** on editors for all entities that have the column.

### Data expectations (product)

- **All entities** expose **Status** and **ColourCode** (API + UI).
- Wire customer/project/task (and any missing) status dropdowns + colour picker when backend already has columns; add API/UI where missing.

---

## Dialog inventory (current code vs guide)

### Shared partials

| Id | File | Current | Gap vs guide |
|----|------|---------|----------------|
| `journal-editor-dialog` | `_JournalEditor.cshtml` | wide + resizable; journal; target; times; tags/notes | Not single size; no session geometry; no status/colour; labels above; no id footer |
| `journal-target-editor-dialog` | `_JournalTargetEditor.cshtml` | wide + resizable; C/P/T; meta code/name/tags/notes | C/P/T should sit **left under name**; status/colour missing; same chrome gaps |

### Home (`Index.cshtml`)

| Id | Current | Gap vs guide |
|----|---------|----------------|
| `home-upload-dialog` | fixed form-dialog; 3× grid-2; notes **input** | Resizable + session geo; notes textarea; field placement vs entity rules (upload is hybrid) |
| `home-row-edit-dialog` | resizable; tags/notes subset | Session geo; OK as tags/notes subset |
| `home-bulk-edit-dialog` | fixed; checklist | Resizable + session geo; optional align notes/tags placement |
| `shot-viewer` | viewer-dialog | Special case — keep separate |

### Customers (create dialogs)

| Id | Current | Gap vs guide |
|----|---------|----------------|
| `create-customer-dialog` | meta-fields, fixed size | Resizable + session; status + colour; label-left later; id N/A on create |
| `create-project-dialog` | same | same |
| `create-customer-task-dialog` | same | same |
| `create-project-task-dialog` | same | same |

### Admin

| Id | Current | Gap vs guide |
|----|---------|----------------|
| `create-user-dialog` | grid-2 stacks; no tags/notes | Entity layout: code/name left; status/colour right; email left; role/tz right; tags; notes |
| `password-dialog` | separate shell | → form-dialog; resizable optional (small) |
| `register-machine-dialog` | stacked; name before code | Code top-left, name below; status/colour; tags/notes; machine attrs left |

### Page card editors

| Page | Current | Gap vs guide |
|------|---------|----------------|
| `Customers/Edit|Project|TaskEdit` | meta-fields code/name/tags/notes | Status dropdown + colour; id footer bottom-right (admin id today is conditional) |
| `Machines/Edit` | has-status meta + extra grids | Colour; machine fields left under name; id footer style |
| `Admin/UserEdit` | has-status (numeric!) | Status **select**; colour; email left; role/tz right; id footer |
| `Account/Profile` | meta + email/tz | Align columns; colour/status if profile is “entity”; id footer |
| `Screenshots/View` | tags/notes grid-2, notes input | tags/notes pattern; notes ≥2-line textarea |
| `Journals/Index` start | machine + notes line | Notes multiline if kept |

---

## Open implementation work (from notes)

Ordered for alignment with decisions:

1. **CSS/layout:** generalise entity two-column grid; Code/Name/Tags left; Status/Colour/attrs/Notes right; narrow stack order as above.
2. **Notes:** enforce textarea ≥2 rows everywhere (upload, Screenshots/View, journal start, …).
3. **Status:** dropdown on all entity editors; replace UserEdit numeric input; add C/P/T status UI.
4. **ColourCode:** picker control + bind on all entity editors (persist even if unused in product yet).
5. **Dialog chrome:** one editor size; all resizable; **sessionStorage** position/size by `id`.
6. **Fold** password-dialog into form-dialog; drop wide-as-second-size.
7. **Id26 footer** bottom-right muted on edit surfaces.
8. **Labels left** of controls (Notes may stay above) — migrate from label-above.
9. **Shared partial** (e.g. `_EntityFields.cshtml`) once grid is stable — reduce copy-paste.
10. Optional: `formaction="?handler=…"` hygiene later.

---

## Resolved vs previous undecided

| Was undecided | Decision |
|---------------|----------|
| Empty status column vs notes up | **Status always top-right** (all entities have status) |
| Multiple dialog widths | **One** editor popup size |
| Which dialogs resize | **All** editor dialogs (+ session geometry) |
| password-dialog | Fold into form-dialog |
| Notes length cue | **≥ 2 lines** (prefer 3) |
| Label position | **Left of control** (Notes exception OK) |
| Colour | First-class field under status |

---

## Your notes (source)

> consistency is important - a field with a specific name should always be in at least more-or-less the same place.  
> expanding the 'meta fields' dialog to be more generic.  
> In any entity page/dialog/card: code top-left, name below; entity lookups and additional text below name on the left; tags last on the left.  
> Top-right status (dropdown); below it colour code (picker → named or HTML); below that category selectors (timezone, role).  
> Notes last on the right, or below tags spanning both columns; notes ≥ two lines.  
> Entity id Id26 bottom-right, muted.  
> All entities: status + ColourCode (colour future use).  
> All dialogs resizable; remember position for session (not DB).  
> One size for editor popups (image viewer special).  
> Prefer labels left of editor/selector (except maybe notes).

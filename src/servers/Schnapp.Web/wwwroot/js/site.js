(function () {
  document.querySelectorAll("[data-confirm]").forEach(function (el) {
    el.addEventListener("click", function (e) {
      var msg = el.getAttribute("data-confirm") || "Are you sure?";
      if (!window.confirm(msg)) {
        e.preventDefault();
        e.stopPropagation();
      }
    });
  });

  // Journals: cascade project/task by parent (grid rows, start dialog, full editor page).
  (function initJournalCptCascade() {
    function filterOptions(select, parentId) {
      if (!select) return;
      var keep = select.value;
      var firstOk = "";
      var keepOk = false;
      Array.prototype.forEach.call(select.options, function (opt) {
        if (!opt.value) {
          opt.hidden = false;
          return;
        }
        var isNone = opt.getAttribute("data-none") === "1";
        var p = opt.getAttribute("data-parent") || "";
        var show = isNone || !parentId || !p || p === parentId;
        opt.hidden = !show;
        if (show && !firstOk) firstOk = opt.value;
        if (show && opt.value === keep) keepOk = true;
      });
      select.value = keepOk ? keep : (firstOk || "");
    }

    function wireGroup(root) {
      var cust = root.querySelector('[data-journal-cascade="customer"]');
      var proj = root.querySelector('[data-journal-cascade="project"]');
      var task = root.querySelector('[data-journal-cascade="task"]');
      if (!cust || !proj || !task) return;
      function syncProjects() {
        filterOptions(proj, cust.value);
        filterOptions(task, proj.value);
      }
      function syncTasks() {
        filterOptions(task, proj.value);
      }
      cust.addEventListener("change", function () {
        proj.value = "";
        task.value = "";
        syncProjects();
      });
      proj.addEventListener("change", function () {
        task.value = "";
        syncTasks();
      });
      syncProjects();
    }

    document.querySelectorAll("tr[data-journal-row]").forEach(wireGroup);
    document.querySelectorAll("[data-journal-start-dialog]").forEach(wireGroup);
    document.querySelectorAll("[data-journal-edit-page]").forEach(wireGroup);
    document.querySelectorAll("#journals-bulk-edit-dialog").forEach(wireGroup);
  })();

  // Journals grid: multi-row selection + bulk edit/delete (checkboxes always visible).
  (function initJournalsMultiSelect() {
    var page = document.getElementById("journals-page");
    var grid = document.getElementById("journals-grid");
    if (!page || !grid) return;

    page.classList.add("row-select-enabled");

    var editBtn = document.getElementById("journals-bulk-edit-rows");
    var deleteBtn = document.getElementById("journals-bulk-delete-rows");
    var countEl = document.getElementById("journals-row-selected-count");
    var selectAll = document.getElementById("journals-select-all-rows");
    var bulkEditDlg = document.getElementById("journals-bulk-edit-dialog");
    var bulkIdsHost = document.getElementById("journals-bulk-edit-ids");

    function rowInputs() {
      return Array.prototype.slice.call(grid.querySelectorAll("input.row-select"));
    }

    function selectedCount() {
      return rowInputs().filter(function (el) { return el.checked; }).length;
    }

    function openBulkEdit() {
      if (!bulkEditDlg || typeof bulkEditDlg.showModal !== "function") return;
      if (bulkIdsHost) {
        bulkIdsHost.innerHTML = "";
        rowInputs().filter(function (el) { return el.checked; }).forEach(function (el) {
          var input = document.createElement("input");
          input.type = "hidden";
          input.name = "SelectedIds";
          input.value = el.value;
          bulkIdsHost.appendChild(input);
        });
      }
      bulkEditDlg.showModal();
    }

    function syncSelection() {
      var rows = rowInputs();
      var selected = rows.filter(function (el) { return el.checked; });
      var n = selected.length;
      if (countEl) {
        countEl.textContent = n + " row(s) selected";
        countEl.hidden = false;
      }
      if (deleteBtn) deleteBtn.disabled = n === 0;
      if (editBtn) {
        editBtn.disabled = n === 0;
        editBtn.title = n > 0 ? "Edit selected rows" : "Select rows to edit";
      }
      if (selectAll && rows.length) {
        selectAll.checked = n === rows.length && n > 0;
        selectAll.indeterminate = n > 0 && n < rows.length;
      }
      rows.forEach(function (el) {
        var tr = el.closest("tr");
        if (tr) tr.classList.toggle("is-selected", el.checked);
      });
    }

    if (editBtn) {
      editBtn.addEventListener("click", function () {
        if (selectedCount() > 0) openBulkEdit();
      });
    }

    document.addEventListener("change", function (e) {
      var t = e.target;
      if (!t || !page.contains(t)) return;
      if (t.classList && t.classList.contains("row-select")) syncSelection();
      if (t.id === "journals-select-all-rows") {
        var checked = !!t.checked;
        rowInputs().forEach(function (el) { el.checked = checked; });
        syncSelection();
      }
    });

    page.addEventListener("click", function (e) {
      var t = e.target;
      if (!t || !t.closest) return;
      if (t.closest(".row-select, input, button, a, label, select, textarea, .row-lead")) return;
      var tr = t.closest("tr.journal-data-row");
      if (!tr || !grid.contains(tr)) return;
      var cb = tr.querySelector("input.row-select");
      if (!cb) return;
      cb.checked = !cb.checked;
      syncSelection();
    });

    function wireBulkField(checkId, fieldId) {
      var check = document.getElementById(checkId);
      var field = document.getElementById(fieldId);
      if (!check || !field) return;
      function sync() { field.disabled = !check.checked; }
      check.addEventListener("change", sync);
      sync();
    }
    wireBulkField("journals-bulk-set-customer", "journals-bulk-customer");
    wireBulkField("journals-bulk-set-project", "journals-bulk-project");
    wireBulkField("journals-bulk-set-task", "journals-bulk-task");

    syncSelection();
  })();

  // List code suggestion: STEM (2–4) + always ### (e.g. MI001). Matches ListCodeGenerator.cs.
  (function initListCodeSuggest() {
    function splitWords(name) {
      var words = [];
      var cur = "";
      var s = (name || "").trim();
      for (var i = 0; i < s.length; i++) {
        var ch = s.charAt(i);
        if (/[A-Za-z0-9]/.test(ch)) cur += ch.toUpperCase();
        else if (cur) { words.push(cur); cur = ""; }
      }
      if (cur) words.push(cur);
      return words;
    }
    function stemFromName(name) {
      var words = splitWords(name);
      var stem;
      if (!words.length) stem = "XX";
      else if (words.length === 1) {
        var w = words[0];
        if (w.length < 2) stem = (w + "XX").slice(0, 2);
        else stem = w.slice(0, Math.min(4, w.length));
        if (stem.length < 2) stem = (stem + "XX").slice(0, 2);
      } else {
        stem = "";
        for (var i = 0; i < words.length && stem.length < 4; i++) stem += words[i].charAt(0);
        var first = words[0];
        var j = 1;
        while (stem.length < 2 && j < first.length) { stem += first.charAt(j); j++; }
        while (stem.length < 2) stem += "X";
        if (stem.length > 4) stem = stem.slice(0, 4);
      }
      stem = stem.toUpperCase();
      if (stem === "NONE" || stem === "NULL") stem = "XX";
      return stem;
    }
    function allocate(name, existing) {
      var stem = stemFromName(name);
      var taken = {};
      (existing || []).forEach(function (c) {
        if (c) taken[String(c).trim().toUpperCase()] = true;
      });
      for (var n = 1; n <= 999; n++) {
        var code = stem + ("000" + n).slice(-3);
        if (!taken[code.toUpperCase()]) return code;
      }
      return stem + "999X";
    }
    function parseExisting(form) {
      try {
        var raw = form.getAttribute("data-code-existing") || "[]";
        var arr = JSON.parse(raw);
        return Array.isArray(arr) ? arr : [];
      } catch (_) { return []; }
    }
    document.querySelectorAll("form[data-code-suggest]").forEach(function (form) {
      var nameEl = form.querySelector("[data-name-field]");
      var codeEl = form.querySelector("[data-code-field]");
      if (!nameEl || !codeEl) return;
      var lastAuto = "";
      function suggest() {
        var name = nameEl.value || "";
        if (!name.trim()) {
          if (!codeEl.value || codeEl.value === lastAuto) {
            codeEl.value = "";
            lastAuto = "";
          }
          return;
        }
        // Only overwrite empty code or our previous suggestion (don't stomp manual edits).
        if (codeEl.value && codeEl.value !== lastAuto) return;
        var next = allocate(name, parseExisting(form));
        codeEl.value = next;
        lastAuto = next;
      }
      nameEl.addEventListener("input", suggest);
      nameEl.addEventListener("change", suggest);
      codeEl.addEventListener("input", function () {
        if (codeEl.value !== lastAuto) lastAuto = ""; // user took over
      });
    });
  })();

  // Dialog geometry (session only) + open/close
  // v2: height only restored after user resize (fit-content default for form dialogs).
  function dlgGeoKey(dlg) {
    return "schnapp.dlg.geo.v2." + (dlg.id || "anon");
  }
  function saveDialogGeometry(dlg) {
    if (!dlg.id || !dlg.open) return;
    try {
      var r = dlg.getBoundingClientRect();
      var payload = {
        w: Math.round(r.width),
        t: Math.round(r.top),
        l: Math.round(r.left)
      };
      // Persist height only once the user has resized (inline height set).
      if (dlg.style.height && dlg.style.height !== "fit-content" && dlg.style.height !== "auto") {
        payload.h = Math.round(r.height);
        payload.resized = true;
      } else {
        var prev = null;
        try { prev = JSON.parse(sessionStorage.getItem(dlgGeoKey(dlg)) || "null"); } catch (_) { /* ignore */ }
        if (prev && prev.resized && prev.h) {
          payload.h = prev.h;
          payload.resized = true;
        }
      }
      sessionStorage.setItem(dlgGeoKey(dlg), JSON.stringify(payload));
    } catch (_) { /* ignore */ }
  }
  function restoreDialogGeometry(dlg) {
    if (!dlg.id) return;
    try {
      var raw = sessionStorage.getItem(dlgGeoKey(dlg));
      if (!raw) return;
      var g = JSON.parse(raw);
      if (!g || !g.w) return;
      dlg.style.width = g.w + "px";
      if (g.resized && g.h) dlg.style.height = g.h + "px";
      else dlg.style.height = "";
      // Image viewer always stays centered (old sessions may have pinned left:0).
      if (dlg.classList.contains("viewer-dialog")) {
        dlg.style.margin = "auto";
        dlg.style.position = "";
        dlg.style.top = "";
        dlg.style.left = "";
        dlg.style.inset = "";
        return;
      }
      if (typeof g.t === "number" && typeof g.l === "number") {
        // Ignore clearly broken origins (e.g. left edge from a bad first layout).
        var maxL = Math.max(0, window.innerWidth - (g.w || 200) - 8);
        var maxT = Math.max(0, window.innerHeight - (g.h || 200) - 8);
        var left = Math.min(Math.max(0, g.l), maxL);
        var top = Math.min(Math.max(0, g.t), maxT);
        dlg.style.margin = "0";
        dlg.style.position = "fixed";
        dlg.style.top = top + "px";
        dlg.style.left = left + "px";
      }
    } catch (_) { /* ignore */ }
  }
  function openDialog(dlg) {
    if (!dlg || typeof dlg.showModal !== "function") return;
    dlg.showModal();
    var focus = dlg.querySelector("[autofocus], input:not([type=hidden]), select, textarea");
    if (focus) try { focus.focus(); } catch (_) { /* ignore */ }
  }

  document.querySelectorAll("[data-open-dialog]").forEach(function (btn) {
    btn.addEventListener("click", function () {
      var id = btn.getAttribute("data-open-dialog");
      openDialog(id && document.getElementById(id));
    });
  });
  document.querySelectorAll("dialog").forEach(function (dlg) {
    if (typeof dlg.showModal !== "function") return;
    // Restore geometry for every open path (data-open-dialog, journal editor, row edit, …).
    var nativeShowModal = dlg.showModal.bind(dlg);
    dlg.showModal = function () {
      restoreDialogGeometry(dlg);
      return nativeShowModal();
    };
    dlg.querySelectorAll("[data-close-dialog]").forEach(function (btn) {
      btn.addEventListener("click", function () {
        saveDialogGeometry(dlg);
        dlg.close();
      });
    });

    // Backdrop dismiss: only a true click on the backdrop (not resize drag, not scroll).
    // Resizing fires pointer events with target === dialog; that must not close and wipe edits.
    // Form editors: no backdrop dismiss at all (Cancel / Esc only) to avoid losing work.
    var isEditorDialog = dlg.classList.contains("form-dialog")
      || dlg.classList.contains("password-dialog");
    if (!isEditorDialog) {
      var backdropGesture = null;
      dlg.addEventListener("pointerdown", function (e) {
        if (e.target !== dlg || e.button !== 0) {
          backdropGesture = null;
          return;
        }
        var r = dlg.getBoundingClientRect();
        backdropGesture = {
          x: e.clientX,
          y: e.clientY,
          w: r.width,
          h: r.height,
          pointerId: e.pointerId
        };
      });
      dlg.addEventListener("pointerup", function (e) {
        if (!backdropGesture || e.pointerId !== backdropGesture.pointerId) return;
        var g = backdropGesture;
        backdropGesture = null;
        if (e.target !== dlg) return;
        var r = dlg.getBoundingClientRect();
        var moved = Math.hypot(e.clientX - g.x, e.clientY - g.y) > 5;
        var resized = Math.abs(r.width - g.w) > 1 || Math.abs(r.height - g.h) > 1;
        if (moved || resized) return;
        saveDialogGeometry(dlg);
        dlg.close();
      });
      dlg.addEventListener("pointercancel", function () { backdropGesture = null; });
    }
    dlg.addEventListener("close", function () { saveDialogGeometry(dlg); });
    if (typeof ResizeObserver !== "undefined") {
      var ro = new ResizeObserver(function () {
        if (dlg.open) saveDialogGeometry(dlg);
      });
      ro.observe(dlg);
    }
  });

  // Colour swatch ↔ text sync
  document.querySelectorAll(".colour-field").forEach(function (wrap) {
    var swatch = wrap.querySelector('input[type="color"]');
    var text = wrap.querySelector('input[type="text"]');
    if (!swatch || !text) return;
    function toHex(v) {
      v = (v || "").trim();
      if (/^#[0-9a-fA-F]{6}$/.test(v)) return v.toLowerCase();
      if (/^#[0-9a-fA-F]{3}$/.test(v)) {
        return ("#" + v[1] + v[1] + v[2] + v[2] + v[3] + v[3]).toLowerCase();
      }
      return null;
    }
    function syncFromText() {
      var hex = toHex(text.value);
      if (hex) swatch.value = hex;
    }
    swatch.addEventListener("input", function () { text.value = swatch.value; });
    text.addEventListener("change", syncFromText);
    text.addEventListener("blur", syncFromText);
    syncFromText();
  });

  document.querySelectorAll("form[data-disable-on-submit]").forEach(function (form) {
    form.addEventListener("submit", function () {
      form.querySelectorAll("button[type=submit], input[type=submit]").forEach(function (btn) {
        btn.disabled = true;
      });
    });
  });

  // Home: optional per-image selection mode + row bulk/edit
  (function initHomeGallery() {
    var gallery = document.getElementById("home-gallery");
    if (!gallery) return;

    var enableShotSelect = document.getElementById("home-enable-shot-select");
    var deleteSelected = document.getElementById("home-delete-selected");
    var shotInputs = function () {
      return Array.prototype.slice.call(document.querySelectorAll(".shot-select-input"));
    };
    var rowInputs = function () {
      return Array.prototype.slice.call(document.querySelectorAll(".row-select"));
    };

    function syncShotDeleteButton() {
      if (!deleteSelected) return;
      var enabled = !!(enableShotSelect && enableShotSelect.checked);
      var any = shotInputs().some(function (el) { return el.checked; });
      deleteSelected.disabled = !(enabled && any);
    }

    function setShotSelectMode(on) {
      gallery.classList.toggle("shot-select-enabled", !!on);
      if (!on) {
        shotInputs().forEach(function (el) { el.checked = false; });
      }
      syncShotDeleteButton();
    }

    if (enableShotSelect) {
      enableShotSelect.addEventListener("change", function () {
        setShotSelectMode(enableShotSelect.checked);
      });
      setShotSelectMode(enableShotSelect.checked);
    }

    var dupRange = document.getElementById("home-dup-sensitivity");
    var dupValue = document.getElementById("home-dup-sensitivity-value");
    var markDupBtn = document.getElementById("home-mark-duplicates");
    if (dupRange && dupValue) {
      dupRange.addEventListener("input", function () {
        dupValue.textContent = String(dupRange.value);
      });
    }
    if (markDupBtn) {
      markDupBtn.addEventListener("click", function () {
        var inputs = shotInputs();
        var ids = inputs.map(function (el) { return el.value; }).filter(Boolean);
        if (!ids.length) return;
        var tokenInput = document.querySelector("#home-bulk-delete-form input[name='__RequestVerificationToken']");
        var maxCells = dupRange ? parseInt(dupRange.value, 10) : 2;
        if (!(maxCells >= 1 && maxCells <= 20)) maxCells = 2;
        markDupBtn.disabled = true;
        fetch("/Api/ScreenshotDuplicates", {
          method: "POST",
          credentials: "same-origin",
          headers: {
            "Accept": "application/json",
            "Content-Type": "application/json",
            "RequestVerificationToken": tokenInput ? tokenInput.value : ""
          },
          body: JSON.stringify({ ids: ids, maxDirtyCells: maxCells })
        }).then(function (r) {
          if (!r.ok) throw new Error("scan failed");
          return r.json();
        }).then(function (body) {
          var marked = {};
          (body.duplicateIds || body.DuplicateIds || []).forEach(function (id) {
            marked[String(id)] = true;
          });
          if (enableShotSelect) {
            enableShotSelect.checked = true;
            setShotSelectMode(true);
          }
          inputs.forEach(function (el) {
            el.checked = !!marked[el.value];
          });
          syncShotDeleteButton();
          var n = Object.keys(marked).length;
          var extra = [];
          var bf = body.backfilled || body.Backfilled || 0;
          var fail = body.fingerprintFailed || body.FingerprintFailed || 0;
          if (bf) extra.push(bf + " fingerprinted");
          if (fail) extra.push(fail + " failed");
          var flash = document.getElementById("home-flash");
          if (flash) {
            flash.innerHTML = "<div class=\"flash ok\" role=\"status\">Marked " + n + " duplicate(s)"
              + (extra.length ? " (" + extra.join(", ") + ")" : "")
              + ". Review, then Delete selected.</div>";
          }
        }).catch(function () {
          var flash = document.getElementById("home-flash");
          if (flash) {
            flash.innerHTML = "<div class=\"flash error\" role=\"alert\">Could not mark duplicates. Fingerprints may still be computing.</div>";
          }
        }).then(function () {
          markDupBtn.disabled = false;
        });
      });
    }

    document.addEventListener("change", function (e) {
      var t = e.target;
      if (!t) return;
      if (t.classList && t.classList.contains("shot-select-input")) syncShotDeleteButton();
      if (t.classList && t.classList.contains("row-select")) syncRowSelection();
      if (t.id === "home-select-all-rows") {
        var checked = !!t.checked;
        rowInputs().forEach(function (el) { el.checked = checked; });
        syncRowSelection();
      }
    });

    var editRowsBtn = document.getElementById("home-bulk-edit-rows");
    var deleteRowsBtn = document.getElementById("home-bulk-delete-rows");
    var rowCountEl = document.getElementById("home-row-selected-count");
    var selectAllRows = document.getElementById("home-select-all-rows");
    var bulkEditDlg = document.getElementById("home-bulk-edit-dialog");
    var bulkKeysHost = document.getElementById("home-bulk-edit-row-keys");

    // Row selection checkboxes are always visible.
    gallery.classList.add("row-select-enabled");

    function selectedRowCount() {
      return rowInputs().filter(function (el) { return el.checked; }).length;
    }

    function openBulkEditDialog() {
      if (!bulkEditDlg || typeof bulkEditDlg.showModal !== "function") return;
      if (bulkKeysHost) {
        bulkKeysHost.innerHTML = "";
        rowInputs().filter(function (el) { return el.checked; }).forEach(function (el) {
          var input = document.createElement("input");
          input.type = "hidden";
          input.name = "SelectedRowKeys";
          input.value = el.value;
          bulkKeysHost.appendChild(input);
        });
      }
      bulkEditDlg.showModal();
    }

    function syncRowSelection() {
      var rows = rowInputs();
      var selected = rows.filter(function (el) { return el.checked; });
      var n = selected.length;
      if (rowCountEl) {
        rowCountEl.textContent = n + " row(s) selected";
        rowCountEl.hidden = false;
      }
      if (editRowsBtn) {
        editRowsBtn.disabled = n === 0;
        editRowsBtn.title = n > 0 ? "Edit selected rows" : "Select rows to edit";
      }
      if (deleteRowsBtn) deleteRowsBtn.disabled = n === 0;
      if (selectAllRows && rows.length) {
        selectAllRows.checked = n === rows.length && n > 0;
        selectAllRows.indeterminate = n > 0 && n < rows.length;
      }
      rows.forEach(function (el) {
        var tr = el.closest("tr");
        if (tr) tr.classList.toggle("is-selected", el.checked);
      });
    }

    if (editRowsBtn) {
      editRowsBtn.addEventListener("click", function () {
        if (selectedRowCount() > 0) openBulkEditDialog();
      });
    }

    // Click anywhere on the row toggles selection — except images,
    // per-image checkboxes, and the row tags/notes edit control.
    gallery.addEventListener("click", function (e) {
      var t = e.target;
      if (!t || !t.closest) return;
      if (t.closest("a[data-shot-id], .row-edit-btn, [data-open-row-edit], .shot-select, .row-select, .row-lead, input, button, a, label")) {
        return;
      }
      var tr = t.closest("tr.timeline-data-row");
      if (!tr || !gallery.contains(tr)) return;
      var cb = tr.querySelector("input.row-select");
      if (!cb) return;
      cb.checked = !cb.checked;
      syncRowSelection();
    });

    syncRowSelection();

    // Row edit dialog (tags/notes only)
    var rowEditDlg = document.getElementById("home-row-edit-dialog");
    var rowKeyInput = document.getElementById("home-row-edit-key");
    var rowTags = document.getElementById("home-row-edit-tags");
    var rowNotes = document.getElementById("home-row-edit-notes");
    var rowSubtitle = document.getElementById("home-row-edit-subtitle");
    var rowIdDisplay = document.getElementById("home-row-edit-id");

    function setEntityIdDisplay(el, value) {
      if (!el) return;
      var code = el.querySelector("code");
      var text = value && String(value).trim() ? String(value).trim() : "—";
      if (code) code.textContent = text;
      else el.textContent = "Id: " + text;
    }

    document.querySelectorAll("[data-open-row-edit]").forEach(function (btn) {
      btn.addEventListener("click", function () {
        var rowId = btn.getAttribute("data-open-row-edit");
        var tr = rowId && document.getElementById(rowId);
        if (!tr || !rowEditDlg || typeof rowEditDlg.showModal !== "function") return;
        var key = tr.getAttribute("data-row-key") || "";
        if (rowKeyInput) rowKeyInput.value = key;
        if (rowTags) rowTags.value = tr.getAttribute("data-row-tags") || "";
        if (rowNotes) rowNotes.value = tr.getAttribute("data-row-notes") || "";
        if (rowSubtitle) {
          var time = tr.getAttribute("data-row-time") || "";
          var machine = tr.getAttribute("data-row-machine") || "—";
          var target = tr.getAttribute("data-row-target") || "—";
          rowSubtitle.textContent = time + " · " + machine + " · " + target;
        }
        setEntityIdDisplay(rowIdDisplay, key);
        rowEditDlg.showModal();
        if (rowTags) try { rowTags.focus(); } catch (_) { /* ignore */ }
      });
    });

    function wireBulkField(checkId, fieldId) {
      var check = document.getElementById(checkId);
      var field = document.getElementById(fieldId);
      if (!check || !field) return;
      function sync() { field.disabled = !check.checked; }
      check.addEventListener("change", sync);
      sync();
    }
    wireBulkField("home-bulk-set-journal", "home-bulk-edit-journal");
    wireBulkField("home-bulk-set-tags", "home-bulk-edit-tags");
    wireBulkField("home-bulk-set-notes", "home-bulk-edit-notes");

    // Column visibility (localStorage)
    (function initColumnPicker() {
      var table = document.getElementById("home-timeline");
      var picker = document.getElementById("home-col-picker");
      var btn = document.getElementById("home-col-picker-btn");
      var panel = document.getElementById("home-col-picker-panel");
      var resetBtn = document.getElementById("home-col-picker-reset");
      if (!table || !picker || !btn || !panel) return;

      var STORAGE_KEY = "schnapp.home.cols.v1";
      var ALWAYS_ON = { time: true };

      function loadPrefs() {
        try {
          var raw = localStorage.getItem(STORAGE_KEY);
          if (!raw) return {};
          var o = JSON.parse(raw);
          return o && typeof o === "object" ? o : {};
        } catch (_) {
          return {};
        }
      }
      function savePrefs(prefs) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs)); } catch (_) { /* ignore */ }
      }
      function knownKeys() {
        var keys = [];
        panel.querySelectorAll("[data-col-toggle]").forEach(function (cb) {
          var k = cb.getAttribute("data-col-toggle");
          if (k) keys.push(k);
        });
        return keys;
      }
      function isVisible(prefs, key) {
        if (ALWAYS_ON[key]) return true;
        if (Object.prototype.hasOwnProperty.call(prefs, key)) return !!prefs[key];
        return true; // default: shown
      }
      function apply(prefs) {
        knownKeys().forEach(function (key) {
          var on = isVisible(prefs, key);
          table.querySelectorAll('[data-col="' + key + '"]').forEach(function (cell) {
            cell.hidden = !on;
            cell.classList.toggle("col-is-hidden", !on);
          });
          var cb = panel.querySelector('[data-col-toggle="' + key + '"]');
          if (cb) {
            cb.checked = on;
            if (ALWAYS_ON[key]) cb.disabled = true;
          }
        });
        // Keep mirrored horizontal scroll width in sync
        if (typeof window.schnappSyncTimelineScroll === "function") {
          window.schnappSyncTimelineScroll();
        } else {
          window.dispatchEvent(new Event("resize"));
        }
      }
      function setOpen(open) {
        panel.hidden = !open;
        btn.setAttribute("aria-expanded", open ? "true" : "false");
        picker.classList.toggle("is-open", !!open);
      }

      var prefs = loadPrefs();
      apply(prefs);

      panel.addEventListener("change", function (e) {
        var t = e.target;
        if (!t || !t.getAttribute || !t.getAttribute("data-col-toggle")) return;
        var key = t.getAttribute("data-col-toggle");
        if (ALWAYS_ON[key]) {
          t.checked = true;
          return;
        }
        prefs[key] = !!t.checked;
        // Don't allow hiding every optional column if somehow all unchecked — still fine.
        savePrefs(prefs);
        apply(prefs);
      });

      if (resetBtn) {
        resetBtn.addEventListener("click", function () {
          prefs = {};
          knownKeys().forEach(function (k) { prefs[k] = true; });
          savePrefs(prefs);
          apply(prefs);
        });
      }

      btn.addEventListener("click", function (e) {
        e.stopPropagation();
        setOpen(panel.hidden);
      });
      document.addEventListener("click", function (e) {
        if (!picker.contains(e.target)) setOpen(false);
      });
      document.addEventListener("keydown", function (e) {
        if (e.key === "Escape" && !panel.hidden) setOpen(false);
      });
    })();

    // Thumbnail tier slider: one step per stored thumb size, so images display at natural size.
    (function initThumbScale() {
      var gallery = document.getElementById("home-gallery");
      var input = document.getElementById("home-thumb-scale-input");
      var valueEl = document.getElementById("home-thumb-scale-value");
      if (!gallery || !input) return;

      var tiers = (input.getAttribute("data-tiers") || "128").split(",").map(Number);
      var STORAGE_KEY = "schnapp.home.thumbTier.v2";
      var retierTimer = 0;
      function clamp(n) {
        n = Number(n);
        if (!isFinite(n)) return 1;
        return Math.min(tiers.length, Math.max(1, Math.round(n)));
      }
      function apply(step) {
        step = clamp(step);
        var size = tiers[step - 1];
        gallery.style.setProperty("--shot-tier", String(size));
        input.value = String(step);
        if (valueEl) valueEl.textContent = size + "px";
        window.schnappThumbTier = size;
        if (typeof window.schnappSyncTimelineScroll === "function") {
          // After layout
          requestAnimationFrame(function () {
            window.schnappSyncTimelineScroll();
          });
        }
      }
      function load() {
        try {
          var raw = localStorage.getItem(STORAGE_KEY);
          if (raw == null || raw === "") return 1;
          return clamp(raw);
        } catch (_) {
          return 1;
        }
      }
      function save(step) {
        try { localStorage.setItem(STORAGE_KEY, String(step)); } catch (_) { /* ignore */ }
      }

      apply(load());
      input.addEventListener("input", function () {
        var s = clamp(input.value);
        apply(s);
        save(s);
        // Debounce so dragging across steps does not fetch every tier.
        clearTimeout(retierTimer);
        retierTimer = setTimeout(function () {
          if (typeof window.schnappThumbRetier === "function") window.schnappThumbRetier(tiers[s - 1]);
        }, 150);
      });
    })();

    // Load thumbs in display order (DOM order = current sort). Keep several
    // requests in flight, but always prefer earlier rows so desc timestamp
    // loads newest first instead of browser-random request order.
    (function initThumbPriorityLoad() {
      var imgs = Array.prototype.slice.call(
        gallery.querySelectorAll("img.shot-thumb[data-src]")
      );
      if (!imgs.length) return;

      // Browsers cap HTTP/1.1 at ~6 per host; allow far more when multiplexed (h2/h3).
      var nav = window.performance && performance.getEntriesByType
        ? performance.getEntriesByType("navigation")[0] : null;
      var CONCURRENCY = nav && /^h[23]/.test(nav.nextHopProtocol || "") ? 24 : 8;
      var entries = imgs.map(function (img, index) {
        return {
          img: img,
          index: index,
          base: img.getAttribute("data-src"),
          size: 0,   // tier currently shown (0 = none yet)
          gen: 0,    // bumped on tier change so stale load events are ignored
          wanted: false,
          loading: false,
          done: false
        };
      });
      var tier = window.schnappThumbTier || 128;
      var byImg = typeof Map === "function" ? new Map() : null;
      if (byImg) {
        entries.forEach(function (e) { byImg.set(e.img, e); });
      }
      var inFlight = 0;
      // Wanted entries kept sorted by DOM index so pump() just takes the head.
      var queue = [];

      function enqueue(entry) {
        var lo = 0, hi = queue.length;
        while (lo < hi) {
          var mid = (lo + hi) >> 1;
          if (queue[mid].index < entry.index) lo = mid + 1; else hi = mid;
        }
        queue.splice(lo, 0, entry);
      }
      function finish(entry, gen, ok) {
        if (entry.gen !== gen || entry.done) return;
        entry.done = true;
        entry.loading = false;
        entry.size = ok ? entry.loadingSize : 0;
        inFlight--;
        pump();
      }

      function startLoad(entry) {
        if (!entry.base) {
          entry.done = true;
          return;
        }
        var gen = entry.gen;
        entry.loading = true;
        entry.loadingSize = tier;
        inFlight++;
        entry.img.addEventListener("load", function () { finish(entry, gen, true); }, { once: true });
        entry.img.addEventListener("error", function () { finish(entry, gen, false); }, { once: true });
        entry.img.src = entry.base + "?size=" + tier;
      }

      function pump() {
        while (inFlight < CONCURRENCY && queue.length) {
          startLoad(queue.shift());
        }
      }

      function markWanted(img) {
        var entry = byImg ? byImg.get(img) : null;
        if (!entry) {
          for (var i = 0; i < entries.length; i++) {
            if (entries[i].img === img) {
              entry = entries[i];
              break;
            }
          }
        }
        if (!entry || entry.wanted || entry.done) return;
        entry.wanted = true;
        enqueue(entry);
        pump();
      }

      var io = null;
      if (typeof IntersectionObserver === "function") {
        // Preload a generous band so scrolling stays smooth; still ordered by DOM.
        io = new IntersectionObserver(
          function (ioEntries) {
            for (var i = 0; i < ioEntries.length; i++) {
              if (!ioEntries[i].isIntersecting) continue;
              markWanted(ioEntries[i].target);
              io.unobserve(ioEntries[i].target);
            }
          },
          { root: null, rootMargin: "800px 0px", threshold: 0.01 }
        );
        imgs.forEach(function (img) { io.observe(img); });
      } else {
        entries.forEach(function (e) { e.wanted = true; enqueue(e); });
        pump();
      }

      // Slider moved: re-request every thumb at the new tier (visible/near ones first, in DOM order).
      window.schnappThumbRetier = function (size) {
        if (size === tier) return;
        tier = size;
        queue.length = 0;
        inFlight = 0;
        entries.forEach(function (e) {
          e.gen++;
          e.wanted = false;
          e.loading = false;
          e.done = e.size === size;
          if (e.done) return;
          if (io) {
            io.unobserve(e.img);
            io.observe(e.img);
          } else {
            e.wanted = true;
            enqueue(e);
          }
        });
        pump();
      };
    })();
  })();

  // Split date + time fields ↔ hidden local datetime (yyyy-MM-ddTHH:mm:ss)
  (function initDatetimeParts() {
    function parseLocal(value) {
      value = (value || "").trim();
      if (!value) return { date: "", time: "" };
      // Accept "yyyy-MM-ddTHH:mm[:ss]" or space-separated
      var m = value.match(/^(\d{4}-\d{2}-\d{2})[T\s](\d{2}:\d{2}(?::\d{2})?)/);
      if (!m) return { date: "", time: "" };
      var time = m[2];
      if (time.length === 5) time += ":00";
      return { date: m[1], time: time };
    }
    function joinLocal(date, time) {
      date = (date || "").trim();
      time = (time || "").trim();
      if (!date && !time) return "";
      if (!date) return "";
      if (!time) time = "00:00:00";
      if (time.length === 5) time += ":00";
      return date + "T" + time;
    }
    function syncFromHidden(wrap) {
      var hidden = wrap.querySelector('input[type="hidden"]');
      var dateEl = wrap.querySelector(".datetime-date");
      var timeEl = wrap.querySelector(".datetime-time");
      if (!hidden || !dateEl || !timeEl) return;
      var parts = parseLocal(hidden.value);
      dateEl.value = parts.date;
      timeEl.value = parts.time;
    }
    function syncToHidden(wrap) {
      var hidden = wrap.querySelector('input[type="hidden"]');
      var dateEl = wrap.querySelector(".datetime-date");
      var timeEl = wrap.querySelector(".datetime-time");
      if (!hidden || !dateEl || !timeEl) return;
      hidden.value = joinLocal(dateEl.value, timeEl.value);
      hidden.dispatchEvent(new Event("change", { bubbles: true }));
    }
    function wire(wrap) {
      var dateEl = wrap.querySelector(".datetime-date");
      var timeEl = wrap.querySelector(".datetime-time");
      var hidden = wrap.querySelector('input[type="hidden"]');
      if (!dateEl || !timeEl || !hidden) return;
      syncFromHidden(wrap);
      dateEl.addEventListener("change", function () { syncToHidden(wrap); });
      dateEl.addEventListener("input", function () { syncToHidden(wrap); });
      timeEl.addEventListener("change", function () { syncToHidden(wrap); });
      timeEl.addEventListener("input", function () { syncToHidden(wrap); });
      // When code sets hidden.value, refresh the visible parts
      var desc = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value");
      if (desc && desc.set && desc.get) {
        Object.defineProperty(hidden, "value", {
          configurable: true,
          enumerable: true,
          get: function () { return desc.get.call(this); },
          set: function (v) {
            desc.set.call(this, v);
            syncFromHidden(wrap);
          }
        });
      }
    }
    document.querySelectorAll("[data-datetime-parts]").forEach(wire);

    window.schnappDatetime = {
      set: function (hiddenId, value) {
        var h = document.getElementById(hiddenId);
        if (!h) return;
        h.value = value || "";
        var wrap = h.closest("[data-datetime-parts]");
        if (wrap) syncFromHidden(wrap);
      },
      get: function (hiddenId) {
        var h = document.getElementById(hiddenId);
        return h ? h.value : "";
      },
      nowLocal: function () {
        var now = new Date();
        var pad = function (n) { return n < 10 ? "0" + n : "" + n; };
        return now.getFullYear() + "-" + pad(now.getMonth() + 1) + "-" + pad(now.getDate()) +
          "T" + pad(now.getHours()) + ":" + pad(now.getMinutes()) + ":" + pad(now.getSeconds());
      }
    };

    // Quick actions: data-dt-set="<hidden id>" data-dt-action="now|clear"
    document.querySelectorAll("[data-dt-set][data-dt-action]").forEach(function (btn) {
      btn.addEventListener("click", function () {
        var id = btn.getAttribute("data-dt-set");
        var action = (btn.getAttribute("data-dt-action") || "").toLowerCase();
        if (!id) return;
        if (action === "now") {
          window.schnappDatetime.set(id, window.schnappDatetime.nowLocal());
        } else if (action === "clear") {
          window.schnappDatetime.set(id, "");
        }
        var dateEl = document.getElementById(id + "-date");
        if (dateEl) try { dateEl.focus(); } catch (_) { /* ignore */ }
      });
    });
  })();

  // Searchable combo boxes (filter-as-you-type for long option lists)
  (function initCombos() {
    function optionsOf(combo) {
      return Array.prototype.slice.call(combo.querySelectorAll(".combo-list [role=option]"));
    }
    function getCombo(el) {
      return el && el.closest ? el.closest("[data-combo]") : null;
    }
    function hiddenOf(combo) {
      return combo.querySelector('input[type="hidden"]');
    }
    function inputOf(combo) {
      return combo.querySelector(".combo-input");
    }
    function listOf(combo) {
      return combo.querySelector(".combo-list");
    }
    function selectedLi(combo) {
      var h = hiddenOf(combo);
      var val = h ? h.value : "";
      var opts = optionsOf(combo);
      for (var i = 0; i < opts.length; i++) {
        if ((opts[i].getAttribute("data-value") || "") === val) return opts[i];
      }
      return null;
    }
    function setCaption(combo) {
      var nameId = combo.getAttribute("data-combo-caption");
      var nameEl = nameId ? document.getElementById(nameId) : null;
      // Prefer sibling select-caption after combo
      if (!nameEl) {
        var next = combo.nextElementSibling;
        if (next && next.classList && next.classList.contains("select-caption")) nameEl = next;
      }
      if (!nameEl) return;
      var li = selectedLi(combo);
      nameEl.textContent = li ? (li.getAttribute("data-name") || "") : "";
    }
    function closeList(combo) {
      var list = listOf(combo);
      if (list) list.hidden = true;
      optionsOf(combo).forEach(function (li) { li.classList.remove("is-active"); });
    }
    function closeAll(except) {
      document.querySelectorAll("[data-combo]").forEach(function (c) {
        if (c !== except) closeList(c);
      });
    }
    function filterList(combo, query) {
      var q = (query || "").trim().toLowerCase();
      var parentId = "";
      var parentHidId = combo.getAttribute("data-combo-filter-parent");
      if (parentHidId) {
        var parentHid = document.getElementById(parentHidId);
        parentId = parentHid ? (parentHid.value || "") : "";
      }
      var any = false;
      optionsOf(combo).forEach(function (li) {
        var isNone = li.getAttribute("data-none") === "1";
        var p = li.getAttribute("data-parent") || "";
        var parentOk = isNone || !parentHidId || !parentId || !p || p === parentId;
        var label = (li.getAttribute("data-label") || li.textContent || "").toLowerCase();
        var name = (li.getAttribute("data-name") || "").toLowerCase();
        var code = (li.getAttribute("data-code") || "").toLowerCase();
        var textOk = !q || label.indexOf(q) >= 0 || name.indexOf(q) >= 0 || code.indexOf(q) >= 0;
        var show = parentOk && textOk;
        li.hidden = !show;
        if (show) any = true;
      });
      var list = listOf(combo);
      if (!list) return;
      var empty = list.querySelector(".combo-empty");
      if (!any) {
        if (!empty) {
          empty = document.createElement("li");
          empty.className = "combo-empty";
          empty.textContent = "No matches";
          list.appendChild(empty);
        }
        empty.hidden = false;
      } else if (empty) {
        empty.hidden = true;
      }
    }
    function openList(combo) {
      closeAll(combo);
      var list = listOf(combo);
      var input = inputOf(combo);
      if (!list) return;
      filterList(combo, input ? input.value : "");
      // If value is already set, show full list when opening via toggle
      list.hidden = false;
      var sel = selectedLi(combo);
      optionsOf(combo).forEach(function (li) {
        li.classList.toggle("is-selected", sel === li);
        li.classList.remove("is-active");
      });
      if (sel && !sel.hidden) {
        sel.classList.add("is-active");
        try { sel.scrollIntoView({ block: "nearest" }); } catch (_) { /* ignore */ }
      }
    }
    function setValue(combo, value, opts) {
      opts = opts || {};
      var h = hiddenOf(combo);
      var input = inputOf(combo);
      var prev = h ? h.value : "";
      var val = value == null ? "" : String(value);
      if (h) h.value = val;
      var li = null;
      optionsOf(combo).forEach(function (opt) {
        if ((opt.getAttribute("data-value") || "") === val) li = opt;
      });
      if (input) {
        if (opts.keepQuery) {
          /* leave typed filter text */
        } else {
          input.value = li ? (li.getAttribute("data-label") || li.textContent || "") : (opts.placeholder || "");
        }
      }
      optionsOf(combo).forEach(function (opt) {
        opt.classList.toggle("is-selected", opt === li);
      });
      setCaption(combo);
      if (prev !== val || opts.forceChange) {
        combo.dispatchEvent(new CustomEvent("combo:change", {
          bubbles: true,
          detail: { value: val, label: li ? (li.getAttribute("data-label") || "") : "" }
        }));
        // Cascade children
        var childId = combo.getAttribute("data-combo-parent-for");
        if (childId) {
          var childCombo = document.querySelector('[data-combo] input[type=hidden][id="' + childId + '"]');
          childCombo = childCombo && childCombo.closest("[data-combo]");
          if (childCombo) {
            // Reset child to first visible / none when parent changes (unless keepChild)
            if (!opts.keepChild) {
              var first = "";
              optionsOf(childCombo).forEach(function (cli) {
                if (first) return;
                var isNone = cli.getAttribute("data-none") === "1";
                var p = cli.getAttribute("data-parent") || "";
                if (isNone || !val || !p || p === val) first = cli.getAttribute("data-value") || "";
              });
              setValue(childCombo, first, { forceChange: true });
            } else {
              filterList(childCombo, "");
            }
          }
        }
      }
    }
    function pickLi(combo, li) {
      if (!li || li.classList.contains("combo-empty")) return;
      setValue(combo, li.getAttribute("data-value") || "");
      closeList(combo);
      var input = inputOf(combo);
      if (input) try { input.focus(); } catch (_) { /* ignore */ }
    }
    function activeLi(combo) {
      var list = listOf(combo);
      if (!list || list.hidden) return null;
      return list.querySelector("[role=option].is-active:not([hidden])");
    }
    function moveActive(combo, dir) {
      var opts = optionsOf(combo).filter(function (li) { return !li.hidden; });
      if (!opts.length) return;
      var cur = activeLi(combo);
      var idx = cur ? opts.indexOf(cur) : -1;
      idx = Math.max(0, Math.min(opts.length - 1, idx + dir));
      opts.forEach(function (li) { li.classList.remove("is-active"); });
      opts[idx].classList.add("is-active");
      try { opts[idx].scrollIntoView({ block: "nearest" }); } catch (_) { /* ignore */ }
    }

    // Public helpers on window for editors
    window.schnappCombo = {
      setValue: function (hiddenOrCombo, value, opts) {
        var combo = typeof hiddenOrCombo === "string"
          ? (document.getElementById(hiddenOrCombo) && document.getElementById(hiddenOrCombo).closest("[data-combo]"))
          : getCombo(hiddenOrCombo) || hiddenOrCombo;
        if (combo) setValue(combo, value, opts || {});
      },
      getValue: function (hiddenId) {
        var h = document.getElementById(hiddenId);
        return h ? h.value : "";
      },
      syncDisplay: function (hiddenId) {
        var h = document.getElementById(hiddenId);
        var combo = h && h.closest("[data-combo]");
        if (combo) setValue(combo, h.value, { forceChange: false });
      }
    };

    // While the user is interacting with a list (scrollbar, options), do not close on blur.
    var listInteractCombo = null;
    function markListInteract(combo) {
      listInteractCombo = combo;
    }
    function clearListInteract(combo) {
      if (listInteractCombo === combo) listInteractCombo = null;
    }

    document.querySelectorAll("[data-combo]").forEach(function (combo) {
      // Initial display from hidden value (do not cascade-reset children on load)
      var h = hiddenOf(combo);
      if (h) setValue(combo, h.value, { keepChild: true });

      var input = inputOf(combo);
      var toggle = combo.querySelector(".combo-toggle");
      var list = listOf(combo);
      if (input) {
        input.addEventListener("focus", function () {
          openList(combo);
          // Select all for quick retype search
          try { input.select(); } catch (_) { /* ignore */ }
        });
        input.addEventListener("input", function () {
          openList(combo);
          filterList(combo, input.value);
        });
        input.addEventListener("keydown", function (e) {
          if (e.key === "ArrowDown") {
            e.preventDefault();
            if (list && list.hidden) openList(combo);
            else moveActive(combo, 1);
          } else if (e.key === "ArrowUp") {
            e.preventDefault();
            if (list && list.hidden) openList(combo);
            else moveActive(combo, -1);
          } else if (e.key === "Enter") {
            var act = activeLi(combo);
            if (act) {
              e.preventDefault();
              pickLi(combo, act);
            }
          } else if (e.key === "Escape") {
            if (list && !list.hidden) {
              e.preventDefault();
              closeList(combo);
              // restore label for current value
              setValue(combo, h ? h.value : "");
            }
          }
        });
        input.addEventListener("blur", function () {
          // Delay: scrollbar / option pointerdown must win over blur-close.
          setTimeout(function () {
            if (listInteractCombo === combo) {
              // Keep list open; restore focus so further typing still works.
              try { input.focus(); } catch (_) { /* ignore */ }
              return;
            }
            if (combo.contains(document.activeElement)) return;
            closeList(combo);
            setValue(combo, h ? h.value : "");
          }, 0);
        });
      }
      if (toggle) {
        toggle.addEventListener("pointerdown", function (e) {
          e.preventDefault(); // keep focus on input
          markListInteract(combo);
          if (list && !list.hidden) closeList(combo);
          else {
            if (input) {
              var li = selectedLi(combo);
              input.value = "";
              openList(combo);
              filterList(combo, "");
              if (li) li.classList.add("is-active");
              try { input.focus(); } catch (_) { /* ignore */ }
            } else openList(combo);
          }
        });
      }
      if (list) {
        // pointerdown on list (options OR scrollbar): keep open; do not close via blur.
        // Only preventDefault for option picks — preventDefault on the scrollbar blocks scrolling.
        list.addEventListener("pointerdown", function (e) {
          markListInteract(combo);
          var li = e.target.closest && e.target.closest("[role=option]");
          if (li && list.contains(li) && !li.classList.contains("combo-empty")) {
            e.preventDefault();
            pickLi(combo, li);
            clearListInteract(combo);
          }
        });
        list.addEventListener("scroll", function () {
          markListInteract(combo);
        }, { passive: true });
        list.addEventListener("pointerup", function () {
          // Leave a short window so the delayed blur handler still sees the flag.
          setTimeout(function () { clearListInteract(combo); }, 50);
        });
        list.addEventListener("pointercancel", function () {
          clearListInteract(combo);
        });
      }
    });

    // Close open combos when pressing outside (not on blur alone — that kills scrollbar use).
    document.addEventListener("pointerdown", function (e) {
      var inside = e.target.closest && e.target.closest("[data-combo]");
      if (inside) return;
      document.querySelectorAll("[data-combo]").forEach(function (c) {
        var list = listOf(c);
        if (!list || list.hidden) return;
        closeList(c);
        var h = hiddenOf(c);
        setValue(c, h ? h.value : "");
        clearListInteract(c);
      });
    }, true);
  })();

  // Shared journal / journal-target editors (Home, Journals, viewer)
  (function initJournalEditors() {
    var editorsData = { journals: {}, targets: {} };
    try {
      var dataEl = document.getElementById("journal-editors-data");
      if (dataEl && dataEl.textContent) editorsData = JSON.parse(dataEl.textContent);
    } catch (_) { /* ignore */ }

    var journalDlg = document.getElementById("journal-editor-dialog");
    if (!journalDlg) return;

    var journalId = document.getElementById("journal-editor-id");
    var journalIsNew = document.getElementById("journal-editor-is-new");
    var journalLinkShot = document.getElementById("journal-editor-link-shot");
    var journalMachineWrap = document.getElementById("journal-editor-machine-wrap");
    var journalMachine = document.getElementById("journal-editor-machine");
    var journalTarget = document.getElementById("journal-editor-target");
    var journalTargetName = document.getElementById("journal-editor-target-name");
    var journalStart = document.getElementById("journal-editor-start");
    var journalStop = document.getElementById("journal-editor-stop");
    var journalTags = document.getElementById("journal-editor-tags");
    var journalNotes = document.getElementById("journal-editor-notes");
    var journalTitle = document.getElementById("journal-editor-title");
    var journalNewBtn = document.getElementById("journal-editor-new");
    var journalIdInput = document.getElementById("journal-editor-id-input");
    var journalIdDisplay = document.getElementById("journal-editor-id-display");

    function setJournalIdDisplay(value, isNew) {
      if (!journalIdDisplay) return;
      var code = journalIdDisplay.querySelector("code");
      var text;
      if (isNew) text = "(new)";
      else if (value && String(value).trim()) text = String(value).trim();
      else text = "—";
      if (code) code.textContent = text;
      else journalIdDisplay.textContent = "Id: " + text;
    }

    var targetIdInput = document.getElementById("journal-target-editor-id");
    var targetLinkJournal = document.getElementById("journal-target-editor-link-journal");
    var targetCustomer = document.getElementById("journal-target-editor-customer");
    var targetProject = document.getElementById("journal-target-editor-project");
    var targetTask = document.getElementById("journal-target-editor-task");
    var targetCustomerName = document.getElementById("journal-target-editor-customer-name");
    var targetProjectName = document.getElementById("journal-target-editor-project-name");
    var targetTaskName = document.getElementById("journal-target-editor-task-name");
    var targetCode = document.getElementById("journal-target-editor-code");
    var targetName = document.getElementById("journal-target-editor-name");
    var targetTags = document.getElementById("journal-target-editor-tags");
    var targetNotes = document.getElementById("journal-target-editor-notes");

    function setCombo(id, value, opts) {
      if (window.schnappCombo) window.schnappCombo.setValue(id, value, opts || {});
      else {
        var h = document.getElementById(id);
        if (h) h.value = value || "";
      }
    }

    function findOption(combo, value) {
      if (!combo) return null;
      var opts = combo.querySelectorAll(".combo-list [role=option]");
      for (var i = 0; i < opts.length; i++) {
        if ((opts[i].getAttribute("data-value") || "") === (value || "")) return opts[i];
      }
      return null;
    }

    function syncTargetCaption() {
      if (!journalTargetName) return;
      var tid = journalTarget ? journalTarget.value : "";
      var tMeta = editorsData.targets && editorsData.targets[tid];
      var combo = journalTarget && journalTarget.closest("[data-combo]");
      var li = findOption(combo, tid);
      journalTargetName.textContent =
        (tMeta && (tMeta.displayName || tMeta.name)) ||
        (li && li.getAttribute("data-name")) ||
        "";
    }

    function setNewMode(on, defaultMachineId) {
      if (journalIsNew) journalIsNew.value = on ? "true" : "false";
      if (journalMachineWrap) journalMachineWrap.hidden = !on;
      if (journalTitle) journalTitle.textContent = on ? "New journal" : "Journal";
      if (on) {
        setCombo("journal-editor-id", "");
        setJournalIdDisplay("", true);
        if (journalIdInput) {
          journalIdInput.value = "";
          journalIdInput.placeholder = "New journal (not saved yet)";
          journalIdInput.disabled = true;
        }
        var machineCombo = journalMachine && journalMachine.closest("[data-combo]");
        if (machineCombo) {
          var toggle = machineCombo.querySelector(".combo-toggle");
          var input = machineCombo.querySelector(".combo-input");
          if (toggle) toggle.disabled = false;
          if (input) input.disabled = false;
        }
        if (defaultMachineId) setCombo("journal-editor-machine", defaultMachineId);
        else if (journalMachine && !journalMachine.value) {
          // pick first machine option
          var mCombo = journalMachine.closest("[data-combo]");
          var first = mCombo && mCombo.querySelector(".combo-list [role=option]");
          if (first) setCombo("journal-editor-machine", first.getAttribute("data-value") || "");
        }
        if (journalStart && !journalStart.value) {
          var nowLocal = window.schnappDatetime && window.schnappDatetime.nowLocal
            ? window.schnappDatetime.nowLocal()
            : "";
          if (nowLocal) {
            if (window.schnappDatetime.set) window.schnappDatetime.set("journal-editor-start", nowLocal);
            else journalStart.value = nowLocal;
          }
        }
      } else {
        if (journalIdInput) {
          journalIdInput.disabled = false;
          journalIdInput.placeholder = "Search journals…";
        }
      }
    }

    function setDt(hiddenId, value) {
      if (window.schnappDatetime && window.schnappDatetime.set) {
        window.schnappDatetime.set(hiddenId, value || "");
        return;
      }
      var el = document.getElementById(hiddenId);
      if (el) el.value = value || "";
    }

    function fillJournalEditor(journalIdVal) {
      setNewMode(false);
      setCombo("journal-editor-id", journalIdVal || "");
      setJournalIdDisplay(journalIdVal || "", false);
      var j = journalIdVal && editorsData.journals ? editorsData.journals[journalIdVal] : null;
      if (!j) {
        setCombo("journal-editor-target", "");
        setDt("journal-editor-start", "");
        setDt("journal-editor-stop", "");
        if (journalTags) journalTags.value = "";
        if (journalNotes) journalNotes.value = "";
        syncTargetCaption();
        return;
      }
      setCombo("journal-editor-target", j.targetId || "", { keepChild: true });
      setDt("journal-editor-start", j.startLocal || "");
      setDt("journal-editor-stop", j.stopLocal || "");
      if (journalTags) journalTags.value = j.tags || "";
      if (journalNotes) journalNotes.value = j.notes || "";
      syncTargetCaption();
    }

    function openJournalEditor(opts) {
      opts = opts || {};
      if (typeof journalDlg.showModal !== "function") return;
      if (journalLinkShot) journalLinkShot.value = opts.linkShotId || "";
      if (opts.isNew) {
        fillJournalEditor("");
        setNewMode(true, opts.machineId || "");
        if (opts.targetId) setCombo("journal-editor-target", opts.targetId);
      } else {
        fillJournalEditor(opts.journalId || "");
      }
      journalDlg.showModal();
      var focusEl = opts.isNew
        ? (document.getElementById("journal-editor-machine-input") || journalStart)
        : journalIdInput;
      if (focusEl) try { focusEl.focus(); } catch (_) { /* ignore */ }
    }

    // Expose for shot viewer
    window.schnappOpenJournalEditor = openJournalEditor;

    var journalCombo = journalId && journalId.closest("[data-combo]");
    if (journalCombo) {
      journalCombo.addEventListener("combo:change", function () {
        if (journalIsNew && journalIsNew.value === "true") {
          setJournalIdDisplay("", true);
          return;
        }
        fillJournalEditor(journalId.value || "");
      });
    }

    if (journalNewBtn) {
      journalNewBtn.addEventListener("click", function () {
        var machineId = journalMachine ? journalMachine.value : "";
        // Prefer machine from linked shot when creating from viewer
        var shotMachine = document.getElementById("viewer-machine-id");
        if (shotMachine && shotMachine.value) machineId = shotMachine.value;
        setNewMode(true, machineId);
        setCombo("journal-editor-target", "");
        setDt("journal-editor-stop", "");
        if (journalTags) journalTags.value = "";
        if (journalNotes) journalNotes.value = "";
        syncTargetCaption();
        var mInput = document.getElementById("journal-editor-machine-input");
        if (mInput) try { mInput.focus(); } catch (_) { /* ignore */ }
      });
    }

    // Ensure split date/time parts flush into hidden fields before post
    var journalForm = document.getElementById("journal-editor-form");
    if (journalForm) {
      journalForm.addEventListener("submit", function () {
        journalForm.querySelectorAll("[data-datetime-parts]").forEach(function (wrap) {
          var hidden = wrap.querySelector('input[type="hidden"]');
          var dateEl = wrap.querySelector(".datetime-date");
          var timeEl = wrap.querySelector(".datetime-time");
          if (!hidden || !dateEl || !timeEl) return;
          var d = (dateEl.value || "").trim();
          var t = (timeEl.value || "").trim();
          if (!d) { hidden.value = ""; return; }
          if (!t) t = "00:00:00";
          if (t.length === 5) t += ":00";
          hidden.value = d + "T" + t;
        });
      });
    }

    document.querySelectorAll("[data-open-journal-editor]").forEach(function (btn) {
      btn.addEventListener("click", function () {
        if (btn.disabled) return;
        openJournalEditor({ journalId: btn.getAttribute("data-open-journal-editor") || "" });
      });
    });

    document.querySelectorAll("[data-open-journal-editor-from]").forEach(function (btn) {
      btn.addEventListener("click", function () {
        var selId = btn.getAttribute("data-open-journal-editor-from");
        var sel = selId && document.getElementById(selId);
        var id = sel && sel.value ? sel.value : "";
        openJournalEditor({ journalId: id });
      });
    });

    function syncTargetFieldCaptions() {
      function cap(hidden, el) {
        if (!el || !hidden) return;
        var combo = hidden.closest("[data-combo]");
        var li = findOption(combo, hidden.value || "");
        el.textContent = li ? (li.getAttribute("data-name") || "") : "";
      }
      cap(targetCustomer, targetCustomerName);
      cap(targetProject, targetProjectName);
      cap(targetTask, targetTaskName);
    }

    function fillTargetEditor(targetId, linkJournalId) {
      if (targetLinkJournal) targetLinkJournal.value = linkJournalId || "";
      if (targetIdInput) targetIdInput.value = targetId || "";
      var t = targetId && editorsData.targets ? editorsData.targets[targetId] : null;
      if (!t) {
        var firstCustomer = "";
        var cCombo = targetCustomer && targetCustomer.closest("[data-combo]");
        var firstLi = cCombo && cCombo.querySelector(".combo-list [role=option]");
        if (firstLi) firstCustomer = firstLi.getAttribute("data-value") || "";
        setCombo("journal-target-editor-customer", firstCustomer, { forceChange: true });
        if (targetCode) targetCode.value = "";
        if (targetName) targetName.value = "";
        if (targetTags) targetTags.value = "";
        if (targetNotes) targetNotes.value = "";
        syncTargetFieldCaptions();
        return;
      }
      setCombo("journal-target-editor-customer", t.customerId || "", { keepChild: true });
      setCombo("journal-target-editor-project", t.projectId || "", { keepChild: true });
      setCombo("journal-target-editor-task", t.taskId || "");
      if (targetCode) targetCode.value = t.code || "";
      if (targetName) targetName.value = t.name || "";
      if (targetTags) targetTags.value = t.tags || "";
      if (targetNotes) targetNotes.value = t.notes || "";
      syncTargetFieldCaptions();
    }

    var targetCombo = journalTarget && journalTarget.closest("[data-combo]");
    if (targetCombo) {
      targetCombo.addEventListener("combo:change", function () {
        syncTargetCaption();
      });
    }

    ["journal-target-editor-customer", "journal-target-editor-project", "journal-target-editor-task"].forEach(function (id) {
      var h = document.getElementById(id);
      var combo = h && h.closest("[data-combo]");
      if (combo) combo.addEventListener("combo:change", syncTargetFieldCaptions);
    });

    var editTargetBtn = document.getElementById("journal-editor-edit-target");
    if (editTargetBtn) {
      editTargetBtn.addEventListener("click", function () {
        var tid = journalTarget ? journalTarget.value : "";
        var jid = journalId ? journalId.value : "";
        fillTargetEditor(tid, jid);
      });
    }

    syncTargetCaption();
    syncTargetFieldCaptions();
  })();

  var dialog = document.getElementById("shot-viewer");
  if (!dialog || typeof dialog.showModal !== "function") return;

  var img = document.getElementById("viewer-img");
  var panEl = document.getElementById("viewer-pan");
  var meta = document.getElementById("viewer-meta");
  var notes = document.getElementById("viewer-notes");
  var tags = document.getElementById("viewer-tags");
  var journal = document.getElementById("viewer-journal");
  var journalLabel = document.getElementById("viewer-journal-label");
  var coveringHint = document.getElementById("viewer-covering-hint");
  var journalMachineId = document.getElementById("viewer-machine-id");
  var idInput = document.getElementById("viewer-id");
  var shotIdEl = document.getElementById("viewer-shot-id");
  var dateInput = document.getElementById("viewer-date");
  var viewerEditJournal = document.getElementById("viewer-edit-journal");
  var openTab = document.getElementById("viewer-open-tab");
  var fields = {
    when: document.getElementById("viewer-when"),
    machine: document.getElementById("viewer-machine"),
    monitor: document.getElementById("viewer-monitor"),
    window: document.getElementById("viewer-window"),
    program: document.getElementById("viewer-program"),
    size: document.getElementById("viewer-size"),
    type: document.getElementById("viewer-type"),
    tz: document.getElementById("viewer-tz"),
    received: document.getElementById("viewer-received")
  };

  var scale = 1;
  var tx = 0;
  var ty = 0;
  var shots = [];
  var index = -1;
  var dragging = false;
  var lastX = 0;
  var lastY = 0;
  var fitW = 1;
  var fitH = 1;
  // After closing via button, the same click can "fall through" onto a thumb and reopen.
  var ignoreShotClicksUntil = 0;

  function attr(el, name) {
    return el.getAttribute(name) || "";
  }

  function collectShots() {
    // Prefer home timeline anchors (stable batch order); fall back to any metadata link.
    var timelineAnchors = document.querySelectorAll("#home-timeline a[data-shot-id]");
    var list = timelineAnchors.length
      ? Array.prototype.slice.call(timelineAnchors)
      : Array.prototype.slice.call(document.querySelectorAll("a[data-shot-id]"));
    shots = list.map(function (el) {
      var row = el.closest("tr.timeline-data-row");
      var cell = el.closest("td[data-col]");
      return {
        id: attr(el, "data-shot-id"),
        date: attr(el, "data-shot-date"),
        notes: attr(el, "data-shot-notes"),
        tags: attr(el, "data-shot-tags"),
        journal: attr(el, "data-shot-journal"),
        journalLabel: attr(el, "data-shot-journal-label"),
        coveringJournal: attr(el, "data-shot-covering-journal"),
        coveringLabel: attr(el, "data-shot-covering-label"),
        meta: attr(el, "data-shot-meta"),
        when: attr(el, "data-shot-when"),
        machine: attr(el, "data-shot-machine"),
        machineId: attr(el, "data-shot-machine-id"),
        monitor: attr(el, "data-shot-monitor"),
        window: attr(el, "data-shot-window"),
        program: attr(el, "data-shot-program"),
        size: attr(el, "data-shot-size"),
        type: attr(el, "data-shot-type"),
        tz: attr(el, "data-shot-tz"),
        received: attr(el, "data-shot-received"),
        // Capture batch = timeline row (same second); col = monitor/window column.
        batch: row ? (attr(row, "data-row-key") || row.id || "") : attr(el, "data-shot-when"),
        col: cell ? attr(cell, "data-col") : (attr(el, "data-shot-monitor") || "")
      };
    });
  }

  /** Ordered unique batch keys as they appear in the gallery. */
  function batchOrder() {
    var keys = [];
    var seen = Object.create(null);
    for (var i = 0; i < shots.length; i++) {
      var b = shots[i].batch || ("#" + i);
      if (!seen[b]) {
        seen[b] = true;
        keys.push(b);
      }
    }
    return keys;
  }

  /**
   * Jump ±delta capture batches. Prefers the same column/monitor as the current shot.
   */
  function showBatchDelta(delta) {
    if (!shots.length || index < 0) return;
    var keys = batchOrder();
    if (!keys.length) return;
    var cur = shots[index];
    var curBatch = cur.batch || "";
    var bi = keys.indexOf(curBatch);
    if (bi < 0) bi = 0;
    var n = keys.length;
    var targetBi = ((bi + delta) % n + n) % n;
    var targetBatch = keys[targetBi];
    var preferCol = cur.col || "";
    var preferMonitor = cur.monitor || "";
    var firstInBatch = -1;
    var matchCol = -1;
    var matchMon = -1;
    for (var i = 0; i < shots.length; i++) {
      if ((shots[i].batch || "") !== targetBatch) continue;
      if (firstInBatch < 0) firstInBatch = i;
      if (preferCol && shots[i].col === preferCol && matchCol < 0) matchCol = i;
      if (preferMonitor && shots[i].monitor === preferMonitor && matchMon < 0) matchMon = i;
    }
    var dest = matchCol >= 0 ? matchCol : (matchMon >= 0 ? matchMon : firstInBatch);
    if (dest >= 0) showAt(dest);
  }

  function setText(el, value) {
    if (el) el.textContent = value && String(value).trim() ? value : "—";
  }

  function applyTransform() {
    if (!img) return;
    img.style.transform =
      "translate(-50%, -50%) translate(" + tx + "px, " + ty + "px) scale(" + scale + ")";
  }

  function fitImage() {
    if (!img || !panEl || !img.naturalWidth) return;
    var box = panEl.getBoundingClientRect();
    var pad = 16;
    var aw = Math.max(40, box.width - pad);
    var ah = Math.max(40, box.height - pad);
    fitW = aw / img.naturalWidth;
    fitH = ah / img.naturalHeight;
    scale = Math.min(fitW, fitH, 1);
    tx = 0;
    ty = 0;
    applyTransform();
  }

  function centerViewer() {
    // Undo any session geometry that pinned the dialog to the left edge.
    dialog.style.margin = "auto";
    dialog.style.position = "";
    dialog.style.top = "";
    dialog.style.left = "";
    dialog.style.inset = "";
    dialog.style.transform = "";
  }

  function showAt(i) {
    if (!shots.length) return;
    index = (i + shots.length) % shots.length;
    var s = shots[index];
    centerViewer();
    if (img) {
      img.onload = function () {
        fitImage();
      };
      img.src = "/Screenshots/Image/" + s.id;
      img.alt = s.meta || "screenshot";
      if (img.complete && img.naturalWidth) fitImage();
    }
    if (meta) meta.textContent = s.meta || "Screenshot";
    if (notes) notes.value = s.notes;
    if (tags) tags.value = s.tags;
    if (journal) journal.value = s.journal || "";
    if (journalLabel) {
      var linkedText = s.journalLabel && String(s.journalLabel).trim()
        ? s.journalLabel
        : (s.journal ? s.journal : "None");
      journalLabel.value = linkedText;
    }
    if (coveringHint) {
      var covLabel = (s.coveringLabel || "").trim();
      var linkedId = (s.journal || "").trim();
      var covId = (s.coveringJournal || "").trim();
      // Show time-covering context when it differs from the explicit link (or link is none).
      if (covLabel && covLabel !== "None" && covId !== linkedId) {
        coveringHint.hidden = false;
        coveringHint.textContent = linkedId
          ? ("Covering at capture: " + covLabel)
          : ("No link on image · covering at capture: " + covLabel);
      } else if (!linkedId && (!covLabel || covLabel === "None")) {
        coveringHint.hidden = false;
        coveringHint.textContent = "No linked journal · none covering at capture";
      } else {
        coveringHint.hidden = true;
        coveringHint.textContent = "";
      }
    }
    if (journalMachineId) journalMachineId.value = s.machineId || "";
    if (idInput) idInput.value = s.id;
    if (openTab) {
      if (s.id) {
        openTab.href = "/Screenshots/Image/" + encodeURIComponent(s.id);
        openTab.removeAttribute("aria-disabled");
        openTab.classList.remove("is-disabled");
      } else {
        openTab.href = "#";
        openTab.setAttribute("aria-disabled", "true");
        openTab.classList.add("is-disabled");
      }
    }
    if (shotIdEl) {
      var code = shotIdEl.querySelector("code");
      if (code) code.textContent = s.id || "—";
      else shotIdEl.textContent = "Id: " + (s.id || "—");
    }
    if (dateInput) dateInput.value = s.date;
    clearDeleteError();
    prefetchShot(index + 1);
    prefetchShot(index - 1);
    setText(fields.when, s.when);
    setText(fields.machine, s.machine);
    setText(fields.monitor, s.monitor);
    setText(fields.window, s.window);
    setText(fields.program, s.program);
    setText(fields.size, s.size);
    setText(fields.type, s.type);
    setText(fields.tz, s.tz);
    setText(fields.received, s.received);
    if (!dialog.open) dialog.showModal();
  }

  if (viewerEditJournal) {
    viewerEditJournal.addEventListener("click", function () {
      var shotId = idInput ? idInput.value : "";
      var jid = journal ? journal.value : "";
      var mid = journalMachineId ? journalMachineId.value : "";
      if (typeof window.schnappOpenJournalEditor === "function") {
        window.schnappOpenJournalEditor({
          journalId: jid,
          linkShotId: shotId,
          machineId: mid,
          isNew: false
        });
      }
    });
  }

  document.addEventListener("click", function (e) {
    if (Date.now() < ignoreShotClicksUntil) return;
    // Don't treat clicks inside the open dialog as gallery opens.
    if (dialog.open && dialog.contains(e.target)) return;
    // Let the native checkbox handle its own label/input clicks.
    if (e.target.closest && e.target.closest(".shot-select")) return;
    var t = e.target.closest("a[data-shot-id], [data-shot-id]");
    if (!t) return;
    // Prefer the anchor that carries metadata
    if (!t.getAttribute("data-shot-when")) {
      var anchor = t.querySelector && t.querySelector("a[data-shot-id]");
      if (anchor) t = anchor;
      else if (!t.getAttribute("data-shot-id")) return;
    }
    e.preventDefault();

    // Select-images mode: click toggles checkbox; Shift+click opens viewer.
    var gallery = document.getElementById("home-gallery");
    var selectMode = gallery && gallery.classList.contains("shot-select-enabled");
    if (selectMode && !e.shiftKey) {
      var item = t.closest(".shot-item") || t;
      var cb = item.querySelector && item.querySelector(".shot-select-input");
      if (cb) {
        cb.checked = !cb.checked;
        cb.dispatchEvent(new Event("change", { bubbles: true }));
      }
      return;
    }

    collectShots();
    var id = t.getAttribute("data-shot-id");
    var i = shots.findIndex(function (s) { return s.id === id; });
    showAt(i >= 0 ? i : 0);
  });

  function closeViewer(e) {
    if (e) {
      e.preventDefault();
      e.stopPropagation();
    }
    try {
      if (typeof dialog.close === "function") dialog.close();
    } catch (_) { /* ignore */ }
    // Belt-and-braces if CSS ever breaks closed display again
    dialog.removeAttribute("open");
    ignoreShotClicksUntil = Date.now() + 400;
  }

  function clearDeleteError() {
    var el = document.getElementById("viewer-delete-error");
    if (!el) return;
    el.hidden = true;
    el.textContent = "";
  }

  function showDeleteError(message) {
    var el = document.getElementById("viewer-delete-error");
    if (!el) return;
    el.hidden = false;
    el.textContent = message;
  }

  function prefetchShot(i) {
    if (i < 0 || i >= shots.length || !shots[i] || !shots[i].id) return;
    var pre = new Image();
    pre.decoding = "async";
    pre.src = "/Screenshots/Image/" + encodeURIComponent(shots[i].id);
  }

  function cssEscape(value) {
    if (window.CSS && typeof CSS.escape === "function") return CSS.escape(value);
    return String(value).replace(/"/g, "\\'");
  }

  // Pull the thumbnail out of the timeline. Returns enough to put it back if the delete fails.
  function detachShot(id) {
    var link = document.querySelector('#home-timeline a[data-shot-id="' + cssEscape(id) + '"]');
    if (!link) return null;
    var item = link.closest(".shot-item") || link;
    var parent = item.parentNode;
    var next = item.nextSibling;
    var cell = link.closest("td.shot-cell");
    item.remove();
    var placeholder = null;
    if (cell && !cell.querySelector(".shot-item, a[data-shot-id]")) {
      placeholder = document.createElement("span");
      placeholder.className = "muted";
      placeholder.textContent = "—";
      cell.appendChild(placeholder);
    }
    var row = cell ? cell.closest("tr.timeline-data-row") : null;
    var rowParent = null;
    var rowNext = null;
    if (row && !row.querySelector("a[data-shot-id]")) {
      rowParent = row.parentNode;
      rowNext = row.nextSibling;
      row.remove();
      var countEl = document.getElementById("home-batch-count");
      if (countEl) {
        var n = document.querySelectorAll("#home-timeline tr.timeline-data-row").length;
        countEl.textContent = n + " batch(es)";
      }
    }
    return { item: item, parent: parent, next: next, placeholder: placeholder, row: row, rowParent: rowParent, rowNext: rowNext };
  }

  function restoreShot(detached) {
    if (!detached) return;
    if (detached.row && detached.rowParent) {
      detached.rowParent.insertBefore(detached.row, detached.rowNext);
      var countEl = document.getElementById("home-batch-count");
      if (countEl) {
        var n = document.querySelectorAll("#home-timeline tr.timeline-data-row").length;
        countEl.textContent = n + " batch(es)";
      }
    }
    if (detached.placeholder && detached.placeholder.parentNode) detached.placeholder.remove();
    if (!detached.parent) return;
    if (detached.next && detached.next.parentNode === detached.parent) {
      detached.parent.insertBefore(detached.item, detached.next);
    } else {
      detached.parent.appendChild(detached.item);
    }
  }

  // Advance in the gallery the page already rendered, then soft-delete without reloading the day.
  function softDeleteCurrent(e) {
    var deleteBtn = document.getElementById("viewer-delete");
    var url = deleteBtn ? deleteBtn.getAttribute("data-soft-delete-url") : "";
    if (!url) return;
    if (e) {
      e.preventDefault();
      e.stopPropagation();
    }
    if (!shots.length || index < 0) collectShots();
    if (idInput && idInput.value) {
      var current = shots.findIndex(function (s) { return s.id === idInput.value; });
      if (current >= 0) index = current;
    }
    if (index < 0 || index >= shots.length) return;
    var shot = shots[index];
    var nextId = (index + 1 < shots.length) ? shots[index + 1].id : null;
    var detached = detachShot(shot.id);
    shots.splice(index, 1);
    clearDeleteError();
    if (!shots.length) closeViewer();
    else showAt(index < shots.length ? index : shots.length - 1);

    var form = document.getElementById("viewer-edit-form");
    var tokenInput = form ? form.querySelector("input[name='__RequestVerificationToken']") : null;
    var token = tokenInput ? tokenInput.value : "";
    var body = new URLSearchParams();
    body.set("ViewerId", shot.id);
    if (token) body.set("__RequestVerificationToken", token);
    fetch(url, {
      method: "POST",
      credentials: "same-origin",
      headers: {
        "Content-Type": "application/x-www-form-urlencoded",
        "Accept": "application/json",
        "RequestVerificationToken": token
      },
      body: body.toString()
    }).then(function (r) {
      return r.json().catch(function () { return { error: "Could not delete this screenshot." }; }).then(function (payload) {
        if (!r.ok || (payload && payload.error)) {
          throw new Error((payload && payload.error) || "Could not delete this screenshot.");
        }
      });
    }).catch(function (err) {
      undoSoftDelete(shot, nextId, detached, (err && err.message) || "Could not delete this screenshot. It is still in the gallery.");
    });
  }

  function undoSoftDelete(shot, nextId, detached, message) {
    var dest = shots.length;
    if (nextId) {
      var at = shots.findIndex(function (s) { return s.id === nextId; });
      if (at >= 0) dest = at;
    }
    if (shots.some(function (s) { return s.id === shot.id; })) return;
    shots.splice(dest, 0, shot);
    restoreShot(detached);
    var back = shots.findIndex(function (s) { return s.id === shot.id; });
    if (back >= 0) showAt(back);
    showDeleteError(message);
  }

  var viewerDeleteBtn = document.getElementById("viewer-delete");
  if (viewerDeleteBtn) {
    viewerDeleteBtn.addEventListener("click", softDeleteCurrent, true);
  }

  // Close buttons: native formmethod=dialog + JS fallback
  document.querySelectorAll("[data-viewer-close], #viewer-close, #viewer-close-side").forEach(function (el) {
    el.addEventListener("click", function (e) {
      // formmethod=dialog handles submit-close; still force-close for type=button
      closeViewer(e);
    }, true);
  });

  // Re-open after Save (not Save and close)
  var reopen = (document.body.getAttribute("data-open-viewer") || "").trim();
  if (reopen) {
    collectShots();
    var ri = shots.findIndex(function (s) { return s.id === reopen; });
    if (ri >= 0) showAt(ri);
  }

  var prevBtn = document.getElementById("viewer-prev");
  var nextBtn = document.getElementById("viewer-next");
  if (prevBtn) prevBtn.addEventListener("click", function () { showAt(index - 1); });
  if (nextBtn) nextBtn.addEventListener("click", function () { showAt(index + 1); });
  var batchPrevBtn = document.getElementById("viewer-batch-prev");
  var batchNextBtn = document.getElementById("viewer-batch-next");
  var batchPrev10Btn = document.getElementById("viewer-batch-prev-10");
  var batchNext10Btn = document.getElementById("viewer-batch-next-10");
  if (batchPrevBtn) batchPrevBtn.addEventListener("click", function () { showBatchDelta(-1); });
  if (batchNextBtn) batchNextBtn.addEventListener("click", function () { showBatchDelta(1); });
  if (batchPrev10Btn) batchPrev10Btn.addEventListener("click", function () { showBatchDelta(-10); });
  if (batchNext10Btn) batchNext10Btn.addEventListener("click", function () { showBatchDelta(10); });

  function zoomBy(delta) {
    var next = Math.min(Math.max(scale + delta, 0.25), 6);
    scale = next;
    applyTransform();
  }

  var zoomIn = document.getElementById("viewer-zoom-in");
  var zoomOut = document.getElementById("viewer-zoom-out");
  var zoomReset = document.getElementById("viewer-zoom-reset");
  if (zoomIn) zoomIn.addEventListener("click", function () { zoomBy(0.25); });
  if (zoomOut) zoomOut.addEventListener("click", function () { zoomBy(-0.25); });
  if (zoomReset) zoomReset.addEventListener("click", function () { fitImage(); });

  if (panEl) {
    panEl.addEventListener("pointerdown", function (e) {
      if (e.button !== 0) return;
      dragging = true;
      lastX = e.clientX;
      lastY = e.clientY;
      panEl.classList.add("is-panning");
      panEl.setPointerCapture(e.pointerId);
    });
    panEl.addEventListener("pointermove", function (e) {
      if (!dragging) return;
      tx += e.clientX - lastX;
      ty += e.clientY - lastY;
      lastX = e.clientX;
      lastY = e.clientY;
      applyTransform();
    });
    function endPan(e) {
      if (!dragging) return;
      dragging = false;
      panEl.classList.remove("is-panning");
      try { panEl.releasePointerCapture(e.pointerId); } catch (_) { /* ignore */ }
    }
    panEl.addEventListener("pointerup", endPan);
    panEl.addEventListener("pointercancel", endPan);
    panEl.addEventListener("wheel", function (e) {
      e.preventDefault();
      zoomBy(e.deltaY < 0 ? 0.15 : -0.15);
    }, { passive: false });
  }

  if (typeof ResizeObserver !== "undefined" && dialog) {
    var ro = new ResizeObserver(function () {
      if (dialog.open) fitImage();
    });
    ro.observe(dialog);
  }

  // Backdrop dismiss for viewer: ignore resize/drag (same issue as form dialogs).
  var viewerBackdropGesture = null;
  dialog.addEventListener("pointerdown", function (e) {
    if (e.target !== dialog || e.button !== 0) {
      viewerBackdropGesture = null;
      return;
    }
    var r = dialog.getBoundingClientRect();
    viewerBackdropGesture = {
      x: e.clientX,
      y: e.clientY,
      w: r.width,
      h: r.height,
      pointerId: e.pointerId
    };
  });
  dialog.addEventListener("pointerup", function (e) {
    if (!viewerBackdropGesture || e.pointerId !== viewerBackdropGesture.pointerId) return;
    var g = viewerBackdropGesture;
    viewerBackdropGesture = null;
    if (e.target !== dialog) return;
    var r = dialog.getBoundingClientRect();
    var moved = Math.hypot(e.clientX - g.x, e.clientY - g.y) > 5;
    var resized = Math.abs(r.width - g.w) > 1 || Math.abs(r.height - g.h) > 1;
    if (moved || resized) return;
    closeViewer(e);
  });
  dialog.addEventListener("pointercancel", function () { viewerBackdropGesture = null; });
  dialog.addEventListener("cancel", function () {
    // Esc key — still block click-through after dismiss
    ignoreShotClicksUntil = Date.now() + 400;
  });
  dialog.addEventListener("close", function () {
    ignoreShotClicksUntil = Math.max(ignoreShotClicksUntil, Date.now() + 400);
  });

  document.addEventListener("keydown", function (e) {
    if (!dialog.open) return;
    if (e.target && (e.target.tagName === "INPUT" || e.target.tagName === "TEXTAREA" || e.target.tagName === "SELECT")) {
      return;
    }
    if (e.key === "ArrowLeft") { e.preventDefault(); showAt(index - 1); }
    if (e.key === "ArrowRight") { e.preventDefault(); showAt(index + 1); }
    if (e.key === "ArrowUp") {
      e.preventDefault();
      showBatchDelta(e.ctrlKey || e.metaKey ? -10 : -1);
    }
    if (e.key === "ArrowDown") {
      e.preventDefault();
      showBatchDelta(e.ctrlKey || e.metaKey ? 10 : 1);
    }
    if (e.key === "PageUp") { e.preventDefault(); showBatchDelta(-10); }
    if (e.key === "PageDown") { e.preventDefault(); showBatchDelta(10); }
    if (e.key === "+" || e.key === "=") { e.preventDefault(); zoomBy(0.25); }
    if (e.key === "-") { e.preventDefault(); zoomBy(-0.25); }
    if (e.key === "0") { e.preventDefault(); fitImage(); }
  });
})();

// Pending deletes: enable bulk restore / permanent delete when a row is checked.
(function () {
  var form = document.getElementById("pending-bulk-form");
  if (!form) return;
  var restore = document.getElementById("pending-restore-selected");
  var purge = document.getElementById("pending-purge-selected");
  function inputs() {
    return Array.prototype.slice.call(document.querySelectorAll(".pending-select"));
  }
  function sync() {
    var any = inputs().some(function (el) { return el.checked; });
    if (restore) restore.disabled = !any;
    if (purge) purge.disabled = !any;
  }
  document.addEventListener("change", function (e) {
    var t = e.target;
    if (!t) return;
    if (t.classList && t.classList.contains("pending-select")) sync();
    if (t.id === "pending-select-all") {
      var on = !!t.checked;
      inputs().forEach(function (el) { el.checked = on; });
      sync();
    }
  });
  sync();
})();

// Mirror a top horizontal scrollbar for wide tables (only when needed).
// opts: { top, spacer, wrap, table, onBeforeSync?: function }
window.schnappInitTopScroll = function (opts) {
  var top = opts && opts.top;
  var spacer = opts && opts.spacer;
  var wrap = opts && opts.wrap;
  var table = opts && opts.table;
  if (!top || !spacer || !wrap || !table) return null;

  var syncing = false;
  function syncWidth() {
    if (typeof opts.onBeforeSync === "function") opts.onBeforeSync();
    var tableW = table.scrollWidth;
    var wrapW = wrap.clientWidth;
    spacer.style.width = tableW + "px";
    // Show the extra top bar only when the grid actually overflows horizontally.
    var needed = tableW > wrapW + 1;
    top.classList.toggle("is-needed", needed);
    if (!needed) {
      top.scrollLeft = 0;
      if (wrap.scrollLeft) wrap.scrollLeft = 0;
    } else {
      // Keep bars aligned after size changes
      top.scrollLeft = wrap.scrollLeft;
    }
  }

  top.addEventListener("scroll", function () {
    if (syncing) return;
    syncing = true;
    wrap.scrollLeft = top.scrollLeft;
    syncing = false;
  });
  wrap.addEventListener("scroll", function () {
    if (syncing) return;
    syncing = true;
    top.scrollLeft = wrap.scrollLeft;
    syncing = false;
  });
  syncWidth();
  window.addEventListener("resize", syncWidth);
  if (window.ResizeObserver) {
    new ResizeObserver(syncWidth).observe(table);
    new ResizeObserver(syncWidth).observe(wrap);
  }
  return syncWidth;
};

// Home timeline: top scrollbar + colspan for group/gap rows when columns hide/show.
(function () {
  var top = document.getElementById("home-timeline-scroll-top");
  var spacer = document.getElementById("home-timeline-scroll-top-spacer");
  var wrap = document.getElementById("home-timeline-wrap");
  var table = document.getElementById("home-timeline");
  if (!top || !spacer || !wrap || !table) return;

  /** Match group/gap row colspan to currently *visible* header columns (hidden cols must not inflate width). */
  function updateSpanRows() {
    var head = table.querySelector("thead tr");
    if (!head) return;
    var n = 0;
    head.querySelectorAll("th").forEach(function (th) {
      if (th.hidden || th.classList.contains("col-is-hidden")) return;
      // display:none via CSS class
      if (window.getComputedStyle(th).display === "none") return;
      n++;
    });
    if (n < 1) n = 1;
    table.querySelectorAll("tr.journal-group-row > td, tr.gap-row > td").forEach(function (td) {
      td.colSpan = n;
    });
  }

  // Expose for column picker / thumb scale
  window.schnappSyncTimelineScroll = window.schnappInitTopScroll({
    top: top,
    spacer: spacer,
    wrap: wrap,
    table: table,
    onBeforeSync: updateSpanRows
  });
})();

// Journals + Reports: top horizontal scrollbar when the grid overflows.
(function () {
  window.schnappInitTopScroll({
    top: document.getElementById("journals-scroll-top"),
    spacer: document.getElementById("journals-scroll-top-spacer"),
    wrap: document.getElementById("journals-grid-wrap"),
    table: document.getElementById("journals-grid")
  });
  window.schnappInitTopScroll({
    top: document.getElementById("reports-scroll-top"),
    spacer: document.getElementById("reports-scroll-top-spacer"),
    wrap: document.getElementById("reports-grid-wrap"),
    table: document.getElementById("reports-grid")
  });
})();

// Token-aware tags autocomplete from dictionary (GET /Api/Tags).
(function initTagsAutocomplete() {
  var cache = Object.create(null); // key -> { tags: string[], at: number }
  var CACHE_MS = 60 * 1000;

  function isTagsInput(el) {
    if (!el || el.tagName !== "INPUT" || el.type === "hidden" || el.disabled || el.readOnly) return false;
    if (el.getAttribute("data-tags-autocomplete") === "0") return false;
    if (el.hasAttribute("data-tags-autocomplete")) return true;
    var id = (el.id || "").toLowerCase();
    var name = (el.name || "").toLowerCase();
    return /(^|[-_])tags$/.test(id) || /(^|[-_])tags$/.test(name)
      || id.indexOf("tags") >= 0 || name.indexOf("tag") >= 0;
  }

  function customerIdFor(el) {
    var raw = el.getAttribute("data-tags-customer-id");
    if (raw) return raw;
    var sel = el.getAttribute("data-tags-customer-select");
    if (sel) {
      var node = document.querySelector(sel);
      if (node && node.value) return node.value;
    }
    // Journal cascade: nearest customer select in same form/dialog
    var root = el.closest("form, dialog, [data-journal-start-dialog], [data-journal-edit-page], tr") || document;
    var cust = root.querySelector('[data-journal-cascade="customer"]');
    if (cust && cust.value) return cust.value;
    return "";
  }

  function fetchTags(customerId) {
    var key = customerId || "";
    var hit = cache[key];
    var now = Date.now();
    if (hit && now - hit.at < CACHE_MS) return Promise.resolve(hit.tags);
    var url = "/Api/Tags" + (key ? ("?customerId=" + encodeURIComponent(key)) : "");
    return fetch(url, { credentials: "same-origin", headers: { Accept: "application/json" } })
      .then(function (r) {
        if (!r.ok) return [];
        return r.json();
      })
      .then(function (body) {
        var tags = (body && body.tags) || [];
        cache[key] = { tags: tags, at: Date.now() };
        return tags;
      })
      .catch(function () { return []; });
  }

  function tokenBounds(value, caret) {
    var i = caret;
    while (i > 0 && !/[\s,;]/.test(value.charAt(i - 1))) i--;
    var start = i;
    i = caret;
    while (i < value.length && !/[\s,;]/.test(value.charAt(i))) i++;
    return { start: start, end: i, token: value.slice(start, i) };
  }

  // Host popup inside the nearest <dialog> when present. Native dialogs use the top
  // layer; a body-level list sits behind the modal (viewer, row edit, journals, …).
  function hostFor(input) {
    return input.closest("dialog") || document.body;
  }

  function ensureList(input) {
    var id = input.getAttribute("data-tags-list-id");
    if (id) {
      var existing = document.getElementById(id);
      if (existing) {
        // Re-parent if the input is now in a different dialog (or left one).
        var host = hostFor(input);
        if (existing.parentNode !== host) host.appendChild(existing);
        return existing;
      }
    }
    id = "tags-ac-" + Math.random().toString(36).slice(2, 10);
    input.setAttribute("data-tags-list-id", id);
    var ul = document.createElement("ul");
    ul.id = id;
    ul.className = "tags-autocomplete";
    ul.hidden = true;
    ul.setAttribute("role", "listbox");
    hostFor(input).appendChild(ul);
    return ul;
  }

  function positionList(input, ul) {
    var r = input.getBoundingClientRect();
    // Fixed coords = viewport; works for body and dialog hosts alike.
    ul.style.position = "fixed";
    ul.style.left = r.left + "px";
    ul.style.top = (r.bottom + 2) + "px";
    ul.style.minWidth = Math.max(r.width, 120) + "px";
    ul.style.zIndex = "2147483646";
  }

  function hideList(ul) {
    ul.hidden = true;
    ul.innerHTML = "";
  }

  function applySuggestion(input, start, end, suggestion) {
    var v = input.value;
    var before = v.slice(0, start);
    var after = v.slice(end);
    // Keep a trailing space so the next tag can be typed immediately.
    var insert = suggestion + (after.length === 0 || /^[\s,;]/.test(after) ? " " : "");
    input.value = before + insert + after;
    var pos = before.length + insert.length;
    input.setSelectionRange(pos, pos);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  }

  function showSuggestions(input, ul, matches, bounds) {
    ul.innerHTML = "";
    if (!matches.length) {
      hideList(ul);
      return;
    }
    matches.slice(0, 12).forEach(function (tag, idx) {
      var li = document.createElement("li");
      li.setAttribute("role", "option");
      li.textContent = tag;
      li.tabIndex = -1;
      if (idx === 0) li.classList.add("active");
      li.addEventListener("mousedown", function (e) {
        e.preventDefault();
        applySuggestion(input, bounds.start, bounds.end, tag);
        hideList(ul);
        input.focus();
      });
      ul.appendChild(li);
    });
    positionList(input, ul);
    ul.hidden = false;
  }

  function activeItem(ul) {
    return ul.querySelector("li.active") || ul.querySelector("li");
  }

  function moveActive(ul, delta) {
    var items = ul.querySelectorAll("li");
    if (!items.length) return;
    var i = 0;
    for (var n = 0; n < items.length; n++) {
      if (items[n].classList.contains("active")) { i = n; break; }
    }
    items[i].classList.remove("active");
    i = (i + delta + items.length) % items.length;
    items[i].classList.add("active");
    items[i].scrollIntoView({ block: "nearest" });
  }

  function wire(input) {
    if (input.__tagsAcWired) return;
    input.__tagsAcWired = true;
    var ul = ensureList(input);
    var lastBounds = { start: 0, end: 0, token: "" };

    function refresh() {
      var caret = input.selectionStart == null ? input.value.length : input.selectionStart;
      lastBounds = tokenBounds(input.value, caret);
      var prefix = lastBounds.token;
      if (!prefix) {
        hideList(ul);
        return;
      }
      fetchTags(customerIdFor(input)).then(function (tags) {
        var lower = prefix.toLowerCase();
        var matches = tags.filter(function (t) {
          return t.toLowerCase().indexOf(lower) === 0 && t !== prefix;
        });
        // Prefer exact-case-insensitive remaining suggestions
        showSuggestions(input, ul, matches, lastBounds);
      });
    }

    input.addEventListener("input", refresh);
    input.addEventListener("focus", refresh);
    input.addEventListener("click", refresh);
    input.addEventListener("keydown", function (e) {
      if (ul.hidden) return;
      if (e.key === "ArrowDown") {
        e.preventDefault();
        moveActive(ul, 1);
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        moveActive(ul, -1);
      } else if (e.key === "Enter" || e.key === "Tab") {
        var li = activeItem(ul);
        if (li && !ul.hidden) {
          e.preventDefault();
          applySuggestion(input, lastBounds.start, lastBounds.end, li.textContent);
          hideList(ul);
        }
      } else if (e.key === "Escape") {
        hideList(ul);
      }
    });
    input.addEventListener("blur", function () {
      // Delay so mousedown on option can fire first.
      setTimeout(function () { hideList(ul); }, 150);
    });

    var custSel = input.getAttribute("data-tags-customer-select");
    if (custSel) {
      var node = document.querySelector(custSel);
      if (node) node.addEventListener("change", function () {
        var key = customerIdFor(input);
        delete cache[key];
        delete cache[""];
        refresh();
      });
    }
    var root = input.closest("form, dialog, [data-journal-start-dialog], [data-journal-edit-page], tr");
    if (root) {
      var cust = root.querySelector('[data-journal-cascade="customer"]');
      if (cust) {
        cust.addEventListener("change", function () {
          delete cache[customerIdFor(input)];
          refresh();
        });
      }
    }
  }

  function scan(root) {
    (root || document).querySelectorAll("input").forEach(function (el) {
      if (isTagsInput(el)) wire(el);
    });
  }

  scan(document);
  // Dialogs / HTMX may inject inputs later
  if (window.MutationObserver) {
    new MutationObserver(function (muts) {
      muts.forEach(function (m) {
        m.addedNodes.forEach(function (n) {
          if (n.nodeType !== 1) return;
          if (n.matches && n.matches("input") && isTagsInput(n)) wire(n);
          if (n.querySelectorAll) scan(n);
        });
      });
    }).observe(document.body, { childList: true, subtree: true });
  }
  document.addEventListener("toggle", function (e) {
    if (e.target && e.target.tagName === "DIALOG" && e.target.open) scan(e.target);
  }, true);
})();

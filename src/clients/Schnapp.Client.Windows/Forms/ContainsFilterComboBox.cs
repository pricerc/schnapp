namespace Schnapp.Client.Windows.Forms;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

/// <summary>
/// Editable combobox that filters its dropdown with substring (contains) matching.
/// </summary>
/// <remarks>
/// WinForms built-in <see cref="ComboBox.AutoCompleteMode"/> only matches prefixes.
/// This control keeps a full candidate list and, while the user types, rebuilds
/// <see cref="ComboBox.Items"/> to entries that contain the typed text (case-insensitive),
/// then opens the dropdown. Leaving the control restores the full list.
/// <para>
/// Item mutation is done carefully: clearing <see cref="ComboBox.Items"/> while
/// <see cref="ComboBox.SelectedIndex"/> is non-negative (or while dropped down) can throw
/// <see cref="ArgumentOutOfRangeException"/> from the native text path — see
/// <see cref="ReplaceItems"/>.
/// </para>
/// </remarks>
public class ContainsFilterComboBox : ComboBox
{
    /// <summary>Full unfiltered candidate set used by contains matching.</summary>
    private List<string> _candidates = [];

    /// <summary>
    /// True while this control is mutating items/text so recursive
    /// <see cref="OnTextUpdate"/> filtering is skipped.
    /// </summary>
    private bool _filterBusy;

    /// <summary>
    /// Nested-call counter for <see cref="SuspendFiltering"/>; filtering is off while &gt; 0.
    /// </summary>
    private int _suppressFilter;

    /// <summary>
    /// Creates a drop-down combobox with built-in prefix autocomplete disabled.
    /// </summary>
    public ContainsFilterComboBox()
    {
        // Because WinForms AutoCompleteMode only does prefix matching and fights our contains filter.
        DropDownStyle = ComboBoxStyle.DropDown;
        AutoCompleteMode = AutoCompleteMode.None;
        AutoCompleteSource = AutoCompleteSource.None;
        FormattingEnabled = true;
    }

    /// <summary>
    /// When true, filtering on <see cref="ComboBox.TextUpdate"/> is skipped
    /// (e.g. while a parent form programmatically repopulates several combos).
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool FilteringSuspended => _suppressFilter > 0;

    /// <summary>
    /// Full candidate set used for filtering (not only the currently displayed items).
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<string> Candidates => _candidates;

    /// <summary>
    /// Temporarily disables contains-filtering (nested calls are reference-counted).
    /// Dispose the returned token to resume.
    /// </summary>
    /// <returns>An <see cref="IDisposable"/> that resumes filtering when disposed.</returns>
    public IDisposable SuspendFiltering()
    {
        _suppressFilter++;
        return new FilterSuspendScope(this);
    }

    /// <summary>
    /// Replaces the candidate list with distinct, trimmed, ordered names and shows them all.
    /// </summary>
    /// <param name="names">Source names (null/blank entries are skipped).</param>
    /// <param name="selected">Text to show after reload (may be empty).</param>
    /// <param name="includeNone">
    /// When true, inserts <c>(none)</c> as the first candidate (journal C/P/T pickers).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> is null.</exception>
    public void SetCandidates(IEnumerable<string> names, string? selected = null, bool includeNone = false)
    {
        if (names == null)
        {
            throw new ArgumentNullException(nameof(names));
        }

        // Because blank names are useless; also strip any existing "(none)" so includeNone cannot double it
        // (the API/cache may store a real list item named "(none)").
        var list = names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Where(x => !string.Equals(x, "(none)", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (includeNone)
        {
            list.Insert(0, "(none)");
        }

        SetCandidatesExact(list, selected ?? "");
    }

    /// <summary>
    /// Replaces candidates with an already-prepared list (no reordering or <c>(none)</c> injection).
    /// </summary>
    /// <param name="candidates">Full list to store and display.</param>
    /// <param name="selected">Text to show after reload.</param>
    /// <exception cref="ArgumentNullException"><paramref name="candidates"/> is null.</exception>
    public void SetCandidatesExact(IReadOnlyList<string> candidates, string? selected = null)
    {
        if (candidates == null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        // Because callers may pass a non-List IReadOnlyList; we need a mutable private copy.
        _candidates = candidates as List<string> ?? [.. candidates];
        AutoCompleteMode = AutoCompleteMode.None;
        AutoCompleteSource = AutoCompleteSource.None;

        _filterBusy = true;
        try
        {
            ReplaceItems(_candidates, selected ?? "", selectionStart: null, dropDown: false);
        }
        finally
        {
            _filterBusy = false;
        }
    }

    /// <summary>
    /// Current edit text, tolerant of WinForms index/text races.
    /// </summary>
    public string SafeText
    {
        get => SafeGetText();
        set => SafeSetText(value ?? "");
    }

    /// <inheritdoc />
    /// <remarks>Applies contains filtering after the base text update, unless suspended or busy.</remarks>
    protected override void OnTextUpdate(EventArgs e)
    {
        base.OnTextUpdate(e);
        // Because TextUpdate also fires during our own ReplaceItems/SafeSetText work.
        if (_filterBusy || FilteringSuspended || !IsHandleCreated)
        {
            return;
        }

        ApplyContainsFilter();
    }

    /// <inheritdoc />
    /// <remarks>Restores the full candidate list before parent Leave handlers cascade.</remarks>
    protected override void OnLeave(EventArgs e)
    {
        // Because cascade/leave handlers on the parent expect the full list, not a filtered subset.
        if (!_filterBusy && !FilteringSuspended && IsHandleCreated && _candidates.Count > 0)
        {
            RestoreFullList();
        }

        base.OnLeave(e);
    }

    /// <summary>
    /// Filters <see cref="ComboBox.Items"/> to candidates containing the typed text.
    /// </summary>
    private void ApplyContainsFilter()
    {
        var typed = SafeGetText();
        var selStart = SelectionStart;

        // Because empty typed text should show the full list, not an empty dropdown.
        var list = string.IsNullOrEmpty(typed)
            ? _candidates
            : [.. _candidates.Where(x => x.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0)];

        _filterBusy = true;
        try
        {
            // Because only drop down when focused so programmatic reloads do not steal focus.
            ReplaceItems(list, typed, selStart, dropDown: list.Count > 0 && Focused);
        }
        finally
        {
            _filterBusy = false;
        }
    }

    /// <summary>Shows every candidate again while preserving the edit text.</summary>
    private void RestoreFullList()
    {
        var text = SafeGetText();
        _filterBusy = true;
        try
        {
            ReplaceItems(_candidates, text, selectionStart: null, dropDown: false);
        }
        finally
        {
            _filterBusy = false;
        }
    }

    /// <summary>
    /// Rebuilds <see cref="ComboBox.Items"/> safely, then restores text and optional caret.
    /// </summary>
    /// <param name="items">Items to put into the combobox (filtered or full list).</param>
    /// <param name="text">Edit text to restore after the rebuild.</param>
    /// <param name="selectionStart">Caret position to restore, or null to leave default.</param>
    /// <param name="dropDown">When true, opens the dropdown after rebuild if items exist.</param>
    /// <remarks>
    /// Clearing items while <see cref="ComboBox.SelectedIndex"/> still points at a slot, or while
    /// the list is dropped down, commonly throws from the native <c>get_Text</c> path
    /// (<c>InvalidArgument=Value of '0' is not valid for 'index'</c>).
    /// </remarks>
    private void ReplaceItems(
        IReadOnlyList<string> items,
        string text,
        int? selectionStart,
        bool dropDown)
    {
        // Because clearing Items while DroppedDown throws from the native combobox path.
        if (DroppedDown)
        {
            DroppedDown = false;
        }

        // Because SelectedIndex must be -1 before Clear or native get_Text uses a stale index.
        if (SelectedIndex != -1)
        {
            SelectedIndex = -1;
        }

        BeginUpdate();
        try
        {
            Items.Clear();
            for (var i = 0; i < items.Count; i++)
            {
                Items.Add(items[i]);
            }
        }
        finally
        {
            EndUpdate();
        }

        // Because EndUpdate can re-select the first item; clear again before setting text.
        if (SelectedIndex != -1)
        {
            SelectedIndex = -1;
        }

        SafeSetText(text ?? "");
        RestoreCaret(text, selectionStart);

        if (dropDown && items.Count > 0)
        {
            DroppedDown = true;
            // Because opening the dropdown can switch the cursor to the hourglass on some themes.
            Cursor.Current = Cursors.Default;
            RestoreCaret(text, selectionStart);
        }
    }

    /// <summary>
    /// Restores the caret (selection start) after an items rebuild, clamped to text length.
    /// </summary>
    /// <param name="text">Text whose length bounds the caret.</param>
    /// <param name="selectionStart">Desired caret index, or null to skip restoration.</param>
    private void RestoreCaret(string? text, int? selectionStart)
    {
        if (selectionStart is not int s)
        {
            return;
        }

        // Because SelectionStart must stay within [0, text.Length] or WinForms throws.
        var len = text?.Length ?? 0;
        var caret = s < 0 ? 0 : (s > len ? len : s);
        try
        {
            SelectionStart = caret;
            SelectionLength = 0;
        }
        catch (ArgumentOutOfRangeException)
        {
            // ignore caret restore failures
        }
    }

    /// <summary>
    /// Reads <see cref="ComboBox.Text"/>, recovering from native index/text races.
    /// </summary>
    /// <returns>Current text, or empty string if unreadable.</returns>
    private string SafeGetText()
    {
        try
        {
            return Text ?? "";
        }
        catch (ArgumentOutOfRangeException)
        {
            // Because SelectedIndex can point past Items after a concurrent clear.
            try { SelectedIndex = -1; } catch { /* ignore */ }
            try { return Text ?? ""; }
            catch { return ""; }
        }
    }

    /// <summary>
    /// Sets <see cref="ComboBox.Text"/> after clearing any selection, recovering from native races.
    /// </summary>
    /// <param name="text">Text to assign (null treated as empty by callers).</param>
    private void SafeSetText(string text)
    {
        try
        {
            // Because assigning Text while SelectedIndex is set can rewrite the item or throw.
            if (SelectedIndex != -1)
            {
                SelectedIndex = -1;
            }

            Text = text;
        }
        catch (ArgumentOutOfRangeException)
        {
            try
            {
                SelectedIndex = -1;
                Text = text;
            }
            catch
            {
                // leave control as-is rather than crash the host form
            }
        }
    }

    /// <summary>
    /// Disposable token that decrements <see cref="_suppressFilter"/> when disposed.
    /// </summary>
    private sealed class FilterSuspendScope : IDisposable
    {
        /// <summary>Owning combobox; nulled after dispose to make dispose idempotent.</summary>
        private ContainsFilterComboBox? _owner;

        /// <summary>
        /// Creates a scope for the given owner (caller has already incremented the counter).
        /// </summary>
        /// <param name="owner">Combobox whose filter suppress count will be decremented.</param>
        public FilterSuspendScope(ContainsFilterComboBox owner) => _owner = owner;

        /// <summary>Decrements the owner's suppress count once; subsequent calls are no-ops.</summary>
        public void Dispose()
        {
            var o = _owner;
            if (o == null)
            {
                return;
            }

            // Because dispose must be idempotent if the token is disposed twice.
            _owner = null;
            if (o._suppressFilter > 0)
            {
                o._suppressFilter--;
            }
        }
    }
}

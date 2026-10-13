namespace Schnapp.Client.Windows.Forms;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Medo;
using Schnapp.Client.Core.Tags;
using Schnapp.Client.Core.Upload;

/// <summary>
/// Multi-tag text field with token-aware autocomplete dropdown (TextBox + ListBox popup).
/// </summary>
/// <remarks>
/// Designer surface owns <see cref="Box"/>; the suggestion list is a floating
/// <see cref="ToolStripDropDown"/> created at runtime (not on the design surface).
/// Suggestions come from the local tag dictionary cache. The active token under the
/// caret is completed; earlier tags are left intact. Behavior matches Avalonia
/// <c>TagsInput</c> and the web autocomplete.
/// </remarks>
public partial class TagsInputControl : UserControl
{
    private ListBox _list = null!;
    private ToolStripDropDown _dropDown = null!;
    private ToolStripControlHost _host = null!;
    private IReadOnlyList<string> _candidates = Array.Empty<string>();
    private TagTokenAutocomplete.TokenSpan _span;
    private bool _suppress;
    private bool _popupReady;

    /// <summary>Designer / runtime constructor.</summary>
    public TagsInputControl()
    {
        InitializeComponent();
        EnsurePopup();
        WireEditorEvents();
    }

    /// <summary>Tags text (space-separated tokens).</summary>
    [Browsable(true)]
    [Category("Data")]
    [Description("Space-separated tag tokens.")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public override string Text
    {
        get => Box.Text ?? "";
        set
        {
            _suppress = true;
            try { Box.Text = value ?? ""; }
            finally { _suppress = false; }
        }
    }

    /// <summary>Inner text box (for focus/tab order).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TextBox InnerTextBox => Box;

    /// <summary>Border style of the inner text box.</summary>
    [Browsable(true)]
    [Category("Appearance")]
    [DefaultValue(BorderStyle.FixedSingle)]
    [Description("Border style of the tags text box.")]
    public BorderStyle TextBoxBorderStyle
    {
        get => Box.BorderStyle;
        set => Box.BorderStyle = value;
    }

    /// <summary>Raised when the tags text changes.</summary>
    [Category("Property Changed")]
    [Description("Occurs when the tags text changes.")]
    public new event EventHandler? TextChanged
    {
        add => Box.TextChanged += value;
        remove => Box.TextChanged -= value;
    }

    /// <summary>Raised when the inner editor loses focus.</summary>
    [Category("Focus")]
    [Description("Occurs when the tags editor loses focus.")]
    public new event EventHandler? Leave
    {
        add => Box.Leave += value;
        remove => Box.Leave -= value;
    }

    /// <summary>
    /// Loads suggestion candidates from the reference-data cache.
    /// </summary>
    /// <param name="cache">Local tag dictionary; null clears suggestions.</param>
    /// <param name="customerId">Optional customer scope (global + customer).</param>
    public void SetSuggestions(ReferenceDataCache? cache, Uuid7? customerId = null)
    {
        if (cache is null)
        {
            _candidates = Array.Empty<string>();
            HideList();
            return;
        }

        try
        {
            _candidates = cache.ListTagValues(customerId) ?? Array.Empty<string>();
        }
        catch
        {
            _candidates = Array.Empty<string>();
        }
    }

    /// <summary>Replaces the candidate list directly (tests / offline inject).</summary>
    public void SetSuggestions(IReadOnlyList<string>? tags)
    {
        _candidates = tags ?? Array.Empty<string>();
    }

    /// <inheritdoc />
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Box.Font = Font;
        if (_popupReady)
        {
            _list.Font = Font;
        }

        Height = Box.PreferredHeight + 2;
    }

    /// <inheritdoc />
    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Box.Enabled = Enabled;
        if (!Enabled)
        {
            HideList();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            HideList();
            if (_popupReady)
            {
                _dropDown.Dispose();
                _host.Dispose();
                _list.Dispose();
                _popupReady = false;
            }
            if (components != null)
            {
                components.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    private void WireEditorEvents()
    {
        Box.TextChanged += Box_TextChanged;
        Box.PreviewKeyDown += Box_PreviewKeyDown;
        Box.KeyDown += Box_KeyDown;
        Box.Leave += Box_Leave;
        Box.Click += (_, __) => RefreshSuggestions();
        Box.GotFocus += (_, __) => RefreshSuggestions();
    }

/// <summary>
/// Ensures the suggestion popup is created (lazy init).
/// </summary>
    private void EnsurePopup()
    {
        if (_popupReady || LicenseManager.UsageMode == LicenseUsageMode.Designtime)
        {
            return;
        }

        _list = new ListBox
        {
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Box.Font,
            Height = 140,
            Width = 200
        };
        _list.Click += List_Click;
        _list.KeyDown += List_KeyDown;

        _host = new ToolStripControlHost(_list)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false
        };
        _dropDown = new ToolStripDropDown
        {
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            AutoClose = false,
            AutoSize = false
        };
        _dropDown.Items.Add(_host);
        _popupReady = true;
    }

    private void Box_TextChanged(object? sender, EventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        RefreshSuggestions();
    }

    private void Box_Leave(object? sender, EventArgs e)
    {
        // Delay so List_Click can run first when choosing a suggestion.
        BeginInvoke(new Action(() =>
        {
            if (!_popupReady)
            {
                return;
            }

            if (!_list.Focused && !Box.Focused)
            {
                HideList();
            }
        }));
    }

    /// <summary>
    /// When the suggestion list is open, treat Tab as an input key so focus does not
    /// leave the editor (dialog-key navigation would otherwise steal it).
    /// </summary>
    private void Box_PreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        if (e.KeyCode != Keys.Tab)
        {
            return;
        }

        EnsurePopup();
        if (_popupReady && _dropDown.Visible && _list.Items.Count > 0)
        {
            e.IsInputKey = true;
        }
    }

    private void Box_KeyDown(object? sender, KeyEventArgs e)
    {
        EnsurePopup();
        if (!_popupReady || !_dropDown.Visible || _list.Items.Count == 0)
        {
            return;
        }

        if (e.KeyCode == Keys.Down)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            MoveSelection(1);
        }
        else if (e.KeyCode == Keys.Up)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            MoveSelection(-1);
        }
        else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab)
        {
            if (_list.SelectedItem is string s)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ApplySelection(s);
            }
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            HideList();
        }
    }

    private void List_Click(object? sender, EventArgs e)
    {
        if (_list.SelectedItem is string s)
        {
            ApplySelection(s);
        }
    }

    private void List_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _list.SelectedItem is string s)
        {
            e.Handled = true;
            ApplySelection(s);
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            HideList();
            Box.Focus();
        }
    }

    private void RefreshSuggestions()
    {
        if (_suppress || !Enabled)
        {
            return;
        }

        EnsurePopup();
        if (!_popupReady)
        {
            return;
        }

        var caret = Box.SelectionStart;
        _span = TagTokenAutocomplete.TokenAt(Box.Text, caret);
        if (_span.Token.Length == 0)
        {
            HideList();
            return;
        }

        var matches = TagTokenAutocomplete.Filter(_candidates, _span.Token, max: 12);
        if (matches.Count == 0)
        {
            HideList();
            return;
        }

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var m in matches)
            {
                _list.Items.Add(m);
            }

            _list.SelectedIndex = 0;
        }
        finally
        {
            _list.EndUpdate();
        }

        var width = Math.Max(Box.Width, 120);
        var itemH = Math.Max(_list.ItemHeight, 16);
        var height = Math.Min(matches.Count, 8) * itemH + 4;
        _list.Size = new Size(width, height);
        _host.Size = _list.Size;
        _dropDown.Size = new Size(width + 2, height + 2);

        if (!_dropDown.Visible)
        {
            var screen = Box.PointToScreen(new Point(0, Box.Height));
            _dropDown.Show(screen);
        }
    }

    private void MoveSelection(int delta)
    {
        if (_list.Items.Count == 0)
        {
            return;
        }

        var i = _list.SelectedIndex;
        if (i < 0)
        {
            i = 0;
        }

        i = (i + delta + _list.Items.Count) % _list.Items.Count;
        _list.SelectedIndex = i;
    }

    private void ApplySelection(string suggestion)
    {
        var (text, caret) = TagTokenAutocomplete.Apply(Box.Text, _span, suggestion);
        _suppress = true;
        try
        {
            Box.Text = text;
            Box.SelectionStart = caret;
            Box.SelectionLength = 0;
        }
        finally
        {
            _suppress = false;
        }
        HideList();
        Box.Focus();
        RefreshSuggestions();
    }

    private void HideList()
    {
        if (_popupReady && _dropDown.Visible)
        {
            _dropDown.Close();
        }

        if (_popupReady)
        {
            _list.Items.Clear();
        }
    }
}

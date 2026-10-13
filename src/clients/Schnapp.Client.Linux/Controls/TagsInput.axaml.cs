namespace Schnapp.Client.Linux.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Medo;

using Schnapp.Client.Core.Tags;
using Schnapp.Client.Core.Upload;

/// <summary>
/// Multi-tag text field with token-aware autocomplete (TextBox + ListBox popup).
/// </summary>
/// <remarks>
/// Mirrors the Windows <c>TagsInputControl</c> and web token-aware suggestions.
/// Candidates come from the local tag dictionary cache.
/// </remarks>
public partial class TagsInput : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TagsInput, string?>(nameof(Text), defaultValue: "");

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<TagsInput, string?>(nameof(PlaceholderText));

    public static readonly StyledProperty<int> MaxLengthProperty =
        AvaloniaProperty.Register<TagsInput, int>(nameof(MaxLength), defaultValue: 500);

    private IReadOnlyList<string> _candidates = Array.Empty<string>();
    private TagTokenAutocomplete.TokenSpan _span;
    private bool _suppress;
    private bool _wired;

    public TagsInput()
    {
        InitializeComponent();
    }

    /// <summary>Tags text (space-separated tokens).</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Placeholder shown when empty.</summary>
    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>Maximum characters in the editor.</summary>
    public int MaxLength
    {
        get => GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>Raised when tags text changes.</summary>
    public event EventHandler? TextChanged;

    /// <summary>Raised when the editor loses focus.</summary>
    public event EventHandler? LostFocusTags;

    /// <summary>
    /// Loads suggestion candidates from the reference-data cache.
    /// </summary>
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

    /// <summary>Replaces the candidate list directly.</summary>
    public void SetSuggestions(IReadOnlyList<string>? tags) =>
        _candidates = tags ?? Array.Empty<string>();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        WireEditor();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        WireEditor();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && Editor is not null && !_suppress)
        {
            _suppress = true;
            try { Editor.Text = change.GetNewValue<string?>() ?? ""; }
            finally { _suppress = false; }
        }
        else if (change.Property == PlaceholderTextProperty && Editor is not null)
        {
            Editor.PlaceholderText = change.GetNewValue<string?>();
        }
        else if (change.Property == MaxLengthProperty && Editor is not null)
        {
            Editor.MaxLength = change.GetNewValue<int>();
        }
    }

    private void WireEditor()
    {
        if (_wired || Editor is null)
        {
            return;
        }

        _wired = true;

        Editor.Text = Text ?? "";
        Editor.PlaceholderText = PlaceholderText;
        Editor.MaxLength = MaxLength;
        if (SuggestPopup is not null)
        {
            SuggestPopup.PlacementTarget = Editor;
        }

        Editor.TextChanged += (_, __) =>
        {
            if (_suppress)
            {
                return;
            }

            _suppress = true;
            try { SetCurrentValue(TextProperty, Editor.Text); }
            finally { _suppress = false; }
            TextChanged?.Invoke(this, EventArgs.Empty);
            RefreshSuggestions();
        };
        Editor.LostFocus += (_, __) =>
        {
            // Delay so list pointer selection can apply first.
            Dispatcher.UIThread.Post(() =>
            {
                if (SuggestList?.IsFocused != true && Editor?.IsFocused != true)
                {
                    HideList();
                }
            }, DispatcherPriority.Background);
            LostFocusTags?.Invoke(this, EventArgs.Empty);
        };
        Editor.GotFocus += (_, __) => RefreshSuggestions();
        Editor.AddHandler(KeyDownEvent, Editor_KeyDown, RoutingStrategies.Tunnel);

        if (SuggestList is not null)
        {
            SuggestList.PointerReleased += (_, __) =>
            {
                if (SuggestList.SelectedItem is string s)
                {
                    ApplySelection(s);
                }
            };
            SuggestList.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && SuggestList.SelectedItem is string s)
                {
                    e.Handled = true;
                    ApplySelection(s);
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    HideList();
                    Editor?.Focus();
                }
            };
        }
    }

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (SuggestPopup is null || !SuggestPopup.IsOpen || SuggestList is null || SuggestList.ItemCount == 0)
        {
            return;
        }

        if (e.Key == Key.Down)
        {
            e.Handled = true;
            MoveSelection(1);
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            MoveSelection(-1);
        }
        else if (e.Key is Key.Enter or Key.Tab)
        {
            if (SuggestList.SelectedItem is string s)
            {
                e.Handled = true;
                ApplySelection(s);
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideList();
        }
    }

    private void RefreshSuggestions()
    {
        if (Editor is null || SuggestList is null || SuggestPopup is null)
        {
            return;
        }

        var text = Editor.Text ?? "";
        var caret = Editor.CaretIndex;
        _span = TagTokenAutocomplete.TokenAt(text, caret);
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

        SuggestList.ItemsSource = matches;
        SuggestList.SelectedIndex = 0;
        SuggestPopup.Width = Math.Max(Editor.Bounds.Width, 120);
        SuggestPopup.IsOpen = true;
    }

    private void MoveSelection(int delta)
    {
        if (SuggestList is null || SuggestList.ItemCount == 0)
        {
            return;
        }

        var i = SuggestList.SelectedIndex;
        if (i < 0)
        {
            i = 0;
        }

        i = (i + delta + SuggestList.ItemCount) % SuggestList.ItemCount;
        SuggestList.SelectedIndex = i;
        if (SuggestList.SelectedItem is not null)
        {
            SuggestList.ScrollIntoView(SuggestList.SelectedItem);
        }
    }

    private void ApplySelection(string suggestion)
    {
        if (Editor is null)
        {
            return;
        }

        var (text, caret) = TagTokenAutocomplete.Apply(Editor.Text, _span, suggestion);
        _suppress = true;
        try
        {
            Editor.Text = text;
            Editor.CaretIndex = caret;
            SetCurrentValue(TextProperty, text);
        }
        finally
        {
            _suppress = false;
        }
        TextChanged?.Invoke(this, EventArgs.Empty);
        HideList();
        Editor.Focus();
    }

    private void HideList()
    {
        if (SuggestPopup is not null)
        {
            SuggestPopup.IsOpen = false;
        }

        if (SuggestList is not null)
        {
            SuggestList.ItemsSource = null;
        }
    }
}

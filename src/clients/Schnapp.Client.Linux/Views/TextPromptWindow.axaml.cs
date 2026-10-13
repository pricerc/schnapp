namespace Schnapp.Client.Linux.Views;

using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;

using Schnapp.Client.Core.Upload;

/// <summary>Movable/resizable prompt used from tray journal menus (notes, tags, names).</summary>
public partial class TextPromptWindow : Window
{
    private bool _useTags;

    public TextPromptWindow()
    {
        InitializeComponent();
    }

    /// <summary>Shows a plain text prompt.</summary>
    public static Task<string?> PromptAsync(Window owner, string title, string prompt, string? initial = null) =>
        PromptCoreAsync(owner, title, prompt, initial, tagCache: null);

    /// <summary>Shows a multi-tag autocomplete prompt when <paramref name="tagCache"/> is set.</summary>
    public static Task<string?> PromptTagsAsync(
        Window owner,
        string title,
        string prompt,
        string? initial,
        ReferenceDataCache? tagCache) =>
        PromptCoreAsync(owner, title, prompt, initial, tagCache);

    private static async Task<string?> PromptCoreAsync(
        Window owner,
        string title,
        string prompt,
        string? initial,
        ReferenceDataCache? tagCache)
    {
        var w = new TextPromptWindow { Title = title };
        w.PromptText.Text = prompt;
        var useTags = tagCache is not null;
        w._useTags = useTags;
        if (useTags)
        {
            w.InputBox.IsVisible = false;
            w.TagsInput.IsVisible = true;
            w.TagsInput.Text = initial ?? "";
            w.TagsInput.SetSuggestions(tagCache);
        }
        else
        {
            w.InputBox.Text = initial ?? "";
        }

        // Center on the pointer's screen (not bottom-right tray corner).
        WindowPlacement.PlaceCenteredOnPointerScreen(w);
        w.Opened += (_, _) =>
        {
            // Re-center after layout so SizeToContent height is known.
            WindowPlacement.PlaceCenteredOnPointerScreen(w);
            if (useTags)
            {
                w.TagsInput.Focus();
            }
            else
            {
                w.InputBox.Focus();
                w.InputBox.SelectAll();
            }
        };
        return await w.ShowDialog<string?>(owner);
    }

    private void OnOk(object? sender, RoutedEventArgs e) =>
        Close(_useTags ? TagsInput.Text : InputBox.Text);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}

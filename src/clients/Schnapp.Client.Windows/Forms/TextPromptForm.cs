namespace Schnapp.Client.Windows.Forms;

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Schnapp.Client.Core.Upload;

/// <summary>
/// Simple modal text prompt (notes, tags, new list names).
/// </summary>
public partial class TextPromptForm : Form
{
    private TagsInputControl? _tagsInput;

    /// <summary>User-entered text when OK is pressed.</summary>
    public string Value =>
        _tagsInput is not null ? (_tagsInput.Text ?? "") : (InputBox.Text ?? "");

    /// <summary>
    /// Creates a prompt with the given title, prompt label, and initial value.
    /// </summary>
    /// <param name="title">Window title.</param>
    /// <param name="prompt">Label text shown above the input.</param>
    /// <param name="initial">Initial contents of the input box.</param>
    /// <param name="multiline">When true, expands the input into a multi-line editor.</param>
    /// <param name="tagSuggestions">When set, uses multi-tag autocomplete control.</param>
    public TextPromptForm(
        string title,
        string prompt,
        string initial = "",
        bool multiline = false,
        IReadOnlyList<string>? tagSuggestions = null)
    {
        InitializeComponent();
        Text = title;
        PromptText.Text = prompt;
        InputBox.Text = initial ?? "";
        if (multiline)
        {
            InputBox.Multiline = true;
            InputBox.AcceptsReturn = true;
            InputBox.Height = 96;
            ClientSize = new System.Drawing.Size(ClientSize.Width, ClientSize.Height + 60);
        }
        else if (tagSuggestions is not null)
        {
            _tagsInput = new TagsInputControl
            {
                Location = InputBox.Location,
                Size = InputBox.Size,
                Anchor = InputBox.Anchor,
                TabIndex = InputBox.TabIndex,
                Text = initial ?? ""
            };
            _tagsInput.SetSuggestions(tagSuggestions);
            Controls.Add(_tagsInput);
            InputBox.Visible = false;
            ActiveControl = _tagsInput.InnerTextBox;
        }
    }

    /// <summary>Shows a prompt and returns the value, or null if cancelled.</summary>
    public static string? Prompt(
        IWin32Window? owner,
        string title,
        string prompt,
        string initial = "",
        bool multiline = false,
        ReferenceDataCache? referenceData = null)
    {
        IReadOnlyList<string>? suggestions = null;
        if (!multiline && referenceData is not null
            && (title.IndexOf("tag", StringComparison.OrdinalIgnoreCase) >= 0
                || prompt.IndexOf("tag", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            try { suggestions = referenceData.ListTagValues(); }
            catch { /* ignore cache errors */ }
            // Empty list still enables TagsInput so multi-token UX is consistent.
            suggestions ??= Array.Empty<string>();
        }

        using (var f = new TextPromptForm(title, prompt, initial, multiline, suggestions))
        {
            return f.ShowDialog(owner) == DialogResult.OK ? f.Value : null;
        }
    }

    private void OkButton_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CancelBtn_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }
}

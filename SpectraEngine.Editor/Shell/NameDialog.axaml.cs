using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.IO;
using System.Linq;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Asks for one name: a new map, a new project, a "save as".
/// </summary>
// Not a save-file picker: a map is a folder, and platform save dialogs name files.
// Do not add a parameterless InitializeComponent here. It shadows the generated
// one and leaves every x:Name field null.
public partial class NameDialog : Window
{
    public NameDialog()
    {
        InitializeComponent();
        Opened += (_, _) => Input.Focus();
        Opened += (_, _) => DarkCaption.Apply(this);
    }

    /// <summary>
    /// Shows the dialog and returns the trimmed name, or null when cancelled.
    /// </summary>
    public static System.Threading.Tasks.Task<string?> AskAsync(
        Window owner, string title, string prompt, string suggested)
    {
        var dialog = new NameDialog { Title = title };
        dialog.PromptText.Text = prompt;
        dialog.Input.Text = suggested;
        dialog.Input.SelectAll();
        return dialog.ShowDialog<string?>(owner);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(null); e.Handled = true; }
    }

    private void OnAccept(object? sender, RoutedEventArgs e) => Accept();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void Accept()
    {
        string name = (Input.Text ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            Reject("A name is needed.");
            return;
        }

        // A separator would create the bundle in a folder the user never chose.
        if (name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            Reject("A name cannot contain a path separator.");
            return;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        if (name.Any(invalid.Contains))
        {
            Reject("That name contains characters a file name cannot carry.");
            return;
        }

        Close(name);
    }

    private void Reject(string reason)
    {
        ErrorText.Text = reason;
        ErrorText.IsVisible = true;
        Input.Focus();
    }
}

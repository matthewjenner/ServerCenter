using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = $"ServerCenter {AppVersion.Current}";
    }

    // Copy the string in the button's Tag to the clipboard. Clipboard access is a view concern, so it
    // lives here rather than in a view-model. Reusable for any "copy this text" button.
    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text } && !string.IsNullOrEmpty(text) && Clipboard is not null)
        {
            await Clipboard.SetTextAsync(text);
        }
    }
}

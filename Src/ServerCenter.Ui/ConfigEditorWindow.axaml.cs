using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ServerCenter.Ui.ViewModels;

namespace ServerCenter.Ui;

// The raw config-file editor modal. Its DataContext is a ConfigEditorViewModel built for the selected
// server instance; it loads that instance's config file list when opened.
public partial class ConfigEditorWindow : Window
{
    public ConfigEditorWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is ConfigEditorViewModel vm)
        {
            await vm.LoadFilesAsync();
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}

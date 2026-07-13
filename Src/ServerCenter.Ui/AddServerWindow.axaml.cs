using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ServerCenter.Ui.ViewModels;

namespace ServerCenter.Ui;

// The add-server modal. Its DataContext is the shared ServersViewModel (same form fields the tab used),
// so Create posts through the existing command; the window closes itself on the ServerCreated event.
public partial class AddServerWindow : Window
{
    public AddServerWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnWindowClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is ServersViewModel vm)
        {
            vm.ServerCreated += OnServerCreated;
            await vm.LoadGamesAsync();   // ensure the picker is populated even before a refresh
        }
    }

    private void OnServerCreated() => Close();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (DataContext is ServersViewModel vm)
        {
            vm.ServerCreated -= OnServerCreated;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}

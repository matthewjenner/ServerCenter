using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ServerCenter.Ui.Services;
using ServerCenter.Ui.ViewModels;

namespace ServerCenter.Ui;

public partial class MainWindow : Window
{
    // Ignore restored sizes smaller than this (a corrupt/absurd value shouldn't shrink the window away).
    private const double MinRestoreSize = 400;

    private ConnectionSettings? _settings;
    private UiWindowState? _pending;
    private readonly DispatcherTimer _lastSeenTimer;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"ServerCenter {AppVersion.Current}";
        Opened += OnOpened;
        Closing += OnClosing;

        // Tick every second so each card's "last seen" keeps counting even when no snapshot arrives
        // (e.g. the controller/host is down). The view drives this; the VM does the clock math.
        _lastSeenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lastSeenTimer.Tick += (_, _) => (DataContext as MainWindowViewModel)?.Fleet.RefreshLastSeen();
        _lastSeenTimer.Start();
    }

    // Called by App before the window is shown: restore the saved size now (no flicker) and stash the
    // rest to apply once the window is open (position needs a live screen list to validate against).
    public void AttachSettings(ConnectionSettings settings)
    {
        _settings = settings;
        _pending = settings.LoadWindow();

        if (_pending.Width is > MinRestoreSize)
        {
            Width = _pending.Width.Value;
        }

        if (_pending.Height is > MinRestoreSize)
        {
            Height = _pending.Height.Value;
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_pending is null)
        {
            return;
        }

        // Only restore a saved position if it actually lands on a connected screen. This defends
        // against a monitor being unplugged AND against Avalonia reporting an off-screen position
        // (e.g. -1 / -32000) when the app was closed while minimized - either way we don't lose the
        // window off-screen; it just opens at the OS default location.
        if (_pending.X is not null && _pending.Y is not null)
        {
            PixelPoint position = new PixelPoint((int)_pending.X.Value, (int)_pending.Y.Value);
            if (IsOnAScreen(position))
            {
                Position = position;
            }
        }

        if (_pending.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        if (Tabs is not null && _pending.SelectedTab >= 0 && _pending.SelectedTab < Tabs.ItemCount)
        {
            Tabs.SelectedIndex = _pending.SelectedTab;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }

        UiWindowState previous = _settings.LoadWindow();
        int selectedTab = Tabs?.SelectedIndex ?? previous.SelectedTab;

        // While minimized, Avalonia reports bogus geometry (position -1 / -32000, off-screen). Don't
        // capture it - keep the last good geometry and only update the selected tab.
        if (WindowState == WindowState.Minimized)
        {
            _settings.SaveWindow(previous with { SelectedTab = selectedTab });
            return;
        }

        // Maximized: keep the last NORMAL size/position to un-maximize into, just record the flag.
        bool maximized = WindowState == WindowState.Maximized;
        double width = maximized ? previous.Width ?? Width : Width;
        double height = maximized ? previous.Height ?? Height : Height;
        double x = maximized ? previous.X ?? Position.X : Position.X;
        double y = maximized ? previous.Y ?? Position.Y : Position.Y;

        // Final belt-and-braces: if the position we're about to save isn't on any screen, fall back
        // to the previous good one rather than persisting something that would restore off-screen.
        if (!IsOnAScreen(new PixelPoint((int)x, (int)y)) && previous.X is not null && previous.Y is not null)
        {
            x = previous.X.Value;
            y = previous.Y.Value;
        }

        _settings.SaveWindow(new UiWindowState(width, height, x, y, maximized, selectedTab));
    }

    private bool IsOnAScreen(PixelPoint point)
    {
        try
        {
            foreach (Screen screen in Screens.All)
            {
                if (screen.Bounds.Contains(point))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Screens unavailable - treat as "don't restore" so we never land off-screen.
        }

        return false;
    }

    // Open the add-server modal, bound to the shared ServersViewModel.
    private void AddServer_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel main)
        {
            _ = new AddServerWindow { DataContext = main.Servers, Icon = Icon }.ShowDialog(this);
        }
    }

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    // Settings is the 3rd tab (Fleet, Servers, Settings, Jobs).
    private void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (Tabs is not null)
        {
            Tabs.SelectedIndex = 2;
        }
    }

    private async void Docs_Click(object? sender, RoutedEventArgs e)
    {
        if (Launcher is not null)
        {
            await Launcher.LaunchUriAsync(new Uri("https://github.com/matthewjenner/ServerCenter"));
        }
    }

    // Force an immediate update check (no restart needed) and report the result.
    private async void CheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel main)
        {
            return;
        }

        string? available = await main.Update.CheckNowAsync();
        string message = available is null
            ? $"You are on the latest version ({AppVersion.Current})."
            : $"Version {available} is available." +
              (main.Update.CanInstall ? " Use the banner at the top to install it." : " Install the packaged app to auto-update.");
        await ShowInfoAsync("Check for Updates", message);
    }

    private async void About_Click(object? sender, RoutedEventArgs e) => await ShowInfoAsync(
        "About ServerCenter",
        $"ServerCenter {AppVersion.Current}\n\n" +
        "A self-hosted control plane for a home virtualization rack.\n" +
        "https://github.com/matthewjenner/ServerCenter");

    // A minimal modal info dialog (About / update result), built in code to avoid a second XAML file.
    private Task ShowInfoAsync(string title, string message)
    {
        Button ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
        Window dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = Icon,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    ok
                }
            }
        };
        ok.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog(this);
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

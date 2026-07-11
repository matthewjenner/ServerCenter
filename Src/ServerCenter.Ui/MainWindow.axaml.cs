using Avalonia.Controls;
using ServerCenter.Ui.Services;

namespace ServerCenter.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = $"ServerCenter {AppVersion.Current}";
    }
}

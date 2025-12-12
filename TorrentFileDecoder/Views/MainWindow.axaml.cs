using Avalonia.Controls;
using Avalonia.Interactivity;
using TorrentFileDecoder.ViewModels;

namespace TorrentFileDecoder.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        var vt = new ViewTorrent()
        {
            DataContext = new ViewTorrentViewModel()
        };
        vt.Show();
    }
}
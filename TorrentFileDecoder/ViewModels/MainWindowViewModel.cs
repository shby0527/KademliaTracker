using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using TorrentFileDecoder.Views;

namespace TorrentFileDecoder.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [RelayCommand]
    private async Task OpenViewTorrent(Window parent, CancellationToken token = default)
    {
        ViewTorrent torrent = new()
        {
            DataContext = new ViewTorrentViewModel()
        };

        await torrent.ShowDialog(parent);
    }


    [RelayCommand]
    private async Task OpenCommandWindows(Window parent, CancellationToken token = default)
    {
        Window dialog = new NetworkTorrentControl()
        {
            DataContext = new NetworkTorrentControlViewModel()
        };
        await dialog.ShowDialog<int>(parent);
    }

    [RelayCommand]
    private async Task OpenOtherWindows(Window parent, CancellationToken token = default)
    {
        MessageBoxWindows dialog = new()
        {
            DataContext = new MessageBoxViewModel
            {
                Title = "提示",
                Message = "暂时没有实现"
            }
        };
        await dialog.ShowDialog<int>(parent);
    }
}
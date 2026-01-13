using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using TorrentFileDecoder.Views;

namespace TorrentFileDecoder.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public required Window View { get; init; }

    [RelayCommand]
    private async Task OpenViewTorrent(CancellationToken token = default)
    {
        ViewTorrent torrent = new()
        {
            DataContext = new ViewTorrentViewModel()
        };

        await torrent.ShowDialog(View);
    }


    [RelayCommand]
    private async Task OpenCommandWindows(CancellationToken token = default)
    {
        Window dialog = new NetworkTorrentControl();
        dialog.DataContext = new NetworkTorrentControlViewModel()
        {
            View = dialog
        };
        await dialog.ShowDialog<int>(View);
    }

    [RelayCommand]
    private async Task OpenOtherWindows(CancellationToken token = default)
    {
        MessageBoxWindows dialog = new()
        {
            DataContext = new MessageBoxViewModel
            {
                Title = "提示",
                Message = "暂时没有实现"
            }
        };
        await dialog.ShowDialog<int>(View);
    }
}
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TorrentFileDecoder.ViewModels;

public partial class NetworkTorrentControlViewModel : ViewModelBase
{
    [ObservableProperty] private string _address = string.Empty;

    [ObservableProperty] private int _port;

    [ObservableProperty] private bool _isConnected;

    [ObservableProperty] private bool _isConnecting;

    [ObservableProperty] private string _connectionStatus = "未连接";


    [RelayCommand]
    private void OnConnect()
    {
        IsConnecting = true;
    }
}
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentFileDecoder.Models;

namespace TorrentFileDecoder.ViewModels;

public partial class NetworkTorrentControlViewModel : ViewModelBase
{
    [ObservableProperty] private ServerEndpointModule _serverEndpointModule = new();

    [ObservableProperty] private bool _isConnected;

    [ObservableProperty] private bool _isConnecting;

    [ObservableProperty] private bool _requireAuthentication;

    [ObservableProperty] private string _connectionStatus = "未连接";

    [ObservableProperty] private UserInfoModule _userInfoModule = new();


    [RelayCommand]
    private void OnConnect()
    {
        IsConnecting = true;
    }
}
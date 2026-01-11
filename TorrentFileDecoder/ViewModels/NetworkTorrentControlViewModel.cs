using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentFileDecoder.Models;
using TorrentFileDecoder.Protocol;
using TorrentFileDecoder.Views;

namespace TorrentFileDecoder.ViewModels;

public partial class NetworkTorrentControlViewModel : ViewModelBase
{
    [ObservableProperty] private ServerEndpointModule _serverEndpointModule = new();

    [ObservableProperty] private bool _isConnected;

    [ObservableProperty] private bool _isConnecting;

    [ObservableProperty] private bool _requireAuthentication;

    [ObservableProperty] private string _connectionStatus = "未连接";

    [ObservableProperty] private UserInfoModule _userInfoModule = new();


    private TorrentProtocol? _protocol;


    [RelayCommand]
    private async Task OnConnect(Window parent)
    {
        if (IsConnected || IsConnecting) return;
        if (!ServerEndpointModule.TryGetAddress(out var address))
        {
            MessageBoxWindows win = new()
            {
                DataContext = new MessageBoxViewModel()
                {
                    Title = "Error",
                    Message = "无法识别的IP格式"
                }
            };
            await win.ShowDialog(parent);
            return;
        }

        _protocol = new TorrentProtocol(address, ServerEndpointModule.Port);
        await _protocol.ConnectAsync();
    }

    [RelayCommand]
    private void OnAuthentication()
    {
    }


    public void OnWindowClosed(object? sender, EventArgs e)
    {
        _protocol?.Dispose();
    }
}
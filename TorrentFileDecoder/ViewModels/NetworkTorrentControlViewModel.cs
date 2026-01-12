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

    [ObservableProperty] private UserInfoModule _userInfoModule = new();


    public string ConnectionStatus
    {
        get
        {
            if (IsConnecting) return "正在连接";
            if (IsConnected) return "已连接";
            return "未连接";
        }
    }

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ConnectionStatus));
    }

    partial void OnIsConnectingChanged(bool value)
    {
        OnPropertyChanged(nameof(ConnectionStatus));
    }

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

        IsConnecting = true;
        _protocol = new TorrentProtocol(address, ServerEndpointModule.Port);
        _protocol.HandshakeComplete += ProtocolOnHandshakeComplete;
        _protocol.Closed += ProtocolOnClosed;
        await _protocol.ConnectAsync();
        IsConnecting = false;
    }

    private void ProtocolOnClosed(object? sender, EventArgs e)
    {
        if (sender is TorrentProtocol protocol)
        {
            protocol.Dispose();
            _protocol = null;
            IsConnected = false;
        }
    }

    private void ProtocolOnHandshakeComplete(object? sender, HandshakeCompleteEventArg args)
    {
        if (!args.Success)
        {
            MessageBoxWindows win = new()
            {
                DataContext = new MessageBoxViewModel()
                {
                    Title = "Error",
                    Message = args.Message ?? "错误"
                }
            };
            win.Show();
            if (sender is TorrentProtocol protocol) protocol.Dispose();
            _protocol = null;
            return;
        }

        IsConnected = true;
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
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
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

    [ObservableProperty] private bool _isAuthentication;

    [ObservableProperty] private UserInfoModule _userInfoModule = new();

    private TorrentProtocol? _protocol;

    public required Window View { get; init; }

    private Window? _authWindow = null;


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


    [RelayCommand]
    private async Task OnConnect(CancellationToken token = default)
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
                },
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            await win.ShowDialog(View);
            return;
        }

        IsConnecting = true;
        _protocol = new TorrentProtocol(address, ServerEndpointModule.Port);
        _protocol.HandshakeComplete += ProtocolOnHandshakeComplete;
        _protocol.AuthenticationComplete += ProtocolOnAuthenticationComplete;
        _protocol.Closed += ProtocolOnClosed;
        await _protocol.ConnectAsync(token);
        IsConnecting = false;
    }

    private void ProtocolOnAuthenticationComplete(object? sender, AuthenticationCompleteEventArg e)
    {
        IsAuthentication = e.Success;
        if (!e.Success)
        {
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var msgBox = new MessageBoxWindows()
                {
                    DataContext = new MessageBoxViewModel()
                    {
                        Title = "Error",
                        Message = e.Message ?? ""
                    },
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                await msgBox.ShowDialog(View);
            });
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(() => _authWindow?.Close(true));
        }
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
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var msgBox = new MessageBoxWindows()
                {
                    DataContext = new MessageBoxViewModel()
                    {
                        Title = "错误",
                        Message = "握手失败"
                    }
                };
                await msgBox.ShowDialog(View);
            });
            if (sender is TorrentProtocol protocol) protocol.Dispose();
            _protocol = null;
            return;
        }

        IsConnected = true;
        if (!args.RequireAuthentication)
        {
            IsAuthentication = true;
            return;
        }

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var auth = new Authentication()
            {
                DataContext = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (!await auth.ShowDialog<bool>(View))
            {
                _protocol?.Dispose();
                _protocol = null;
                _authWindow = null;
            }
        });
    }

    [RelayCommand]
    private async Task OnAuthentication(Window window, CancellationToken token = default)
    {
        if (_protocol is not null)
        {
            _authWindow = window;
            await _protocol.SystemAuthenticateAsync(UserInfoModule.Username, UserInfoModule.Password, token);
            return;
        }

        var msgBox = new MessageBoxWindows()
        {
            DataContext = new MessageBoxViewModel()
            {
                Title = "错误",
                Message = "没有可用连接"
            }
        };
        await msgBox.ShowDialog(View);
    }

    [RelayCommand]
    private void OnCancel(Window window)
    {
        window.Close(false);
    }


    [RelayCommand]
    private void OnWindowClosed()
    {
        _protocol?.Dispose();
    }
}
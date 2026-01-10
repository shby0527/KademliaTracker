using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TorrentFileDecoder.Models;

public partial class ServerEndpointModule : ObservableObject
{
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private int _port;
}
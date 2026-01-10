using CommunityToolkit.Mvvm.ComponentModel;

namespace TorrentFileDecoder.Models;

public partial class UserInfoModule : ObservableObject
{
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;

    [ObservableProperty] private bool _authenticated = false;
}
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TorrentFileDecoder.ViewModels;

public partial class MessageBoxViewModel : ViewModelBase
{
    [ObservableProperty] private string _message = "test";

    [ObservableProperty] private string _title = "Title";


    [RelayCommand]
    private void OkButtonClick(Window window)
    {
        window.Close(1);
    }

    [RelayCommand]
    private void CancelButtonClick(Window window)
    {
        window.Close(0);
    }
}
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TorrentFileDecoder.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{

    protected ViewModelBase()
    {
        ErrorMessages  = [];
    }
    
    [ObservableProperty] 
    private ObservableCollection<string> _errorMessages;
}
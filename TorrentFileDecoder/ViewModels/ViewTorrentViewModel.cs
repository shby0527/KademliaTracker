using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TorrentFileDecoder.Views;
using Umi.Dht.Client.TorrentIO.Utils;

namespace TorrentFileDecoder.ViewModels;

public partial class ViewTorrentViewModel : ViewModelBase
{
    [ObservableProperty] private string _torrentPath = "";

    [ObservableProperty] private ObservableCollection<string> _filesList = [];

    [ObservableProperty] private string _torrentContent = "";

    [ObservableProperty] private string _selectedItem = "";


    [RelayCommand]
    private async Task FileSelector(CancellationToken token)
    {
        ErrorMessages.Clear();
        FilesList.Clear();
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                throw new NullReferenceException("Missing StorageProvider instance.");

            var window = desktop.Windows.FirstOrDefault(e => e is ViewTorrent);
            if (window is null) throw new NullReferenceException("Missing StorageProvider instance.");
            var folder = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                Title = "请选择 Torrent 所在目录",
                AllowMultiple = false
            });
            if (folder.Count > 0)
            {
                var storageFolder = folder[0];
                TorrentPath = storageFolder.Path.AbsolutePath;
                var items = storageFolder.GetItemsAsync();
                await foreach (var file in items)
                {
                    FilesList.Add(file.Name);
                }
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add(e.Message);
        }
    }

    partial void OnSelectedItemChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var file = Path.Combine(TorrentPath, value);
        using var s = File.OpenRead(file);
        Memory<byte> content = new byte[s.Length];
        _ = s.Read(content.Span);
        var d = TorrentFileDecode.Decode(content.Span);
        TorrentContent = d.ToString();
    }
}
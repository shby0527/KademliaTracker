using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TorrentFileDecoder.ViewModels;

namespace TorrentFileDecoder.Views;

public partial class NetworkTorrentControl : Window
{
    public NetworkTorrentControl()
    {
        InitializeComponent();
    }

    private void TopLevel_OnClosed(object? sender, EventArgs e)
    {
        if (this.DataContext is NetworkTorrentControlViewModel viewModel)
        {
            viewModel.WindowClosedCommand.Execute(null);
        }
    }
}
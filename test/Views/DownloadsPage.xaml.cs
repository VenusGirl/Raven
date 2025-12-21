using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using test.Contracts.Services;
using test.Models;
using test.Services;
using test.ViewModels;
using System.IO;

namespace test.Views;

public sealed partial class DownloadsPage : Page
{
    public DownloadsViewModel ViewModel { get; }
    private readonly INavigationService _navigationService;

    public DownloadsPage()
    {
        ViewModel = App.GetService<DownloadsViewModel>();
        _navigationService = App.GetService<INavigationService>();
        InitializeComponent();
    }

    private void DownloadsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DownloadItem item)
        {
            // Pass the DownloadItem - AppPage will handle fetching product if needed
            _navigationService.NavigateTo(typeof(AppViewModel).FullName!, item);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var downloadsPath = Path.Combine(AppContext.BaseDirectory, "downloads");
        Directory.CreateDirectory(downloadsPath);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = downloadsPath,
            UseShellExecute = true
        };

        System.Diagnostics.Process.Start(psi);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string productId)
        {
            DownloadManagerService.Instance.CancelDownload(productId);
        }
    }

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string productId)
        {
            DownloadManagerService.Instance.RemoveDownload(productId);
        }
    }
}

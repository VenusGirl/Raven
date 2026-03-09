using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StoreListings.Library;
using test.Contracts.Services;
using test.Helpers;
using test.Models;
using test.Services;
using test.ViewModels;

namespace test.Views;

public sealed partial class UpdatesPage : Page
{
    public UpdatesViewModel ViewModel { get; }

    private readonly INavigationService _navigationService;
    private DownloadItemStatusAnimator? _animator;
    private readonly HashSet<string> _subscribedProductIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DownloadItem> _mirroredDownloadItems = new(StringComparer.OrdinalIgnoreCase);

    public UpdatesPage()
    {
        ViewModel = App.GetService<UpdatesViewModel>();
        ViewModel.DispatcherQueue = this.DispatcherQueue;
        _navigationService = App.GetService<INavigationService>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DownloadManagerService.Instance.BeginObserving();

        _animator ??= new DownloadItemStatusAnimator(this.DispatcherQueue);

        foreach (var updateItem in ViewModel.AvailableUpdates)
        {
            SubscribeToUpdateItemIfNeeded(updateItem);

            if (updateItem.Status is DownloadStatus.Pending or DownloadStatus.Downloading or DownloadStatus.Installing or DownloadStatus.Cancelling)
            {
                var downloadItem = FindDownloadItem(updateItem.ProductId);
                if (downloadItem != null)
                {
                    _mirroredDownloadItems[updateItem.ProductId] = downloadItem;
                    RestartAnimation(downloadItem, updateItem.Status.Value);
                }
            }
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        DownloadManagerService.Instance.EndObserving();

        if (_animator != null)
        {
            foreach (var dl in _mirroredDownloadItems.Values)
                _animator.Stop(dl);
        }

        foreach (var updateItem in ViewModel.AvailableUpdates)
            updateItem.PropertyChanged -= OnUpdateItemPropertyChanged;

        _subscribedProductIds.Clear();
        _mirroredDownloadItems.Clear();
    }

    private void SubscribeToUpdateItemIfNeeded(UpdateItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ProductId))
            return;

        if (_subscribedProductIds.Contains(item.ProductId))
            return;

        item.PropertyChanged -= OnUpdateItemPropertyChanged;
        item.PropertyChanged += OnUpdateItemPropertyChanged;
        _subscribedProductIds.Add(item.ProductId);
    }

    private void OnUpdateItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not UpdateItem item)
            return;

        if (e.PropertyName is nameof(UpdateItem.Status))
        {
            if (item.Status is DownloadStatus.Pending or DownloadStatus.Downloading or DownloadStatus.Installing or DownloadStatus.Cancelling)
            {
                var downloadItem = FindOrCacheDownloadItem(item.ProductId);
                if (downloadItem != null)
                    RestartAnimation(downloadItem, item.Status.Value);
            }
            else
            {
                if (_mirroredDownloadItems.TryGetValue(item.ProductId, out var dl))
                    _animator?.Stop(dl);
            }
        }
    }

    private void RestartAnimation(DownloadItem downloadItem, DownloadStatus status)
    {
        if (_animator == null)
            return;

        var fallback = status switch
        {
            DownloadStatus.Pending => "Fetching download URLs",
            DownloadStatus.Downloading => "Downloading",
            DownloadStatus.Installing => "Installing",
            DownloadStatus.Cancelling => "Cancelling",
            _ => "Pending",
        };

        downloadItem.StatusTextOverride = null;
        _animator.Start(downloadItem, fallback);
    }

    private DownloadItem? FindDownloadItem(string productId)
    {
        return DownloadManagerService.Instance.Downloads.FirstOrDefault(d =>
            string.Equals(d.ProductId, productId, StringComparison.OrdinalIgnoreCase));
    }

    private DownloadItem? FindOrCacheDownloadItem(string productId)
    {
        if (_mirroredDownloadItems.TryGetValue(productId, out var cached))
            return cached;

        var dl = FindDownloadItem(productId);
        if (dl != null)
            _mirroredDownloadItems[productId] = dl;

        return dl;
    }

    private void ItemCheckBox_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is UpdateItem item)
            item.IsSelected = cb.IsChecked == true;
    }

    private void UpdateItem_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not UpdateItem item)
            return;

        var navItem = new DownloadItem
        {
            ProductId = item.ProductId,
            InstallerType = InstallerType.Packaged,
            LogoUrl = item.LogoUrl,
        };
        _navigationService.NavigateTo(typeof(AppViewModel).FullName!, navItem);
    }

    private void StopCheckButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelCheck();
    }

    private void CancelUpdateButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string productId })
            DownloadManagerService.Instance.CancelDownload(productId);
    }

    private void ActionButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.CheckForUpdatesOrUpdateCommand.Execute(null);
    }

    private void SelectAll_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.ToggleSelectAllCommand.Execute(null);
    }
}


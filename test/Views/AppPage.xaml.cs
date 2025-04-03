using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StoreListings.Library;
using test.Helpers;
using test.Models;
using test.ViewModels;

namespace test.Views;

public sealed partial class AppPage : Page
{
    public AppViewModel ViewModel { get; }

    public AppPage()
    {
        ViewModel = App.GetService<AppViewModel>();
        InitializeComponent();
    }

    public AppInfo AppData { get; set; } = new();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is StoreEdgeFDProduct ProductInfo)
        {
            LoadData(ProductInfo);
        }
    }

    private void LoadData(StoreEdgeFDProduct ProductInfo)
    {
        {
            DisplayItem.Visibility = Visibility.Collapsed;
            LoadingOverlay.Visibility = Visibility.Visible;

            AppData.SetValues(
                ProductInfo.Logo,
                ProductInfo.Screenshots,
                ProductInfo.RevisionId,
                ProductInfo.Title,
                ProductInfo.PublisherName,
                ProductInfo.Description,
                ProductInfo.Rating,
                ProductInfo.RatingCount,
                ProductInfo.Size
            );

            LoadingOverlay.Visibility = Visibility.Collapsed;
            DisplayItem.Visibility = Visibility.Visible;
        }
    }
}

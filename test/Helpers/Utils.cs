using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StoreListings.Library;
using test.Models;
using test.Views;

namespace test.Helpers;

class Utils
{
    public static async Task<Product> ProductOrBundle(string productId)
    {
        var deviceFamily = DeviceFamily.Desktop;
        var market = Market.US;
        var language = Lang.en;

        Result<StoreEdgeFDProduct> productresult = await StoreEdgeFDProduct.GetProductAsync(
            productId,
            deviceFamily,
            market,
            language
        );

        if (productresult.IsSuccess)
        {
            if (productresult.Value.IsBundle)
            {
                Result<StoreEdgeFDQuery> bundlesResult = await StoreEdgeFDQuery.GetBundles(
                    productId,
                    deviceFamily,
                    market,
                    language
                );
                if (bundlesResult.IsSuccess)
                {
                    return new Product(productresult.Value, bundlesResult.Value);
                }
                else
                {
                    throw bundlesResult.Exception;
                }
            }
            else
            {
                return new Product(productresult.Value, null);
            }
        }
        else
        {
            throw productresult.Exception;
        }
    }

    public static int GetComboBoxItemIndexByTag(ComboBox comboBox, object tag)
    {
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item && item.Tag.Equals(tag))
            {
                return i;
            }
        }
        return 0;
    }

    public static void HandleCardTapped(
        FrameworkElement? sender,
        Frame navigationFrame,
        UIElement displayItem,
        UIElement errorIcon,
        UIElement loadingOverlay,
        TextBlock errorIconText
    )
    {
        if (sender is FrameworkElement element && element.Tag is string productId)
        {
            // Show loading indicator

            NavigateToProductOrBundle(
                productId,
                navigationFrame,
                displayItem,
                errorIcon,
                loadingOverlay,
                errorIconText
            );
        }
        else
        {
            Debug.WriteLine("Failed to get ProductId for navigation");
        }
    }

    public static async void NavigateToProductOrBundle(
        string productId,
        Frame navigationFrame,
        UIElement displayItem,
        UIElement errorIcon,
        UIElement loadingOverlay,
        TextBlock errorIconText
    )
    {
        displayItem.Visibility = Visibility.Collapsed;
        errorIcon.Visibility = Visibility.Collapsed;
        loadingOverlay.Visibility = Visibility.Visible;
        try
        {
            // Await the API call directly on the UI thread.
            var product = await Utils.ProductOrBundle(productId);

            // Update UI and navigate directly (since async/await yields control properly)
            loadingOverlay.Visibility = Visibility.Collapsed;

            if (product.ProductInfo.IsBundle)
            {
                // Navigate to BundlesPage with both product and bundle.
                navigationFrame.Navigate(
                    typeof(BundlesPage),
                    (product.ProductInfo, product.BundleInfo)
                );
            }
            else
            {
                // Navigate to AppPage with just the product.
                navigationFrame.Navigate(typeof(AppPage), product.ProductInfo);
            }
        }
        catch (Exception ex)
        {
            loadingOverlay.Visibility = Visibility.Collapsed;
            errorIcon.Visibility = Visibility.Visible;
            errorIconText.Text = ex.Message;
            Debug.WriteLine($"Failed to load product: {ex.Message}");
        }
    }

    public static void CreateOrUpdateSpringAnimation(
        ref SpringVector3NaturalMotionAnimation? springAnimation,
        Compositor compositor,
        float finalValue
    )
    {
        if (springAnimation == null)
        {
            springAnimation = compositor.CreateSpringVector3Animation();
            springAnimation.Target = "Scale";
        }

        springAnimation.FinalValue = new Vector3(finalValue);
    }

    public static void HandlePointerEntered(
        object sender,
        PointerRoutedEventArgs e,
        ref SpringVector3NaturalMotionAnimation? springAnimation,
        Compositor compositor
    )
    {
        // Scale up to 1.025
        CreateOrUpdateSpringAnimation(ref springAnimation, compositor, 1.025f);
        if (sender is FrameworkElement element)
        {
            element.CenterPoint = new Vector3(
                (float)(element.ActualWidth / 2.0),
                (float)(element.ActualHeight / 2.0),
                1f
            );
            element.StartAnimation(springAnimation);
        }
    }

    public static void HandlePointerExited(
        object sender,
        PointerRoutedEventArgs e,
        ref SpringVector3NaturalMotionAnimation? springAnimation,
        Compositor compositor
    )
    {
        // Scale back down to 1.0
        CreateOrUpdateSpringAnimation(ref springAnimation, compositor, 1.0f);
        if (sender is FrameworkElement element)
        {
            element.CenterPoint = new Vector3(
                (float)(element.ActualWidth / 2.0),
                (float)(element.ActualHeight / 2.0),
                1f
            );
            element.StartAnimation(springAnimation);
        }
    }

    internal static void HandlePointerEntered(
        object sender,
        PointerRoutedEventArgs e,
        ref object springAnimation,
        object compositor
    ) => throw new NotImplementedException();
}

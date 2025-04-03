using StoreListings.Library;

namespace test.Models;

public class Product
{
    public StoreEdgeFDProduct ProductInfo { get; set; }
    public StoreEdgeFDQuery? BundleInfo { get; set; }
    public bool IsBundle => BundleInfo != null;

    public Product(StoreEdgeFDProduct product, StoreEdgeFDQuery? bundle)
    {
        ProductInfo = product;
        BundleInfo = bundle;
    }
}

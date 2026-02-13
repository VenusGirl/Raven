using StoreListings.Library;

namespace test.Models;

public class Product
{
    public ProductData ProductInfo { get; set; }
    public StoreEdgeFDQuery? BundleInfo { get; set; }
    public bool IsBundle => BundleInfo != null;

    public Product(ProductData product, StoreEdgeFDQuery? bundle)
    {
        ProductInfo = product;
        BundleInfo = bundle;
    }
}

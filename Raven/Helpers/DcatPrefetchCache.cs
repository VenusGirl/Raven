using StoreListings.Library;

namespace Raven.Helpers;

/// <summary>
/// Single-entry, TTL-gated cache of the last DCAT package list fetched when a product page
/// was opened. Consumed only by the install path (<see cref="GetDownloadUrl"/>), so clicking
/// Install/Update seconds after opening an AppPage skips re-downloading the exact catalog
/// response that just rendered the page. Deliberately NOT consulted by version checks, which
/// keep fresh-fetch semantics, and never attached to ProductData (which would pin it on the
/// DownloadManagerService singleton).
/// </summary>
internal static class DcatPrefetchCache
{
    private static readonly object _gate = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static string? _productId;
    private static Market _market;
    private static Lang _language;
    private static DateTimeOffset _fetchedAt;
    private static IReadOnlyList<DCATPackage>? _packages;

    public static void Store(string productId, Market market, Lang language, IReadOnlyList<DCATPackage> packages)
    {
        lock (_gate)
        {
            _productId = productId;
            _market = market;
            _language = language;
            _fetchedAt = DateTimeOffset.UtcNow;
            _packages = packages;
        }
    }

    public static IReadOnlyList<DCATPackage>? TryGet(string productId, Market market, Lang language)
    {
        lock (_gate)
        {
            if (_packages is not null
                && string.Equals(_productId, productId, StringComparison.OrdinalIgnoreCase)
                && _market == market
                && _language == language
                && DateTimeOffset.UtcNow - _fetchedAt < Ttl)
            {
                return _packages;
            }

            // Expired or mismatched: release the pinned graph now rather than holding the
            // last-viewed product's catalog metadata until the next product page overwrites it.
            _packages = null;
            _productId = null;
            return null;
        }
    }
}

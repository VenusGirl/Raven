using System.Threading.Tasks;
using StoreListings.Library;
using test.Services;

namespace test.Services
{
    public class StoreService : IStoreService
    {
        public async Task<SearchResult> SearchProducts(
            string query,
            MediaTypeSearch mediaType,
            PriceType priceType,
            int skip,
            int take = 25
        )
        {
            try
            {
                var result = await StoreEdgeFDQuery.GetSearchProduct(
                    query,
                    DeviceFamily.Desktop,
                    Market.US,
                    Lang.en,
                    skip,
                    mediaType
                );

                return new SearchResult
                {
                    IsSuccess = result.IsSuccess,
                    Cards = result.Value?.Cards.ToArray() ?? new Card[0],
                    HasMoreItems = result.Value.Cards.ToArray().Length == 0 ? false : true,
                    ErrorMessage = result.Exception?.Message,
                };
            }
            catch (System.Exception ex)
            {
                return new SearchResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }
    }
}

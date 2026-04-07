using CoinBank.Services._PreSale.DTOs.Results;
using CoinBank.Services._PreSale.DTOs.Updates;
using Utilities.DTOs;

namespace CoinBank.Services._PreSale
{
    public interface IPreSaleService
    {

        //admin
        Task<PreSaleResult> CreatePreSaleTokenAsync(CreatePreSaleTokenUpdate update);


        //global
        Task<PreSaleListResult> GetAllPreSaleTokensAsync(Pagination pagination); 
        Task<PreSaleResult> GetOnePreSaleTokenAsync(GetOnePreSaleTokenUpdate update);

        //internal
        Task SyncExpirePreSaleTokenAsync();
        Task SyncCompletedPreSalesAsync();

    }
}

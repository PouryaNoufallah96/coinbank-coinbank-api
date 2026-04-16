using CoinBank.Domain.Collections;
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
        Task<PreSaleListResult> GetAllPreSaleTokensForUserAsync(Pagination pagination, string publicKey, string evmWalletAddress); 
        Task<PreSaleResult> GetOnePreSaleTokenAsync(GetOnePreSaleTokenUpdate update);
        Task<List<PreSaleUserStatResult>> GetUserPreSaleStatsAsync(string publicKey, string evmWalletAddress);
        //internal
        Task InitializePreSaleStorageAsync();
        Task SyncExpirePreSaleTokenAsync();
        Task SyncPreSaleToStorageAsync(string preSaleReference);
        Task<PreSale> GetPreSaleDataByReferenceForInternalUsageAsync(string preSaleReference);
    }
}

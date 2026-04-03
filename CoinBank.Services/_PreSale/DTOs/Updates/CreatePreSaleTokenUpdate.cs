using CoinBank.Domain.Collections;
using Utilities.Attributes;

namespace CoinBank.Services._PreSale.DTOs.Updates
{
    public class CreatePreSaleTokenUpdate
    {
        [StringInputValidation(maxLength: 100, minLength: 2)] public string Name { get; set; }
        [StringInputValidation(maxLength: 100, minLength: 2)] public string Symbol { get; set; }
        [StringInputValidation(maxLength: 300, minLength: 2)] public string LogoUrl { get; set; }
        [StringInputValidation(isRequired: false, maxLength: 1000)] public string Description { get; set; }

        [NumericInputValidation(isRequired:true,mustBeNonZero:true)] public decimal TotalSupply { get; set; }
        [NumericInputValidation(isRequired: true, mustBeNonZero: true)] public decimal MaxPerOrder { get; set; }
        [NumericInputValidation(isRequired: true, mustBeNonZero: true)] public decimal MinPerOrder { get; set; }
        [NumericInputValidation(isRequired: true, mustBeNonZero: true)] public decimal Price { get; set; }

        public DateTime StartSellingAt { get; set; }
        public DateTime EndSellingAt { get; set; }
        public List<PreSaleReleaseStep> ReleaseSchedule { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utilities.Attributes;

namespace CoinBank.Services._PreSale.DTOs.Updates
{
    public class GetOnePreSaleTokenUpdate
    {
       [StringInputValidation] public string PreSaleReference { get; set; } 
    }
}

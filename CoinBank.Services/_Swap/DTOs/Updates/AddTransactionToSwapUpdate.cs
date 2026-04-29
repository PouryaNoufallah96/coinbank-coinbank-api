using CoinBank.Domain.Collections;
using System.Numerics;

namespace CoinBank.Services._Swap.DTOs.Updates
{
    public class AddTransactionToSwapUpdate
    {
        public string SwapReference { get; set; }
        public string Hash { get; set; }
        public string Network { get; set; }
        public string TokenAddress { get; set; }
        public BigInteger Amount { get; set; }
        public SwapTransactionType Type { get; set; }
    }
}

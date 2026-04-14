using CoinBank.Utilities.Exceptions;
using Utilities.Enums;
using Utilities.Exceptions.Common;


namespace Utilities.Exceptions
{
    public class InsufficientBalanceException : BadRequestException
    {
        public InsufficientBalanceException()
          : base(ApiResultStatusCode.InsufficientBalance, ExceptionMessages.NonceNotFoundException)
        {
        }
    }
}

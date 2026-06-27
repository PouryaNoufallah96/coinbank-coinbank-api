using Microsoft.AspNetCore.Mvc.Filters;
using Utilities.Enums;
using Utilities.Utilities;
using Utilities.Extension;
using Utilities.Exceptions;

namespace Utilities.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class AuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string[] _claims;
        public bool RequireActiveUser { get; set; } = false;
        public bool RequireActiveWithBEP20Wallet { get; set; } = false;
        public bool JustBSC { get; set; } = false;
        public bool AllowAdmin { get; set; } = false;
        public AuthorizeAttribute()
        {
        }

        public AuthorizeAttribute(params string[] claims)
        {
            _claims = claims;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var jwtSecurityToken = context.HttpContext.GetToken();

            if (jwtSecurityToken == null)
                throw new AuthorizationException("Authorization error");

            if (_claims != null && !_claims.Any(c => jwtSecurityToken.HasClaim(Claims.Permission.ToDisplay(), c)))
                throw new AuthorizationException( "Access denied");

            if (!AllowAdmin && (_claims == null || _claims.Length == 0))
            {
                var userTypeClaim = jwtSecurityToken.Claims
                    .FirstOrDefault(c => c.Type == Claims.UserType.ToDisplay());

                if (userTypeClaim != null && userTypeClaim.Value == UserType.Admin.ToDisplay())
                    throw new AuthorizationException("This endpoint is not available for admin accounts");
            }

            if (RequireActiveUser || RequireActiveWithBEP20Wallet)
            {
                var statusClaim = jwtSecurityToken.Claims
                    .FirstOrDefault(c => c.Type == Claims.UserStatus.ToDisplay());

                if (statusClaim == null || statusClaim.Value != "Active")
                    throw new AuthorizationException("User is not active");
            }

            if (RequireActiveWithBEP20Wallet || JustBSC)
            {
                var networkClaim = jwtSecurityToken.Claims
                    .FirstOrDefault(c => c.Type == Claims.NetworkType.ToDisplay());

                if (networkClaim == null || networkClaim.Value.ToString() != "BEP20")
                    throw new AuthorizationException("BSC wallet is required");
            }

           
        }
    }
}

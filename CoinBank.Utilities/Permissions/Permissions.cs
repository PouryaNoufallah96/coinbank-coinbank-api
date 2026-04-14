namespace Utilities.Permissions
{
    public static class Permissions
    {
        // User Permissions
        public const string CreateUser = "U1A#";
        public const string EditUser = "U2B!";
        public const string GetAllUsers = "U3C@";
        public const string ArchiveUser = "U4D$";
        public const string BanUser = "U5E%";
        public const string DeleteUser = "U6F^";

        // PreSale Permissions
        public const string PreSaleManage = "PS6F^";

        public static readonly List<PermissionMeta> PermissionsList =
        [
            // User Permissions
            new PermissionMeta(CreateUser, nameof(CreateUser), "Create new user", ["admin"]),
            new PermissionMeta(EditUser, nameof(EditUser), "Edit existing user", ["admin"]),
            new PermissionMeta(GetAllUsers, nameof(GetAllUsers), "View all users", ["admin"]),
            new PermissionMeta(ArchiveUser, nameof(ArchiveUser), "Archive user account", ["admin"]),
            new PermissionMeta(BanUser, nameof(BanUser), "Ban user account", ["admin"]),
            new PermissionMeta(DeleteUser, nameof(DeleteUser), "Delete user", ["admin"]),

            // PreSale Permissions
            new PermissionMeta(PreSaleManage, nameof(PreSaleManage), "Manage pre sale tokens", ["admin"]),
        ];

        public static readonly IEnumerable<string> AllRoles = ["admin"];
    }

    public record PermissionMeta(
        string Code,
        string Title,
        string Description,
        IEnumerable<string> Roles
    );
}

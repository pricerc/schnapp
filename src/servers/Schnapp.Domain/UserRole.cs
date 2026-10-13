namespace Schnapp.Domain;

/// <summary>
/// Well-known values for <see cref="Entities.User.Role"/>.
/// </summary>
public static class UserRole
{
    /// <summary>Ordinary org member: own data only; may CRUD shared lists.</summary>
    public const string User = "User";

    /// <summary>Administrator: can provision users and manage org settings.</summary>
    public const string Admin = "Admin";
}

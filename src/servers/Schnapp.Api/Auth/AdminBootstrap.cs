using Schnapp.Data.Repositories;
using Schnapp.Domain;
using Schnapp.Domain.Entities;

namespace Schnapp.Api.Auth;

/// <summary>
/// First-run bootstrap that creates the initial admin account when the database is empty.
/// </summary>
/// <remarks>
/// <para>
/// Schnapp does not allow public self-registration. The first administrator must be created
/// out-of-band so someone can log in and provision colleagues via
/// <c>POST /api/v1/admin/users</c>.
/// </para>
/// <para>
/// Called once from API startup in <c>Program.cs</c>, after DbUp migrations and before
/// HTTP traffic is accepted. If credentials are missing or users already exist, this is a no-op.
/// </para>
/// <list type="table">
/// <listheader>
/// <term>Config key</term>
/// <description>Purpose</description>
/// </listheader>
/// <item>
/// <term><c>SCHNAPP_ADMIN_EMAIL</c> / <c>Schnapp:AdminEmail</c></term>
/// <description>Email for the seed admin (unique login identity).</description>
/// </item>
/// <item>
/// <term><c>SCHNAPP_ADMIN_PASSWORD</c> / <c>Schnapp:AdminPassword</c></term>
/// <description>Password for the seed admin; hashed with <see cref="PasswordService"/> before insert.</description>
/// </item>
/// </list>
/// </remarks>
public static class AdminBootstrap
{
    /// <summary>
    /// Seeds a single admin user when the database has zero users and bootstrap credentials are configured.
    /// </summary>
    /// <param name="services">
    /// Root <see cref="IServiceProvider"/> from the host; a scope is created to resolve
    /// <see cref="UserRepository"/> and <see cref="PasswordService"/>.
    /// </param>
    /// <param name="config">
    /// Application configuration used to read admin email/password (config keys and env vars).
    /// </param>
    /// <param name="logger">
    /// Logger for the one-time "Seeded admin user" information message.
    /// </param>
    /// <returns>A task that completes when bootstrap has finished (whether or not a user was created).</returns>
    /// <remarks>
    /// <para><b>Why this exists:</b> empty DBs need a way to get the first admin without SQL.</para>
    /// <para><b>When it runs:</b> API process startup only; not on every request.</para>
    /// <para><b>Safety:</b> never overwrites an existing admin; if <c>CountAsync() &gt; 0</c>, returns immediately
    /// so production restarts with env vars set cannot create a second bootstrap user.</para>
    /// </remarks>
    public static async Task EnsureAdminAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var email = config["SCHNAPP_ADMIN_EMAIL"]
            ?? config["Schnapp:AdminEmail"]
            ?? Environment.GetEnvironmentVariable("SCHNAPP_ADMIN_EMAIL");
        var password = config["SCHNAPP_ADMIN_PASSWORD"]
            ?? config["Schnapp:AdminPassword"]
            ?? Environment.GetEnvironmentVariable("SCHNAPP_ADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

        if (await users.CountAsync() > 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = IdFactory.NewId(),
            Email = email.Trim(),
            Code = "admin",
            Name = "Admin",
            Role = UserRole.Admin,
            TimeZoneId = "UTC",
            Status = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwords.HashPasswordOrEmpty(user, password);
        await users.InsertAsync(user);
        logger.LogInformation("Seeded admin user {Email}", user.Email);
    }
}

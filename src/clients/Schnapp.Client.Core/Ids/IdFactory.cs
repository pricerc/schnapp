namespace Schnapp.Client.Core.Ids;

using Medo;

/// <summary>
/// Client-side id generation (native <see cref="Uuid7"/>), aligned with server Domain.
/// </summary>
public static class IdFactory
{
    /// <summary>Creates a new time-ordered unique id.</summary>
    public static Uuid7 NewId() => Uuid7.NewUuid7();
}

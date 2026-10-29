namespace Schnapp.Domain;

using Medo;

/// <summary>
/// Application-side id generation for new entity rows (native <see cref="Uuid7"/>).
/// </summary>
/// <remarks>
/// Uses <see cref="Uuid7.NewUuid7"/> with a monotonic counter. Ids sort by creation time
/// in RFC text form and as PostgreSQL <c>uuid</c> when mapped via <c>Uuid7TypeHandler</c>
/// (network byte order). Never use <see cref="WellKnownIds.None"/> for normal entities.
/// </remarks>
public static class IdFactory
{
    /// <summary>
    /// Creates a new time-ordered unique id for a normal (non-sentinel) entity.
    /// </summary>
    public static Uuid7 NewId() => Uuid7.NewUuid7();
}

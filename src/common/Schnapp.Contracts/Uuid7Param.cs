namespace Schnapp.Contracts;

using System;

using Medo;

/// <summary>
/// Route or query bindable Id26 (or legacy RFC) text that converts to <see cref="Uuid7"/>.
/// Use for minimal-API and MVC route parameters so Id26 binds correctly
/// (<c>Medo.Uuid7.TryParse</c> only accepts RFC UUID layout).
/// </summary>
public readonly struct Uuid7Param : IEquatable<Uuid7Param>
{
    /// <summary>Creates a param wrapper around an existing id.</summary>
    public Uuid7Param(Uuid7 value) => Value = value;

    /// <summary>Underlying UUIDv7 value.</summary>
    public Uuid7 Value { get; }

    public static implicit operator Uuid7(Uuid7Param p) => p.Value;

    public static implicit operator Uuid7Param(Uuid7 value) => new(value);

    /// <summary>Formats as Id26 text.</summary>
    public override string ToString() => Uuid7Text.Format(Value);

    public bool Equals(Uuid7Param other) => Value.Equals(other.Value);

    public override bool Equals(object? obj) => obj is Uuid7Param other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(Uuid7Param left, Uuid7Param right) => left.Equals(right);

    public static bool operator !=(Uuid7Param left, Uuid7Param right) => !left.Equals(right);

    public static bool operator ==(Uuid7Param left, Uuid7 right) => left.Value.Equals(right);

    public static bool operator !=(Uuid7Param left, Uuid7 right) => !left.Value.Equals(right);

    public static bool operator ==(Uuid7 left, Uuid7Param right) => left.Equals(right.Value);

    public static bool operator !=(Uuid7 left, Uuid7Param right) => !left.Equals(right.Value);

    /// <summary>
    /// Attempts to parse Id26 or legacy RFC UUID text into a <see cref="Uuid7Param"/>.
    /// </summary>
    public static bool TryParse(string? s, out Uuid7Param result)
    {
        if (Uuid7Text.TryParse(s, out var id))
        {
            result = new Uuid7Param(id);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Parses Id26 or legacy RFC UUID text into a <see cref="Uuid7Param"/>.
    /// </summary>
    /// <exception cref="FormatException">When the text is not a valid Uuid7 form.</exception>
    public static Uuid7Param Parse(string s) => new(Uuid7Text.Parse(s));
}

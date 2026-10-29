namespace Schnapp.Domain.Entities;

using Medo;

/// <summary>
/// Tag dictionary entry suggested when editing free-text <c>tags</c> fields.
/// </summary>
/// <remarks>
/// Rows are collated from free-text writes and optional explicit creates.
/// <see cref="CustomerId"/> null means org-global; set associates the tag with one customer
/// (autocomplete for that customer shows global + their tags).
/// Uniqueness is (TagValue) for global rows and (TagValue, CustomerId) per customer.
/// </remarks>
public sealed class Tag
{
    /// <summary>Canonical tag string.</summary>
    public string TagValue { get; set; } = "";

    public string? Notes { get; set; }

    /// <summary>Optional customer id when the tag is associated with one customer; null = global.</summary>
    public Uuid7? CustomerId { get; set; }
}

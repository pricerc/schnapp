namespace Schnapp.Data.Blobs;

/// <summary>
/// Stores and retrieves screenshot image bytes (filesystem today; S3/MinIO later).
/// </summary>
public interface IImageBlobStore
{
    /// <summary>Writes bytes to the given storage key.</summary>
    Task PutAsync(string key, Stream data, string contentType, CancellationToken ct = default);

    /// <summary>Opens a read stream for the key, or null if missing.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);

    /// <summary>Returns size and last-write time for the key (for ETag/Last-Modified), or null if missing.</summary>
    Task<BlobStat?> StatAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes the object if it exists.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>Blob size and last-write time (UTC).</summary>
public sealed record BlobStat(long Length, DateTimeOffset LastModifiedUtc);

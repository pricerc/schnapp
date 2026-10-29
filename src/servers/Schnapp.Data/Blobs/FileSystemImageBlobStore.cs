namespace Schnapp.Data.Blobs;

using Microsoft.Extensions.Logging;

/// <summary>
/// Filesystem implementation of <see cref="IImageBlobStore"/> under a configured root directory.
/// </summary>
/// <remarks>
/// Keys are relative paths; path traversal is rejected. Called from screenshot upload/delete paths.
/// </remarks>
public sealed class FileSystemImageBlobStore : IImageBlobStore
{
    private readonly string _root;
    private readonly ILogger<FileSystemImageBlobStore> _logger;

    /// <summary>
    /// Creates a store rooted at <paramref name="rootDirectory"/> (created if missing).
    /// </summary>
    /// <param name="rootDirectory">Absolute or relative blob root path.</param>
    /// <param name="logger">Logger for blob store diagnostics.</param>
    public FileSystemImageBlobStore(string rootDirectory, ILogger<FileSystemImageBlobStore> logger)
    {
        _logger = logger;
        _root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_root);
        _logger.LogInformation("FileSystemImageBlobStore initialized with root {Root}", _root);
    }

    /// <inheritdoc />
    public async Task PutAsync(string key, Stream data, string contentType, CancellationToken ct = default)
    {
        var path = Resolve(key);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        _logger.LogDebug("Writing blob {Key} to {Path} (contentType={ContentType})", key, path, contentType);
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await data.CopyToAsync(fs, ct);
        _logger.LogInformation("Wrote blob {Key} to {Path} ({Bytes} bytes)", key, path, fs.Length);
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);
        if (!File.Exists(path))
        {
            _logger.LogWarning("Blob not found for key {Key} at path {Path}", key, path);
            return Task.FromResult<Stream?>(null);
        }
        _logger.LogDebug("Opening blob {Key} at {Path}", key, path);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    /// <inheritdoc />
    public Task<BlobStat?> StatAsync(string key, CancellationToken ct = default)
    {
        var info = new FileInfo(Resolve(key));
        return Task.FromResult<BlobStat?>(
            info.Exists ? new BlobStat(info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)) : null);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Deleted blob {Key} at {Path}", key, path);
        }
        else
        {
            _logger.LogDebug("Delete requested for missing blob {Key} at path {Path}", key, path);
        }
        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains("..", StringComparison.Ordinal))
        {
            _logger.LogWarning("Rejected invalid storage key {Key}", key);
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root, StringComparison.Ordinal))
        {
            _logger.LogWarning("Rejected storage key {Key} that escapes root {Root} (resolved {Full})", key, _root, full);
            throw new ArgumentException("Storage key escapes root.", nameof(key));
        }
        return full;
    }
}

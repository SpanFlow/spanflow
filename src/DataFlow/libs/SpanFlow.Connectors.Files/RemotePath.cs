namespace SpanFlow.Connectors.Files;

/// <summary>
/// Path handling shared by the remote stores (sftp/ftp). Remote paths always use forward slashes.
/// <c>..</c> segments are rejected outright: the request path is user-templated, and unlike the
/// local store there is no filesystem root to resolve escapes against — refusing traversal keeps a
/// templated value from wandering out of the connection's configured area.
/// </summary>
public static class RemotePath
{
    /// <summary>Normalizes the request path and prefixes the optional configured root.</summary>
    public static string Combine(string? root, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("file connector request requires a 'path'.");
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new InvalidOperationException($"path '{path}' must not contain '..' segments.");
        }

        if (string.IsNullOrWhiteSpace(root)) return normalized;

        var prefix = root.Replace('\\', '/').TrimEnd('/');
        return $"{prefix}/{normalized.TrimStart('/')}";
    }

    /// <summary>The directory part of a remote path, or "" for a top-level file.</summary>
    public static string Parent(string path)
    {
        var index = path.LastIndexOf('/');
        return index <= 0 ? string.Empty : path[..index];
    }

    /// <summary>A sibling temp name used by the write-then-rename replace.</summary>
    public static string TempFor(string path) => $"{path}.{Guid.NewGuid():n}.tmp";
}

/// <summary>
/// Wraps a data stream so that disposing it also tears down the owning connection (sftp/ftp client).
/// The connector's formats read/write the stream and dispose it; the session must live exactly as
/// long as the stream does.
/// </summary>
public sealed class OwnedStream(Stream inner, params IDisposable[] owners) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => inner.WriteAsync(buffer, ct);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            foreach (var owner in owners) owner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        foreach (var owner in owners) owner.Dispose();
        await base.DisposeAsync();
    }
}

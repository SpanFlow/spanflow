using System.Text.Json.Nodes;

using SpanFlow.Connectors;

namespace SpanFlow.Connectors.Files;

/// <summary>How a deliver opens its target file.</summary>
public enum FileWriteMode
{
    /// <summary>Fail if the file already exists.</summary>
    Create,
    /// <summary>Replace the file's contents.</summary>
    Overwrite,
    /// <summary>Append to the file, creating it when absent.</summary>
    Append,
}

/// <summary>
/// A place files live — local disk today, later ftp/sftp/s3/azureBlob (E04-T4/T5). A store is
/// built from a connection's config and exposes streaming read/write over paths *relative to the
/// store root*; the format layer (<see cref="IFileFormat"/>) parses/emits the bytes. Keeping the
/// store byte-oriented means one format works across every store.
/// </summary>
public interface IFileStore
{
    Task<bool> ExistsAsync(string path, CancellationToken ct);

    /// <summary>Opens the file for reading. Throws if it does not exist.</summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);

    /// <summary>Opens a writable stream per <paramref name="mode"/> (parent folders created as needed).</summary>
    Task<Stream> OpenWriteAsync(string path, FileWriteMode mode, CancellationToken ct);

    /// <summary>
    /// Writes the file's whole new contents and swaps it into place, atomically where the store can
    /// (local: temp file + rename) so a reader never sees a half-written file. Used by the
    /// read-modify-write <c>update</c> deliver mode.
    /// </summary>
    Task ReplaceAsync(string path, Func<Stream, Task> writeContent, CancellationToken ct);

    /// <summary>Cheap reachability/validity check (e.g. the local root exists).</summary>
    Task<ConnectionCheck> ValidateAsync(CancellationToken ct);

    /// <summary>
    /// Lists one directory level (files + subdirectories) at <paramref name="path"/> ("" = the
    /// store root), for the designer's file browser. Entry paths are store-root-relative so the
    /// UI can drill down or use them directly in a request.
    /// </summary>
    Task<IReadOnlyList<FileEntryDto>> ListAsync(string path, CancellationToken ct);
}

/// <summary>Path helpers shared by the stores' <c>ListAsync</c> implementations.</summary>
public static class FileEntryPaths
{
    /// <summary>The store-root-relative path of a child of the browsed directory.</summary>
    public static string Child(string browsedPath, string name) =>
        string.IsNullOrWhiteSpace(browsedPath) || browsedPath == "/"
            ? name
            : $"{browsedPath.Replace('\\', '/').TrimEnd('/')}/{name}";
}

/// <summary>Builds an <see cref="IFileStore"/> for one store kind from a connection's config.</summary>
public interface IFileStoreProvider
{
    /// <summary>The <c>store</c> discriminator this provider handles ("local", "sftp", …).</summary>
    string Store { get; }

    IFileStore Create(JsonObject config);
}

/// <summary>DI-backed lookup of a file store by its <c>store</c> config field (defaults to "local").</summary>
public class FileStoreRegistry(IEnumerable<IFileStoreProvider> providers)
{
    private readonly Dictionary<string, IFileStoreProvider> _providers =
        providers.ToDictionary(p => p.Store, StringComparer.OrdinalIgnoreCase);

    public IFileStore Create(JsonObject config)
    {
        var store = config["store"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(store)) store = "local";
        return _providers.TryGetValue(store, out var provider)
            ? provider.Create(config)
            : throw new InvalidOperationException($"No file store registered for '{store}'.");
    }
}

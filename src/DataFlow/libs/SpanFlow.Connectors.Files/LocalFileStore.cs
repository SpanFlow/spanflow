using System.Text.Json.Nodes;

using SpanFlow.Connectors;

namespace SpanFlow.Connectors.Files;

/// <summary>
/// Builds a <see cref="LocalFileStore"/> from <c>{ "store":"local", "rootPath":"C:/data" }</c>, after
/// <see cref="LocalFileRoots.CheckOnOpen"/> has agreed to the root (E50-T6).
/// </summary>
public class LocalFileStoreProvider(LocalFileRoots roots) : IFileStoreProvider
{
    public string Store => "local";

    /// <summary>No root rule at all, for tests that point a store at a temp folder. Never registered by the API.</summary>
    public static LocalFileStoreProvider Unrestricted() => new(new LocalFileRoots(new LocalFileOptions { Unrestricted = true }));

    public IFileStore Create(JsonObject config)
    {
        var rootPath = config["rootPath"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("local file store config requires a 'rootPath'.");
        }
        return new LocalFileStore(roots.CheckOnOpen(rootPath));
    }
}

/// <summary>
/// Reads/writes files on local disk. Every request path is resolved *under* the configured root
/// and rejected if it escapes it (via <c>..</c>, an absolute path, a different drive, or a symbolic link
/// inside the root that points out of it), so a pipeline can only touch files inside the folder the
/// connection was scoped to.
/// </summary>
public sealed class LocalFileStore(string rootPath) : IFileStore
{
    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

    public Task<bool> ExistsAsync(string path, CancellationToken ct) =>
        Task.FromResult(File.Exists(Resolve(path)));

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        var full = Resolve(path);
        if (!File.Exists(full)) throw new FileNotFoundException($"file not found: {path}");
        return Task.FromResult<Stream>(new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public Task<Stream> OpenWriteAsync(string path, FileWriteMode mode, CancellationToken ct)
    {
        var full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var fileMode = mode switch
        {
            FileWriteMode.Create => FileMode.CreateNew,
            FileWriteMode.Overwrite => FileMode.Create,
            FileWriteMode.Append => FileMode.Append,
            _ => FileMode.Create,
        };
        return Task.FromResult<Stream>(new FileStream(full, fileMode, FileAccess.Write, FileShare.None));
    }

    public async Task ReplaceAsync(string path, Func<Stream, Task> writeContent, CancellationToken ct)
    {
        var full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        // Temp lives in the same directory (same volume) so the move is an atomic rename.
        var temp = full + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await writeContent(stream);
            }
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp);
            throw;
        }
    }

    public Task<ConnectionCheck> ValidateAsync(CancellationToken ct) =>
        Task.FromResult(Directory.Exists(_root)
            ? new ConnectionCheck(true)
            : new ConnectionCheck(false, $"root path does not exist: {_root}"));

    public Task<IReadOnlyList<FileEntryDto>> ListAsync(string path, CancellationToken ct)
    {
        var full = string.IsNullOrWhiteSpace(path) || path == "/" ? _root : Resolve(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"directory not found: {path}");

        var entries = new List<FileEntryDto>();
        foreach (var dir in Directory.EnumerateDirectories(full).Order())
        {
            var name = Path.GetFileName(dir);
            entries.Add(new FileEntryDto { Name = name, Path = FileEntryPaths.Child(path, name), IsDirectory = true });
        }
        foreach (var file in Directory.EnumerateFiles(full).Order())
        {
            var info = new FileInfo(file);
            entries.Add(new FileEntryDto
            {
                Name = info.Name,
                Path = FileEntryPaths.Child(path, info.Name),
                IsDirectory = false,
                Size = info.Length,
                ModifiedAt = info.LastWriteTimeUtc,
            });
        }
        return Task.FromResult<IReadOnlyList<FileEntryDto>>(entries);
    }

    /// <summary>Resolves a request path under the root, rejecting anything that escapes it.</summary>
    internal string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("file connector request requires a 'path'.");
        }

        // Combine ignores the root when `path` is rooted; the prefix check below still catches escapes.
        var full = Path.GetFullPath(Path.Combine(_root, path));
        var isInside = full == _root || full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (!isInside)
        {
            throw new InvalidOperationException($"path '{path}' escapes the store root.");
        }

        // A link inside the root (root/data → /etc) passes the text check above. Judge where the bytes are (E50-T6).
        if (!LocalFileRoots.IsInside(LocalFileRoots.RealPath(full), LocalFileRoots.RealPath(_root)))
        {
            throw new InvalidOperationException($"path '{path}' leads out of the store root through a symbolic link.");
        }
        return full;
    }
}

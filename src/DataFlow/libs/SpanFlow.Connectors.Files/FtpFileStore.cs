using System.Text.Json.Nodes;
using FluentFTP;

using SpanFlow.Connectors;

namespace SpanFlow.Connectors.Files;

/// <summary>Builds an <see cref="FtpFileStore"/> from
/// <c>{ "store":"ftp", "host","port"?,"username","password","tls"?,"rootPath"? }</c>.</summary>
public class FtpFileStoreProvider : IFileStoreProvider
{
    public string Store => "ftp";

    public IFileStore Create(JsonObject config) => new FtpFileStore(FtpStoreConfig.Parse(config));
}

internal sealed record FtpStoreConfig(
    string Host, int Port, string Username, string Password, bool Tls, string? RootPath)
{
    public static FtpStoreConfig Parse(JsonObject config)
    {
        var host = config["host"]?.GetValue<string>();
        var username = config["username"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("ftp store config requires a 'host'.");
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("ftp store config requires a 'username'.");

        return new FtpStoreConfig(
            host,
            config["port"]?.GetValue<int>() ?? 21,
            username,
            config["password"]?.GetValue<string>() ?? string.Empty,
            config["tls"]?.GetValue<bool>() ?? false,
            config["rootPath"]?.GetValue<string>());
    }
}

/// <summary>
/// FTP store via FluentFTP. Each operation opens its own session; a returned stream owns its
/// session and closes it on dispose. FTP has no atomic rename-over: Replace writes a sibling temp
/// file and moves it over the target (delete + rename server-side) — documented as NON-atomic, a
/// reader can glimpse a missing file during the swap. Certificates are validated normally; with
/// <c>tls</c> on, explicit FTPS is used.
/// </summary>
internal sealed class FtpFileStore(FtpStoreConfig config) : IFileStore
{
    private async Task<AsyncFtpClient> ConnectAsync(CancellationToken ct)
    {
        var client = new AsyncFtpClient(config.Host, config.Username, config.Password, config.Port);
        if (config.Tls)
        {
            client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
        }
        await client.Connect(ct);
        return client;
    }

    private string Resolve(string path) => RemotePath.Combine(config.RootPath, path);

    public async Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        using var client = await ConnectAsync(ct);
        return await client.FileExists(Resolve(path), ct);
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        var client = await ConnectAsync(ct);
        try
        {
            var full = Resolve(path);
            if (!await client.FileExists(full, ct))
            {
                throw new FileNotFoundException($"file not found: {path}");
            }
            return new OwnedStream(await client.OpenRead(full, FtpDataType.Binary, token: ct), client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<Stream> OpenWriteAsync(string path, FileWriteMode mode, CancellationToken ct)
    {
        var client = await ConnectAsync(ct);
        try
        {
            var full = Resolve(path);
            if (mode == FileWriteMode.Create && await client.FileExists(full, ct))
            {
                throw new InvalidOperationException($"file '{path}' already exists — use writeMode 'overwrite' or 'append'.");
            }
            await EnsureParentAsync(client, full, ct);
            var stream = mode == FileWriteMode.Append
                ? await client.OpenAppend(full, FtpDataType.Binary, checkIfFileExists: false, token: ct)
                : await client.OpenWrite(full, FtpDataType.Binary, checkIfFileExists: false, token: ct);
            return new OwnedStream(stream, client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task ReplaceAsync(string path, Func<Stream, Task> writeContent, CancellationToken ct)
    {
        using var client = await ConnectAsync(ct);
        var full = Resolve(path);
        var temp = RemotePath.TempFor(full);
        try
        {
            await EnsureParentAsync(client, full, ct);
            await using (var stream = await client.OpenWrite(temp, FtpDataType.Binary, checkIfFileExists: false, token: ct))
            {
                await writeContent(stream);
            }
            await client.MoveFile(temp, full, FtpRemoteExists.Overwrite, ct);
        }
        catch
        {
            if (await client.FileExists(temp, ct)) await client.DeleteFile(temp, ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<FileEntryDto>> ListAsync(string path, CancellationToken ct)
    {
        using var client = await ConnectAsync(ct);
        // Null lists the login directory — where relative request paths actually resolve.
        var full = string.IsNullOrWhiteSpace(path)
            ? (string.IsNullOrWhiteSpace(config.RootPath) ? null : config.RootPath)
            : Resolve(path);

        var listing = await client.GetListing(full, ct);
        return listing
            .Where(item => item.Type is FtpObjectType.File or FtpObjectType.Directory)
            .OrderByDescending(item => item.Type == FtpObjectType.Directory).ThenBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => new FileEntryDto
            {
                Name = item.Name,
                Path = FileEntryPaths.Child(path, item.Name),
                IsDirectory = item.Type == FtpObjectType.Directory,
                Size = item.Type == FtpObjectType.Directory ? null : item.Size,
                ModifiedAt = item.Modified == DateTime.MinValue ? null : item.Modified,
            })
            .ToList();
    }

    private static async Task EnsureParentAsync(AsyncFtpClient client, string fullPath, CancellationToken ct)
    {
        var parent = RemotePath.Parent(fullPath);
        if (parent.Length > 0)
        {
            await client.CreateDirectory(parent, ct); // recursive; no-op when it already exists
        }
    }

    public async Task<ConnectionCheck> ValidateAsync(CancellationToken ct)
    {
        try
        {
            using var client = await ConnectAsync(ct);
            await client.GetListing(string.IsNullOrWhiteSpace(config.RootPath) ? "/" : config.RootPath, ct);
            return new ConnectionCheck(true);
        }
        catch (Exception ex)
        {
            return new ConnectionCheck(false, ex.Message);
        }
    }
}

using System.Text.Json.Nodes;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using SpanFlow.Connectors;

namespace SpanFlow.Connectors.Files;

/// <summary>Builds an <see cref="AzureBlobFileStore"/> from
/// <c>{ "store":"azureBlob", "container", "connectionString" | "accountUrl"+"sasToken", "rootPath"? }</c>.</summary>
public class AzureBlobFileStoreProvider : IFileStoreProvider
{
    public string Store => "azureBlob";

    public IFileStore Create(JsonObject config) => new AzureBlobFileStore(AzureBlobStoreConfig.Parse(config));
}

internal sealed record AzureBlobStoreConfig(
    string Container, string? ConnectionString, string? AccountUrl, string? SasToken, string? RootPath)
{
    public static AzureBlobStoreConfig Parse(JsonObject config)
    {
        var container = config["container"]?.GetValue<string>();
        var connectionString = config["connectionString"]?.GetValue<string>();
        var accountUrl = config["accountUrl"]?.GetValue<string>();
        var sasToken = config["sasToken"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(container))
        {
            throw new InvalidOperationException("azureBlob store config requires a 'container'.");
        }
        if (string.IsNullOrWhiteSpace(connectionString) && (string.IsNullOrWhiteSpace(accountUrl) || string.IsNullOrWhiteSpace(sasToken)))
        {
            throw new InvalidOperationException("azureBlob store config requires a 'connectionString' or an 'accountUrl' + 'sasToken'.");
        }

        return new AzureBlobStoreConfig(
            container,
            string.IsNullOrWhiteSpace(connectionString) ? null : connectionString,
            string.IsNullOrWhiteSpace(accountUrl) ? null : accountUrl,
            string.IsNullOrWhiteSpace(sasToken) ? null : sasToken,
            config["rootPath"]?.GetValue<string>());
    }
}

/// <summary>
/// Azure Blob Storage store (block blobs). Like s3, writes buffer in memory and upload on dispose
/// (a block-blob commit replaces the blob atomically — Replace is the same upload); append is
/// emulated by pre-loading the existing blob. The container client is lightweight and connection-
/// less, so no per-operation session ownership is needed.
/// </summary>
internal sealed class AzureBlobFileStore(AzureBlobStoreConfig config) : IFileStore
{
    private BlobContainerClient Container() =>
        config.ConnectionString is not null
            ? new BlobContainerClient(config.ConnectionString, config.Container)
            : new BlobContainerClient(
                new Uri($"{config.AccountUrl!.TrimEnd('/')}/{config.Container}"),
                new AzureSasCredential(config.SasToken!));

    private BlobClient Blob(string path) =>
        Container().GetBlobClient(RemotePath.Combine(config.RootPath, path).TrimStart('/'));

    public async Task<bool> ExistsAsync(string path, CancellationToken ct) =>
        (await Blob(path).ExistsAsync(ct)).Value;

    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        var blob = Blob(path);
        if (!(await blob.ExistsAsync(ct)).Value)
        {
            throw new FileNotFoundException($"file not found: {path}");
        }
        return await blob.OpenReadAsync(cancellationToken: ct);
    }

    public async Task<Stream> OpenWriteAsync(string path, FileWriteMode mode, CancellationToken ct)
    {
        var blob = Blob(path);
        var buffer = new BufferedUploadStream(
            content => blob.UploadAsync(content, overwrite: true, CancellationToken.None));

        if (mode == FileWriteMode.Append && (await blob.ExistsAsync(ct)).Value)
        {
            // Emulated append: start the buffer from the existing blob's bytes.
            await using var existing = await blob.OpenReadAsync(cancellationToken: ct);
            await existing.CopyToAsync(buffer, ct);
        }
        return buffer;
    }

    public async Task ReplaceAsync(string path, Func<Stream, Task> writeContent, CancellationToken ct)
    {
        // A block-blob upload commits atomically — no temp/rename dance needed.
        await using var buffer = await OpenWriteAsync(path, FileWriteMode.Overwrite, ct);
        await writeContent(buffer);
    }

    public async Task<IReadOnlyList<FileEntryDto>> ListAsync(string path, CancellationToken ct)
    {
        var container = Container();
        var basePath = string.IsNullOrWhiteSpace(path)
            ? (config.RootPath ?? string.Empty)
            : RemotePath.Combine(config.RootPath, path);
        var prefix = basePath.Trim('/') is { Length: > 0 } trimmed ? trimmed + "/" : string.Empty;

        var entries = new List<FileEntryDto>();
        await foreach (var item in container.GetBlobsByHierarchyAsync(BlobTraits.None, BlobStates.None, delimiter: "/", prefix: prefix, cancellationToken: ct))
        {
            if (item.IsPrefix)
            {
                var name = item.Prefix[prefix.Length..].TrimEnd('/');
                entries.Add(new FileEntryDto { Name = name, Path = FileEntryPaths.Child(path, name), IsDirectory = true });
            }
            else
            {
                var name = item.Blob.Name[prefix.Length..];
                entries.Add(new FileEntryDto
                {
                    Name = name,
                    Path = FileEntryPaths.Child(path, name),
                    IsDirectory = false,
                    Size = item.Blob.Properties.ContentLength,
                    ModifiedAt = item.Blob.Properties.LastModified?.UtcDateTime,
                });
            }
        }

        return entries
            .OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<ConnectionCheck> ValidateAsync(CancellationToken ct)
    {
        try
        {
            var container = Container();
            return (await container.ExistsAsync(ct)).Value
                ? new ConnectionCheck(true)
                : new ConnectionCheck(false, $"container '{config.Container}' does not exist.");
        }
        catch (Exception ex)
        {
            return new ConnectionCheck(false, ex.Message);
        }
    }
}

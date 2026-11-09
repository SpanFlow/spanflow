using System.Net;
using System.Text.Json.Nodes;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

using SpanFlow.Connectors;

namespace SpanFlow.Connectors.Files;

/// <summary>Builds an <see cref="S3FileStore"/> from
/// <c>{ "store":"s3", "bucket","region"?,"accessKeyId","secretAccessKey","endpoint"?,"rootPath"? }</c>.
/// <c>endpoint</c> points at an S3-compatible service (MinIO etc.) and switches to path-style URLs.</summary>
public class S3FileStoreProvider : IFileStoreProvider
{
    public string Store => "s3";

    public IFileStore Create(JsonObject config) => new S3FileStore(S3StoreConfig.Parse(config));
}

internal sealed record S3StoreConfig(
    string Bucket, string? Region, string AccessKeyId, string SecretAccessKey, string? Endpoint, string? RootPath)
{
    public static S3StoreConfig Parse(JsonObject config)
    {
        var bucket = config["bucket"]?.GetValue<string>();
        var accessKeyId = config["accessKeyId"]?.GetValue<string>();
        var secretAccessKey = config["secretAccessKey"]?.GetValue<string>();
        var region = config["region"]?.GetValue<string>();
        var endpoint = config["endpoint"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(bucket)) throw new InvalidOperationException("s3 store config requires a 'bucket'.");
        if (string.IsNullOrWhiteSpace(accessKeyId)) throw new InvalidOperationException("s3 store config requires an 'accessKeyId'.");
        if (string.IsNullOrWhiteSpace(secretAccessKey)) throw new InvalidOperationException("s3 store config requires a 'secretAccessKey'.");
        if (string.IsNullOrWhiteSpace(region) && string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("s3 store config requires a 'region' (or an 'endpoint' for S3-compatible services).");
        }

        return new S3StoreConfig(bucket, region, accessKeyId, secretAccessKey, endpoint, config["rootPath"]?.GetValue<string>());
    }
}

/// <summary>
/// Amazon S3 (or S3-compatible) object store. Objects have no write handle or append: writes buffer
/// in memory and PUT on dispose (a PUT replaces the object atomically — Replace is the same PUT);
/// append is emulated by pre-loading the existing object into the buffer. One client per operation,
/// owned by the returned stream where one is returned.
/// </summary>
internal sealed class S3FileStore(S3StoreConfig config) : IFileStore
{
    private AmazonS3Client Connect()
    {
        var clientConfig = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(config.Endpoint))
        {
            clientConfig.ServiceURL = config.Endpoint;
            clientConfig.ForcePathStyle = true; // MinIO and friends route by path, not virtual host
            if (!string.IsNullOrWhiteSpace(config.Region)) clientConfig.AuthenticationRegion = config.Region;
        }
        else
        {
            clientConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(config.Region);
        }
        return new AmazonS3Client(config.AccessKeyId, config.SecretAccessKey, clientConfig);
    }

    private string Key(string path) => RemotePath.Combine(config.RootPath, path).TrimStart('/');

    public async Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        using var client = Connect();
        try
        {
            await client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = config.Bucket, Key = Key(path) }, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        var client = Connect();
        try
        {
            var response = await client.GetObjectAsync(config.Bucket, Key(path), ct);
            return new OwnedStream(response.ResponseStream, response, client);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            client.Dispose();
            throw new FileNotFoundException($"file not found: {path}");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<Stream> OpenWriteAsync(string path, FileWriteMode mode, CancellationToken ct)
    {
        var client = Connect();
        try
        {
            var key = Key(path);
            var buffer = new BufferedUploadStream(
                content => client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = config.Bucket,
                    Key = key,
                    InputStream = content,
                    AutoCloseStream = false,
                }, CancellationToken.None), client);

            if (mode == FileWriteMode.Append)
            {
                // Emulated append: start the buffer from the existing object's bytes.
                try
                {
                    using var existing = await client.GetObjectAsync(config.Bucket, key, ct);
                    await existing.ResponseStream.CopyToAsync(buffer, ct);
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound) { /* new object */ }
            }
            return buffer;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task ReplaceAsync(string path, Func<Stream, Task> writeContent, CancellationToken ct)
    {
        // An object PUT replaces atomically — no temp/rename dance needed.
        await using var buffer = await OpenWriteAsync(path, FileWriteMode.Overwrite, ct);
        await writeContent(buffer);
    }

    public async Task<ConnectionCheck> ValidateAsync(CancellationToken ct)
    {
        try
        {
            using var client = Connect();
            await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = config.Bucket, MaxKeys = 1 }, ct);
            return new ConnectionCheck(true);
        }
        catch (Exception ex)
        {
            return new ConnectionCheck(false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<FileEntryDto>> ListAsync(string path, CancellationToken ct)
    {
        using var client = Connect();
        var basePath = string.IsNullOrWhiteSpace(path)
            ? (config.RootPath ?? string.Empty)
            : RemotePath.Combine(config.RootPath, path);
        var prefix = basePath.Trim('/') is { Length: > 0 } trimmed ? trimmed + "/" : string.Empty;

        var entries = new List<FileEntryDto>();
        var request = new ListObjectsV2Request { BucketName = config.Bucket, Prefix = prefix, Delimiter = "/" };
        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request, ct);
            // One level down: common prefixes are the "directories". SDK v4 leaves an empty list null.
            foreach (var common in response.CommonPrefixes ?? [])
            {
                var name = common[prefix.Length..].TrimEnd('/');
                entries.Add(new FileEntryDto { Name = name, Path = FileEntryPaths.Child(path, name), IsDirectory = true });
            }
            foreach (var obj in response.S3Objects ?? [])
            {
                if (obj.Key == prefix) continue; // the directory-placeholder object some tools create
                var name = obj.Key[prefix.Length..];
                entries.Add(new FileEntryDto
                {
                    Name = name,
                    Path = FileEntryPaths.Child(path, name),
                    IsDirectory = false,
                    Size = obj.Size,
                    ModifiedAt = obj.LastModified?.ToUniversalTime(),
                });
            }
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);

        return entries
            .OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }
}

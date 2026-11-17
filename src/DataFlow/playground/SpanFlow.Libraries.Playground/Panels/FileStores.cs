using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SpanFlow.Connectors.Files;

namespace SpanFlow.Libraries.Playground.Panels;

/// <summary>One file store as the page sees it (secrets masked).</summary>
public sealed record StoreView(string Id, string Name, string Store, string Display, string Source);

/// <summary>
/// Self-hosted only: the file stores the Files panel may read from and write to (D71.11): the owner's folder, S3,
/// Azure Blob and FTP, through the library's own stores. A <c>local</c> store may only point inside
/// <see cref="PlaygroundOptions.LocalFileRoots"/> (the library's own root check). Hosted, there are none and this
/// refuses every lookup: the hosted Files panel converts what the visitor pastes and nothing else.
/// </summary>
public sealed class FileStores
{
    private readonly PlaygroundOptions _options;
    private readonly IServiceProvider _services;
    private readonly FileStoreRegistry _registry;

    public FileStores(IOptions<PlaygroundOptions> options, IServiceProvider services, IHostEnvironment env)
    {
        _options = options.Value;
        _services = services;
        var dataRoot = Path.GetDirectoryName(Path.GetFullPath(Path.IsPathRooted(_options.DataFile) ? _options.DataFile : Path.Combine(env.ContentRootPath, _options.DataFile)))!;
        AllowedRoots = _options.LocalFileRoots is { Count: > 0 } roots ? roots.Select(Path.GetFullPath).ToList() : [Path.Combine(dataRoot, "files")];
        var local = new LocalFileOptions { Enabled = !_options.IsHosted, DataRoot = dataRoot };
        local.AllowedRoots.AddRange(AllowedRoots);
        _registry = new FileStoreRegistry([
            new LocalFileStoreProvider(new LocalFileRoots(local)), new S3FileStoreProvider(), new AzureBlobFileStoreProvider(), new FtpFileStoreProvider(),
        ]);
    }

    public static readonly IReadOnlyList<string> Kinds = ["local", "s3", "azureBlob", "ftp"];

    /// <summary>Folders a <c>local</c> store may use.</summary>
    public IReadOnlyList<string> AllowedRoots { get; }

    private SavedData? Saved => _options.IsHosted ? null : _services.GetService<SavedData>();

    public IReadOnlyList<StoreView> List()
    {
        if (_options.IsHosted) return [];
        var list = _options.Stores.Select(s => View(s.Id, s.Name, ToJson(s.Config), "config")).ToList();
        if (Saved is { } saved) list.AddRange(saved.Read(s => s.Stores.Select(x => View(x.Id, x.Name, x.Config, "saved")).ToList()));
        return list;
    }

    public IFileStore Open(string id)
    {
        if (_options.IsHosted) throw PanelRefusal.Invalid("The hosted playground has no file stores: paste or upload a file instead.");
        var config = _options.Stores.FirstOrDefault(s => s.Id == id) is { } configured
            ? ToJson(configured.Config)
            : Saved?.Read(s => s.Stores.FirstOrDefault(x => x.Id == id)?.Config.DeepClone().AsObject())
              ?? throw PanelRefusal.NotFound($"No file store '{id}'.");
        return Create(config);
    }

    /// <summary>Builds a store from its config, or refuses with the library's own reason (a missing bucket, a folder outside the roots).</summary>
    public IFileStore Create(JsonObject config)
    {
        if (_options.IsHosted) throw PanelRefusal.Invalid("The hosted playground has no file stores.");
        var kind = config["store"]?.GetValue<string>() ?? "local";
        if (!Kinds.Contains(kind, StringComparer.OrdinalIgnoreCase)) throw PanelRefusal.Invalid($"\"store\" is one of {string.Join(", ", Kinds)}.");
        try
        {
            return _registry.Create(config);
        }
        catch (InvalidOperationException ex)
        {
            throw PanelRefusal.Invalid(ex.Message);
        }
    }

    private static StoreView View(string id, string name, JsonObject config, string source)
    {
        var kind = config["store"]?.GetValue<string>() ?? "local";
        var display = kind switch
        {
            "local" => config["rootPath"]?.ToString() ?? "",
            "s3" => $"s3://{config["bucket"]}/{config["rootPath"]}".TrimEnd('/'),
            "azureBlob" => $"{config["accountUrl"]?.ToString() ?? "azure"}/{config["container"]}".TrimEnd('/'),
            "ftp" => $"ftp://{config["username"]}@{config["host"]}:{config["port"]?.ToString() ?? "21"}/{config["rootPath"]}".TrimEnd('/'),
            _ => kind,
        };
        return new StoreView(id, name.Length > 0 ? name : id, kind, display, source);
    }

    private static JsonObject ToJson(Dictionary<string, string> config)
    {
        var json = new JsonObject();
        foreach (var (key, value) in config)
        {
            json[key] = bool.TryParse(value, out var b) && key is "tls" ? JsonValue.Create(b)
                : int.TryParse(value, out var i) && key is "port" ? JsonValue.Create(i)
                : JsonValue.Create(value);
        }
        return json;
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace SpanFlow.Libraries.Playground;

/// <summary>A database connection someone saved on their own server (self-hosted).</summary>
public sealed class SavedConnection
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "postgres";
    public string ConnectionString { get; set; } = "";
    public bool AllowWrites { get; set; }
}

/// <summary>A file store someone saved on their own server (self-hosted): the library's store config.</summary>
public sealed class SavedStore
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public JsonObject Config { get; set; } = [];
}

/// <summary>Everything the self-hosted playground keeps: one JSON file on the volume the owner mounts (D71.13).</summary>
public sealed class SavedState
{
    public List<SavedConnection> Connections { get; set; } = [];
    public List<SavedStore> Stores { get; set; } = [];

    /// <summary>Postman-style collections: <c>[{ id, name, requests: [{ id, name, method, url, params, headers, auth, bodyType, body, settings }] }]</c>.</summary>
    public JsonArray Collections { get; set; } = [];

    /// <summary><c>[{ id, name, values: { baseUrl: "...", ... } }]</c>, read by <c>{{name}}</c> in a request.</summary>
    public JsonArray Environments { get; set; } = [];

    /// <summary>Node flows from the canvas: <c>[{ id, name, nodes: [{ id, type, name, x, y, config }], edges: [{ from, to }] }]</c>.</summary>
    public JsonArray Flows { get; set; } = [];
}

/// <summary>
/// Reads and writes <see cref="PlaygroundOptions.DataFile"/>. Self-hosted only: the hosted playground keeps nothing
/// on disk (its collections live in the visitor's browser) and this is never constructed there. Writes go to a temp
/// file first and replace the old one, so a crash never leaves half a file.
/// </summary>
public sealed class SavedData
{
    public const int MaxFileBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly Lock _lock = new();
    private SavedState _state;

    public SavedData(IOptions<PlaygroundOptions> options, IHostEnvironment env)
    {
        _path = Path.GetFullPath(Path.IsPathRooted(options.Value.DataFile) ? options.Value.DataFile : Path.Combine(env.ContentRootPath, options.Value.DataFile));
        _state = Load(_path);
    }

    public string FilePath => _path;

    public T Read<T>(Func<SavedState, T> read)
    {
        lock (_lock) return read(_state);
    }

    public void Change(Action<SavedState> change)
    {
        lock (_lock)
        {
            var copy = JsonSerializer.Deserialize<SavedState>(JsonSerializer.Serialize(_state, Json), Json)!;
            change(copy);
            var text = JsonSerializer.Serialize(copy, Json);
            if (text.Length > MaxFileBytes) throw PanelRefusal.TooLarge($"Saved data would pass {MaxFileBytes / 1024 / 1024} MB; remove something first.");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, text);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, _path, overwrite: true);
            _state = copy;
        }
    }

    private static SavedState Load(string path)
    {
        if (!File.Exists(path)) return new SavedState();
        return JsonSerializer.Deserialize<SavedState>(File.ReadAllText(path), Json) ?? new SavedState();
    }
}

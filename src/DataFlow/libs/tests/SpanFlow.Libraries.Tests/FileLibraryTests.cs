using System.Text.Json.Nodes;

namespace SpanFlow.Libraries.Tests;

/// <summary>SpanFlow.Connectors.Files on its own: the row formats round-trip, and the local store stays in its folder.</summary>
public class FileLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spanflow-files-" + Guid.NewGuid().ToString("n"));

    public FileLibraryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static JsonArray Rows() =>
    [
        new JsonObject { ["id"] = "1", ["name"] = "Ada, Countess", ["note"] = "said \"hi\"" },
        new JsonObject { ["id"] = "2", ["name"] = "Grace", ["note"] = "" },
    ];

    /// <summary>The text writers close the stream they are handed (a store's stream is theirs to finish), so
    /// the bytes are read back from a fresh one.</summary>
    private static async Task<byte[]> Write(IFileFormat format, JsonNode payload)
    {
        var stream = new MemoryStream();
        await format.WriteAsync(stream, payload, new JsonObject(), suppressHeader: false, default);
        return stream.ToArray();
    }

    private static async Task<JsonNode> RoundTrip(IFileFormat format, JsonNode payload)
    {
        using var stream = new MemoryStream(await Write(format, payload));
        return await format.ReadAsync(stream, new JsonObject(), default);
    }

    private static void AssertSameRows(JsonNode expected, JsonNode actual)
    {
        var want = expected.AsArray();
        var got = actual.AsArray();
        Assert.Equal(want.Count, got.Count);
        for (var i = 0; i < want.Count; i++)
        {
            foreach (var (key, value) in want[i]!.AsObject())
            {
                Assert.Equal(value?.ToString() ?? "", got[i]![key]?.ToString() ?? "");
            }
        }
    }

    [Fact]
    public async Task Csv_round_trips_quotes_and_commas()
    {
        AssertSameRows(Rows(), await RoundTrip(new CsvFormat(), Rows()));
    }

    [Fact]
    public async Task Tsv_round_trips()
    {
        AssertSameRows(Rows(), await RoundTrip(new TsvFormat(), Rows()));
    }

    [Fact]
    public async Task Ndjson_round_trips_one_object_per_line()
    {
        AssertSameRows(Rows(), await RoundTrip(new NdjsonFormat(), Rows()));
    }

    [Fact]
    public async Task Xlsx_round_trips()
    {
        AssertSameRows(Rows(), await RoundTrip(new XlsxFormat(), Rows()));
    }

    [Fact]
    public async Task Csv_rows_can_be_streamed_one_at_a_time()
    {
        var format = new CsvFormat();
        Assert.True(((IFileFormat)format).SupportsRowStreaming);

        using var stream = new MemoryStream(await Write(format, Rows()));

        var count = 0;
        await foreach (var row in ((IFileFormat)format).ReadRowsAsync(stream, new JsonObject(), default))
        {
            Assert.NotNull(row["id"]);
            count++;
        }
        Assert.Equal(2, count);
    }

    [Fact]
    public void The_registry_finds_a_format_by_name_and_refuses_an_unknown_one()
    {
        var registry = new FileFormatRegistry([new CsvFormat(), new JsonFormat()]);
        Assert.Equal("csv", registry.Get("CSV").Format);
        Assert.Throws<InvalidOperationException>(() => registry.Get("parquet"));
    }

    [Fact]
    public async Task The_local_store_writes_lists_and_reads_inside_its_root()
    {
        var store = new LocalFileStore(_dir);
        await using (var write = await store.OpenWriteAsync("out/rows.csv", FileWriteMode.Create, default))
        {
            await new CsvFormat().WriteAsync(write, Rows(), new JsonObject(), suppressHeader: false, default);
        }

        Assert.True(await store.ExistsAsync("out/rows.csv", default));
        var entries = await store.ListAsync("out", default);
        Assert.Contains(entries, e => e.Name == "rows.csv" && !e.IsDirectory);

        await using var read = await store.OpenReadAsync("out/rows.csv", default);
        AssertSameRows(Rows(), await new CsvFormat().ReadAsync(read, new JsonObject(), default));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    public async Task The_local_store_refuses_a_path_that_leaves_its_root(string path)
    {
        var store = new LocalFileStore(_dir);
        await Assert.ThrowsAnyAsync<Exception>(() => store.OpenReadAsync(path, default));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_dir)!, "escape.txt")));
    }

    [Fact]
    public void A_root_outside_the_allowed_roots_is_refused()
    {
        var allowed = Path.Combine(_dir, "allowed");
        Directory.CreateDirectory(allowed);
        var roots = new LocalFileRoots(new LocalFileOptions { AllowedRoots = [allowed], DataRoot = Path.Combine(_dir, "data") });

        Assert.Equal(LocalFileRoots.RealPath(allowed), roots.CheckOnOpen(allowed));
        Assert.Throws<LocalFileRootRefusedException>(() => roots.CheckOnOpen(Path.Combine(_dir, "elsewhere")));
        Assert.Throws<LocalFileRootRefusedException>(() => roots.CheckOnOpen("relative/path"));
    }

    [Fact]
    public void Remote_paths_get_the_root_prefix_and_refuse_parent_segments()
    {
        Assert.Equal("base/in/a.csv", RemotePath.Combine("base/", "/in/a.csv"));
        Assert.Equal("in/a.csv", RemotePath.Combine(null, "in\\a.csv"));
        Assert.Throws<InvalidOperationException>(() => RemotePath.Combine("base", "../a.csv"));
        Assert.Equal("in", RemotePath.Parent("in/a.csv"));
    }
}

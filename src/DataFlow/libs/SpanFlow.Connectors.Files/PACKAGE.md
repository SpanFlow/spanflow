# SpanFlow.Connectors.Files

Reads and writes rows in common file formats, and stores files on local disk or in the cloud behind one
interface. It comes from [SpanFlow](https://github.com/SpanFlow/spanflow), where it powers the file connector.

**Formats** (`IFileFormat`): `csv`, `tsv` and other delimited text, `json`, `ndjson`, `xlsx` and binary. Tabular
formats read into a JSON array of row objects and write one back. `csv`, `tsv` and `ndjson` can also stream rows
one at a time without loading the whole file.

**Stores** (`IFileStore`):
- a local folder: `LocalFileStore` refuses any path that leaves its root, and `LocalFileRoots` limits which
  folders may be used at all
- Amazon S3 or any S3-compatible service
- Azure Blob Storage
- FTP/FTPS

```csharp
using System.Text.Json.Nodes;
using SpanFlow.Connectors.Files;

var rows = new JsonArray
{
    new JsonObject { ["id"] = 1, ["name"] = "Ada" },
    new JsonObject { ["id"] = 2, ["name"] = "Grace" },
};

var store = new LocalFileStore("/srv/exports");
await using (var output = await store.OpenWriteAsync("people.csv", FileWriteMode.Overwrite, CancellationToken.None))
{
    await new CsvFormat().WriteAsync(output, rows, new JsonObject(), suppressHeader: false, CancellationToken.None);
}

// Stream the rows back one at a time.
IFileFormat csv = new CsvFormat();
await using var input = await store.OpenReadAsync("people.csv", CancellationToken.None);
await foreach (var row in csv.ReadRowsAsync(input, new JsonObject(), CancellationToken.None))
{
    Console.WriteLine(row["name"]);
}
```

Each cloud store's provider creates it from a JSON config, for example `new S3FileStoreProvider().Create(config)`,
so connection settings can live in your own configuration.

Targets .NET 8, 9 and 10. MIT licensed.

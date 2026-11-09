using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Files;

/// <summary>
/// A file being read <b>lazily</b>, one row at a time (E22-T1) — the alternative to a retrieve's
/// materialized <c>JsonArray</c> payload.
/// <para>
/// A feed is the one thing in a run that is <b>not</b> a value. Every other payload is an in-memory
/// JSON tree that can be cloned, re-read and carried anywhere; a feed is a live cursor over an open
/// stream, so it can be read exactly once and only by the node the executor hands it to. That is why
/// it never becomes a node's payload: the pipeline engine either walks it into a split
/// region's chunks (the streaming path) or materializes it into an ordinary array (everywhere else),
/// and downstream nodes only ever see the second kind of thing.
/// </para>
/// </summary>
public interface IRowFeed : IAsyncDisposable
{
    /// <summary>What is being streamed, for the trace placeholder and error messages (e.g. "in/big.csv (csv)").</summary>
    string Describe();

    /// <summary>
    /// The row cap that applies when this feed has to be <b>materialized</b> after all — the request's
    /// <c>maxRows</c>. Streaming into a split has no such cap (that is the point of streaming); a feed
    /// nothing streams from becomes an ordinary payload and is guarded exactly like a normal read.
    /// </summary>
    int MaxRows { get; }

    /// <summary>
    /// The rows, read as they are consumed. Enumerating a second time throws — the underlying stream
    /// has already been walked, and silently yielding nothing would look like an empty file.
    /// </summary>
    IAsyncEnumerable<JsonNode> ReadAsync(CancellationToken ct);
}

/// <summary>
/// A feed over one open file stream. Owns the stream: disposing the feed closes it, whether it was
/// read to the end, abandoned halfway (a failed chunk) or never read at all (a node that errored
/// before the split).
/// </summary>
public sealed class FileRowFeed(Stream stream, IFileFormat format, JsonObject request, string path) : IRowFeed
{
    private bool _started;

    public string Describe() => $"{path} ({format.Format})";

    /// <summary>The most rows a read returns unless the request sets <c>maxRows</c>.</summary>
    public const int DefaultMaxRows = 100_000;

    public int MaxRows => request["maxRows"]?.GetValue<int>() ?? DefaultMaxRows;

    public async IAsyncEnumerable<JsonNode> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (_started)
        {
            throw new InvalidOperationException(
                $"the streaming read of '{path}' has already been consumed. A streamed file is read once — "
                + "re-running the step that consumes it (a retry, or a second split over the same retrieve) "
                + "needs the retrieve to run again, so remove 'stream' from the request or retry the retrieve.");
        }
        _started = true;

        await foreach (var row in format.ReadRowsAsync(stream, request, ct).WithCancellation(ct))
        {
            yield return row;
        }
    }

    public ValueTask DisposeAsync() => stream.DisposeAsync();
}

/// <summary>Turning a feed back into an ordinary payload, for the paths that cannot stream.</summary>
public static class RowFeeds
{
    /// <summary>
    /// Reads the whole feed into an array, refusing past <see cref="IRowFeed.MaxRows"/>. This is what
    /// a <c>stream: true</c> retrieve does when nothing downstream can consume rows lazily: the run
    /// behaves exactly as it would without the flag, cap included, rather than silently doing
    /// something different from what the pipeline says.
    /// </summary>
    public static async Task<JsonArray> MaterializeAsync(IRowFeed feed, CancellationToken ct)
    {
        var rows = new JsonArray();
        var max = feed.MaxRows;
        await foreach (var row in feed.ReadAsync(ct))
        {
            if (max > 0 && rows.Count >= max)
            {
                throw new InvalidOperationException(
                    $"'{feed.Describe()}' has more than the maxRows limit of {max} rows — raise maxRows, or "
                    + "send this retrieve straight into a split node so its rows are streamed in chunks "
                    + "instead of being held at once.");
            }
            rows.Add(row);
        }
        return rows;
    }
}

namespace SpanFlow.Connectors.Files;

/// <summary>
/// Write stream for object stores (s3/azureBlob), where a "write" is a whole-object PUT rather
/// than an open file handle. The format layer writes into this in-memory buffer; disposing it
/// performs the upload exactly once, then disposes the owning client. Append is emulated by the
/// store pre-loading the existing object's bytes (Position left at the end). Object PUTs replace
/// the object atomically, which also makes this the Replace implementation.
/// </summary>
internal sealed class BufferedUploadStream(
    Func<MemoryStream, Task> upload, params IDisposable[] owners) : MemoryStream
{
    private bool _uploaded;

    public override async ValueTask DisposeAsync()
    {
        if (!_uploaded)
        {
            _uploaded = true;
            try
            {
                Position = 0;
                await upload(this);
            }
            finally
            {
                foreach (var owner in owners) owner.Dispose();
            }
        }
        await base.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        // Sync dispose happens when a format wraps us in a non-async writer; run the upload once.
        if (disposing && !_uploaded)
        {
            _uploaded = true;
            try
            {
                Position = 0;
                upload(this).GetAwaiter().GetResult();
            }
            finally
            {
                foreach (var owner in owners) owner.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}

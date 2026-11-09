namespace SpanFlow.Connectors.Files;

/// <summary>One entry from browsing a file connection's store (E04-T6 file browser).</summary>
public class FileEntryDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Store-root-relative path — usable directly as a file request's <c>path</c>
    /// (for a directory, pass it back as <c>?path=</c> to drill down).</summary>
    public string Path { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }
    public long? Size { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

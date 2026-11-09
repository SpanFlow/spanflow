namespace SpanFlow.Connectors.Database;

public class DbTableDto
{
    public string? Schema { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Schema-qualified name usable as a table-mode target (e.g. "public.orders").</summary>
    public string QualifiedName => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";
}

public class DbColumnDto
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool Nullable { get; set; }
    public bool IsPrimaryKey { get; set; }
}

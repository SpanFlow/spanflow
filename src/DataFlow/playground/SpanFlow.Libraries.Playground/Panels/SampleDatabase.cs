using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace SpanFlow.Libraries.Playground.Panels;

/// <summary>
/// The database panel's sample: SQLite <c>:memory:</c>, made and filled for one request and gone when it ends
/// (D71.4). <see cref="OpenLockedAsync"/> hands it over only after it is locked down: an authorizer that allows reads
/// and nothing else, limits on attachments, value and statement sizes, in-memory temp storage, and a progress
/// handler that interrupts at the time cap.
/// </summary>
public static class SampleDatabase
{
    /// <summary>The tables a visitor can query, for the page.</summary>
    public static readonly IReadOnlyList<(string Table, string Columns)> Tables =
    [
        ("customers", "id, name, country, city, joined_on"),
        ("products", "id, name, category, price"),
        ("orders", "id, customer_id, ordered_on, status"),
        ("order_items", "order_id, product_id, quantity, unit_price"),
    ];

    /// <summary>A value or row larger than this is refused by SQLite itself (randomblob(1e9) cannot eat the memory).</summary>
    public const int MaxValueBytes = 1_000_000;

    public const int MaxSqlBytes = 32_000;

    /// <summary>SQLite's whole heap, process-wide: the sample is tiny, so this is only ever hit by a runaway query.</summary>
    public const long HeapLimitBytes = 256L * 1024 * 1024;

    /// <summary>VM steps between two looks at the clock.</summary>
    private const int ProgressSteps = 1000;

    // https://sqlite.org/c3ref/c_alter_table.html
    private const int SqliteRead = 20;
    private const int SqliteSelect = 21;
    private const int SqliteFunction = 31;
    private const int SqliteRecursive = 33;

    /// <summary>Functions refused even inside a read: none of these exist in this build, but a later one must not appear unnoticed.</summary>
    private static readonly HashSet<string> DeniedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "load_extension", "readfile", "writefile", "edit", "fts3_tokenizer", "sqlite_dbpage", "zipfile", "sqlar_compress", "sqlar_uncompress",
    };

    private static readonly string[] Seed =
    [
        "PRAGMA temp_store = MEMORY",
        $"PRAGMA hard_heap_limit = {HeapLimitBytes}",
        "CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, country TEXT NOT NULL, city TEXT NOT NULL, joined_on TEXT NOT NULL)",
        "CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT NOT NULL, category TEXT NOT NULL, price REAL NOT NULL)",
        "CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER NOT NULL REFERENCES customers(id), ordered_on TEXT NOT NULL, status TEXT NOT NULL)",
        "CREATE TABLE order_items (order_id INTEGER NOT NULL REFERENCES orders(id), product_id INTEGER NOT NULL REFERENCES products(id), quantity INTEGER NOT NULL, unit_price REAL NOT NULL)",
        """
        INSERT INTO customers VALUES
        (1, 'Ada Lovelace', 'GB', 'London', '2024-01-15'), (2, 'Grace Hopper', 'US', 'New York', '2024-02-03'),
        (3, 'Alan Turing', 'GB', 'Manchester', '2024-03-22'), (4, 'Katherine Johnson', 'US', 'Hampton', '2024-04-09'),
        (5, 'Edsger Dijkstra', 'NL', 'Rotterdam', '2024-05-30'), (6, 'Barbara Liskov', 'US', 'Boston', '2024-06-11'),
        (7, 'Tim Berners-Lee', 'GB', 'Oxford', '2024-07-01'), (8, 'Margaret Hamilton', 'US', 'Boston', '2024-08-19'),
        (9, 'Linus Torvalds', 'FI', 'Helsinki', '2024-09-05'), (10, 'Radia Perlman', 'US', 'Seattle', '2024-10-27'),
        (11, 'Donald Knuth', 'US', 'Stanford', '2024-11-12'), (12, 'Frances Allen', 'US', 'Peru', '2024-12-08')
        """,
        """
        INSERT INTO products VALUES
        (1, 'Keyboard', 'hardware', 49.90), (2, 'Mouse', 'hardware', 19.50), (3, 'Monitor', 'hardware', 189.00),
        (4, 'USB-C cable', 'accessories', 9.99), (5, 'Laptop stand', 'accessories', 34.00), (6, 'Headset', 'hardware', 79.00),
        (7, 'Webcam', 'hardware', 59.00), (8, 'Desk lamp', 'office', 27.50), (9, 'Notebook', 'office', 4.20), (10, 'Pen set', 'office', 6.80)
        """,
        """
        INSERT INTO orders VALUES
        (1, 1, '2025-01-04', 'shipped'), (2, 2, '2025-01-09', 'shipped'), (3, 1, '2025-02-11', 'shipped'),
        (4, 3, '2025-02-14', 'cancelled'), (5, 4, '2025-03-02', 'shipped'), (6, 5, '2025-03-18', 'open'),
        (7, 6, '2025-04-01', 'shipped'), (8, 2, '2025-04-22', 'open'), (9, 7, '2025-05-05', 'shipped'),
        (10, 8, '2025-05-30', 'shipped'), (11, 9, '2025-06-12', 'open'), (12, 10, '2025-06-28', 'shipped'),
        (13, 11, '2025-07-07', 'shipped'), (14, 12, '2025-07-21', 'open'), (15, 3, '2025-08-02', 'shipped'),
        (16, 4, '2025-08-15', 'shipped'), (17, 6, '2025-09-09', 'cancelled'), (18, 8, '2025-09-30', 'open')
        """,
        """
        INSERT INTO order_items VALUES
        (1, 1, 1, 49.90), (1, 2, 1, 19.50), (2, 3, 2, 189.00), (3, 4, 3, 9.99), (4, 6, 1, 79.00), (5, 5, 1, 34.00),
        (5, 9, 5, 4.20), (6, 7, 1, 59.00), (7, 8, 2, 27.50), (8, 1, 1, 49.90), (8, 4, 2, 9.99), (9, 3, 1, 189.00),
        (10, 10, 4, 6.80), (11, 2, 2, 19.50), (12, 6, 1, 79.00), (12, 7, 1, 59.00), (13, 5, 2, 34.00), (14, 9, 10, 4.20),
        (15, 1, 1, 49.90), (16, 3, 1, 189.00), (17, 8, 1, 27.50), (18, 4, 4, 9.99), (18, 10, 2, 6.80)
        """,
    ];

    /// <summary>A fresh, filled, locked sample. Dispose it when the request is done.</summary>
    public static async Task<SqliteConnection> OpenLockedAsync(Stopwatch clock, int timeoutMs, CancellationToken ct)
    {
        // Mode=Memory with no name: private to this connection, never a file, gone when it closes.
        var connection = new SqliteConnection("Data Source=:memory:");
        try
        {
            await connection.OpenAsync(ct);
            foreach (var statement in Seed)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement;
                await command.ExecuteNonQueryAsync(ct);
            }

            Lock(connection.Handle!, clock, timeoutMs, ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static void Lock(sqlite3 db, Stopwatch clock, int timeoutMs, CancellationToken ct)
    {
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_ATTACHED, 0);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_LENGTH, MaxValueBytes);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_SQL_LENGTH, MaxSqlBytes);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_COLUMN, 200);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_EXPR_DEPTH, 100);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_COMPOUND_SELECT, 50);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_VARIABLE_NUMBER, 100);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_LIKE_PATTERN_LENGTH, 1000);

        raw.sqlite3_set_authorizer(db, Authorize, null);

        // Non-zero stops the statement (SQLITE_INTERRUPT): a recursive CTE with no end stops at the time cap.
        raw.sqlite3_progress_handler(db, ProgressSteps, _ => ct.IsCancellationRequested || clock.ElapsedMilliseconds > timeoutMs ? 1 : 0, null);
    }

    /// <summary>Reads only (D71.4): SELECT, reading a column, calling a function, a recursive CTE. Everything else, refused.</summary>
    internal static int Authorize(object userData, int action, utf8z first, utf8z second, utf8z database, utf8z trigger)
    {
        switch (action)
        {
            case SqliteSelect or SqliteRead or SqliteRecursive:
                return raw.SQLITE_OK;
            case SqliteFunction:
                // For SQLITE_FUNCTION the function's name is the second argument.
                return DeniedFunctions.Contains(second.utf8_to_string() ?? "") ? raw.SQLITE_DENY : raw.SQLITE_OK;
            default:
                return raw.SQLITE_DENY;
        }
    }
}

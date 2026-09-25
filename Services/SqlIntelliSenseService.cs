using MySqlManagementStudio.Models;

namespace MySqlManagementStudio.Services;

public sealed class SqlIntelliSenseService
{
    private static readonly string[] Keywords =
    [
        "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "LIKE", "BETWEEN", "IS", "NULL",
        "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "CREATE", "ALTER", "DROP", "TABLE",
        "DATABASE", "SCHEMA", "INDEX", "VIEW", "PROCEDURE", "FUNCTION", "TRIGGER", "JOIN", "INNER",
        "LEFT", "RIGHT", "OUTER", "CROSS", "ON", "AS", "DISTINCT", "GROUP", "BY", "ORDER", "HAVING",
        "LIMIT", "OFFSET", "ASC", "DESC", "UNION", "ALL", "EXISTS", "CASE", "WHEN", "THEN", "ELSE",
        "END", "IF", "ELSEIF", "WHILE", "LOOP", "LEAVE", "ITERATE", "DECLARE", "BEGIN", "COMMIT",
        "ROLLBACK", "TRANSACTION", "SHOW", "DESCRIBE", "EXPLAIN", "USE", "GRANT", "REVOKE",
        "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CONSTRAINT", "UNIQUE", "DEFAULT", "AUTO_INCREMENT",
        "ENGINE", "CHARSET", "COLLATE", "COMMENT", "REPLACE", "TRUNCATE", "RENAME", "ADD", "COLUMN",
        "MODIFY", "CHANGE", "WITH", "RECURSIVE", "OVER", "PARTITION", "ROW_NUMBER", "COUNT", "SUM",
        "AVG", "MIN", "MAX", "COALESCE", "IFNULL", "NULLIF", "CAST", "CONVERT", "NOW", "CURDATE",
        "CURTIME", "DATE", "DATETIME", "TIMESTAMP", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND",
        "TRUE", "FALSE", "INTERVAL", "ASC", "DESC", "FORCE", "IGNORE", "LOW_PRIORITY", "HIGH_PRIORITY",
        "STRAIGHT_JOIN", "SQL_CALC_FOUND_ROWS", "FOR", "UPDATE", "LOCK", "SHARE", "MODE", "UNLOCK", "TABLES"
    ];

    private static readonly string[] Snippets =
    [
        "SELECT * FROM",
        "SELECT COUNT(*) FROM",
        "INSERT INTO",
        "UPDATE SET",
        "DELETE FROM",
        "CREATE TABLE",
        "ALTER TABLE",
        "DROP TABLE",
        "SHOW TABLES",
        "SHOW DATABASES",
        "DESCRIBE",
        "EXPLAIN"
    ];

    private readonly MySqlDatabaseService _db;
    private readonly object _gate = new();
    private List<SqlCompletionData> _schemaItems = [];
    private string? _cachedKey;
    private DateTime _loadedAt = DateTime.MinValue;

    public SqlIntelliSenseService(MySqlDatabaseService db)
    {
        _db = db;
    }

    public async Task EnsureSchemaAsync(ConnectionProfile profile, string? database, CancellationToken ct = default)
    {
        var key = $"{profile.Id}|{database ?? "*"}";
        lock (_gate)
        {
            if (_cachedKey == key && DateTime.UtcNow - _loadedAt < TimeSpan.FromMinutes(2))
                return;
        }

        var items = new List<SqlCompletionData>();

        try
        {
            var databases = await _db.GetDatabasesAsync(profile, ct);
            foreach (var dbName in databases)
                items.Add(new SqlCompletionData(dbName, $"Database · {dbName}", "🗄", priority: 1));

            var targetDb = databases.FirstOrDefault(d =>
                               string.Equals(d, database, StringComparison.OrdinalIgnoreCase))
                           ?? databases.FirstOrDefault();

            if (targetDb is not null)
            {
                var tables = await _db.GetTablesAsync(profile, targetDb, ct);
                foreach (var table in tables)
                {
                    items.Add(new SqlCompletionData(table, $"Table · {targetDb}.{table}", "▦", priority: 2));
                    items.Add(new SqlCompletionData($"`{targetDb}`.`{table}`", $"Qualified table · {targetDb}.{table}", "▦", priority: 1.5));
                }

                // Column metadata for a bounded set of tables keeps IntelliSense responsive.
                foreach (var table in tables.Take(40))
                {
                    try
                    {
                        var columns = await _db.GetColumnsAsync(profile, targetDb, table, ct);
                        foreach (var (name, type, keyCol, nullable) in columns)
                        {
                            var nullText = nullable ? "NULL" : "NOT NULL";
                            var keyText = string.IsNullOrWhiteSpace(keyCol) ? "" : $" · {keyCol}";
                            items.Add(new SqlCompletionData(
                                name,
                                $"Column · {targetDb}.{table}.{name} ({type}{keyText}, {nullText})",
                                "▪",
                                priority: 3));
                        }
                    }
                    catch
                    {
                        // Skip columns for this table if metadata fails.
                    }
                }

                var views = await _db.GetViewsAsync(profile, targetDb, ct);
                foreach (var view in views)
                    items.Add(new SqlCompletionData(view, $"View · {targetDb}.{view}", "👁", priority: 2));
            }
        }
        catch
        {
            // Schema suggestions are optional; keywords still work.
        }

        lock (_gate)
        {
            _schemaItems = items;
            _cachedKey = key;
            _loadedAt = DateTime.UtcNow;
        }
    }

    public IReadOnlyList<SqlCompletionData> GetSuggestions(string prefix)
    {
        prefix ??= string.Empty;
        var startsWith = prefix.Trim('`');

        var results = new List<SqlCompletionData>();

        foreach (var snippet in Snippets)
        {
            if (startsWith.Length == 0 || snippet.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase))
                results.Add(new SqlCompletionData(snippet, "Snippet", "✂", priority: 0.5));
        }

        foreach (var keyword in Keywords.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (startsWith.Length == 0 || keyword.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase))
                results.Add(new SqlCompletionData(keyword, "MySQL keyword", "🔑", priority: 0));
        }

        List<SqlCompletionData> schema;
        lock (_gate)
            schema = _schemaItems;

        foreach (var item in schema)
        {
            if (startsWith.Length == 0 ||
                item.Text.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase) ||
                item.Text.Trim('`').StartsWith(startsWith, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(item);
            }
        }

        return results
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.Text, StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToList();
    }
}

using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using MySqlConnector;
using MySqlManagementStudio.Models;

namespace MySqlManagementStudio.Services;

public sealed class MySqlDatabaseService
{
    public async Task TestConnectionAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        await using var connection = new MySqlConnection(profile.BuildConnectionString());
        await connection.OpenAsync(ct);
    }

    public async Task<List<string>> GetDatabasesAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        await using var connection = new MySqlConnection(profile.BuildConnectionString());
        await connection.OpenAsync(ct);

        var databases = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT SCHEMA_NAME
            FROM information_schema.SCHEMATA
            WHERE SCHEMA_NAME NOT IN ('information_schema', 'performance_schema', 'mysql', 'sys')
            ORDER BY SCHEMA_NAME;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            databases.Add(reader.GetString(0));

        // Ensure A→Z order regardless of server collation.
        databases.Sort(StringComparer.OrdinalIgnoreCase);
        return databases;
    }

    public async Task<List<string>> GetTablesAsync(ConnectionProfile profile, string database, CancellationToken ct = default)
    {
        return await GetSchemaNamesAsync(profile, database, "BASE TABLE", ct);
    }

    public async Task<List<string>> GetViewsAsync(ConnectionProfile profile, string database, CancellationToken ct = default)
    {
        return await GetSchemaNamesAsync(profile, database, "VIEW", ct);
    }

    public async Task<List<(string Name, string Type, string? Key, bool Nullable)>> GetColumnsAsync(
        ConnectionProfile profile,
        string database,
        string table,
        CancellationToken ct = default)
    {
        await using var connection = new MySqlConnection(profile.BuildConnectionString(database));
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COLUMN_NAME, COLUMN_TYPE, COLUMN_KEY, IS_NULLABLE
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION;
            """;
        cmd.Parameters.AddWithValue("@db", database);
        cmd.Parameters.AddWithValue("@table", table);

        var columns = new List<(string, string, string?, bool)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                string.Equals(reader.GetString(3), "YES", StringComparison.OrdinalIgnoreCase)));
        }

        return columns;
    }

    public async Task<List<string>> GetPrimaryKeyColumnsAsync(
        ConnectionProfile profile,
        string database,
        string table,
        CancellationToken ct = default)
    {
        var columns = await GetColumnsAsync(profile, database, table, ct);
        return columns
            .Where(c => string.Equals(c.Key, "PRI", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name)
            .ToList();
    }

    public async Task UpdateCellAsync(
        ConnectionProfile profile,
        string database,
        string table,
        string column,
        object? newValue,
        IReadOnlyDictionary<string, object?> primaryKeyValues,
        CancellationToken ct = default)
    {
        if (primaryKeyValues.Count == 0)
            throw new InvalidOperationException("Cannot update a row without a primary key.");

        await using var connection = new MySqlConnection(profile.BuildConnectionString(database));
        await connection.OpenAsync(ct);

        var whereParts = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.Parameters.AddWithValue("@newValue", newValue ?? DBNull.Value);

        var i = 0;
        foreach (var (pkColumn, pkValue) in primaryKeyValues)
        {
            var pName = $"@pk{i}";
            whereParts.Add($"`{pkColumn.Replace("`", "``")}` <=> {pName}");
            cmd.Parameters.AddWithValue(pName, pkValue ?? DBNull.Value);
            i++;
        }

        cmd.CommandText =
            $"UPDATE `{database.Replace("`", "``")}`.`{table.Replace("`", "``")}` " +
            $"SET `{column.Replace("`", "``")}` = @newValue " +
            $"WHERE {string.Join(" AND ", whereParts)} LIMIT 1;";

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        if (affected == 0)
            throw new InvalidOperationException("No rows were updated. The row may have changed or the primary key did not match.");
    }

    public async Task<List<string>> GetRoutinesAsync(
        ConnectionProfile profile,
        string database,
        string routineType,
        CancellationToken ct = default)
    {
        await using var connection = new MySqlConnection(profile.BuildConnectionString(database));
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT ROUTINE_NAME
            FROM information_schema.ROUTINES
            WHERE ROUTINE_SCHEMA = @db AND ROUTINE_TYPE = @type
            ORDER BY ROUTINE_NAME;
            """;
        cmd.Parameters.AddWithValue("@db", database);
        cmd.Parameters.AddWithValue("@type", routineType);

        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            names.Add(reader.GetString(0));

        return names;
    }

    public async Task<string?> GetCreateScriptAsync(
        ConnectionProfile profile,
        string database,
        string objectType,
        string objectName,
        CancellationToken ct = default)
    {
        await using var connection = new MySqlConnection(profile.BuildConnectionString(database));
        await connection.OpenAsync(ct);

        var showType = objectType.ToUpperInvariant() switch
        {
            "TABLE" or "VIEW" => objectType,
            "PROCEDURE" => "PROCEDURE",
            "FUNCTION" => "FUNCTION",
            _ => objectType
        };

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SHOW CREATE {showType} `{objectName.Replace("`", "``")}`;";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        // SHOW CREATE TABLE/VIEW => column 1
        // SHOW CREATE PROCEDURE/FUNCTION => "Create Procedure"/"Create Function" is column 2
        // (column 1 is sql_mode). Prefer matching by column name.
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (name.StartsWith("Create ", StringComparison.OrdinalIgnoreCase) && !reader.IsDBNull(i))
                return reader.GetString(i);
        }

        var preferredIndex = showType.Equals("PROCEDURE", StringComparison.OrdinalIgnoreCase)
                             || showType.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase)
            ? 2
            : 1;

        if (reader.FieldCount > preferredIndex && !reader.IsDBNull(preferredIndex))
            return reader.GetString(preferredIndex);

        return reader.FieldCount > 1 ? reader.GetString(1) : reader.GetString(0);
    }

    public async Task<QueryExecutionResult> ExecuteAsync(
        ConnectionProfile profile,
        string? database,
        string sql,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var messages = new List<string>();

        try
        {
            await using var connection = new MySqlConnection(profile.BuildConnectionString(database));
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 120;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var hasResultSet = reader.FieldCount > 0;

            if (!hasResultSet)
            {
                sw.Stop();
                var affected = reader.RecordsAffected;
                messages.Add($"Commands completed successfully.");
                messages.Add($"({affected} row(s) affected)");
                messages.Add($"Completion time: {sw.Elapsed:mm\\:ss\\.fff}");

                return new QueryExecutionResult
                {
                    Succeeded = true,
                    Messages = string.Join(Environment.NewLine, messages),
                    Duration = sw.Elapsed,
                    RecordsAffected = affected
                };
            }

            var columns = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (string.IsNullOrWhiteSpace(name))
                    name = $"Column{i}";
                // Ensure unique column names
                var unique = name;
                var suffix = 1;
                while (columns.Contains(unique, StringComparer.OrdinalIgnoreCase))
                    unique = $"{name}_{suffix++}";
                columns.Add(unique);
            }

            var table = new DataTable();
            foreach (var column in columns)
                table.Columns.Add(column, typeof(object));

            var rows = new ObservableCollection<ResultRow>();
            while (await reader.ReadAsync(ct))
            {
                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                var dataRow = table.NewRow();
                for (var i = 0; i < columns.Count; i++)
                {
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    values[columns[i]] = value;
                    dataRow[i] = value ?? DBNull.Value;
                }

                table.Rows.Add(dataRow);
                rows.Add(new ResultRow(values));
            }

            sw.Stop();
            messages.Add($"({rows.Count} row(s) returned)");
            messages.Add($"Completion time: {sw.Elapsed:mm\\:ss\\.fff}");

            return new QueryExecutionResult
            {
                Succeeded = true,
                Messages = string.Join(Environment.NewLine, messages),
                Duration = sw.Elapsed,
                RecordsAffected = rows.Count,
                Table = table,
                Rows = rows,
                Columns = columns
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new QueryExecutionResult
            {
                Succeeded = false,
                Messages = $"Msg {ex.HResult}, Level 16{Environment.NewLine}{ex.Message}{Environment.NewLine}Completion time: {sw.Elapsed:mm\\:ss\\.fff}",
                Duration = sw.Elapsed
            };
        }
    }

    private static async Task<List<string>> GetSchemaNamesAsync(
        ConnectionProfile profile,
        string database,
        string tableType,
        CancellationToken ct)
    {
        await using var connection = new MySqlConnection(profile.BuildConnectionString(database));
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TABLE_NAME
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @db AND TABLE_TYPE = @type
            ORDER BY TABLE_NAME;
            """;
        cmd.Parameters.AddWithValue("@db", database);
        cmd.Parameters.AddWithValue("@type", tableType);

        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            names.Add(reader.GetString(0));

        return names;
    }
}

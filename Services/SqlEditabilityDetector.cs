using System.Text.RegularExpressions;

namespace MySqlManagementStudio.Services;

public static partial class SqlEditabilityDetector
{
    public static (string? Database, string? Table)? TryDetectSingleTable(string sql, string? fallbackDatabase)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return null;

        // Only simple single-table SELECTs are editable.
        if (JoinRegex().IsMatch(sql) ||
            UnionRegex().IsMatch(sql) ||
            GroupByRegex().IsMatch(sql) ||
            DistinctRegex().IsMatch(sql))
        {
            return null;
        }

        if (!SelectRegex().IsMatch(sql))
            return null;

        var match = FromRegex().Match(sql);
        if (!match.Success)
            return null;

        var db = match.Groups["db"].Success ? Unquote(match.Groups["db"].Value) : fallbackDatabase;
        var table = Unquote(match.Groups["table"].Value);
        if (string.IsNullOrWhiteSpace(table))
            return null;

        return (db, table);
    }

    private static string Unquote(string value) =>
        value.Trim().Trim('`').Trim('"').Trim('\'');

    [GeneratedRegex(@"\bJOIN\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JoinRegex();

    [GeneratedRegex(@"\bUNION\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnionRegex();

    [GeneratedRegex(@"\bGROUP\s+BY\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GroupByRegex();

    [GeneratedRegex(@"\bSELECT\s+DISTINCT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DistinctRegex();

    [GeneratedRegex(@"^\s*SELECT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelectRegex();

    [GeneratedRegex(
        @"\bFROM\s+(?:(?<db>[`""\w]+)\s*\.\s*)?(?<table>[`""\w]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FromRegex();
}

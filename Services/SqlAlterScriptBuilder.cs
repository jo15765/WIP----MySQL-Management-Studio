using System.Text;
using System.Text.RegularExpressions;

namespace MySqlManagementStudio.Services;

public static partial class SqlAlterScriptBuilder
{
    public static string BuildAlterViewScript(string database, string viewName, string createScript)
    {
        var selectBody = ExtractViewSelectBody(createScript);
        if (string.IsNullOrWhiteSpace(selectBody))
            selectBody = "SELECT 1";

        selectBody = SqlPrettyPrinter.FormatSelect(selectBody.Trim().TrimEnd(';'));

        var sb = new StringBuilder();
        sb.AppendLine($"USE `{EscapeIdent(database)}`;");
        sb.AppendLine();
        sb.AppendLine($"-- Alter view `{viewName}`");
        sb.AppendLine($"ALTER VIEW `{EscapeIdent(viewName)}`");
        sb.AppendLine("AS");
        sb.AppendLine(selectBody.TrimEnd());
        sb.Append(';');
        sb.AppendLine();
        return sb.ToString();
    }

    public static string BuildAlterProcedureScript(string database, string procedureName, string createScript)
    {
        // Do NOT run the aggressive pretty-printer on routines — it breaks
        // "DROP PROCEDURE IF EXISTS", "LEFT JOIN", and comment text into invalid SQL.
        var body = NormalizeRoutineBody(createScript);

        var sb = new StringBuilder();
        sb.AppendLine($"USE `{EscapeIdent(database)}`;");
        sb.AppendLine();
        sb.AppendLine($"-- Alter stored procedure `{procedureName}`");
        sb.AppendLine("-- Edit the definition below, then execute (F5).");
        sb.AppendLine("-- MySQL applies procedure body changes via DROP PROCEDURE + CREATE PROCEDURE.");
        sb.AppendLine();
        sb.AppendLine($"DROP PROCEDURE IF EXISTS `{EscapeIdent(procedureName)}`;");
        sb.AppendLine();
        sb.AppendLine(body);
        sb.AppendLine(";");
        return sb.ToString();
    }

    public static string BuildAlterFunctionScript(string database, string functionName, string createScript)
    {
        var body = NormalizeRoutineBody(createScript);

        var sb = new StringBuilder();
        sb.AppendLine($"USE `{EscapeIdent(database)}`;");
        sb.AppendLine();
        sb.AppendLine($"-- Alter function `{functionName}`");
        sb.AppendLine("-- Edit the definition below, then execute (F5).");
        sb.AppendLine();
        sb.AppendLine($"DROP FUNCTION IF EXISTS `{EscapeIdent(functionName)}`;");
        sb.AppendLine();
        sb.AppendLine(body);
        sb.AppendLine(";");
        return sb.ToString();
    }

    /// <summary>
    /// Pulls only the query after the final VIEW ... AS clause from SHOW CREATE VIEW.
    /// Avoids keeping ALGORITHM/DEFINER/CREATE prefixes that caused duplication.
    /// </summary>
    private static string ExtractViewSelectBody(string createScript)
    {
        var text = createScript
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\\n", "\n")
            .Trim()
            .TrimEnd(';');

        var match = ViewAsRegex().Match(text);
        if (match.Success)
            return text[(match.Index + match.Length)..].Trim();

        var viewIdx = text.IndexOf(" VIEW ", StringComparison.OrdinalIgnoreCase);
        if (viewIdx < 0)
            viewIdx = 0;

        var asIdx = text.IndexOf(" AS ", viewIdx, StringComparison.OrdinalIgnoreCase);
        if (asIdx >= 0)
            return text[(asIdx + 4)..].Trim();

        return text;
    }

    private static string NormalizeRoutineBody(string createScript)
    {
        var body = createScript
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\\n", "\n")
            .Replace("\\t", "\t")
            .Trim()
            .TrimEnd(';');

        // SHOW CREATE may emit ANSI_QUOTES identifiers ("name"). Convert to backticks
        // so the script runs under default MySQL sql_mode.
        body = ConvertAnsiQuotesToBackticks(body);

        return body;
    }

    private static string ConvertAnsiQuotesToBackticks(string sql)
    {
        // DEFINER="user"@"host"
        sql = DefinerQuoteRegex().Replace(sql, "DEFINER=`$1`@`$2`");

        // PROCEDURE "name" / FUNCTION "name"
        sql = RoutineNameQuoteRegex().Replace(sql, "$1 `$2`");

        return sql;
    }

    private static string EscapeIdent(string value) => value.Replace("`", "``");

    [GeneratedRegex(
        @"\bVIEW\s+(?:`[^`]+`|[A-Za-z0-9_]+)\s+AS\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ViewAsRegex();

    [GeneratedRegex(
        @"DEFINER\s*=\s*""([^""]+)""\s*@\s*""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefinerQuoteRegex();

    [GeneratedRegex(
        @"\b(PROCEDURE|FUNCTION)\s+""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoutineNameQuoteRegex();
}

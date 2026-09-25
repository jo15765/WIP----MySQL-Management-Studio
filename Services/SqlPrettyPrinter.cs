using System.Text;
using System.Text.RegularExpressions;

namespace MySqlManagementStudio.Services;

/// <summary>
/// Lightweight MySQL-oriented formatter so ALTER/CREATE DDL is readable in the editor.
/// </summary>
public static partial class SqlPrettyPrinter
{
    private static readonly string[] BreakBefore =
    [
        "SELECT", "FROM", "WHERE", "AND", "OR", "JOIN", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN",
        "LEFT OUTER JOIN", "RIGHT OUTER JOIN", "CROSS JOIN", "STRAIGHT_JOIN", "ON", "GROUP BY",
        "ORDER BY", "HAVING", "LIMIT", "UNION", "UNION ALL", "SET", "VALUES", "BEGIN", "END",
        "DECLARE", "IF", "ELSEIF", "ELSE", "WHILE", "LOOP", "REPEAT", "UNTIL", "CASE", "WHEN",
        "THEN", "RETURNS", "RETURN", "LEAVE", "ITERATE", "CALL", "INSERT INTO", "UPDATE",
        "DELETE FROM", "CREATE", "ALTER", "DROP", "DELIMITER"
    ];

    private static readonly string[] SelectClauseKeywords =
    [
        "UNION ALL", "UNION", "INTERSECT", "EXCEPT",
        "LEFT OUTER JOIN", "RIGHT OUTER JOIN", "INNER JOIN", "CROSS JOIN",
        "LEFT JOIN", "RIGHT JOIN", "JOIN",
        "GROUP BY", "ORDER BY",
        "WITH", "SELECT", "FROM", "WHERE", "HAVING", "LIMIT", "OFFSET", "ON"
    ];

    /// <summary>Formats a SELECT / CTE body used by ALTER VIEW.</summary>
    public static string FormatSelect(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return sql;

        var text = NormalizeNewlines(sql);
        text = ConvertDoubleQuotedIdentifiers(text);
        text = CollapseSpacesOutsideStrings(text);
        text = UppercaseMajorKeywords(text);

        // CTE opener: name AS (
        text = CteAsOpenRegex().Replace(text, "$1 AS (\n");

        // Put major clauses on their own lines.
        foreach (var keyword in SelectClauseKeywords.OrderByDescending(k => k.Length))
        {
            text = Regex.Replace(
                text,
                $@"(?i)(?<![\w`])\s*\b({Regex.Escape(keyword)})\b",
                $"\n$1",
                RegexOptions.CultureInvariant);
        }

        // WITH name — keep name on same visual block
        text = Regex.Replace(text, @"(?i)\nWITH\s+", "\nWITH ", RegexOptions.CultureInvariant);

        // Put columns on the next line after SELECT / GROUP BY / ORDER BY
        text = Regex.Replace(text, @"(?i)\b(SELECT)\s+", "$1\n", RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"(?i)\b(GROUP BY)\s+", "$1\n", RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"(?i)\b(ORDER BY)\s+", "$1\n", RegexOptions.CultureInvariant);

        // AND / OR under filters
        text = Regex.Replace(text, @"(?i)\s+\b(AND|OR)\b\s+", "\n  $1 ", RegexOptions.CultureInvariant);

        // Pull CTE-closing parens onto their own line before the outer SELECT / next CTE.
        text = Regex.Replace(
            text,
            @"\)\s*(?=SELECT\b)",
            "\n)\n",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(
            text,
            @"\)\s*,\s*(?=(?:`[^`]+`|[A-Za-z_][A-Za-z0-9_]*)\s+AS\s*\()",
            "\n),\n",
            RegexOptions.IgnoreCase);

        // Break list commas at CTE/select depth, but not inside function args (COALESCE(x, 0)).
        text = BreakCommas(text, maxDepth: 1);

        // Space after commas inside function argument lists: COALESCE(x,0) -> COALESCE(x, 0)
        text = Regex.Replace(text, @",(\S)", ", $1");

        // Normalize indentation / alignment.
        text = IndentSelectStyle(text);
        return text.Trim() + "\n";
    }

    public static string Format(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return sql;

        var text = NormalizeNewlines(sql);
        text = CollapseSpacesOutsideStrings(text);

        foreach (var keyword in BreakBefore.OrderByDescending(k => k.Length))
        {
            text = Regex.Replace(
                text,
                $@"(?i)(?<!\n)\s+\b({Regex.Escape(keyword)})\b",
                $"\n$1",
                RegexOptions.CultureInvariant);
        }

        text = BreakCommas(text, maxDepth: 1);
        return IndentLines(text, baseIndent: 0);
    }

    public static string ConvertDoubleQuotedIdentifiers(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var inSingle = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];

            if (inSingle)
            {
                sb.Append(c);
                if (c == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    sb.Append(sql[++i]); // escaped quote
                    continue;
                }

                if (c == '\'')
                    inSingle = false;
                continue;
            }

            if (c == '\'')
            {
                inSingle = true;
                sb.Append(c);
                continue;
            }

            // Skip already-backticked identifiers.
            if (c == '`')
            {
                sb.Append(c);
                i++;
                while (i < sql.Length && sql[i] != '`')
                    sb.Append(sql[i++]);
                if (i < sql.Length)
                    sb.Append('`');
                continue;
            }

            if (c == '"')
            {
                i++;
                var ident = new StringBuilder();
                while (i < sql.Length && sql[i] != '"')
                    ident.Append(sql[i++]);
                sb.Append('`');
                sb.Append(ident.ToString().Replace("`", "``"));
                sb.Append('`');
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string UppercaseMajorKeywords(string sql)
    {
        string[] keywords =
        [
            "UNION ALL", "GROUP BY", "ORDER BY", "LEFT OUTER JOIN", "RIGHT OUTER JOIN",
            "INNER JOIN", "CROSS JOIN", "LEFT JOIN", "RIGHT JOIN",
            "WITH", "SELECT", "FROM", "WHERE", "HAVING", "LIMIT", "OFFSET", "JOIN", "ON",
            "AND", "OR", "AS", "SUM", "COUNT", "AVG", "MIN", "MAX", "COALESCE", "IFNULL",
            "DISTINCT", "CASE", "WHEN", "THEN", "ELSE", "END"
        ];

        foreach (var keyword in keywords.OrderByDescending(k => k.Length))
        {
            sql = Regex.Replace(
                sql,
                $@"(?i)(?<![\w`])\b({Regex.Escape(keyword)})\b(?![\w])",
                keyword.ToUpperInvariant(),
                RegexOptions.CultureInvariant);
        }

        return sql;
    }

    private static string IndentSelectStyle(string text)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        var indent = 0;
        var inSelectList = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (sb.Length > 0 && sb[^1] != '\n')
                    sb.AppendLine();
                continue;
            }

            var upper = line.ToUpperInvariant();

            if (upper is ")" or "),")
                indent = Math.Max(0, indent - 1);

            if (upper.StartsWith("FROM") || upper.StartsWith("WHERE") || upper.StartsWith("GROUP BY") ||
                upper.StartsWith("ORDER BY") || upper.StartsWith("HAVING") || upper.StartsWith("LIMIT") ||
                upper.StartsWith("JOIN") || upper.StartsWith("LEFT ") || upper.StartsWith("RIGHT ") ||
                upper.StartsWith("INNER ") || upper.StartsWith("CROSS ") || upper.StartsWith("ON ") ||
                upper.StartsWith("UNION") || upper == "SELECT" || upper.StartsWith("WITH"))
            {
                inSelectList = upper == "SELECT";
            }

            // Columns under SELECT / GROUP BY get +1 indent visually via trailing content lines.
            var lineIndent = indent;
            if (inSelectList && upper != "SELECT")
                lineIndent = indent + 1;
            if (!inSelectList && (upper.StartsWith("GROUP BY") is false) &&
                indent > 0 &&
                !upper.StartsWith("FROM") && !upper.StartsWith("WHERE") && !upper.StartsWith("HAVING") &&
                !upper.StartsWith("ORDER") && !upper.StartsWith("LIMIT") && !upper.StartsWith("JOIN") &&
                !upper.StartsWith("LEFT") && !upper.StartsWith("RIGHT") && !upper.StartsWith("INNER") &&
                !upper.StartsWith("ON") && upper is not ")" and not ")," &&
                !upper.StartsWith("WITH") && !upper.StartsWith("SELECT") && !upper.StartsWith("UNION"))
            {
                // continuation lines (e.g. column lists after GROUP BY linebreak)
                if (!upper.StartsWith("GROUP BY") && !upper.StartsWith("ORDER BY"))
                    lineIndent = indent + 1;
            }

            // Indent column continuations after GROUP BY / ORDER BY / SELECT
            if ((upper.StartsWith("GROUP BY") || upper.StartsWith("ORDER BY")) is false &&
                sb.Length > 0)
            {
                // Detect previous clause
            }

            sb.Append(new string(' ', lineIndent * 2));

            if (upper == "SELECT" || upper.StartsWith("GROUP BY") || upper.StartsWith("ORDER BY"))
            {
                // Put keyword alone, columns on following indented lines when already split.
                sb.AppendLine(line);
                inSelectList = upper == "SELECT" || upper.StartsWith("GROUP BY") || upper.StartsWith("ORDER BY");
            }
            else if (upper.StartsWith("WITH "))
            {
                sb.AppendLine(line);
                indent++;
            }
            else if (line.EndsWith(" AS (", StringComparison.OrdinalIgnoreCase) ||
                     line.EndsWith(" AS(", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine(line);
                indent++;
            }
            else if (upper is ")" or "),")
            {
                sb.AppendLine(line);
                inSelectList = false;
            }
            else
            {
                // Prefer indented columns under SELECT/GROUP BY
                if (inSelectList && upper != "SELECT" && !upper.StartsWith("GROUP BY") &&
                    !upper.StartsWith("ORDER BY"))
                {
                    // already accounted via lineIndent
                }

                sb.AppendLine(line);
            }

            if (upper.StartsWith("FROM") || upper.StartsWith("WHERE") || upper.StartsWith("HAVING") ||
                upper.StartsWith("LIMIT") || upper.StartsWith("JOIN") || upper.StartsWith("LEFT ") ||
                upper.StartsWith("RIGHT ") || upper.StartsWith("INNER ") || upper.StartsWith("ON"))
            {
                inSelectList = false;
            }
        }

        return PostAlignClauses(sb.ToString());
    }

    private static string PostAlignClauses(string text)
    {
        // Ensure SELECT / GROUP BY columns are indented one level under the keyword.
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var indentColumns = false;

        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                sb.AppendLine();
                continue;
            }

            var upper = trimmed.ToUpperInvariant();
            if (upper is "SELECT" || upper.StartsWith("GROUP BY") || upper.StartsWith("ORDER BY"))
            {
                sb.AppendLine(trimmed);
                indentColumns = true;
                continue;
            }

            if (upper.StartsWith("FROM") || upper.StartsWith("WHERE") || upper.StartsWith("HAVING") ||
                upper.StartsWith("LIMIT") || upper.StartsWith("JOIN") || upper.StartsWith("LEFT ") ||
                upper.StartsWith("RIGHT ") || upper.StartsWith("INNER ") || upper.StartsWith("CROSS ") ||
                upper.StartsWith("ON") || upper.StartsWith("UNION") || upper.StartsWith("WITH") ||
                upper is ")" or "),")
            {
                indentColumns = false;
                sb.AppendLine(trimmed);
                continue;
            }

            if (indentColumns)
                sb.AppendLine("  " + trimmed);
            else if (trimmed.EndsWith(" AS (", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.EndsWith(" AS(", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine(trimmed);
            else if (raw.StartsWith("  ") || raw.StartsWith("\t"))
                sb.AppendLine(raw.TrimEnd());
            else
                sb.AppendLine(trimmed);
        }

        // Final pass: indent CTE bodies between AS ( and )
        return IndentCteBodies(sb.ToString());
    }

    private static string IndentCteBodies(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var cteDepth = 0;

        foreach (var raw in lines)
        {
            var preserved = raw.TrimEnd();
            var trimmed = preserved.Trim();
            if (trimmed.Length == 0)
            {
                sb.AppendLine();
                continue;
            }

            var upper = trimmed.ToUpperInvariant();
            var closesCte = upper is ")" or "),";
            if (closesCte && cteDepth > 0)
            {
                cteDepth--;
                sb.AppendLine(new string(' ', cteDepth * 2) + trimmed);
                continue;
            }

            if (cteDepth > 0)
            {
                // Keep column indentation from earlier passes, then add CTE indent.
                var leading = preserved.StartsWith("  ", StringComparison.Ordinal) ? "  " : "";
                sb.AppendLine(new string(' ', cteDepth * 2) + leading + trimmed.TrimStart());
            }
            else
            {
                sb.AppendLine(preserved);
            }

            if (trimmed.EndsWith(" AS (", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith(" AS(", StringComparison.OrdinalIgnoreCase))
            {
                cteDepth++;
            }
        }

        return sb.ToString();
    }

    private static string NormalizeNewlines(string sql) =>
        sql.Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\\n", "\n")
            .Replace("\\t", "  ");

    private static string IndentLines(string text, int baseIndent)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        var indent = baseIndent;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (sb.Length > 0 && sb[^1] != '\n')
                    sb.AppendLine();
                continue;
            }

            if (EndsBlock(line))
                indent = Math.Max(baseIndent, indent - 1);

            sb.Append(new string(' ', indent * 2));
            sb.AppendLine(line);

            if (StartsBlock(line))
                indent++;
        }

        return sb.ToString().Trim() + "\n";
    }

    private static bool StartsBlock(string line)
    {
        return line.Equals("BEGIN", StringComparison.OrdinalIgnoreCase)
               || line.Equals("LOOP", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("CASE", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("IF ", StringComparison.OrdinalIgnoreCase)
               || line.Equals("ELSE", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("ELSEIF", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("WHILE", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("REPEAT", StringComparison.OrdinalIgnoreCase)
               || line.EndsWith('(');
    }

    private static bool EndsBlock(string line)
    {
        return line.StartsWith("END", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("ELSE", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("ELSEIF", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("UNTIL", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith(')');
    }

    private static string BreakCommas(string text, int maxDepth)
    {
        var sb = new StringBuilder(text.Length + 64);
        var inSingle = false;
        var inBacktick = false;
        var depth = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inSingle)
            {
                sb.Append(c);
                if (c == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    sb.Append(text[++i]);
                    continue;
                }

                if (c == '\'')
                    inSingle = false;
                continue;
            }

            if (inBacktick)
            {
                sb.Append(c);
                if (c == '`')
                    inBacktick = false;
                continue;
            }

            switch (c)
            {
                case '\'':
                    inSingle = true;
                    sb.Append(c);
                    break;
                case '`':
                    inBacktick = true;
                    sb.Append(c);
                    break;
                case '(':
                    depth++;
                    sb.Append(c);
                    break;
                case ')':
                    depth = Math.Max(0, depth - 1);
                    sb.Append(c);
                    break;
                case ',' when depth <= maxDepth:
                    sb.Append(",\n");
                    while (i + 1 < text.Length && text[i + 1] == ' ')
                        i++;
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string CollapseSpacesOutsideStrings(string text)
    {
        var sb = new StringBuilder(text.Length);
        var inSingle = false;
        var inBacktick = false;
        var lastWasSpace = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inSingle)
            {
                sb.Append(c);
                if (c == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    sb.Append(text[++i]);
                    continue;
                }

                if (c == '\'')
                    inSingle = false;
                continue;
            }

            if (inBacktick)
            {
                sb.Append(c);
                if (c == '`')
                    inBacktick = false;
                continue;
            }

            if (c == '\'')
            {
                inSingle = true;
                sb.Append(c);
                lastWasSpace = false;
                continue;
            }

            if (c == '`')
            {
                inBacktick = true;
                sb.Append(c);
                lastWasSpace = false;
                continue;
            }

            if (c == '\n')
            {
                sb.Append(c);
                lastWasSpace = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        return sb.ToString();
    }

    // `Name` AS (   or   Name AS (
    [GeneratedRegex(
        @"(?i)(`[^`]+`|[A-Za-z_][A-Za-z0-9_]*)\s+AS\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex CteAsOpenRegex();
}

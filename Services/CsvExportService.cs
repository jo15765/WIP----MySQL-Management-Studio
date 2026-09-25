using System.Globalization;
using System.Text;
using MySqlManagementStudio.Models;

namespace MySqlManagementStudio.Services;

public static class CsvExportService
{
    public static string ToCsv(IReadOnlyList<string> columns, IEnumerable<ResultRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', columns.Select(Escape)));

        foreach (var row in rows)
        {
            var cells = columns.Select(c =>
            {
                var value = row.GetRaw(c);
                return value is null ? "" : Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
            });
            sb.AppendLine(string.Join(',', cells));
        }

        return sb.ToString();
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}

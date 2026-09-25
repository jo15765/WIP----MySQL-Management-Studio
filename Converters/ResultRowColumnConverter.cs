using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using MySqlManagementStudio.Models;

namespace MySqlManagementStudio.Converters;

/// <summary>
/// Binds a ResultRow cell without putting the column name into an Avalonia binding path.
/// View/table columns often contain spaces, (), *, commas, etc. which crash ArgumentListParser.
/// </summary>
public sealed class ResultRowColumnConverter : IValueConverter
{
    private readonly string _columnName;

    public ResultRowColumnConverter(string columnName)
    {
        _columnName = columnName;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ResultRow row)
            return BindingOperations.DoNothing;

        return row[_columnName];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // ConvertBack receives the edited cell value; DataGrid applies it via the binding target.
        // We need the ResultRow — Avalonia typically calls ConvertBack with just the new value.
        // So editing is handled in CellEditEnded using Tag on the column; ConvertBack is a no-op path.
        return BindingOperations.DoNothing;
    }
}

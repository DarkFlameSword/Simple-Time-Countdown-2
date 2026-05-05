using System.Globalization;
using System.Windows.Data;

namespace TimeCountdown.Converters;

public sealed class RomanDateConverter : IValueConverter
{
    private static readonly string[] MonthNames =
    [
        "JANUARY", "FEBRUARY", "MARCH", "APRIL", "MAY", "JUNE",
        "JULY", "AUGUST", "SEPTEMBER", "OCTOBER", "NOVEMBER", "DECEMBER"
    ];

    public bool IncludeTime { get; set; } = true;

    public bool ShortMonth { get; set; } = true;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var local = value switch
        {
            DateTimeOffset dto => dto.LocalDateTime,
            DateTime dt => dt,
            _ => DateTime.Now
        };

        var month = MonthNames[local.Month - 1];
        if (ShortMonth)
        {
            month = month[..Math.Min(3, month.Length)];
        }

        var day = ToRoman(local.Day);
        var year = ToRoman(local.Year);

        if (!IncludeTime)
        {
            return $"{day} {month} {year}";
        }

        return $"{day} {month} {year} · {local:HH:mm} HRS";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    public static string ToRoman(int number)
    {
        if (number <= 0)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        ReadOnlySpan<string> symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < values.Length; i++)
        {
            while (number >= values[i])
            {
                sb.Append(symbols[i]);
                number -= values[i];
            }
        }

        return sb.ToString();
    }
}

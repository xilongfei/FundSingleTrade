using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FundSingleTrade.Shell;

/// <summary>将日期显示为可居中编辑的 yyyy-MM-dd 文本。</summary>
public sealed class DateTextConverter : IValueConverter
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>把日期转换成固定格式字符串。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is DateTime date ? date.ToString(DateFormat, CultureInfo.InvariantCulture) : string.Empty;
    }

    /// <summary>把有效日期文本转换回日期；输入错误时保留原绑定值。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return DateTime.TryParseExact(value as string, DateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : Binding.DoNothing;
    }
}

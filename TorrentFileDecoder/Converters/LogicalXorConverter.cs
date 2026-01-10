using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace TorrentFileDecoder.Converters;

public class LogicalXorConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var enumerable = values.Where(p => p is bool)
            .Cast<bool>()
            .ToArray();
        return enumerable.Length > 0 && enumerable.Aggregate((x, y) => x ^ y);
    }
}
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace KognaServer.Converters
{
    public class BoolToBrushConverter : IValueConverter
    {
        public IBrush TrueBrush { get; set; } = Brushes.Green;
        public IBrush FalseBrush { get; set; } = Brushes.Red;
        public IBrush NullBrush { get; set; } = Brushes.Gray;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? TrueBrush : FalseBrush;
            }
            return NullBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException("BoolToBrushConverter can only be used for one-way conversion.");
        }
    }

    public static class BoolToBrushConverters
    {
        public static readonly BoolToBrushConverter TrueToGreenFalseToRed = new()
        {
            TrueBrush = Brushes.LimeGreen,
            FalseBrush = Brushes.OrangeRed,
            NullBrush = Brushes.Gray
        };
    }
}

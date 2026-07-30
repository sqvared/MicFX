using System;
using System.Globalization;
using System.Windows.Data;

namespace MicFXApp.Converters
{
    /// <summary>Maps a 0.0-1.0 peak level to a pixel width for a meter bar, given a max width via parameter.</summary>
    public class LevelToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double level = value is float f ? f : (value is double d ? d : 0.0);
            double maxWidth = parameter != null ? System.Convert.ToDouble(parameter) : 200.0;
            level = Math.Clamp(level, 0.0, 1.0);
            return level * maxWidth;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

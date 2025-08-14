using System;
using System.Globalization;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters
{
    /// <summary>
    /// Converter to check if a string is not equal to another string
    /// </summary>
    public class NotEqualToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string source = value as string;
            string target = parameter as string;

            // If either is null, consider them different
            if (source == null || target == null)
                return true;

            // Return true if they're different (case-insensitive comparison)
            return !string.Equals(source, target, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
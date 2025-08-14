using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters // Ensure namespace matches your project
{
    /// <summary>
    /// Converts an integer count to Visibility. Visible if count > 0, Collapsed otherwise.
    /// </summary>
    [ValueConversion(typeof(int), typeof(Visibility))]
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            // Default to Collapsed if value is not an integer or is null
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Not typically needed for one-way bindings
            throw new NotImplementedException();
        }
    }
}
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters // Ensure namespace matches your project
{
    /// <summary>
    /// Converts an integer count to Visibility (inversely). Collapsed if count > 0, Visible otherwise.
    /// </summary>
    [ValueConversion(typeof(int), typeof(Visibility))]
    public class InverseCountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            // Default to Visible if value is not an integer or is null (show placeholder)
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Not typically needed for one-way bindings
            throw new NotImplementedException();
        }
    }
}
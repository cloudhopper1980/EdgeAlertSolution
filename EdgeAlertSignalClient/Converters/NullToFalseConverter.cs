// Converters/NullToFalseConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters
{
    /// <summary>
    /// Converts null to false, and non-null to true.
    /// Useful for enabling/disabling controls based on whether an object (like SelectedItem) is null.
    /// </summary>
    public class NullToFalseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null; // Returns true if value is NOT null, false if it IS null
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Not needed for IsEnabled binding
            throw new NotImplementedException();
        }
    }
}
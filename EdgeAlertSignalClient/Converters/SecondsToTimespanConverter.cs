using System;
using System.Globalization;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters
{
    /// <summary>
    /// Converts a double representing total seconds into a formatted TimeSpan string (hh:mm:ss).
    /// </summary>
    [ValueConversion(typeof(double), typeof(string))]
    public class SecondsToTimespanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double totalSeconds)
            {
                // Handle potential negative values or zero gracefully
                if (totalSeconds < 0) totalSeconds = 0;
                TimeSpan timeSpan = TimeSpan.FromSeconds(totalSeconds);
                // Format as hh:mm:ss
                return timeSpan.ToString(@"hh\:mm\:ss");
            }

            // Return empty string or placeholder if input is not a double
            return "00:00:00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // ConvertBack is not typically needed for display formatting
            throw new NotImplementedException();
        }
    }
}
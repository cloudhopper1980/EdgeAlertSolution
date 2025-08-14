using System;
using System.Globalization;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters
{
    /// <summary>
    /// Converts a TimeSpan value into a formatted string showing total hours, minutes, and seconds (e.g., "71h 55m 41s").
    /// Handles potential non-TimeSpan inputs gracefully.
    /// </summary>
    public class TotalTimeSpanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan timeSpan)
            {
                // Format the TimeSpan to show total hours, minutes, and seconds.
                return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
            }

            // Optional: Handle potential double input (representing seconds) if needed,
            // though binding directly to the TimeSpan property is cleaner.
            if (value is double totalSeconds)
            {
                if (totalSeconds < 0) totalSeconds = 0;
                TimeSpan tsFromDouble = TimeSpan.FromSeconds(totalSeconds);
                return $"{(int)tsFromDouble.TotalHours}h {tsFromDouble.Minutes}m {tsFromDouble.Seconds}s";
            }

            // Return default or empty string if input is not a TimeSpan or double
            return "0h 0m 0s";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // ConvertBack is typically not needed for one-way display formatting.
            throw new NotImplementedException("Cannot convert formatted time string back to TimeSpan.");
        }
    }
}
using System;
using System.Globalization;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters
{
    public class DateTimeToTimeStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                return dateTime.ToString("h:mm tt"); // Format as "7:00 AM"
            }
            return "12:00 AM"; // Default
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string timeString)
            {
                // Parse time string like "7:00 AM"
                if (DateTime.TryParseExact(timeString, "h:mm tt", CultureInfo.InvariantCulture,
                                         DateTimeStyles.None, out DateTime result))
                {
                    // Keep the current date but use the new time
                    return new DateTime(
                        DateTime.Today.Year,
                        DateTime.Today.Month,
                        DateTime.Today.Day,
                        result.Hour,
                        result.Minute,
                        0);
                }
            }

            // Default to current day at midnight
            return new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 0, 0, 0);
        }
    }
}
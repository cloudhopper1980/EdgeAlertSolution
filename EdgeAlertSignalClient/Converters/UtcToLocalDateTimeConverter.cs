using System;
using System.Globalization;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters // Ensure namespace matches your project
{
    /// <summary>
    /// Converts a DateTime assumed to be UTC into Local DateTime.
    /// </summary>
    [ValueConversion(typeof(DateTime), typeof(DateTime))]
    public class UtcToLocalDateTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime utcDateTime)
            {
                // If the kind is unspecified, assume it's UTC based on our knowledge of the source.
                // If it's already Local, return it as is. If it's UTC, convert it.
                if (utcDateTime.Kind == DateTimeKind.Unspecified)
                {
                    // Assuming UTC if unspecified, then convert to Local
                    return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, TimeZoneInfo.Local);
                }
                // If Kind is already Utc, convert it
                else if (utcDateTime.Kind == DateTimeKind.Utc)
                {
                    return utcDateTime.ToLocalTime();
                }
                // If Kind is already Local, return directly (shouldn't happen based on source, but safe to handle)
                else // DateTimeKind.Local
                {
                    return utcDateTime;
                }
            }
            // Return the original value if it's not a DateTime or is null
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // ConvertBack is not needed if the binding is one-way
            if (value is DateTime localDateTime)
            {
                return localDateTime.ToUniversalTime();
            }
            throw new NotImplementedException();
        }
    }
}
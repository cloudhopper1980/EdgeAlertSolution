// Converters/BooleanToStringConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters
{
    public class BooleanToStringConverter : IValueConverter
    {
        public string TrueValue { get; set; } = "ON";
        public string FalseValue { get; set; } = "OFF";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? TrueValue : FalseValue;
            }
            return FalseValue; // Default if not bool
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException(); // Not needed for one-way toggle display
        }
    }
}
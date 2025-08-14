using System;
using System.Globalization;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters // Ensure this namespace matches your project structure
{
    [ValueConversion(typeof(bool), typeof(bool))]
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            // Return false or Binding.DoNothing if the input is not a boolean
            return false; // Or return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false; // Or return Binding.DoNothing;
            // Or throw new NotSupportedException(); if you don't need ConvertBack
        }
    }
}
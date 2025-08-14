// Converters/NullableBooleanToIndeterminateConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters
{
    /// <summary>
    /// Converts a nullable boolean (bool?) to a nullable boolean suitable for
    /// controls that support an Indeterminate state (like CheckBox).
    /// True -> True (Checked)
    /// False -> False (Unchecked)
    /// Null -> Null (Indeterminate)
    /// </summary>
    [ValueConversion(typeof(bool?), typeof(bool?))]
    public class NullableBooleanToIndeterminateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // The input (value) is already bool?, which is what CheckBox.IsChecked expects
            // for its three states (true, false, null). So, just pass it through.
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Convert the CheckBox state (true, false, null) back to the source bool?
            return value;
        }
    }
}
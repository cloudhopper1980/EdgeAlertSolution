using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows;

namespace EdgeAlertSignalClient.Converters
{
    [ValueConversion(typeof(double), typeof(double))]
    public class ScreenWidthToWindowLeftConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || !(value is double))
                return DependencyProperty.UnsetValue;

            double screenWidth = (double)value;
            double margin = parameter != null && double.TryParse(parameter.ToString(), out double marginValue) ? marginValue : 0;

            return screenWidth - margin;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

using System;
using System.Globalization;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters
{
    public class PercentageWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double listViewWidth && parameter is string percentage)
            {
                double.TryParse(percentage, out double percent);
                return listViewWidth * percent;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

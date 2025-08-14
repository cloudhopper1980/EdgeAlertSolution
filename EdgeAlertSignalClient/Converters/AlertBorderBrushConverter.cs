using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Collections.ObjectModel;
using EdgeAlertSignalClient.Services;

namespace EdgeAlertSignalClient.Converters
{
    public class AlertBorderBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length != 2)
                return new SolidColorBrush(Color.FromRgb(192, 192, 192)); // Default color

            var alertHasBeenSet = values[0] as bool?;
            var responders = values[1] as ObservableCollection<Responder>;

            if (alertHasBeenSet == true && responders != null && responders.Count > 0)
                return new SolidColorBrush(Color.FromRgb(255, 191, 0)); // Amber for alert with responders

            if (alertHasBeenSet == true)
                return new SolidColorBrush(Color.FromRgb(205, 31, 37)); // Red for alert

            return new SolidColorBrush(Color.FromRgb(192, 192, 192)); // Default color
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

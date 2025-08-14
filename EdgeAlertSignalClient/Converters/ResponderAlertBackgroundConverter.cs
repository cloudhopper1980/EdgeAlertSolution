using EdgeAlertSignalClient.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System;

namespace EdgeAlertSignalClient.Converters
{
    public class ResponderAlertBackgroundConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length != 2)
                return new SolidColorBrush(Color.FromRgb(205, 234, 251)); // default color

            if (values[1] is ObservableCollection<Responder> responders)
            {
                bool hasResponder = false;
                foreach (var responder in responders)
                {
                    if (!string.IsNullOrEmpty(responder.ResponderName) || !string.IsNullOrEmpty(responder.ResponderCancelled))
                    {
                        hasResponder = true;
                        break;
                    }
                }

                if (hasResponder)
                    return new SolidColorBrush(Color.FromRgb(255, 191, 0)); // amber
            }

            if (values[0] is string alertValue && !string.IsNullOrEmpty(alertValue))
                return new SolidColorBrush(Color.FromRgb(205, 31, 37)); // red

            return new SolidColorBrush(Color.FromRgb(205, 234, 251)); // default color
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
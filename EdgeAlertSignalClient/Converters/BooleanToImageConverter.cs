using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace EdgeAlertSignalClient.Converters
{
    public class BooleanToImageConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 2 || !(values[0] is bool) || !(values[1] is string))
            {
                return null;
            }

            bool connected = (bool)values[0];
            string lastOnline = (string)values[1];

            if (connected)
            {
                if (lastOnline == "Online")
                {
                    return new BitmapImage(new Uri("/Resources/green_icon_small.png", UriKind.Relative));
                }
                else if (lastOnline.StartsWith("Away "))
                {
                    return new BitmapImage(new Uri("/Resources/amber_icon_small.png", UriKind.Relative));
                }
            }
            else
            {
                return new BitmapImage(new Uri("/Resources/red_icon_small.png", UriKind.Relative));
            }

            // Fallback if none of the conditions are met
            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
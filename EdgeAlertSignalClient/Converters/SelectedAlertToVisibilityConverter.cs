using EdgeAlertSignalClient.Services;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters
{
    public class SelectedAlertToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var user = values[0] as EdgeUserViewModel;

            if (user is null) return Visibility.Collapsed;

            var currentUserName = values[1] as string;
            var currentHostName = values[2] as string;

            // If there is an alert, the alert isn't triggered by current user and the user isn't already listed as a Responder by both name and host name, return visible
            if (!string.IsNullOrEmpty(user.Alert)
                && !(user.UserName.ToUpper() == currentUserName.ToUpper() && user.HostName.ToUpper() == currentHostName.ToUpper())
                && !user.Responders.Any(r => string.Equals(r.ResponderName, currentUserName, StringComparison.OrdinalIgnoreCase)
                                              && string.Equals(r.ResponderHostName, currentHostName, StringComparison.OrdinalIgnoreCase)))
            {
                return Visibility.Visible;
            }

            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

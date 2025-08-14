using EdgeAlertSignalClient.Models;
using EdgeAlertSignalClient.Services;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace EdgeAlertSignalClient.Converters
{
    /// <summary>
    /// Converter to check if string is null/empty and convert to boolean
    /// </summary>
    public class StringToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter to check if a collection of Responders contains only empty responders
    /// </summary>
    public class HasEmptyResponderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var responders = value as ObservableCollection<Responder>;
            if (responders == null || responders.Count == 0)
                return true;

            // Check if all responders are empty
            return responders.All(r =>
                string.IsNullOrEmpty(r.AlertTime) &&
                string.IsNullOrEmpty(r.ResponderName) &&
                string.IsNullOrEmpty(r.ResponderHostName) &&
                string.IsNullOrEmpty(r.AlertName) &&
                string.IsNullOrEmpty(r.ResponderCancelled));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter to check if a collection of Responders contains any non-empty responders
    /// </summary>
    public class HasNonEmptyResponderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var responders = value as ObservableCollection<Responder>;
            if (responders == null || responders.Count == 0)
                return false;

            // Check if any non-empty responders exist
            return responders.Any(r =>
                !string.IsNullOrEmpty(r.ResponderName) ||
                !string.IsNullOrEmpty(r.ResponderHostName));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
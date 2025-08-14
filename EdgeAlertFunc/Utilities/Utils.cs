using System;
using System.Globalization;

namespace EdgeAlertFunc
{
    public static class Utils
    {
        public static string ConvertUtcToGmtWithDst(DateTime utcDateTime)
        {
            // Determine the time zone information for GMT
            TimeZoneInfo gmtTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");

            // Check if DST is in effect for the specified UTC time
            bool isDstInEffect = gmtTimeZone.IsDaylightSavingTime(utcDateTime);

            // Convert the UTC time to GMT time
            DateTime gmtDateTime = utcDateTime.ToUniversalTime().AddHours(isDstInEffect ? 1 : 0);

            // Return the GMT time in the same format as the input UTC time
            return gmtDateTime.ToString("dd/MM/yyyy HH:mm:ss");
        }

        public static DateTime ConvertUtcToGmtWithDstToDateTime(DateTime utcDateTime)
        {
            // Determine the time zone information for GMT
            TimeZoneInfo gmtTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");

            // Check if DST is in effect for the specified UTC time
            bool isDstInEffect = gmtTimeZone.IsDaylightSavingTime(utcDateTime);

            // Convert the UTC time to GMT time
            DateTime gmtDateTime = utcDateTime.ToUniversalTime().AddHours(isDstInEffect ? 1 : 0);

            return gmtDateTime;
        }

        public static string ConvertDateFormat(string inputString)
        {
            string inputFormat = "dd/MM/yyyy HH:mm:ss";
            string outputFormat = "ddMMyyyyhhmmss";
            DateTime dateTime;

            if (DateTime.TryParseExact(inputString, inputFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
            {
                return dateTime.ToString(outputFormat);
            }
            else
            {
                return null;
            }
        }
    }
}


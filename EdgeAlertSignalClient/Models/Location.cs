using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace EdgeAlertSignalClient.Models
{
    public class Location
    {
        [JsonPropertyName("ODS")]
        public string ODS { get; set; }

        [JsonPropertyName("practice")]
        public string Practice { get; set; }

        [JsonPropertyName("siteName")]
        public string SiteName { get; set; }

        [JsonPropertyName("ipAddressLow")]
        public string IpAddressLow { get; set; }

        [JsonPropertyName("ipAddressHigh")]
        public string IpAddressHigh { get; set; }

        [JsonPropertyName("domain")]
        public List<string> Domains { get; set; }

        [JsonPropertyName("rooms")]
        public ObservableCollection<string> Rooms { get; set; }
    }

    public class RootObject
    {
        [JsonPropertyName("Locations")]
        public ObservableCollection<Location> Locations { get; set; }
    }
}

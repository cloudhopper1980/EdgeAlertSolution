using CommunityToolkit.Mvvm.ComponentModel;
using EdgeAlertSignalClient.Handlers;
using EdgeAlertSignalClient.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Stores
{
    public partial class RoomStore : ObservableObject
    {
        [ObservableProperty]
        public string _room;

        [ObservableProperty]
        public string _oDS;

        [ObservableProperty]
        public bool _stationaryDevice;

        [ObservableProperty]
        public EdgeUserViewModel _user;

        [ObservableProperty]
        public bool _forceHide;

        [ObservableProperty]
        public bool _blocked;

        private readonly JSONHandler _jsonHandler;
        private readonly ILogger<RoomStore> _log;

        public RoomStore(JSONHandler jsonHandler,
                        ILogger<RoomStore> ilogger)
        {
            _jsonHandler = jsonHandler;
            _log = ilogger;
        }

        /// <summary>
                    /// Gets the actual ODS code based on the currently stored Practice Name (_oDS property).
                    /// </summary>
                    /// <returns>The ODS code string, or string.Empty if not found or invalid.</returns>
        public string GetTrueODS()
        {
            // *** ADDED LOGGING: Log the value of _oDS (Practice Name) being used ***
            // Note: Accessing the backing field directly if _oDS is auto-property,
            // or the property itself if manually implemented with backing field.
            // Assuming auto-property for this example:
            string currentPracticeName = ODS; // Read the property value
                                              // Consider adding a null check/log for _jsonHandler if it could be null here
                                              // _log?.LogDebug($"GetTrueODS called. Current Practice Name in store (ODS property): '{currentPracticeName}'"); // Requires injecting ILogger into RoomStore

            if (string.IsNullOrEmpty(currentPracticeName) || _jsonHandler == null)
            {
                _log?.LogWarning($"GetTrueODS: Returning Empty because Practice Name is '{currentPracticeName}' or JSONHandler is null.");
                return string.Empty; // Return empty if practice name isn't set or handler isn't available
            }

            // Use the currentPracticeName for the lookup
            var location = _jsonHandler.GetLocationByPractice(currentPracticeName);
            string foundOds = location?.ODS ?? string.Empty;

            // *** ADDED LOGGING: Log the result of the lookup ***
            _log?.LogDebug($"GetTrueODS: Looked up Practice '{currentPracticeName}', found ODS: '{foundOds}'.");

            return foundOds; // Return ODS code or empty if not found
        }
    }
}

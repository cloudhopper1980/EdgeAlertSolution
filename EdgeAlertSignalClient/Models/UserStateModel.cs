using System;

namespace EdgeAlertSignalClient.Models
{
    /// <summary>
    /// Represents the possible states a user can be in
    /// </summary>
    public enum UserState
    {
        Active,     // User is actively using the application
        Away,       // User is away (no activity for defined period)
        Offline     // Application is closed or disconnected
    }

    /// <summary>
    /// Represents a single state transition event
    /// </summary>
    public class UserStateLog
    {
        /// <summary>
        /// Unique identifier for this log entry
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // REMOVED: public string UserID { get; set; }

        /// <summary>
        /// The user's login name
        /// </summary>
        public string UserName { get; set; } // Keep

        /// <summary>
        /// The machine identifier (hostname)
        /// </summary>
        public string MachineID { get; set; } // Keep

        /// <summary>
        /// The ODS code for the user's location
        /// </summary>
        public string ODS { get; set; }

        /// <summary>
        /// The state the user transitioned to
        /// </summary>
        public UserState State { get; set; }

        /// <summary>
        /// The timestamp when this state began (in UTC)
        /// </summary>
        public DateTime StateStartTime { get; set; }

        /// <summary>
        /// Duration in seconds of the previous state before this transition
        /// </summary>
        public double? DurationSeconds { get; set; }

        /// <summary>
        /// Optional context about this state change (e.g., "Application Start", "User Lock")
        /// </summary>
        public string Context { get; set; }

        /// <summary>
        /// Creates a new UserStateLog with the current time as the start time
        /// </summary>
        public UserStateLog()
        {
            StateStartTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Creates a new UserStateLog with specified parameters - Updated Constructor
        /// </summary>
        public UserStateLog(string userName, string machineId, string ods, UserState state, string context = null) // Updated constructor signature
        {
            UserName = userName;
            MachineID = machineId;
            ODS = ods;
            State = state;
            StateStartTime = DateTime.UtcNow;
            Context = context;
        }
    }
}
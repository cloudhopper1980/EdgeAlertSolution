using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using EdgeAlertSignalClient.Models;
using EdgeAlertSignalClient.Stores;
// using EdgeAlertSignalClient.Handlers; // No longer needed if FeatureSwitchHandler is removed
using EdgeAlertSignalClient.Utilities;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using System.Collections.Concurrent; // Added back if logLock uses it, but List<> is fine with manual lock

namespace EdgeAlertSignalClient.Services
{
    /// <summary>
    /// Service responsible for monitoring user state (Active, Away, Offline)
    /// and logging state transitions for reporting purposes.
    /// Relies on FeatureSettingsCache to determine if logging is enabled.
    /// </summary>
    public class UserStateMonitorService : IDisposable
    {
        // Import Win32 function to detect last input time
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private readonly ILogger<UserStateMonitorService> _logger;
        private readonly RoomStore _roomStore;
        private readonly Utils _utils;
        private readonly UserTimeLogService _userTimeLogService;
        private readonly FeatureSettingsCache _featureSettingsCache;
        private const string TimeTrackingFeatureName = "UserTimeTracking-Feature";
        private bool _isTimeLoggingEffectivelyEnabled = false; // Tracks effective status

        // Collection to store logs that haven't been sent to the server yet
        private readonly List<UserStateLog> _pendingLogs = new List<UserStateLog>();

        // Timer for checking user activity
        private DispatcherTimer _activityTimer;

        // Timer for sending batched logs
        private DispatcherTimer _sendLogsTimer;

        // Timeout for Away status
        private int _awayTimeoutSeconds = 180;

        // Last recorded state
        private UserState _currentState = UserState.Active;

        // Timestamp of the last state change
        private DateTime _lastStateChangeTime;

        // Timestamp of the last log send attempt
        private DateTime _lastLogSendTime;

        // Lock object for thread safety on _pendingLogs
        private readonly object _logLock = new object();

        private bool _initialLogAdded = false; // Prevents logging duplicate initial states
        private bool _isNetworkChangeSubscribed = false; // Track subscription status
        private bool _isServiceInitialized = false; // Prevent multiple initializations

        /// <summary>
        /// Initializes a new instance of the UserStateMonitorService
        /// </summary>
        public UserStateMonitorService(
            ILogger<UserStateMonitorService> logger,
            RoomStore roomStore,
            Utils utils,
            UserTimeLogService userTimeLogService,
            FeatureSettingsCache featureSettingsCache)
        {
            _logger = logger;
            _roomStore = roomStore;
            _utils = utils;
            _userTimeLogService = userTimeLogService;
            _featureSettingsCache = featureSettingsCache;
            _lastStateChangeTime = DateTime.UtcNow;
            _lastLogSendTime = DateTime.UtcNow;

            // Set Away Timeout based on Build Configuration
#if DEBUG
            _awayTimeoutSeconds = 10; // Set to 10 seconds for DEBUG
            _logger.LogInformation($"UserStateMonitorService: Using DEBUG Away Timeout: {_awayTimeoutSeconds} seconds.");
#else
            // For RELEASE, read the setting from registry
            int awayTimeoutMinutes = 3; // Default before trying registry
            try {
                 awayTimeoutMinutes = Task.Run(() => _utils.GetRegistryValueAsync<int>("AwayTimeoutMinutes", 3, defaultValue: 3)).Result;
            } catch (AggregateException aggEx) {
                 _logger.LogError(aggEx.Flatten(), "Error retrieving AwayTimeoutMinutes from registry (sync wait). Defaulting to 3.");
                 awayTimeoutMinutes = 3;
            } catch (Exception ex) {
                  _logger.LogError(ex, "Unexpected error retrieving AwayTimeoutMinutes from registry. Defaulting to 3.");
                 awayTimeoutMinutes = 3;
            }
            if (awayTimeoutMinutes < 1 || awayTimeoutMinutes > 60) // Validation
            {
                 _logger.LogWarning($"Invalid AwayTimeoutMinutes ({awayTimeoutMinutes}) read from registry. Defaulting to 3 minutes.");
                 awayTimeoutMinutes = 3;
            }
             _awayTimeoutSeconds = awayTimeoutMinutes * 60;
             _logger.LogInformation($"UserStateMonitorService: Using RELEASE Away Timeout: {awayTimeoutMinutes} minutes ({_awayTimeoutSeconds} seconds).");
#endif
            _logger.LogInformation("UserStateMonitorService: Instance created. Waiting for InitializeAsync.");
        }

        /// <summary>
        /// Asynchronously initializes the service after dependencies (like cache) are ready.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isServiceInitialized) return;
            _logger.LogInformation("UserStateMonitorService: Starting asynchronous initialization...");

            // --- Wait for Cache Initialization FIRST ---
            _logger.LogInformation("UserStateMonitorService: Waiting for FeatureSettingsCache to initialize...");
            bool cacheInitializedSuccessfully = false;
            try
            {
                // Await the result of the cache initialization
                cacheInitializedSuccessfully = await _featureSettingsCache.EnsureInitializedAsync(); // <-- Await the Task<bool>
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UserStateMonitorService: Exception occurred while waiting for FeatureSettingsCache initialization.");
                // cacheInitializedSuccessfully remains false
            }

            if (!cacheInitializedSuccessfully)
            {
                _logger.LogError("UserStateMonitorService: FeatureSettingsCache failed to initialize. Monitoring will be disabled.");
                _isTimeLoggingEffectivelyEnabled = false; // Ensure disabled state
                _isServiceInitialized = true; // Mark as initialized (though failed) to prevent retries
                return; // Exit initialization
            }
            _logger.LogInformation("UserStateMonitorService: FeatureSettingsCache initialized successfully.");
            // --- End Cache Wait ---


            // Now proceed with checking the monitoring status, cache is ready
            await CheckAndUpdateMonitoringStatusAsync(); // <-- This now uses the initialized cache

            // Handle initial network state only *if* monitoring should be active
            if (_isTimeLoggingEffectivelyEnabled)
            {
                // Subscribe only if enabled
                SubscribeToNetworkChanges();
                // Process current network state
                try
                {
                    HandleNetworkStatusChange(NetworkInterface.GetIsNetworkAvailable());
                }
                catch (Exception netEx)
                {
                    _logger.LogError(netEx, "Error checking initial network status during InitializeAsync.");
                    // Potentially default to offline state?
                    LogStateTransition(UserState.Offline, "Initialization - Network Status Error");
                }
            }

            _isServiceInitialized = true; // Mark as successfully initialized
            _logger.LogInformation("UserStateMonitorService: Asynchronous initialization complete.");
        }

        /// <summary>
        /// Checks the effective time logging status from the cache and updates monitoring state.
        /// </summary>
        private async Task CheckAndUpdateMonitoringStatusAsync()
        {
            string currentUser = _utils?.GetComputerName();
            string currentOds = _roomStore?.GetTrueODS();

            if (string.IsNullOrWhiteSpace(currentUser) || string.IsNullOrWhiteSpace(currentOds) || currentOds == "UNKNOWN_ODS")
            {
                _logger.LogWarning("CheckAndUpdateMonitoringStatusAsync: Cannot determine effective status - User ('{User}') or ODS ('{ODS}') is unknown/invalid. Disabling monitoring.", currentUser, currentOds);
                if (_isTimeLoggingEffectivelyEnabled) // If it *was* enabled
                {
                    _isTimeLoggingEffectivelyEnabled = false;
                    StopMonitoringAndUnsubscribe();
                }
                return;
            }

            bool newEffectiveStatus = false;
            try
            {
                // Get effective status from cache for the specific feature
                newEffectiveStatus = _featureSettingsCache.GetEffectiveSetting(
                    TimeTrackingFeatureName,
                    currentUser,
                    currentOds
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting effective setting from cache. Defaulting to disabled.");
                newEffectiveStatus = false; // Default to disabled on error
            }


            // Only log/act if the status has actually changed
            if (newEffectiveStatus != _isTimeLoggingEffectivelyEnabled)
            {
                _logger.LogInformation("Effective Time Logging Status CHANGED for User='{User}', ODS='{ODS}' to: {Status}", currentUser, currentOds, newEffectiveStatus);
                _isTimeLoggingEffectivelyEnabled = newEffectiveStatus;

                if (_isTimeLoggingEffectivelyEnabled)
                {
                    InitializeAndStartTimers();
                    SubscribeToNetworkChanges(); // Subscribe now that it's enabled
                }
                else
                {
                    StopMonitoringAndUnsubscribe(); // Stop timers & unsubscribe
                }
            }
            await Task.CompletedTask; // Return completed task
        }

        private void SubscribeToNetworkChanges()
        {
            if (!_isNetworkChangeSubscribed)
            {
                NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
                _isNetworkChangeSubscribed = true;
                _logger.LogInformation("Subscribed to NetworkAvailabilityChanged events.");
            }
        }

        private void StopMonitoringAndUnsubscribe()
        {
            if (_activityTimer?.IsEnabled == true)
            {
                _activityTimer.Stop();
                _logger.LogInformation("Activity Timer Stopped.");
            }
            if (_sendLogsTimer?.IsEnabled == true)
            {
                _sendLogsTimer.Stop();
                _logger.LogInformation("Send Logs Timer Stopped.");
            }

            if (_isNetworkChangeSubscribed)
            {
                NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
                _isNetworkChangeSubscribed = false;
                _logger.LogInformation("Unsubscribed from NetworkAvailabilityChanged events.");
            }
            // Reset the state variables when monitoring stops
            // _currentState = UserState.Active; // Or perhaps Offline? Needs consideration
            // _initialLogAdded = false;
            // lock(_logLock) { _pendingLogs.Clear(); } // Clear pending logs if they shouldn't be sent
        }


        /// <summary>
        /// Logs the initial application state if monitoring is enabled.
        /// </summary>
        public void LogApplicationStart()
        {
            // This is called by MainViewModel AFTER InitializeAsync has run

            // Only proceed with logging if effectively enabled AND initial log hasn't happened
            if (!_isTimeLoggingEffectivelyEnabled || _initialLogAdded) return;

            try
            {
                bool isNetworkAvailable = NetworkInterface.GetIsNetworkAvailable();
                UserState initialState = isNetworkAvailable ? UserState.Active : UserState.Offline;
                string context = isNetworkAvailable ? "Application Start" : "Application Start - No Network";
                _logger.LogInformation($"Attempting to log explicit Application Start state: {initialState}");
                LogStateTransition(initialState, context); // This method now checks enablement internally
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during explicit Application Start logging.");
            }
        }

        private async void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            _logger.LogInformation($"Network availability changed event received. IsAvailable: {e.IsAvailable}");
            // Re-check status before proceeding
            await CheckAndUpdateMonitoringStatusAsync();

            if (!_isTimeLoggingEffectivelyEnabled)
            {
                _logger.LogDebug("OnNetworkAvailabilityChanged: Monitoring is disabled after re-check, skipping network state logging.");
                return;
            }
            HandleNetworkStatusChange(e.IsAvailable);
        }


        private void HandleNetworkStatusChange(bool isNetworkAvailable)
        {
            // No need to check _isTimeLoggingEffectivelyEnabled here,
            // as the caller (OnNetworkAvailabilityChanged) already does.
            try
            {
                if (!isNetworkAvailable && _currentState != UserState.Offline)
                {
                    LogStateTransition(UserState.Offline, "Network Unavailable");
                }
                else if (isNetworkAvailable && _currentState == UserState.Offline)
                {
                    LogStateTransition(UserState.Active, "Network Available");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling network status change: {Message}", ex.Message);
            }
        }

        private void InitializeAndStartTimers()
        {
            if (_activityTimer == null)
            {
                _activityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                _activityTimer.Tick += ActivityTimer_Tick;
                _logger.LogDebug("Activity Timer Initialized.");
            }
            if (!_activityTimer.IsEnabled)
            {
                _activityTimer.Start();
                _logger.LogInformation("Activity Timer Started.");
            }


            if (_sendLogsTimer == null)
            {
                _sendLogsTimer = new DispatcherTimer();
#if DEBUG
                _sendLogsTimer.Interval = TimeSpan.FromSeconds(30);
                _logger.LogInformation("UserStateMonitorService: Send logs timer set to 30 seconds (DEBUG mode)");
#else
                    _sendLogsTimer.Interval = TimeSpan.FromMinutes(3);
                    _logger.LogInformation("UserStateMonitorService: Send logs timer set to 3 minutes (RELEASE mode)");
#endif
                _sendLogsTimer.Tick += SendLogsTimer_Tick;
                _logger.LogDebug("Send Logs Timer Initialized.");
            }
            if (!_sendLogsTimer.IsEnabled)
            {
                _sendLogsTimer.Start();
                _logger.LogInformation("Send Logs Timer Started.");
            }
        }

        // REMOVE the old IsFeatureEnabled method completely
        // private bool IsFeatureEnabled()
        // {
        //     return _featureSwitchHandler.IsFeatureEnabled("UserTimeTracking-Feature");
        // }

        private void ActivityTimer_Tick(object sender, EventArgs e)
        {
            // Check effective status at the start of the tick
            if (!_isTimeLoggingEffectivelyEnabled || _currentState == UserState.Offline) return;

            try
            {
                var idleSeconds = GetSecondsSinceLastInput();
                if (_currentState == UserState.Active && idleSeconds > _awayTimeoutSeconds)
                {
                    LogStateTransition(UserState.Away, $"Idle for {idleSeconds} seconds");
                }
                else if (_currentState == UserState.Away && idleSeconds <= _awayTimeoutSeconds)
                {
                    LogStateTransition(UserState.Active, "Activity detected");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in activity timer: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Gets the number of seconds since the last user input event.
        /// </summary>
        private int GetSecondsSinceLastInput()
        {
            var lastInputInfo = new LASTINPUTINFO();
            lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);
            if (GetLastInputInfo(ref lastInputInfo))
            {
                // Use Environment.TickCount64 for potentially longer uptimes
                long systemUptimeMillis = Environment.TickCount64;
                long lastInputMillis = lastInputInfo.dwTime;

                // Handle potential TickCount wrap-around (though less likely with TickCount64)
                long idleTimeMillis = systemUptimeMillis - lastInputMillis;
                if (idleTimeMillis < 0)
                {
                    // This case should be rare with TickCount64 but handle defensively
                    idleTimeMillis = long.MaxValue - lastInputMillis + systemUptimeMillis;
                }

                return (int)(idleTimeMillis / 1000);
            }
            _logger.LogWarning("GetLastInputInfo failed. Returning 0 idle seconds.");
            return 0;
        }

        private async void SendLogsTimer_Tick(object sender, EventArgs e)
        {
            // Check effective status before sending
            if (!_isTimeLoggingEffectivelyEnabled) return;

            try
            {
                await SendPendingLogs();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in send logs timer: {Message}", ex.Message);
            }
        }

        public void LogStateTransition(UserState newState, string context = null)
        {
            // Check effective status before logging
            if (!_isTimeLoggingEffectivelyEnabled)
            {
                // _logger.LogTrace("LogStateTransition: Skipped logging state {NewState} because time logging is effectively disabled.", newState);
                return;
            }

            if (_initialLogAdded && newState == _currentState) return; // Skip redundant

            try
            {
                var now = DateTime.UtcNow;
                string odsValue = _roomStore.GetTrueODS();
                if (string.IsNullOrWhiteSpace(odsValue) || odsValue == "UNKNOWN_ODS")
                {
                    _logger.LogWarning("LogStateTransition: Cannot log state transition - ODS is unknown.");
                    return;
                }
                double durationSeconds = (now - _lastStateChangeTime).TotalSeconds;
                var logEntry = new UserStateLog
                {
                    UserName = _utils.GetComputerName(),
                    MachineID = _utils.GetHostName(),
                    ODS = odsValue,
                    State = newState,
                    StateStartTime = now,
                    DurationSeconds = durationSeconds > 0 ? durationSeconds : (double?)null,
                    Context = context
                };
                lock (_logLock) { _pendingLogs.Add(logEntry); }
                UserState previousState = _currentState;
                _currentState = newState;
                _lastStateChangeTime = now;
                if (!_initialLogAdded) { _initialLogAdded = true; _logger.LogInformation("Initial log added flag SET."); }
                _logger.LogInformation($"State transition LOGGED: {previousState} -> {newState}, ODS: {odsValue}, duration: {durationSeconds:F1}s, context: {context}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging state transition to {NewState}.", newState);
            }
        }

        public async Task SendPendingLogs()
        {
            // Check effective status before sending
            if (!_isTimeLoggingEffectivelyEnabled)
            {
                _logger.LogDebug("SendPendingLogs: Skipped sending logs because time logging is effectively disabled.");
                return;
            }

            if (_pendingLogs.Count == 0) return;

            List<UserStateLog> logsToSend = null;
            try
            {
                lock (_logLock)
                {
                    if (_pendingLogs.Count == 0) return;
                    logsToSend = new List<UserStateLog>(_pendingLogs);
                    _pendingLogs.Clear();
                }
                _logger.LogInformation($"Attempting to send {logsToSend.Count} logs to server via UserTimeLogService.");
                bool success = await _userTimeLogService.SendLogsAsync(logsToSend);
                if (success)
                {
                    _logger.LogInformation($"Successfully sent {logsToSend.Count} logs.");
                    _lastLogSendTime = DateTime.UtcNow;
                }
                else
                {
                    _logger.LogError($"Failed to send {logsToSend.Count} logs. Re-queueing them.");
                    lock (_logLock) { _pendingLogs.InsertRange(0, logsToSend); }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending pending logs");
                if (logsToSend != null && logsToSend.Count > 0)
                {
                    _logger.LogWarning($"Re-queueing {logsToSend.Count} logs due to exception during sending.");
                    lock (_logLock) { _pendingLogs.InsertRange(0, logsToSend); }
                }
            }
        }


        public async Task HandleApplicationClosing()
        {
            // Recheck status one last time
            await CheckAndUpdateMonitoringStatusAsync();

            if (!_isTimeLoggingEffectivelyEnabled)
            {
                _logger.LogInformation("HandleApplicationClosing: Time logging is disabled. Skipping final log/send.");
                return;
            }

            try
            {
                LogStateTransition(UserState.Offline, "Application Exit");
                await SendPendingLogs();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in application closing handler: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Disposes resources
        /// </summary>
        public void Dispose()
        {
            StopMonitoringAndUnsubscribe(); // Stops timers and unsubscribes

            _activityTimer = null;
            _sendLogsTimer = null;

            _logger.LogInformation("UserStateMonitorService: Disposed");
        }
    }
}
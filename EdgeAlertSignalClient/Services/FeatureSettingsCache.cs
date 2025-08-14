using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading; // For DispatcherTimer
using EdgeAlertSignalClient.Services; // Assuming IEdgeAlertService is here
using EdgeAlertSignalClient.Models; // <<< Ensure this using points to where FeatureSettingInfo is defined
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Windows; // For thread-safe dictionary


namespace EdgeAlertSignalClient.Services // Or appropriate namespace
{
    /// <summary>
    /// Manages caching and retrieval of feature settings from the backend.
    /// Implements the User > ODS > Global hierarchy logic client-side based on cached data.
    /// Handles scheduled cache refreshes.
    /// </summary>
    public class FeatureSettingsCache : IDisposable
    {
        private readonly ILogger<FeatureSettingsCache> _log;
        private readonly IEdgeAlertService _edgeAlertService; // To fetch data
        private readonly DispatcherTimer _refreshTimer;
        // Target refresh time(e.g., 5:00 AM local time)
        private readonly TimeSpan _targetRefreshTimeOfDay = new TimeSpan(5, 0, 0);
        private readonly TimeSpan _initialDelay = TimeSpan.FromMinutes(1); // Delay before first refresh

        // Cache: Dictionary<FeatureName, Dictionary<ScopeIdentifier, IsEnabled>>
        private ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _featureCache =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);

        private const string TimeTrackingFeatureName = "UserTimeTracking-Feature";

        private TaskCompletionSource<bool> _initializationTcs;
        private bool _isInitialized = false;
        private readonly object _initializationLock = new object();
        private Task _initializationTask;


        public FeatureSettingsCache(ILogger<FeatureSettingsCache> log, IEdgeAlertService edgeAlertService)
        {
            _log = log;
            _edgeAlertService = edgeAlertService;

            // Initialize timer but don't set Interval immediately
            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background);
            _refreshTimer.Tick += RefreshTimer_Tick;

            // Don't start the timer until initialized successfully
        }

        /// <summary>
        /// Calculates the interval until the next target refresh time (e.g., 5 AM).
        /// </summary>
        private TimeSpan CalculateIntervalToNextRefresh()
        {
            DateTime now = DateTime.Now;
            DateTime nextRefreshTime = now.Date.Add(_targetRefreshTimeOfDay);

            // If the target time has already passed for today, schedule for tomorrow
            if (now >= nextRefreshTime)
            {
                nextRefreshTime = nextRefreshTime.AddDays(1);
            }

            TimeSpan interval = nextRefreshTime - now;
            _log.LogInformation($"Next scheduled cache refresh at: {nextRefreshTime}. Interval: {interval}");
            return interval;
        }

        /// <summary>
        /// Timer Tick event handler for refreshing the cache daily.
        /// </summary>
        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            _log.LogInformation("Refresh timer ticked.");
            // Stop the timer briefly to prevent re-entrancy while refresh is running
            _refreshTimer.Stop();

            await RefreshCacheAsync("Scheduled Daily Refresh");

            // Reschedule the timer for the next day's target time
            _refreshTimer.Interval = CalculateIntervalToNextRefresh();
            _refreshTimer.Start(); // Restart the timer for the next cycle
        }

        /// <summary>
        /// Ensures the cache is initialized by fetching settings from the backend.
        /// Returns a Task<bool> indicating success (true) or failure (false) of initialization.
        /// Can be called multiple times safely.
        /// </summary>
        public Task<bool> EnsureInitializedAsync()
        {
            lock (_initializationLock)
            {
                if (_initializationTcs == null || _initializationTcs.Task.IsCompleted)
                {
                    _log.LogInformation("Initiating feature settings cache population...");
                    _initializationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                    _initializationTask = RefreshCacheAsync("Initial Load");

                    _initializationTask.ContinueWith(t => {
                        bool success = false;
                        if (t.IsCompletedSuccessfully)
                        {
                            _isInitialized = true;
                            _log.LogInformation("Cache Initialized.");
                            // Start the daily refresh timer AFTER successful initialization
                            Application.Current.Dispatcher.Invoke(() => // Ensure timer setup is on UI thread
                            {
                                _refreshTimer.Interval = CalculateIntervalToNextRefresh(); // Calculate initial interval
                                _refreshTimer.Start();
                                _log.LogInformation("Daily refresh timer started.");
                            });
                            success = true;
                        }
                        else
                        {
                            _log.LogError(t.Exception?.Flatten(), "Initial cache population failed.");
                            success = false;
                        }
                        _initializationTcs.TrySetResult(success);
                    }, TaskScheduler.Default);
                }
                else
                {
                    _log.LogDebug("Initialization task already running or created.");
                }
                return _initializationTcs.Task;
            }
        }

        // RefreshCacheAsync remains the same as provided in Phase 1
        public async Task RefreshCacheAsync(string reason)
        {
            _log.LogInformation("Starting feature settings cache refresh. Reason: {Reason}", reason);
            try
            {
                _log.LogInformation("RefreshCacheAsync: Calling backend to fetch all feature settings...");
                var allSettings = await _edgeAlertService.GetAllFeatureSettingsAsync();

                var newCache = new ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);

                if (allSettings != null)
                {
                    var groupedSettings = allSettings.GroupBy(s => s.FeatureName, StringComparer.OrdinalIgnoreCase);

                    foreach (var group in groupedSettings)
                    {
                        var featureName = group.Key;
                        var scopeSettings = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                        foreach (var setting in group)
                        {
                            scopeSettings.TryAdd(setting.ScopeIdentifier, setting.IsEnabled);
                        }
                        newCache.TryAdd(featureName, scopeSettings);
                    }
                    _log.LogInformation("Successfully refreshed cache with {Count} features from backend.", newCache.Count);
                }
                else
                {
                    _log.LogError("Received NULL list from backend during cache refresh. Cache will be cleared.");
                }
                Interlocked.Exchange(ref _featureCache, newCache);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to refresh feature settings cache. Previous cache state retained (if any).");
                // Do not re-throw here for scheduled refresh, allow timer to reschedule
                if (reason == "Initial Load")
                {
                    throw; // Re-throw ONLY for initial load so EnsureInitializedAsync fails correctly
                }
            }
        }

        // IsInitialized property remains the same
        public bool IsInitialized => _isInitialized;

        // GetEffectiveSetting remains the same as provided in Phase 1
        /// <summary>
        /// Calculates the effective feature setting status based on cached data and desired default behavior.
        /// Hierarchy: User > ODS > Global.
        /// Default: Enabled for all features EXCEPT UserTimeTracking-Feature (defaults to disabled).
        /// Handles cases where the cache is not initialized or the specific setting is not found.
        /// </summary>
        /// <param name="featureName">The name of the feature (e.g., "InternetCheck-Feature").</param>
        /// <param name="userName">The username (will be normalized to uppercase).</param>
        /// <param name="odsCode">The ODS code.</param>
        /// <returns>True if the feature is effectively enabled, false otherwise.</returns>
        public bool GetEffectiveSetting(string featureName, string userName, string odsCode)
        {
            // Determine the default state FIRST based on the feature name
            bool defaultState = true; // Default to ENABLED for most features
            if (featureName.Equals(TimeTrackingFeatureName, StringComparison.OrdinalIgnoreCase))
            {
                defaultState = false; // Default Time Tracking specifically to DISABLED
            }

            // --- Handle Cache Initialization Failure ---
            if (!IsInitialized) // Check if cache is ready
            {
                // Log the specific default being applied
                _log.LogWarning("GetEffectiveSetting: Cache is not initialized. Defaulting Feature='{FeatureName}' to {DefaultState}.", featureName, defaultState);
                return defaultState; // Return the calculated default state
            }

            // --- Handle Invalid Parameters ---
            if (string.IsNullOrWhiteSpace(featureName) || string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(odsCode))
            {
                _log.LogWarning("GetEffectiveSetting called with null or empty parameters (Feature='{Feature}', User='{User}', ODS='{ODS}'). Defaulting to {DefaultState}.", featureName, userName, odsCode, defaultState);
                return defaultState; // Return the calculated default state
            }

            // --- Check Cache Hierarchy (User > ODS > Global) ---
            string normalizedUserName = userName.ToUpperInvariant();
            string userScope = $"USER_{normalizedUserName}";
            string odsScope = $"ODS_{odsCode}";
            string globalScope = "GLOBAL:";

            var currentCache = _featureCache; // Use the thread-safe cache instance

            // Try to find settings for the specific feature
            if (currentCache.TryGetValue(featureName, out var featureSettings))
            {
                // 1. Check User Setting
                if (featureSettings.TryGetValue(userScope, out bool userEnabled))
                {
                    _log.LogDebug("Effective Status (Feature: {Feature}, User: {User}, ODS: {ODS}) determined by User setting ({Scope}): {Status}", featureName, normalizedUserName, odsCode, userScope, userEnabled);
                    return userEnabled; // Explicit user setting found
                }

                // 2. Check ODS Setting
                if (featureSettings.TryGetValue(odsScope, out bool odsEnabled))
                {
                    _log.LogDebug("Effective Status (Feature: {Feature}, User: {User}, ODS: {ODS}) determined by ODS setting ({Scope}): {Status}", featureName, normalizedUserName, odsCode, odsScope, odsEnabled);
                    return odsEnabled; // Explicit ODS setting found
                }

                // 3. Check Global Setting
                if (featureSettings.TryGetValue(globalScope, out bool globalEnabled))
                {
                    _log.LogDebug("Effective Status (Feature: {Feature}, User: {User}, ODS: {ODS}) determined by Global setting ({Scope}): {Status}", featureName, normalizedUserName, odsCode, globalScope, globalEnabled);
                    return globalEnabled; // Explicit Global setting found
                }
            }
            else
            {
                // Log if the entire feature is missing from the cache
                _log.LogWarning("Feature '{FeatureName}' not found in the cache. Defaulting to {DefaultState}.", featureName, defaultState);
            }

            // --- Apply Default Logic if No Explicit Setting Found ---
            // Log the specific default being applied because no setting was found in the hierarchy
            _log.LogDebug("Effective Status (Feature: {Feature}, User: {User}, ODS: {ODS}) - No specific or global setting found in cache, using default: {DefaultState}.", featureName, normalizedUserName, odsCode, defaultState);
            return defaultState; // Return the calculated default state
        }

        // Dispose remains the same
        public void Dispose()
        {
            _refreshTimer?.Stop();
            _log.LogInformation("FeatureSettingsCache disposed, refresh timer stopped.");
            GC.SuppressFinalize(this);
        }
    }
}
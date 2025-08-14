using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EdgeAlertSignalClient.Handlers;
using EdgeAlertSignalClient.Models;
using EdgeAlertSignalClient.Services;
using EdgeAlertSignalClient.Stores;
using EdgeAlertSignalClient.Utilities;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.VisualBasic.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Message;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Threading;
using Microsoft.Win32;
using Serilog.Core;
using System.Windows.Input;
using System.Text;
using EdgeAlertSignalClient.Extensions;
using Azure.Data.Tables;
using static EdgeAlertSignalClient.Services.JoinTables;
using System.Reflection;
using Microsoft.VisualBasic.Logging;
using System.Net.NetworkInformation;
using EdgeAlertSignalClient.Views;
using System.Net.Http;
using Azure;
using System.Text.Json;
using System.Net;
using System.ComponentModel.DataAnnotations;

namespace EdgeAlertSignalClient.ViewModels
{
    public partial class MainViewModel : ObservableValidator,
                                         IRecipient<ExitApplicationMessage>,
                                         IRecipient<ApplicationRestartMessage>,
                                         IRecipient<DoubleClickMessage>,
                                         IRecipient<MainViewModelMessage>
    {
        private readonly ILogger<MainViewModel> _log;
        private IEdgeAlertService _edgeAlertService;
        private readonly JSONHandler _jSONHandler;
        private readonly NetworkHandler _networkHandler;
        private readonly Utils _utils;
        private readonly AlertStore _alertStore;
        private readonly RoomStore _roomStore;
        private readonly AzureHandler _azureHandler;
        private readonly IPAddressMonitor _ipAddressMonitor;
        private readonly NavigationStore _navigationStore;
        private readonly UserStateMonitorService _userStateMonitorService;
        private readonly FeatureSettingsCache _featureSettingsCache;
        private readonly AudioDeviceService _audioDeviceService;        private const string AzureSyncFeatureName = "AzureSync-Feature";
        private const string AwayFeatureName = "Away-Feature";
        private const string AssetTableName = "Assets"; // Asset table name constant        // Fields for manually created instances
        private EdgeAlertTables _edgeAlertTables = null!;
        private ResponderTables _responderTables = null!;
        private AlertTables _alertTables = null!;
        private JoinTables _joinTables = null!;
        private SystemInfoTables _systemInfoTables = null!;
        private AssetTableService? _assetTableService;

        // --- Public Properties to expose instances ---
        public EdgeAlertTables EdgeAlertTablesInstance => _edgeAlertTables;
        public ResponderTables ResponderTablesInstance => _responderTables;
        public AlertTables AlertTablesInstance => _alertTables;
        public JoinTables JoinTablesInstance => _joinTables;
        public SystemInfoTables SystemInfoTablesInstance => _systemInfoTables;
        public AssetTableService? AssetTableServiceInstance => _assetTableService;

        //private readonly IPAddressHandler _ipAddressHandler;
        private HubConnection _connection = null!;
        private IDisposable _receiveEdgeAlertMessageSubscription = null!;
        private IDisposable _updateConnectedUsersSubscription = null!;
        private IDisposable _updateConnectedUserGroupsSubscription = null!;
        private IDisposable _pongSubscription = null!;
        private IDisposable _edgeAlertBlockSubscription = null!;
        private static SemaphoreSlim _updateSemaphore = new SemaphoreSlim(1, 1);        [ObservableProperty]
        public ObservableCollection<EdgeUserViewModel> _edgeUsers = new ObservableCollection<EdgeUserViewModel>();

        [ObservableProperty]
        public EdgeUser _initialUser = null!;

        [ObservableProperty]
        public EdgeUserViewModel _selectedEdgeUser = null!;

        [ObservableProperty]
        public ObservableCollection<Responder> _responders = new ObservableCollection<Responder>();

        [ObservableProperty]
        public string _userName = string.Empty;

        [ObservableProperty]
        public string _hostName = string.Empty;

        [ObservableProperty]
        public string _oDS = string.Empty;

        [ObservableProperty]
        public string _trueODS = string.Empty;

        [ObservableProperty]
        public string _loggedIn = string.Empty;

        [ObservableProperty]
        public ObservableCollection<string> _blankIPODS = new ObservableCollection<string>();

        [ObservableProperty]
        public string _selectedBlankIPODS = string.Empty;

        [ObservableProperty]
        public int _oDSIndex;

        [ObservableProperty]
        public bool _blankIPAddress;

        [ObservableProperty]
        public string _alert = string.Empty;

        [ObservableProperty]
        public ObservableCollection<string> _rooms = new ObservableCollection<string>();

        [ObservableProperty]
        public string _selectedRoom = string.Empty;

        [ObservableProperty]
        public int _roomIndex;

        [ObservableProperty]
        public string _highlightedUserName = string.Empty;

        [ObservableProperty]
        public string _highlightedHostName = string.Empty;

        [ObservableProperty]
        public bool _alertHasBeenSet;

        [ObservableProperty]
        public bool _responderHasBeenSet;

        [ObservableProperty]
        public bool _stationaryDevice;

        [ObservableProperty]
        public bool _stationaryDeviceIsEnabled;

        [ObservableProperty]
        public bool _roomEnabled = true;        [ObservableProperty]
        public string _lastOnline = string.Empty;

        [ObservableProperty]
        public bool _alertSounding = false;

        [ObservableProperty]
        public bool _isMachineLocked = false;

        [ObservableProperty]
        public Visibility _stopButtonVisibility = Visibility.Hidden;

        private bool _blocked;
        private readonly object _lock = new object();
        private readonly object _syncLock = new object();

        public bool Blocked
        {
            get
            {
                lock (_lock)
                {
                    return _blocked;
                }
            }
            set
            {
                lock (_lock)
                {
                    if (_blocked != value)
                    {
                        SetProperty(ref _blocked, value);
                    }
                }
            }
        }

        private Location _location = null!;

        // --- Audio Fields ---
        private WaveOutEvent _wavePlayer = null!;
        private AudioFileReader _audioFileReader = null!; // Keep this loaded
        private bool _stopRequested = false;

        // --- Add a field to store the loaded preferred device ID ---
        private string _preferredAudioDeviceId = Utils.SYSTEM_DEFAULT_AUDIO_DEVICE_ID;

        private const int MAX_CONNECTION_TRIES = -1; // set to -1 for infinite tries (-1 is the default value)
        private int _connectionTries = 0;

        private bool _isFirstRun = true;
        private bool _programmaticChangeInProgress = false;
        private volatile bool _updateRequestedWhileBusy = false;
        private System.Timers.Timer _simulationTimer = null!;
        private bool _isNetworkAvailable;
        private IPAddress _lastKnownIPAddress;
        private CancellationTokenSource _debounceCts = null!; // Shared CancellationTokenSource for both events
        private CancellationTokenSource _connectionCancellationTokenSource;
        private bool _isConnecting = false; // New flag to prevent concurrent connections
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MinDebounceInterval = TimeSpan.FromSeconds(5); // Adjustable debounce delay
        private DateTime _lastDebounceTime = DateTime.MinValue; // Tracks last debounce time

        private static readonly SemaphoreSlim _userUpdateSemaphore = new SemaphoreSlim(1, 1); // Add this line for specific user update control

        [ObservableProperty]
        [Range(1, 60, ErrorMessage = "Away timeout must be between 1 and 60 minutes.")] // Basic validation
        private int _awayTimeoutMinutes = 3; // Default to 3 minutes

        public MainViewModel(ILogger<MainViewModel> log,
                             IEdgeAlertService edgeAlertService,
                             JSONHandler jSONHandler, // Ensure casing matches field
                             NetworkHandler networkHandler,
                             Utils utils,
                             AlertStore alertStore,
                             RoomStore roomStore,
                             AzureHandler azureHandler,
                             IPAddressMonitor ipAddressMonitor,
                             NavigationStore navigationStore,
                             UserStateMonitorService userStateMonitorService,
                             FeatureSettingsCache featureSettingsCache,
                             AudioDeviceService audioDeviceService)
        {
            _log = log;

            _log.LogInformation("MainViewModel: Initialising");

            _edgeAlertService = edgeAlertService;
            _jSONHandler = jSONHandler;
            _networkHandler = networkHandler;
            _utils = utils;
            _alertStore = alertStore;
            _roomStore = roomStore;
            _azureHandler = azureHandler;
            _ipAddressMonitor = ipAddressMonitor;
            _navigationStore = navigationStore;
            _userStateMonitorService = userStateMonitorService;
            _featureSettingsCache = featureSettingsCache;
            _audioDeviceService = audioDeviceService;
            _connectionCancellationTokenSource = new CancellationTokenSource();

            _log.LogInformation("MainViewModel: InitializeAudioPlayer");
            InitializeAudioPlayer();

            // Subscribe to Messages
            SubscribeToEvents();

            InitializeAsync().Await(HandleError);

            // Initialize last known IP address
            _lastKnownIPAddress = IPAddressHandler.GetTrustedIPAddress();
        }

        private void HandleError(Exception ex)
        {
            _log.LogError($"HandleError: {ex.Message}");
        }

        private void SubscribeToEvents()
        {
            NetworkChange.NetworkAvailabilityChanged += (s, e) => OnNetworkOrIPChanged();
            _ipAddressMonitor.OnIPAddressChanged += (s, e) => OnNetworkOrIPChanged();

            WeakReferenceMessenger.Default.Register<ExitApplicationMessage>(this);
            WeakReferenceMessenger.Default.Register<ApplicationRestartMessage>(this);
            WeakReferenceMessenger.Default.Register<DoubleClickMessage>(this);
            WeakReferenceMessenger.Default.Register<MainViewModelMessage>(this);
        }

        // Unified event handler for both network and IP change events
        private async void OnNetworkOrIPChanged()
        {
            _log.LogInformation("Network or IP address change detected; triggering debounce.");
            try
            {
                await TriggerDebounce();
            }
            catch (Exception ex)
            {
                _log.LogError($"OnNetworkOrIPChanged encountered an exception: {ex.Message}");
            }
        }

        // Unified debounce trigger method
        private async Task TriggerDebounce()
        {
            try
            {
                // Check if the minimum debounce interval has passed
                var now = DateTime.UtcNow;
                if (now - _lastDebounceTime < MinDebounceInterval)
                {
                    _log.LogInformation("TriggerDebounce: Suppressing debounce as the minimum interval hasn't passed.");
                    return;
                }

                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = new CancellationTokenSource();
                _lastDebounceTime = now; // Update the last debounce trigger time

                // Wait for the debounce delay period before proceeding
                await Task.Delay(DebounceDelay, _debounceCts.Token);

                // Perform network/IP handling logic after debounce period
                await HandleNetworkOrIPChange();
            }
            catch (TaskCanceledException)
            {
                _log.LogInformation("Debounce task canceled due to new network or IP event.");
            }
            catch (Exception ex)
            {
                _log.LogError($"TriggerDebounce encountered an exception: {ex.Message}");
                throw; // Rethrow to be caught in OnNetworkOrIPChanged
            }
        }

        // Consolidated network/IP change handling method
        private async Task HandleNetworkOrIPChange()
        {
            _log.LogInformation("Debounce period finished; checking network and IP status...");

            // Verify internet connectivity
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                if (await _networkHandler.IsInternetAvailableAsync(cts.Token))
                {
                    _log.LogInformation("HandleNetworkOrIPChange: Network is available");
                    _isNetworkAvailable = true;
                    await InitializeAsync();
                }
                else
                {
                    _log.LogInformation("HandleNetworkOrIPChange: Network is unavailable");
                    _isNetworkAvailable = false;
                    RoomEnabled = false;
                    // Cancel any ongoing connection attempts if network is lost
                    _connectionCancellationTokenSource?.Cancel();
                    SendMessage("No Internet Connection.");
                    HideApplication();
                }
            }
        }

        private void HideApplication()
        {
            WeakReferenceMessenger.Default.Send(new MainViewModelMessage("ForceHide"));
        }

        private void ShowApplication()
        {
            WeakReferenceMessenger.Default.Send(new MainViewModelMessage("MainWindowOpen"));
        }

        private async Task InitializeAsync()
        {
            // SET FLAG AT THE VERY START
            _programmaticChangeInProgress = true;
            _log.LogInformation("InitializeAsync: Starting FULL initialization, _programmaticChangeInProgress=true");

            // Prevent re-entrancy if already connecting
            if (_isConnecting)
            {
                _log.LogWarning("InitializeAsync called while already connecting. Skipping.");
                _programmaticChangeInProgress = false; // Reset flag before exiting
                return;
            }
            _isConnecting = true; // Mark as connecting

            bool initSuccess = false; // Track overall success
            try
            {
                // 1. Check Network Availability
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10))) // Reduced timeout slightly
                {
                    _isNetworkAvailable = await _networkHandler.IsInternetAvailableAsync(cts.Token);
                }

                if (!_isNetworkAvailable)
                {
                    _log.LogWarning("InitializeAsync: Network not available. Aborting initialization.");
                    HandleNoInternet(); // Handles UI messaging and hiding
                    // Exit early, finally block will handle flags
                    return;
                }
                _log.LogInformation("InitializeAsync: Network Available.");

                // 2. Initialize Azure Handler (Handles its own reset/reinit logic)
                _log.LogInformation("InitializeAsync: Resetting and Reinitializing AzureHandler...");
                bool azureInitSuccess = await _azureHandler.ResetAndReinitializeAsync();
                if (!azureInitSuccess || _azureHandler._settings == null)
                {
                    // Throw specific exception to be caught by the main try-catch
                    throw new InvalidOperationException("AzureHandler failed to initialize successfully or settings are null.");
                }
                _log.LogInformation("InitializeAsync: AzureHandler Initialized.");

                // 3. Load Non-Location Registry Settings & Initialize Table Clients
                await LoadRegistryValues(); // Loads non-audio, non-location settings
                InitializeEdgeAlertTables(); // Throws on failure, caught by main try-catch
                _log.LogInformation("InitializeAsync: Registry loaded (non-location), Table clients initialized.");

                // 4. Load Preferred Audio Device ID (Does not depend on location)
                _preferredAudioDeviceId = await _utils.GetPreferredAudioDeviceIDAsync();
                _log.LogInformation("InitializeAsync: Loaded Preferred Audio Device ID: {DeviceId}", _preferredAudioDeviceId);

                // 5. Initialize Feature Cache
                _log.LogInformation("InitializeAsync: Ensuring Feature Settings Cache is initialized...");
                bool cacheInitialized = await _featureSettingsCache.EnsureInitializedAsync();
                if (!cacheInitialized)
                {
                    _log.LogWarning("InitializeAsync: Feature Settings Cache failed to initialize, proceeding with defaults but monitoring might be affected.");
                }
                else
                {
                    _log.LogInformation("InitializeAsync: Feature Settings Cache initialization done.");
                }

                // --- CRITICAL SECTION START: Location & Connection ---
                // 6. Determine Location (Must complete before connecting/updating user)
                bool locationValid = await SetInitialLocationData(); // This also updates RoomStore
                if (!locationValid)
                {
                    // SetInitialLocationData handles restricting access and logging if needed
                    _log.LogError("InitializeAsync: Location determination failed. Aborting further initialization.");
                    // Exit early, finally block handles flags
                    return;
                }
                _log.LogInformation($"InitializeAsync: Location determined. Practice: '{_roomStore.ODS}', Room: '{_roomStore.Room}'");

                // 7. Initialize User State Monitor (Depends on cache)
                _log.LogInformation("InitializeAsync: Initializing UserStateMonitorService...");
                // InitializeAsync now handles its own errors internally, but we log if needed
                await _userStateMonitorService.InitializeAsync();
                _log.LogInformation("InitializeAsync: UserStateMonitorService initialized.");

                // 8. SignalR Connection (Requires valid location/ODS for user context)
                _roomStore.ForceHide = false; // Reset potential blocking flag
                await NegotiateAndConnectAsync(); // Handles potential errors internally
                RegisterConnectionHandlers();
                await StartConnectionAsync(); // Handles potential errors internally

                // 9. Initial User Data Update (Requires connection)
                var updateResponse = await SetUserDataAsync(); // Handles block messages and window visibility
                if (updateResponse.IsBlocked)
                {
                    // Shutdown is handled within SetUserDataAsync if blocked
                    _log.LogWarning("InitializeAsync: User is blocked by server. Initialization halted.");
                    // Exit early, finally block handles flags
                    return;
                }

                // 10. Post-Connection Updates
                await UpdateSystemInfo(); // Can run after user data is set
                await PerformDailySyncIfNeeded(); // Can run after system info

                // 11. Final Steps
                RoomEnabled = true; // Enable room selection now
                SendMessage("Connected");
                _userStateMonitorService.LogApplicationStart(); // Log app start state
                _log.LogInformation("InitializeAsync: Called LogApplicationStart.");
                WeakReferenceMessenger.Default.Send(new MainViewModelInitialisedMessage("Loaded"));
                _log.LogInformation("Sent MainViewModelInitialisedMessage");
                initSuccess = true; // Mark overall success
                // --- CRITICAL SECTION END ---

            }
            catch (Exception ex)
            {
                _log.LogCritical(ex, "InitializeAsync: CRITICAL Unhandled Error during initialization.");
                HandleNoInternet(); // Attempt cleanup and user notification
                initSuccess = false;
            }
            finally
            {
                _isConnecting = false;
                _programmaticChangeInProgress = false; // Reset flag
                _log.LogInformation("InitializeAsync finished (Success={InitSuccess}), _programmaticChangeInProgress=false", initSuccess); // Use initSuccess variable if defined in try block

                // --- Start: Add Queued Check ---
                if (_updateRequestedWhileBusy)
                {
                    _log.LogInformation("InitializeAsync: Update was requested while initializing. Triggering update.");
                    _updateRequestedWhileBusy = false; // Reset the flag
                    _ = Task.Run(() => UpdateConnectedUsersAsync());
                }
            }
        }

        private void _alertStore_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => AlertHasBeenSet = _alertStore.AlertSet;

        //private void _roomStore_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        //{
        //    SelectedRoom = _roomStore.Room;
        //}

        partial void OnEdgeUsersChanged(ObservableCollection<EdgeUserViewModel> value)
        {
            _navigationStore.ReportChanged = true;
            if (_navigationStore.CurrentView is ReportsView)
            {
                // Pass the data to ReportView
                FireDrill().Await(HandleError);
                AlertHistory().Await(HandleError);
            }
        }

        partial void OnSelectedEdgeUserChanged(EdgeUserViewModel value)
        {
            // If the row hasn't been de-selected, then store it
            if (value != null)
            {
                _roomStore.User = value;
            }
        }

        private async Task AlertHistory()
        {
            if (!_isNetworkAvailable) return;

            try
            {
                var alertHistory = await _joinTables.GetCombinedDataAsync(TrueODS);
                WeakReferenceMessenger.Default.Send(new AlertHistoryMessage(alertHistory));
            }
            catch (Exception ex)
            {
                _log.LogError($"AlertHistory: Exception {ex}");
            }
        }

        private async Task FireDrill()
        {
            if (!_isNetworkAvailable) return;

            try
            {
                var fireDrillUsers = await _joinTables.GetAllUsersBySimilarODSAsync(TrueODS);
                WeakReferenceMessenger.Default.Send(new UserListMessage(fireDrillUsers));
            }
            catch (Exception ex)
            {
                _log.LogError($"FireDrill: Exception {ex}");
            }
        }

        /// <summary>
                /// Performs a lighter re-initialization, often used after unlocking the machine or
                /// minor network changes where a full restart isn't needed. Ensures Azure settings
                /// are refreshed and connection is re-established.
                /// </summary>
        /// <summary>
        /// Performs a lighter re-initialization, often used after unlocking the machine or
        /// minor network changes where a full restart isn't needed. Ensures Azure settings
        /// are refreshed and connection is re-established. Uses a flag to prevent race conditions.
        /// </summary>
        private async Task SoftInitializeConnectionAsync()
        {
            // *** SET FLAG EARLIER ***
            _programmaticChangeInProgress = true;
            _log.LogInformation("SoftInitializeConnectionAsync: Starting SOFT initialization, _programmaticChangeInProgress=true");
            bool softInitSuccess = false; // Track success

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    if (await _networkHandler.IsInternetAvailableAsync(cts.Token))
                    {
                        _isNetworkAvailable = true;
                        _log.LogInformation("SoftInitializeConnectionAsync: Network Available.");

                        // Reset and Reinitialize AzureHandler (Important for potential key rotation/updates)
                        if (_azureHandler != null)
                        {
                            _log.LogInformation("SoftInitializeConnectionAsync: Resetting and Reinitializing AzureHandler...");
                            bool azureReInitialized = await _azureHandler.ResetAndReinitializeAsync();
                            if (!azureReInitialized || _azureHandler._settings == null)
                            {
                                throw new InvalidOperationException("SoftInitializeConnectionAsync: AzureHandler failed to re-initialize successfully.");
                            }
                            _log.LogInformation("SoftInitializeConnectionAsync: AzureHandler Reinitialized.");
                        }
                        else { throw new InvalidOperationException("SoftInitializeConnectionAsync: AzureHandler is null."); }

                        // Re-Initialize Azure Tables (uses the newly refreshed settings)
                        InitializeEdgeAlertTables(); // Throws on failure
                        _log.LogInformation("SoftInitializeConnectionAsync: Azure Table clients re-initialized.");

                        // Location Check/Update (Only if IP changed or network was previously down)
                        IPAddress currentIPAddress = IPAddressHandler.GetTrustedIPAddress() ?? IPAddress.None;
                        bool hasIPAddressChanged = !_lastKnownIPAddress.Equals(currentIPAddress);
                        bool wasInternetPreviouslyUnavailable = !_isNetworkAvailable; // Logic error here, should check previous state, but simplified for now

                        if (hasIPAddressChanged || wasInternetPreviouslyUnavailable) // Basic check, might need refinement
                        {
                            _log.LogInformation("SoftInitializeConnectionAsync: IP address change or previous unavailability detected. Resetting location data.");
                            // SetInitialLocationData handles its own flag internally now, but the outer one is true anyway
                            bool locationValid = await SetInitialLocationData();
                            if (!locationValid)
                            {
                                HandleNoInternet(); // Exits if location invalid
                                                    // Flag reset in finally block
                                return; // Exit SoftInitializeConnectionAsync
                            }
                            _lastKnownIPAddress = currentIPAddress; // Update last known IP
                        }
                        else
                        {
                            _log.LogInformation("SoftInitializeConnectionAsync: IP address unchanged and network was available. Skipping location reset.");
                            // Even if location isn't reset, ensure RoomStore reflects current VM state
                            // Note: This assumes SetInitialLocationData is the source of truth for RoomStore updates
                            // If other logic modifies SelectedRoom/SelectedBlankIPODS, RoomStore might need syncing here too.
                            // For simplicity, assuming SetInitialLocationData keeps things aligned.
                        }
                        _roomStore.ForceHide = false; // Allow showing window if needed


                        // SignalR Reconnection
                        await NegotiateAndConnectAsync(); // Gets new token/URL if needed
                        RegisterConnectionHandlers();
                        await StartConnectionAsync(); // Connects or reuses existing connection

                        // Final Updates
                        await UpdateLocalUser();
                        await UpdateSystemInfo();

                        // Re-verify RoomStore state (optional but safe)
                        _log.LogInformation($"SoftInitializeConnectionAsync: Final state check - RoomStore ODS='{_roomStore.ODS}', Room='{_roomStore.Room}'");


                        RoomEnabled = true;
                        SendMessage("Connected");
                        softInitSuccess = true; // Mark success
                    }
                    else
                    {
                        _isNetworkAvailable = false;
                        _log.LogWarning("SoftInitializeConnectionAsync: Network unavailable after check.");
                        HandleNoInternet();
                        // Flag reset in finally block
                        return; // Exit early
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "SoftInitializeConnectionAsync: CRITICAL Error during soft initialization.");
                HandleNoInternet(); // Attempt to handle gracefully
                softInitSuccess = false;
            }
            finally
            {
                _programmaticChangeInProgress = false; // Reset flag
                                                       // Log success/failure based on softInitSuccess variable defined in try block
                if (!softInitSuccess) { _log.LogError("SoftInitializeConnectionAsync finished unsuccessfully."); }
                else { _log.LogInformation("SoftInitializeConnectionAsync finished successfully, _programmaticChangeInProgress=false"); }

                // --- Start: Add Queued Check ---
                if (_updateRequestedWhileBusy)
                {
                    _log.LogInformation("SoftInitializeConnectionAsync: Update was requested while soft initializing. Triggering update.");
                    _updateRequestedWhileBusy = false; // Reset the flag
                    _ = Task.Run(() => UpdateConnectedUsersAsync());
                }
            }
        }

        private void HandleNoInternet()
        {
            _roomStore.ForceHide = true;
            SendMessage("No Internet Connection.");
            HideApplication();
        }

        private async Task PerformDailySyncIfNeeded()
        {
            const string LastSyncDateKey = "LastSyncDate";
            const string RegistryPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Edgebits\EdgeAlert";

            // *** REVISED Feature Check ***
            string currentUser = _utils?.GetComputerName(); // Or Environment.UserName if Utils not suitable here
            string currentOds = _roomStore?.GetTrueODS() ?? "UNKNOWN_ODS"; // Provide a default if null
            bool isAzureSyncEnabled = false; // Default to disabled

            if (_featureSettingsCache == null || string.IsNullOrWhiteSpace(currentUser) || string.IsNullOrWhiteSpace(currentOds))
            {
                _log?.LogWarning("PerformDailySyncIfNeeded: FeatureSettingsCache or User/ODS info not available. Cannot check feature status. Assuming AzureSync is disabled.");
            }
            else
            {
                // Check effective status - primarily driven by GLOBAL for this feature
                isAzureSyncEnabled = _featureSettingsCache.GetEffectiveSetting(
                    AzureSyncFeatureName,
                    currentUser, // Pass user/ODS context even if only Global expected
                    currentOds
                );
                _log?.LogDebug("PerformDailySyncIfNeeded: Effective status for {FeatureName} is {Status}", AzureSyncFeatureName, isAzureSyncEnabled);
            }

            if (!isAzureSyncEnabled) // <<< Use the effective status variable
            {
                _log.LogInformation("PerformDailySyncIfNeeded: {FeatureName} is disabled. Skipping sync.", AzureSyncFeatureName);
                return; // Exit early if the feature is disabled
            }
            // *** END REVISED Feature Check ***


            // --- Existing Sync Logic ---
            lock (_syncLock) // Ensure thread safety
            {
                try
                {
                    string lastSyncDate = (string)Registry.GetValue(RegistryPath, LastSyncDateKey, null);
                    if (DateTime.TryParse(lastSyncDate, out DateTime lastSync) && lastSync.Date == DateTime.UtcNow.Date)
                    {
                        _log.LogInformation($"PerformDailySyncIfNeeded: Sync already performed today ({lastSyncDate}). Skipping.");
                        return; // Sync already done today
                    }
                    _log.LogInformation("PerformDailySyncIfNeeded: Performing daily sync.");
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"PerformDailySyncIfNeeded: Failed to read last sync date from registry. Assuming sync is needed.");
                    // Continue with sync despite error reading date
                }
            }
            // Perform the sync outside the lock
            try
            {
                await _systemInfoTables.SyncToNewTableAsync();
                lock (_syncLock) // Lock while updating the registry
                {
                    Registry.SetValue(RegistryPath, LastSyncDateKey, DateTime.UtcNow.Date.ToString("yyyy-MM-dd"));
                    _log.LogInformation("PerformDailySyncIfNeeded: Sync completed and date updated.");
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"PerformDailySyncIfNeeded: Error occurred during daily sync execution.");
            }
            // --- End Existing Sync Logic ---
        }

        private async Task LoadRegistryValues()
        {
            _log.LogInformation($"LoadRegistryValues: Initialising non-location settings ONLY");
            try
            {
                // These properties are usually safe to set from background threads
                UserName = _utils.GetComputerName().ToUpperInvariant();
                HostName = _utils.GetHostName().ToUpperInvariant();
                StationaryDevice = await _utils.GetRegistryValueAsync<bool>("StationaryDevice", false, defaultValue: false);
                _roomStore.StationaryDevice = StationaryDevice;
                AwayTimeoutMinutes = await _utils.GetRegistryValueAsync<int>("AwayTimeoutMinutes", 3, defaultValue: 3);

                // *** UI Thread required for ObservableCollection modifications ***
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _log.LogDebug("LoadRegistryValues: Clearing UI collections on Dispatcher thread.");
                    // Initialize collections IF THEY ARE NULL, otherwise just clear them
                    // This prevents overwriting the collection instance if it already exists
                    if (EdgeUsers == null) EdgeUsers = new ObservableCollection<EdgeUserViewModel>(); else EdgeUsers.Clear();
                    if (Responders == null) Responders = new ObservableCollection<Responder>(); else Responders.Clear();
                    if (BlankIPODS == null) BlankIPODS = new ObservableCollection<string>(); else BlankIPODS.Clear();
                    if (Rooms == null) Rooms = new ObservableCollection<string>(); else Rooms.Clear();

                    // Reset properties that might be bound to UI elements being cleared/repopulated
                    SelectedBlankIPODS = string.Empty;
                    SelectedRoom = string.Empty;
                    // Consider resetting indices if necessary, although setting SelectedItem often handles this
                    // ODSIndex = -1;
                    // RoomIndex = -1;
                });
            }
            catch (Exception ex)
            {
                // Log the specific error but allow initialization to continue if possible,
                // as the core issue might be just one registry read or collection clear failing.
                _log.LogError(ex, $"LoadRegistryValues: Error loading non-location settings from registry or clearing collections");
                // Re-throw if this failure is critical and should stop initialization
                // throw;
            }
        }

        private async Task GetInitialUserData()
        {
            // Get or create the current user
            InitialUser = await _edgeAlertTables.GetUserAsync(UserName, HostName);
            if (InitialUser == null)
            {
                _log.LogInformation($"NegotiateAndConnectAsync: Creating user for the first time");
                InitialUser = await CreateUser();
            }
        }

        private void Start401SimulationTimer()
        {
            _simulationTimer = new System.Timers.Timer(20000); // Set for 20 seconds
            _simulationTimer.Elapsed += OnSimulationTimerElapsed;
            _simulationTimer.AutoReset = false; // Run the timer only once
            _simulationTimer.Enabled = true;
        }

        private void OnSimulationTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            _simulationTimer.Stop();
            _simulationTimer.Dispose();
            App.Current.Dispatcher.Invoke(async () =>
            {
                await Simulate401Unauthorized();
            });
        }
        private async Task Simulate401Unauthorized()
        {
            _log.LogWarning("Simulated 401 Unauthorized. Attempting to reconnect...");
            var newToken = await _edgeAlertService.FetchNewToken(); // Fetch new token
            await ReconnectWithNewToken(newToken); // Disconnect and reconnect with new token
        }

        private async Task NegotiateAndConnectAsync()
        {
            if (!_isNetworkAvailable)
            {
                _log.LogWarning("NegotiateAndConnectAsync: Network is not available. Aborting connection attempt.");
                return;
            }

            _connectionCancellationTokenSource = new CancellationTokenSource();
            try
            {
                _log.LogInformation("Connecting to SignalR Hub");

                if (InitialUser == null)
                {
                    await GetInitialUserData();
                }

                var negotiateData = await _edgeAlertService.NegotiateAsync(InitialUser);
                _connection = _edgeAlertService.ConnectToHub(negotiateData);

                if (_connection == null)
                {
                    _log.LogError("Error: Failed to initialize the connection.");
                    SendMessage("Error: Failed to initialize the connection.");
                    return;
                }

                _log.LogInformation("Connected to SignalR Hub successfully.");
            }
            catch (OperationCanceledException)
            {
                _log.LogWarning("NegotiateAndConnectAsync: Connection attempt canceled due to connectivity loss.");
            }
            catch (Exception ex)
            {
                _log.LogError($"NegotiateAndConnectAsync: Exception: {ex.Message}");
                SendMessage("No Internet Connection.");
            }
        }

        private void RegisterConnectionHandlers()
        {
            _log.LogInformation("RegisterConnectionHandlers: Registering Connection Handlers");
            try
            {
                _receiveEdgeAlertMessageSubscription = _connection.On<EdgeUser>("ReceiveEdgeAlertMessage", edgeAlertMessage =>
                {
                    App.Current.Dispatcher.Invoke(() => TriggerAlert(edgeAlertMessage));
                });

                // Version 2 listener endpoint WITH parameters (for older server responses)
                _updateConnectedUserGroupsSubscription = _connection.On<List<EdgeUserViewModel>>("UpdateConnectedUserGroups", async (users) =>
                {
                    await App.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            await UpdateConnectedUsersAsync(users);
                        }
                        catch (Exception ex)
                        {
                            _log.LogError($"UpdateConnectedUserGroups: Error occurred: {ex}");
                        }
                    });
                });

                // Add this line to handle the "Ping" event.
                _pongSubscription = _connection.On("Ping", () =>
                {
                    App.Current.Dispatcher.Invoke(async () => await RespondToPing());
                });
            }
            catch (Exception ex)
            {
                _log.LogError($"RegisterConnectionHandlers: Exception: {ex}");
            }
        }

        private void ShowEdgeAlertBlockMessage(string message)
        {
            MessageBox.Show(message, "License Expired", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        //public async Task UnregisterConnectionHandlers()
        //{
        //    _receiveEdgeAlertMessageSubscription?.Dispose();
        //    _updateConnectedUsersSubscription?.Dispose();
        //    _pongSubscription?.Dispose();
        //}

        private async Task StartConnectionAsync()
        {
            try
            {
                _connection.Closed += async (error) =>
                {
                    _log.LogWarning("SignalR: Connection closed. Trying to reconnect...");
                    SendMessage("Connection lost. Reconnecting...");
                    await Task.Delay(new Random().Next(0, 5) * 1000);
                    await ConnectWithRetryAsync();
                };

                _connection.Reconnecting += error =>
                {
                    _log.LogWarning("SignalR: Attempting to reconnect...");
                    SendMessage("Reconnecting to Alert Server...");
                    return Task.CompletedTask;
                };

                _connection.Reconnected += connectionId =>
                {
                    _log.LogInformation("SignalR: Reconnected to the server");
                    SendMessage("Connected");
                    return Task.CompletedTask;
                };

                await ConnectWithRetryAsync();
            }
            catch (Exception ex)
            {
                _log.LogError($"An error occurred while setting up SignalR connection: {ex.Message}");
                SendMessage("Error: Alert Server connection failed.");
            }
        }

        private async Task ConnectWithRetryAsync()
        {
            if (IsMachineLocked || !_isNetworkAvailable)
            {
                SendMessage("ConnectWithRetryAsync: Suppressed reconnection attempt because the computer is locked or no internet connection.");
                return; // Do not attempt to reconnect if the machine is locked or no internet connection.
            }

            bool tokenRefreshed = false;

            while (true)
            {
                try
                {
                    await _connection.StartAsync();
                    _log.LogInformation("SignalR: Connected to the server");
                    SendMessage("Connected");
                    break; // Exit the loop if connected successfully
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    if (!tokenRefreshed) // Ensure we only refresh token once per retry sequence
                    {
                        _log.LogWarning("SignalR: Unauthorized. Refreshing token...");
                        var newToken = await _edgeAlertService.FetchNewToken(); // Method to get a new token
                        await ReconnectWithNewToken(newToken); // Disconnect and reconnect with new token
                        tokenRefreshed = true; // Prevent multiple token refresh attempts during the same connection attempt
                        continue; // Retry connection with new token
                    }
                    else
                    {
                        _log.LogError("SignalR: Token refresh did not resolve the unauthorized issue.");
                        SendMessage("Authorization failed after token refresh.");
                        break; // Break if refreshing token did not help
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("The HubConnection cannot be started if it is not in the Disconnected state."))
                {
                    _log.LogError($"SignalR: {ex.Message}");
                    SendMessage("ClearStatusText");
                    break; // Exit the loop if InvalidOperationException is caught
                }
                catch (Exception ex)
                {
                    _log.LogError($"SignalR: Error while connecting: {ex.Message}");
                    SendMessage("Connection failed. Retrying...");
                    await Task.Delay(2000); // Wait before retrying
                }
            }
        }

        private async Task ReconnectWithNewToken(string newToken)
        {
            _log.LogWarning("SignalR: Disposing Connection...");
            _connection?.DisposeAsync();
            var negotiateData = await _edgeAlertService.NegotiateAsync(InitialUser);
            _log.LogWarning("SignalR: Connecting with new token...");
            _connection = _edgeAlertService.ConnectToHub(negotiateData); // This should use newToken internally
            _log.LogWarning("SignalR: Re-Registering Connection Handlers...");
            RegisterConnectionHandlers(); // Re-register handlers as they are tied to the connection instance
            _log.LogWarning("SignalR: Reconnecting...");
            await _connection.StartAsync();
        }

        /// <summary>
        /// Initializes the Azure Table client instances.
        /// Logs and re-throws any exception during initialization to prevent silent failures.
        /// </summary>
        private void InitializeEdgeAlertTables()
        {
            try
            {
                _log.LogInformation($"InitializeEdgeAlertTables: Connecting to Azure Data Tables");

                // Ensure connection string is available before attempting to create clients
                string connectionString = _azureHandler?._settings?.DatabaseConnectionString;
                if (string.IsNullOrEmpty(connectionString))
                {
                    // Throw specific exception if connection string is missing
                    throw new InvalidOperationException("Azure DatabaseConnectionString is null or empty. Cannot initialize table clients.");
                }                _edgeAlertTables = new EdgeAlertTables(connectionString, "Users");
                _responderTables = new ResponderTables(connectionString, "Responders");
                _alertTables = new AlertTables(connectionString, "Alerts");
                _joinTables = new JoinTables(connectionString, "Alerts", "Responders", "Users", _jSONHandler);
                _systemInfoTables = new SystemInfoTables(connectionString, "SystemInfo", "SystemInfo2", _jSONHandler, _log);
                // Initialize AssetTableService using the constant
                _assetTableService = new AssetTableService(connectionString, AssetTableName);

                _log.LogInformation($"InitializeEdgeAlertTables: Successfully initialized all table clients.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"InitializeEdgeAlertTables: Exception occurred during table client initialization."); // Log with exception details
                SendMessage("Error initializing connection to Azure Database."); // Send user-friendly message
                throw; // Re-throw the exception to halt further initialization if tables fail
            }
        }

        /// <summary>
        /// Determines the user's location based on network (IP/Domain) or saved preferences (if stationary),
        /// populates relevant UI properties (ODS, Rooms), validates/restores the selected room if applicable,
        /// and updates the registry. Prevents race conditions during programmatic updates using a flag.
        /// </summary>
        /// <returns>True if a location context could be established (even if user selection is needed), false if access should be restricted.</returns>
        private async Task<bool> SetInitialLocationData()
        {
            // Ensure this flag is managed correctly to prevent event handler interference
            _programmaticChangeInProgress = true;
            _log.LogInformation("SetInitialLocationData: Starting, _programmaticChangeInProgress=true");

            Location determinedLocation = null;
            string determinedPracticeName = string.Empty;
            string determinedOdsCode = string.Empty;
            bool isBlankIPScenario = false;
            ObservableCollection<string> determinedRooms = new ObservableCollection<string>(); // Store rooms temporarily
            string roomToApply = string.Empty; // Final room value for UI/Store
            string roomToSaveInRegistry = string.Empty; // Value to actually save

            try
            {
                // --- 1. Try IP Address Lookup First ---
                string ipAddress = IPAddressHandler.GetTrustedIPAddress()?.ToString();
                if (!string.IsNullOrEmpty(ipAddress))
                {
                    _log.LogInformation($"SetInitialLocationData: Trying IP Address: {ipAddress}");
                    determinedLocation = _jSONHandler.GetLocationByIpAddress(ipAddress);
                }

                // Validate IP lookup result
                if (determinedLocation == null || string.IsNullOrEmpty(determinedLocation.ODS) || string.IsNullOrEmpty(determinedLocation.Practice))
                {
                    _log.LogInformation($"SetInitialLocationData: IP lookup failed or returned invalid location (IP: '{ipAddress}'). Falling back.");
                    determinedLocation = null; // Ensure null for fallback checks
                    isBlankIPScenario = true; // Mark as potential blank IP scenario
                }
                else
                {
                    _log.LogInformation($"SetInitialLocationData: Location determined via IP: {determinedLocation.Practice} ({determinedLocation.ODS})");
                    isBlankIPScenario = false; // Confirmed IP match
                    determinedPracticeName = determinedLocation.Practice;
                    determinedOdsCode = determinedLocation.ODS;
                    determinedRooms = determinedLocation.Rooms ?? new ObservableCollection<string>(); // Get rooms now
                }

                // --- 2. If IP Lookup Failed/Invalid, Try Domain Lookup ---
                if (determinedLocation == null)
                {
                    _log.LogInformation($"SetInitialLocationData: Falling back to Domain lookup.");
                    string currentDomain = GetDomainName(); // Assumes this is synchronous and reliable
                    if (!string.IsNullOrEmpty(currentDomain) && currentDomain != "No Active Directory")
                    {
                        var locationsForDomain = _jSONHandler.GetLocationsWithBlankIPs(currentDomain);
                        if (locationsForDomain != null && locationsForDomain.Any())
                        {
                            var sortedPractices = locationsForDomain
                                .Select(l => l.Practice)
                                .Where(p => !string.IsNullOrEmpty(p))
                                .Distinct()
                                .OrderBy(p => p)
                                .ToList();

                            // Update BlankIPODS dropdown choices - must happen on UI thread
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (BlankIPODS == null) BlankIPODS = new ObservableCollection<string>();
                                BlankIPODS.Clear();
                                foreach (var p in sortedPractices) BlankIPODS.Add(p);
                                _log.LogDebug("SetInitialLocationData (Dispatcher): Updated BlankIPODS collection.");
                            });

                            if (sortedPractices.Count == 1)
                            {
                                // Auto-select if only one practice for this domain
                                determinedLocation = locationsForDomain.First();
                                _log.LogInformation($"SetInitialLocationData: Auto-selected single practice '{determinedLocation.Practice}' for domain '{currentDomain}'.");
                                determinedPracticeName = determinedLocation.Practice;
                                determinedOdsCode = determinedLocation.ODS;
                                determinedRooms = determinedLocation.Rooms ?? new ObservableCollection<string>();
                            }
                            else
                            {
                                // Try restoring from registry if multiple practices exist
                                string lastPractice = await _utils.GetRegistryValueAsync<string>("ODS", null, defaultValue: "");
                                _log.LogInformation($"SetInitialLocationData: Domain Fallback - Read saved Practice Name from registry: '{lastPractice}'");
                                if (!string.IsNullOrEmpty(lastPractice) && sortedPractices.Contains(lastPractice))
                                {
                                    determinedLocation = _jSONHandler.GetLocationByPractice(lastPractice);
                                    if (determinedLocation != null)
                                    {
                                        _log.LogInformation($"SetInitialLocationData: Used saved practice '{lastPractice}' from registry for domain '{currentDomain}'.");
                                        determinedPracticeName = determinedLocation.Practice;
                                        determinedOdsCode = determinedLocation.ODS;
                                        determinedRooms = determinedLocation.Rooms ?? new ObservableCollection<string>();
                                    }
                                    else
                                    {
                                        _log.LogWarning($"SetInitialLocationData: Saved practice '{lastPractice}' lookup failed. Clearing selection.");
                                        determinedLocation = null; // Reset determined location
                                    }
                                }
                                else
                                {
                                    _log.LogInformation($"SetInitialLocationData: Multiple practices for domain '{currentDomain}', no valid saved preference found. User selection required.");
                                    determinedLocation = null; // User must select from dropdown
                                }
                            }
                        }
                        else
                        {
                            _log.LogWarning($"SetInitialLocationData: No locations configured for domain '{currentDomain}'.");
                        }
                    }
                    else
                    {
                        _log.LogWarning($"SetInitialLocationData: Cannot determine domain. Location cannot be established.");
                    }
                }

                // --- 3. Final Validation and Room Handling ---
                if (string.IsNullOrEmpty(determinedOdsCode) && isBlankIPScenario && (BlankIPODS == null || !BlankIPODS.Any()))
                {
                    // If still no valid ODS context AND it's a blank IP scenario with NO dropdown options -> restrict
                    _log.LogWarning("SetInitialLocationData: Location could not be determined (IP/Domain failed and no Blank IP options). Restricting access.");
                    RestrictApplicationAccess();
                    return false; // Indicate failure
                }
                // At this point, we either have a determinedPracticeName/determinedOdsCode OR it's a Blank IP scenario where the user MUST select.

                // Load saved room preference (do this regardless of stationary for now)
                string savedRoomFromRegistry = await _utils.GetRegistryValueAsync<string>("Room", null, defaultValue: "");
                _log.LogInformation($"SetInitialLocationData: Loaded saved Room from registry: '{savedRoomFromRegistry}'");

                // Determine the room to apply to UI and the room to save back to registry
                bool isSavedRoomValid = false;
                if (!string.IsNullOrEmpty(determinedOdsCode)) // Only validate/apply room if we have a definite location context
                {
                    isSavedRoomValid = determinedRooms.Contains(savedRoomFromRegistry);
                    if (StationaryDevice)
                    {
                        _log.LogInformation($"SetInitialLocationData: StationaryDevice is TRUE.");
                        roomToSaveInRegistry = savedRoomFromRegistry; // Always intend to save the registry value back if stationary

                        if (isSavedRoomValid)
                        {
                            _log.LogInformation($"SetInitialLocationData: Saved room '{savedRoomFromRegistry}' is VALID for location '{determinedPracticeName}'. Applying selection.");
                            roomToApply = savedRoomFromRegistry; // Apply valid saved room
                        }
                        else
                        {
                            _log.LogWarning($"SetInitialLocationData: Saved room '{savedRoomFromRegistry}' is NOT VALID for current location context ('{determinedPracticeName}'). Clearing UI selection, but preserving registry value.");
                            roomToApply = string.Empty; // Don't apply invalid room to UI
                        }
                    }
                    else // Not Stationary
                    {
                        _log.LogInformation($"SetInitialLocationData: StationaryDevice is FALSE. Clearing room selection.");
                        roomToApply = string.Empty;
                        roomToSaveInRegistry = string.Empty; // Ensure empty room is saved if not stationary
                    }
                }
                else // Blank IP scenario - user must select ODS first, so clear room selection initially
                {
                    _log.LogInformation("SetInitialLocationData: Blank IP Scenario - Clearing initial room selection.");
                    roomToApply = string.Empty;
                    // Decide whether to save the registry room back even if ODS isn't set?
                    // Safer to clear it if the context is unknown.
                    roomToSaveInRegistry = StationaryDevice ? savedRoomFromRegistry : string.Empty;
                }

                // Determine StationaryDeviceIsEnabled state based ONLY on whether a room is actually being applied to the UI
                bool stationaryEnabledState = !string.IsNullOrEmpty(roomToApply);

                // --- 4. Update ViewModel and RoomStore via Dispatcher ---
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _log.LogDebug("SetInitialLocationData (Dispatcher): Updating VM properties...");
                    // Update ODS/Practice related properties
                    SelectedBlankIPODS = determinedPracticeName; // May be empty if user needs to select
                    ODS = determinedPracticeName;
                    TrueODS = determinedOdsCode; // May be empty if user needs to select
                    BlankIPAddress = isBlankIPScenario; // Set based on initial IP lookup failure

                    // Update Rooms collection *before* setting SelectedRoom
                    Rooms.Clear();
                    foreach (var room in determinedRooms) // Use the temporary collection
                    {
                        Rooms.Add(room);
                    }
                    _log.LogDebug("SetInitialLocationData (Dispatcher): Rooms collection updated ({Count} items).", Rooms.Count);

                    // Update Room selection based on calculated `roomToApply`
                    SelectedRoom = roomToApply;
                    RoomIndex = string.IsNullOrEmpty(roomToApply) ? -1 : Rooms.IndexOf(roomToApply); // Find index after populating
                    _log.LogDebug("SetInitialLocationData (Dispatcher): SelectedRoom='{Room}', RoomIndex={Index}", SelectedRoom, RoomIndex);

                    // Update StationaryDevice check state and enabled state
                    StationaryDeviceIsEnabled = stationaryEnabledState; // Based on whether a room is actively selected
                    _log.LogDebug("SetInitialLocationData (Dispatcher): StationaryDeviceIsEnabled={IsEnabled}", StationaryDeviceIsEnabled);

                    _log.LogDebug("SetInitialLocationData (Dispatcher): Finished VM properties update.");
                });

                // --- 5. Update RoomStore directly ---
                // Use the final 'determined' values for consistency
                _roomStore.ODS = determinedPracticeName;
                _roomStore.Room = roomToApply; // Reflect what's actually shown/selected in UI
                // RoomStore.StationaryDevice is already set from LoadRegistryValues
                _log.LogInformation($"SetInitialLocationData: Updated RoomStore: Practice='{_roomStore.ODS}', Room='{_roomStore.Room}', Stationary={_roomStore.StationaryDevice}");

                // --- 6. Update Registry ---
                // Save the determined Practice Name (if valid) back to registry
                if (!string.IsNullOrEmpty(determinedPracticeName))
                {
                    WriteSelectedBlankIPODSToRegistry(); // Uses SelectedBlankIPODS property, which was set in dispatcher
                }
                else
                {
                    _log.LogInformation("SetInitialLocationData: Skipping registry write for ODS (Practice Name) as it was not determined/selected yet.");
                }
                // Save the room value intended for the registry
                WriteSelectedRoomToRegistry(roomToSaveInRegistry);
                _log.LogInformation($"SetInitialLocationData: Finished writing Room='{roomToSaveInRegistry}' to registry.");

                return true; // Indicate success (location context established or user selection pending)
            }
            catch (Exception ex)
            {
                _log.LogCritical(ex, "SetInitialLocationData: CRITICAL Unhandled Error.");
                RestrictApplicationAccess(); // Attempt to restrict on critical failure
                return false; // Indicate failure
            }
            finally
            {
                _programmaticChangeInProgress = false; // Reset flag

                if (_updateRequestedWhileBusy)
                {
                    _log.LogInformation("SoftInitializeConnectionAsync: Update was requested while soft initializing. Triggering update.");
                    _updateRequestedWhileBusy = false; // Reset the flag
                    _ = Task.Run(() => UpdateConnectedUsersAsync());
                }
            }
        }

        // Modify WriteSelectedRoomToRegistry (Recommended)
        /// <summary>
        /// Writes the specified room name to the registry.
        /// </summary>
        /// <param name="roomToSave">The room name to save. Null or empty will be saved as empty string.</param>
        public void WriteSelectedRoomToRegistry(string roomToSave)
        {
            try
            {
                string valueToWrite = roomToSave ?? string.Empty;
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Edgebits\EdgeAlert", "Room", valueToWrite);
                _log.LogInformation($"Successfully wrote Room to registry: '{valueToWrite}'");
            }
            catch (Exception ex)
            {
                // Log error but don't necessarily halt execution unless critical
                _log.LogError(ex, "Error writing Room='{RoomValue}' to registry: {ErrorMessage}", roomToSave, ex.Message);
                // Consider sending a status message to the UI if appropriate
                // SendMessage($"Error saving Room '{roomToSave}' setting.");
            }
        }

        private void RestrictApplicationAccess()
        {
            // Logic to restrict access to the application when location cannot be determined
            SelectedRoom = string.Empty;
            _roomStore.Room = string.Empty;
            RoomIndex = -1;
            BlankIPODS = new ObservableCollection<string>();
            UpdateFieldsAndRegistry();
            RoomEnabled = false;
            SendMessage("Unrecognized network. Limited functionality.");
            _log.LogInformation("Application access restricted due to unrecognized network or domain.");
        }

        private void UpdateFieldsAndRegistry()
        {
            if (_location != null)
            {
                TrueODS = _location.ODS;
                ODS = _location.Practice;
                Rooms = _location.Rooms;
            }
            else
            {
                TrueODS = string.Empty;
                ODS = string.Empty;
                Rooms = new ObservableCollection<string>();
            }

            WriteSelectedBlankIPODSToRegistry();
            WriteSelectedRoomToRegistry();
        }

        private async Task SetLocationData(Location location)
        {
            string selectedRoom = SelectedRoom;  // Capture the value before modifying Rooms

            // If location is null, clear out the relevant properties
            if (location == null)
            {
                _log.LogWarning("SetLocationData: Provided location is null. Clearing Rooms and resetting properties.");

                // Clear Rooms and reset related properties
                Rooms = new ObservableCollection<string>();
                SelectedRoom = string.Empty;
                _roomStore.Room = string.Empty;
                RoomIndex = -1;
            }
            else
            {
                // Assign the location and its rooms if location is not null
                _location = location;
                Rooms = location.Rooms ?? new ObservableCollection<string>(); // Fallback to empty collection if Rooms is null

                // Check if the previously selected room exists in the new Rooms list
                if (!Rooms.Contains(selectedRoom))
                {
                    SelectedRoom = string.Empty;
                    _roomStore.Room = string.Empty;
                    RoomIndex = -1;
                }
                else
                {
                    SelectedRoom = await _utils.GetRegistryValueAsync<string>("Room", null, defaultValue: "");
                    _roomStore.Room = SelectedRoom;
                }
            }
        }

        private string GetDomainName()
        {
            string domainOrSuffix = _networkHandler.GetTrustedDomainOrSuffix();

            if (domainOrSuffix == "Domain not found" || domainOrSuffix == "No Active Directory")
            {
                return string.Empty; // Return empty string if the domain is not found or there is no active directory
            }
            return domainOrSuffix;
        }

        private Location HandleBlankLocation(string ipAddress)
        {
            string thisDomain = GetDomainName();
            string selectedODS = SelectedBlankIPODS;  // Capture the value before modifying BlankIPODS

            var locationsWithBlankIPs = _jSONHandler.GetLocationsWithBlankIPs(thisDomain);

            if (locationsWithBlankIPs == null || !locationsWithBlankIPs.Any())
            {
                _log.LogWarning($"HandleBlankLocation: No locations found with blank IPs for domain {thisDomain}.");
                return new Location { ODS = "", Practice = "", Rooms = new ObservableCollection<string>() };
            }

            var sortedPractices = locationsWithBlankIPs.Select(l => l.Practice).OrderBy(p => p).ToList();
            BlankIPODS = new ObservableCollection<string>(sortedPractices);  // Now it's safe to update

            Location defaultLocation = new Location { ODS = "", Practice = "", Rooms = new ObservableCollection<string>() };

            if (!BlankIPODS.Contains(selectedODS) && !string.IsNullOrEmpty(selectedODS))
            {
                ResetODSAndRoomData();
                return defaultLocation;
            }

            SelectedBlankIPODS = !string.IsNullOrEmpty(selectedODS) ? selectedODS : "";
            ODSIndex = BlankIPODS.IndexOf(selectedODS);

            return string.IsNullOrEmpty(selectedODS) ? defaultLocation : _jSONHandler.GetLocationByPractice(selectedODS);
        }

        private void ResetODSAndRoomData()
        {
            SelectedBlankIPODS = string.Empty;
            ODSIndex = -1;
            SelectedRoom = string.Empty;
            _roomStore.Room = string.Empty;
            RoomIndex = -1;
        }

        private bool IsLocationEmpty(Location location)
        {
            // Checks if the critical properties of the location are null or empty
            return string.IsNullOrEmpty(location.ODS) && string.IsNullOrEmpty(location.Practice) && (location.Rooms == null || !location.Rooms.Any());
        }

        private string LocationToODS(string location)
        {
            var locations = _jSONHandler.Data.Locations;
            _location = _jSONHandler.GetLocationByIpAddress(IPAddressHandler.GetTrustedIPAddress().ToString());

            if (_location == null || string.IsNullOrEmpty(_location.ODS))
            {
                var locationsWithBlankIPs = _jSONHandler.GetLocationsWithBlankIPs(GetDomainName());
                var matchingLocation = locationsWithBlankIPs.FirstOrDefault(l => l.Practice == location);

                if (matchingLocation != null)
                {
                    _location = _jSONHandler.GetLocationByODS(matchingLocation.ODS);
                }
                else
                {
                    return "";
                }
            }
            return _location.ODS;
        }


        private async Task<UpdateUserResponse> SetUserDataAsync()
        {
            UpdateUserResponse _isBlocked = new UpdateUserResponse();
            try
            {
                _log.LogInformation($"SetUserDataAsync: Configuring user data");
                //await SetStationaryDevice();

                EdgeUser user = await _edgeAlertTables.GetUserAsync(UserName, HostName);

                if (user == null)
                {
                    _log.LogInformation($"SetUserDataAsync: Creating user for first time");
                    user = await CreateUser();
                    //await _edgeAlertService.UpdateUser(user);
                }
                else
                {
                    LoggedIn = user.LoggedIn;
                }

                _isBlocked = await SetUserProperties(user);
                //await UpdateLocalUser();

                if (_isBlocked.IsBlocked)
                {
                    _log.LogInformation($"SetUserDataAsync: User ODS Blocked ");
                    _roomStore.ForceHide = true;
                    _roomStore.Blocked = true;
                    ShowEdgeAlertBlockMessage(_isBlocked.Message);
                    HideApplication();
                    // Exit Application
                    Application.Current.Shutdown();
                    return _isBlocked;
                }

                if (string.IsNullOrEmpty(SelectedBlankIPODS) || string.IsNullOrEmpty(SelectedRoom))
                {
                    _log.LogInformation($"SetUserDataAsync: No room selected, show Window ");
                    SendMessage("Open");
                }
                else if (StationaryDevice)
                {
                    _log.LogInformation($"SetUserDataAsync: Set to stationary device, hide Window ");
                    SendMessage("Hide");
                }
                else
                {
                    _log.LogInformation($"SetUserDataAsync: Room selected, but is not a stationairy device, so clear Room and show Window ");
                    SelectedRoom = null;
                    _roomStore.Room = null;
                    SendMessage("Open");
                }
                return _isBlocked;
            }
            catch (Exception ex)
            {
                _log.LogError($"SetUserDataAsync: {ex.Message} ");
                return _isBlocked;
            }
        }

        private async Task UpdateSystemInfo()
        {
            _log.LogInformation($"UpdateSystemInfo: Initialising");
            try
            {
                SystemInfoService systemInfo = SystemInfoService.GetSystemInfo();
                systemInfo.ODS = TrueODS;
                systemInfo.Room = SelectedRoom;
                systemInfo.UserName = UserName.ToUpper();
                systemInfo.LoggedIn = LoggedIn;
                systemInfo.LastOnline = "Online";
                systemInfo.Version = $"v{Assembly.GetExecutingAssembly().GetName().Version.ToString(4)}";
                // Send message to ViewModel to update Report view if the ODS is not Null
                if (!string.IsNullOrEmpty(TrueODS))
                {
                    // Update Azure
                    await _systemInfoTables.AddOrUpdateSystemInfoAsync(systemInfo);
                    // Fetch all asset information for similar ODS
                    var systemInfoList = await _systemInfoTables.GetAllSystemInfoBySimilarODSAsync(TrueODS);
                    WeakReferenceMessenger.Default.Send(new SystemInfoMessage(systemInfoList));
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"UpdateSystemInfo: Exception: {ex}");
            }
        }

        private async Task SetStationaryDevice()
        {
            _roomStore.StationaryDevice = StationaryDevice;
        }

        private async Task<EdgeUser> CreateUser()
        {
            LoggedIn = _utils.ConvertUtcToGmtWithDst(DateTime.UtcNow);

            var edgeUser = new EdgeUser
            {
                UserName = UserName.ToUpper(),
                HostName = HostName,
                ODS = TrueODS,
                LoggedIn = LoggedIn,
                LastPing = _utils.ConvertUtcToGmtWithDst(DateTime.UtcNow),
                Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(4)
            };

            if (_roomStore.StationaryDevice)
            {
                edgeUser.Room = _roomStore.Room;
            }

            return edgeUser;
        }

        private async Task UpdateLocalUser()
        {
            var edgeUser = new EdgeUser
            {
                UserName = UserName.ToUpper(),
                HostName = HostName,
                ODS = TrueODS,
                Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(4),
            };

            if (_roomStore.StationaryDevice)
            {
                edgeUser.Room = _roomStore.Room;
            }

            // Send User to SignalR server
            await UpdateUserWithSemaphore(edgeUser);
        }

        private async Task<UpdateUserResponse> SetUserProperties(EdgeUser user)
        {
            user.Connected = true;
            user.LastOnline = "Online";
            user.LastPing = _utils.ConvertUtcToGmtWithDst(DateTime.UtcNow);

            // Ensure the version is set
            if (string.IsNullOrEmpty(user.Version))
            {
                user.Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(4);
            }

            //if (_roomStore.StationaryDevice)
            //{
            //    // Move this to the RESTART logic
            //    //if (_isFirstRun)
            //    //{
            //    //    // Try and retrieve any values from Registry, only do this once
            //    //    LoadRegistryValues();
            //    //}

            //    if (string.IsNullOrEmpty(SelectedBlankIPODS))
            //    {
            //        Location tempLocation = _jSONHandler.GetLocationByODS(user.ODS);
            //        if (tempLocation == null)
            //        {
            //            // handle the situation when tempLocation is null
            //            // for instance, you could set ODS to a default value
            //            ODS = "";
            //            SelectedBlankIPODS = "";
            //            TrueODS = "";
            //            user.ODS = "";
            //        }
            //        else
            //        {
            //            // Set all values to correct ODS / Practice
            //            SelectedBlankIPODS = tempLocation.Practice ?? "";
            //            ODS = tempLocation.Practice ?? "";
            //            TrueODS = tempLocation.ODS ?? "";
            //            user.ODS = tempLocation.ODS ?? "";
            //        }
            //    }
            //    else
            //    {
            //        // Ensure all other values are aligned from registry on Azure
            //        TrueODS = _jSONHandler.GetLocationByPractice(SelectedBlankIPODS).ODS;
            //        user.ODS = TrueODS;
            //        ODS = SelectedBlankIPODS;
            //    }

            //    // If registry value retrieval failed, use values from Azure DB
            //    if (string.IsNullOrEmpty(SelectedRoom))
            //    {
            //        SelectedRoom = (user.Room ?? "");
            //    }
            //    else
            //    {
            //        // If there is a registry value, make sure Azure is aligned
            //        user.Room = SelectedRoom;
            //    }
            //}
            //else
            //{
            //    SelectedRoom = null;
            //    user.Room = string.Empty;
            //    if (BlankIPAddress)
            //    {
            //        // Also set the ODS of the user in case it changed
            //        user.ODS = null;
            //        SelectedBlankIPODS = string.Empty;
            //    }
            //}

            //// Always store the eventual outcome
            //_roomStore.Room = SelectedRoom;
            //_roomStore.ODS = SelectedBlankIPODS;

            // Mark as not first run
            _isFirstRun = false;

            // Update Azure
            return await UpdateUserWithSemaphore(user);
        }

        public void WriteSelectedRoomToRegistry()
        {
            try
            {
                string valueToWrite = SelectedRoom ?? string.Empty;
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Edgebits\EdgeAlert", "Room", valueToWrite);
                _log.LogInformation($"Successfully wrote SelectedRoom to registry: {valueToWrite}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error writing SelectedRoom to registry: {ErrorMessage}", ex.Message);
            }
        }

        public void WriteSelectedBlankIPODSToRegistry()
        {
            try
            {
                string valueToWrite = SelectedBlankIPODS ?? string.Empty;
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Edgebits\EdgeAlert", "ODS", valueToWrite);
                _log.LogInformation($"Successfully wrote SelectedBlankIPODS to registry: {valueToWrite}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error writing SelectedBlankIPODS to registry: {ErrorMessage}", ex.Message);
            }
        }

        private async void SendMessage(string message)
        {
            try
            {
                await Task.Delay(1000); // Wait for 3 seconds
                await DispatcherHelper.RunOnUIThreadAsync(async () =>
                {
                    WeakReferenceMessenger.Default.Send(new MainViewModelMessage(message));
                });
            }
            catch (Exception ex)
            {
                _log.LogError($"Error sending message: {ex.Message}");
            }
        }

        async partial void OnSelectedBlankIPODSChanged(string value) // Value is Practice Name
        {
            // Prevent execution during initial load or programmatic changes
            if (_programmaticChangeInProgress || _isFirstRun || value == null)
            {
                _log.LogDebug("OnSelectedBlankIPODSChanged: Skipping execution. ProgrammaticChange={IsProgrammatic}, IsFirstRun={IsFirst}, ValueIsNull={IsNull}",
                    _programmaticChangeInProgress, _isFirstRun, value == null);
                return; // Skip execution if change is not from user interaction or invalid value
            }

            // --- User Interaction Logic Starts ---
            _programmaticChangeInProgress = true; // Signal that a user-driven ODS change is in progress
            _log.LogInformation("OnSelectedBlankIPODSChanged: User selected Practice: '{PracticeName}', _programmaticChangeInProgress=true", value);

            Location location = null;
            string newOdsCode = string.Empty;
            ObservableCollection<string> newRooms = new ObservableCollection<string>(); // Prepare new room list

            try
            {
                location = _jSONHandler.GetLocationByPractice(value);

                if (location != null && !string.IsNullOrEmpty(location.ODS)) // Check if location and ODS are valid
                {
                    newOdsCode = location.ODS;
                    if (location.Rooms != null)
                    {
                        foreach (var room in location.Rooms) // Populate temporary list
                        {
                            newRooms.Add(room);
                        }
                    }
                    _roomStore.ODS = location.Practice; // Update store with the selected Practice Name
                    _log.LogInformation("OnSelectedBlankIPODSChanged: Found Location. ODS Code='{OdsCode}', Practice='{Practice}', Rooms Found={RoomCount}", newOdsCode, location.Practice, newRooms.Count);
                }
                else
                {
                    _log.LogError("OnSelectedBlankIPODSChanged: Could not find valid location details for practice '{PracticeName}'", value);
                    // Keep newOdsCode and newRooms empty
                    _roomStore.ODS = value; // Still store the selected (potentially invalid) practice name? Or clear? Let's keep it for now.
                }

                // Update ViewModel properties safely using Dispatcher
                // Crucially, update Rooms collection *before* clearing SelectedRoom
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _log.LogDebug("OnSelectedBlankIPODSChanged (Dispatcher): Updating VM properties...");
                    TrueODS = newOdsCode; // Set TrueODS (might be empty if lookup failed)
                    ODS = value; // Reflect the user's selection in the VM

                    // *** Update Rooms Collection Reliably ***
                    Rooms.Clear(); // Clear existing items
                    foreach (var room in newRooms) // Add items from the temporary list
                    {
                        Rooms.Add(room);
                    }
                    _log.LogDebug("OnSelectedBlankIPODSChanged (Dispatcher): Rooms collection updated ({Count} items).", Rooms.Count);

                    // Clear room selection now that the ODS/Practice has changed
                    SelectedRoom = string.Empty;
                    RoomIndex = -1;
                    StationaryDeviceIsEnabled = false; // Disable stationary until a room is selected
                    _log.LogDebug("OnSelectedBlankIPODSChanged (Dispatcher): Cleared SelectedRoom and disabled StationaryDevice checkbox.");
                });

                // Clear room in store as well
                _roomStore.Room = string.Empty;

                // Update user record on server ONLY if we have a valid ODS code
                if (!string.IsNullOrEmpty(newOdsCode))
                {
                    _log.LogInformation("OnSelectedBlankIPODSChanged: Updating user on server with new ODS '{OdsCode}' and cleared room.", newOdsCode);
                    await UpdateUserWithSemaphore(new EdgeUser
                    {
                        UserName = UserName,
                        HostName = HostName,
                        ODS = newOdsCode, // Use the determined True ODS
                        Room = "", // Room is cleared when ODS changes
                        Alert = Alert, // Preserve existing alert status if any
                        LastPing = _utils.ConvertUtcToGmtWithDst(DateTime.UtcNow),
                        Connected = true, // Assuming connection is active if this runs
                        Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(4)
                    });
                    await UpdateSystemInfo(); // Update system info with new ODS
                }
                else
                {
                    _log.LogWarning("OnSelectedBlankIPODSChanged: Skipping server user update because ODS code is invalid/empty.");
                }

                // Save registry settings (Practice Name and cleared Room)
                WriteSelectedBlankIPODSToRegistry(); // Saves SelectedBlankIPODS (which is 'value')
                WriteSelectedRoomToRegistry(string.Empty); // Explicitly save cleared Room

                // Refresh reports if necessary (consider if needed on every ODS change)
                await FireDrill();
                await AlertHistory();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "OnSelectedBlankIPODSChanged: Error processing ODS change for '{PracticeName}'", value);
                // Consider resetting UI state or showing error message
                await Application.Current.Dispatcher.InvokeAsync(() => {
                    // Optionally clear selections or show error state
                    Rooms.Clear();
                    SelectedRoom = string.Empty;
                    RoomIndex = -1;
                    StationaryDeviceIsEnabled = false;
                });
                _roomStore.Room = string.Empty;
            }
            finally
            {
                _programmaticChangeInProgress = false; // Release the flag
                _log.LogInformation("OnSelectedBlankIPODSChanged: Finished processing for '{PracticeName}', _programmaticChangeInProgress=false", value); // Use 'value' parameter if accessible

                // --- Start: Add Queued Check ---
                if (_updateRequestedWhileBusy)
                {
                    _log.LogInformation("OnSelectedBlankIPODSChanged: Update was requested during ODS change. Triggering update.");
                    _updateRequestedWhileBusy = false; // Reset the flag
                    _ = Task.Run(() => UpdateConnectedUsersAsync());
                }
            }
        }

        // In MainViewModel.cs

        /// <summary>
        /// Handles changes to the SelectedRoom property.
        /// When triggered by user interaction, it updates the user's room on the server and saves the selection.
        /// It prevents execution if the change was triggered programmatically during initialization or reconnect.
        /// </summary>
        /// <param name="value">The newly selected room name, or null/empty if cleared.</param>
        async partial void OnSelectedRoomChanged(string value)
        {
            // *** ADDED CHECK: Exit if change is programmatic or during first run ***
            if (_programmaticChangeInProgress || _isFirstRun)
            {
                _log.LogDebug($"OnSelectedRoomChanged: Skipping execution. ProgrammaticChange={_programmaticChangeInProgress}, IsFirstRun={_isFirstRun}");
                // Still update the IsEnabled state for the StationaryDevice checkbox based on the value
                // even if the change was programmatic. Use Dispatcher for UI property access.
                await Application.Current.Dispatcher.InvokeAsync(() => {
                    StationaryDeviceIsEnabled = !string.IsNullOrEmpty(value);
                });
                return; // *** EXIT EARLY ***
            }

            // --- User Interaction logic below ---
            _log.LogInformation($"OnSelectedRoomChanged: User interaction detected. Selected Room: '{value ?? "null"}'"); // Log null appropriately
            _programmaticChangeInProgress = true; // Set flag before starting async work
            _log.LogDebug("OnSelectedRoomChanged: Set _programmaticChangeInProgress=true");

            try // --- Start Try Block ---
            {
                // Update store immediately reflects user choice
                _roomStore.Room = value ?? string.Empty;

                // Enable/Disable Stationary Checkbox based on user selection
                StationaryDeviceIsEnabled = !string.IsNullOrEmpty(value);

                // Determine Correct ODS to send to server based on the current state of RoomStore
                string currentTrueOds = _roomStore.GetTrueODS(); // Get ODS based on current RoomStore.ODS

                if (string.IsNullOrEmpty(currentTrueOds) || currentTrueOds == "UNKNOWN_ODS")
                {
                    _log.LogError($"OnSelectedRoomChanged: Cannot update server - True ODS code is unknown for Practice '{_roomStore.ODS}'.");
                    // Potentially provide user feedback here?
                }
                else {
                    // Update the user record on the server
                    _log.LogInformation($"OnSelectedRoomChanged: Updating server. User='{UserName}', Host='{HostName}', ODS='{currentTrueOds}', Room='{value ?? "null"}'.");
                    var updateResponse = await UpdateUserWithSemaphore(new EdgeUser
                    {
                        UserName = UserName,
                        HostName = HostName,
                        ODS = currentTrueOds,
                        Room = value ?? string.Empty, // Use selected value
                        Alert = Alert, // Preserve existing alert status
                        LastPing = _utils.ConvertUtcToGmtWithDst(DateTime.UtcNow),
                        Connected = true, // Assume connected if user can change room
                        Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(4)
                    });
                    if (updateResponse == null || updateResponse.IsBlocked)
                    {
                        _log.LogError($"OnSelectedRoomChanged: Server update failed or user blocked when updating room.");
                        // Handle failed update if necessary (e.g., revert UI, show message)
                    }
                }

                // Write to registry - Moved inside try block for consistency
                // No need to check _programmaticChangeInProgress here as we wouldn't be inside the 'else' of the initial check if it were true.
                _log.LogInformation($"OnSelectedRoomChanged: Proceeding with registry write for value '{value ?? "null"}'.");
                WriteSelectedRoomToRegistry(value ?? string.Empty); // Save user's selection

                // Update other parts of the system that might depend on the room change
                await UpdateSystemInfo();
                await FireDrill();
                // await AlertHistory(); // Uncomment if needed

            }
            catch (Exception ex)
            {
                _log.LogError(ex, "OnSelectedRoomChanged: Error during processing room change for value '{RoomValue}'.", value ?? "null");
                // Handle error appropriately - perhaps show a message to the user?
                // SendMessage("Error updating room selection.");
            }
            finally
            {
                _programmaticChangeInProgress = false; // Reset flag
                _log.LogDebug("OnSelectedRoomChanged: Reset _programmaticChangeInProgress=false");

                // Check if an update was requested while busy
                if (_updateRequestedWhileBusy)
                {
                    _log.LogInformation("OnSelectedRoomChanged: Update was requested during room change. Triggering update.");
                    _updateRequestedWhileBusy = false; // Reset the flag
                    _ = Task.Run(() => UpdateConnectedUsersAsync()); // Trigger update
                }
            }
        }

        async partial void OnStationaryDeviceChanged(bool value)
        {
            //Properties.Settings.Default.StationaryDevice = StationaryDevice;
            //Properties.Settings.Default.Save();
            try
            {
                RegistryKey key = Registry.LocalMachine.CreateSubKey("SOFTWARE\\Edgebits\\EdgeAlert");
                key.SetValue("StationaryDevice", StationaryDevice);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error creating or setting Registry key.");
            }
            //RegistryKey key = Registry.LocalMachine.CreateSubKey("SOFTWARE\\Edgebits\\EdgeAlert");
            //key.SetValue("StationaryDevice", StationaryDevice);
        }

        private async Task RespondToPing()
        {
            EdgeUser user = await _edgeAlertTables.GetUserAsync(UserName, HostName);
            user.LastPing = _utils.ConvertUtcToGmtWithDst(DateTime.UtcNow); // Set the LastPing to the current time in ISO 8601 format.
            await UpdateUserWithSemaphore(user); // Update the user's data in the database.
        }

        /// <summary>
        /// Updates the list of connected users displayed in the UI, fetching fresh data and handling alerts.
        /// Ensures only one update process runs at a time using a semaphore.
        /// </summary>
        /// <param name="users">Note: This parameter is received from SignalR but IGNORED. Data is always fetched fresh.</param>
        // In MainViewModel.cs

        /// <summary>
        /// Updates the list of connected users displayed in the UI, fetching fresh data and handling alerts.
        /// Ensures only one update process runs at a time using a semaphore.
        /// </summary>
        /// <param name="usersFromSignalR">Note: This parameter can be from SignalR. 
        /// The method can either use it or fetch fresh data.
        /// Current implementation primarily fetches fresh data via GetUsersAsync().
        /// </param>
        private async Task UpdateConnectedUsersAsync(List<EdgeUserViewModel> usersFromSignalR = null)
        {
            _log.LogTrace("UpdateConnectedUsersAsync: Update request received.");

            if (_programmaticChangeInProgress)
            {
                _log.LogWarning("UpdateConnectedUsersAsync: Programmatic change in progress. Flagging request and skipping immediate execution.");
                _updateRequestedWhileBusy = true;
                return;
            }

            bool acquired = await _userUpdateSemaphore.WaitAsync(TimeSpan.Zero);
            if (!acquired)
            {
                _log.LogWarning("UpdateConnectedUsersAsync: Update already in progress. Skipping this request.");
                return;
            }

            _log.LogInformation("UpdateConnectedUsersAsync: Semaphore acquired, proceeding with update.");

            try
            {
                _log.LogInformation("UpdateConnectedUsersAsync: Processing update - fetching fresh data.");

                if (!CheckSelectedInputs())
                {
                    ClearEdgeUsers();
                    _log.LogWarning("UpdateConnectedUsersAsync: ODS/Room not selected. Cleared users and aborted processing.");
                    return;
                }

                var locations = _jSONHandler.Data?.Locations;
                if (locations == null)
                {
                    _log.LogError("UpdateConnectedUsersAsync: No locations found in JSON data. Skipping screen population.");
                    ClearEdgeUsers();
                    return;
                }

                // Fetch fresh user data (as per your existing logic)
                var fetchedUsers = await GetUsersAsync(); // This returns List<EdgeUser>
                if (fetchedUsers == null)
                {
                    _log.LogError("UpdateConnectedUsersAsync: No Users returned from Database. Skipping screen population.");
                    return;
                }
                _log.LogDebug("UpdateConnectedUsersAsync: Fetched {Count} users from database.", fetchedUsers.Count);

                // Map EdgeUser to EdgeUserViewModel
                var usersToDisplay = fetchedUsers.Select(user => new EdgeUserViewModel
                {
                    UserName = user.UserName,
                    HostName = user.HostName,
                    ODS = user.ODS, // Keep original ODS for location lookup
                    Room = user.Room,
                    Alert = user.Alert,
                    LoggedIn = user.LoggedIn,
                    LastOnline = user.LastOnline,
                    Connected = user.Connected,
                    Version = user.Version,
                    Responders = new ObservableCollection<Responder>() // Initialize responders list
                }).ToList();

                bool alertFoundForOtherUsers = false;
                var edgeUsersViewModelCollection = new ObservableCollection<EdgeUserViewModel>();

                foreach (var userViewModel in usersToDisplay)
                {
                    UpdateUserWithLocation(userViewModel, locations.ToList());

                    userViewModel.IsCurrentUser = string.Equals(userViewModel.UserName, UserName, StringComparison.OrdinalIgnoreCase) &&
                                                  string.Equals(userViewModel.HostName, HostName, StringComparison.OrdinalIgnoreCase);

                    if (!string.IsNullOrEmpty(userViewModel.Alert))
                    {
                        _log.LogDebug("UpdateConnectedUsersAsync: User {User} (Alert: {AlertTime}) is being processed for responders.", userViewModel.UserName, userViewModel.Alert);

                        // ProcessAlertForUser fetches responders and updates userViewModel.Responders.
                        // Its boolean return indicates if (alert is active AND no responders were found by ITS fetch).
                        await ProcessAlertForUser(userViewModel, userViewModel); // Ensure this populates userViewModel.Responders

                        if (!userViewModel.IsCurrentUser) // If it's another user's alert
                        {
                            // AMENDED LOGIC HERE:
                            // Check if THIS specific alert from another user actually needs sound
                            // (i.e., it has an active alert string AND it has no valid/non-empty responders AFTER ProcessAlertForUser ran)
                            bool hasAnyValidResponder = userViewModel.Responders?.Any(r => !IsResponderEmpty(r)) ?? false;
                            bool thisOtherAlertNeedsSound = !hasAnyValidResponder; // Sound is needed if it does NOT have any valid responder.

                            // Detailed Log for debugging this specific condition:
                            int responderCount = userViewModel.Responders?.Count(r => !IsResponderEmpty(r)) ?? 0; // Count valid ones
                            _log.LogDebug("UpdateConnectedUsersAsync Check for {User}: Responders List Exists={Exists}, Has Valid Responder={HasValid} (Count: {ResponderCount}), Needs Sound={NeedsSound}",
                                          userViewModel.UserName,
                                          userViewModel.Responders != null,
                                          hasAnyValidResponder,
                                          responderCount,
                                          thisOtherAlertNeedsSound);

                            if (thisOtherAlertNeedsSound)
                            {
                                // Only if this specific alert (from another user) truly needs sound,
                                // then we consider the global flag.
                                alertFoundForOtherUsers = true;
                                _log.LogDebug("UpdateConnectedUsersAsync: Alert from other user {User} requires sound. Setting alertFoundForOtherUsers = true.", userViewModel.UserName);
                                // Note: Current code processes all users. If you only need to know if *any* alert exists,
                                // you could 'break;' here, but that would skip processing other users for UI updates.
                                // Given current structure, it's okay to let it continue and alertFoundForOtherUsers will be true if any one matches.
                            }
                            else
                            {
                                _log.LogDebug("UpdateConnectedUsersAsync: Alert from other user {User} does NOT require sound (already has responders or alert string is now empty).", userViewModel.UserName);
                            }
                        }
                    }
                    edgeUsersViewModelCollection.Add(userViewModel);
                }

                // Set HasCurrentUserResponded flag after all responders are potentially fetched
                foreach (var userVM in edgeUsersViewModelCollection)
                {
                    userVM.HasCurrentUserResponded = userVM.Responders?.Any(r =>
                        string.Equals(r.ResponderName, UserName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.ResponderHostName, HostName, StringComparison.OrdinalIgnoreCase)) ?? false;

                    if (userVM.IsCurrentUser)
                    {
                        ProcessCurrentUserAlert(userVM);
                    }
                }

                ResponderHasBeenSet = HasNonEmptyResponderForCurrentUser(edgeUsersViewModelCollection);
                _alertStore.ResponderHasBeenSet = ResponderHasBeenSet;

                await MergeTimestampWithEdgeUserViewModel(edgeUsersViewModelCollection);
                UpdateEdgeUsers(edgeUsersViewModelCollection);

                _log.LogInformation("UpdateConnectedUsersAsync: Final check for HandleAlerts with alertFoundForOtherUsers = {Status}", alertFoundForOtherUsers);
                HandleAlerts(alertFoundForOtherUsers);

                _log.LogInformation("UpdateConnectedUsersAsync: Processing complete.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "UpdateConnectedUsersAsync: Error during processing.");
            }
            finally
            {
                _log.LogDebug("UpdateConnectedUsersAsync: Releasing semaphore.");
                _userUpdateSemaphore.Release();

                if (_updateRequestedWhileBusy)
                {
                    _log.LogInformation("UpdateConnectedUsersAsync: Update was requested while busy. Triggering follow-up update.");
                    _updateRequestedWhileBusy = false;
                    _ = Task.Run(() => UpdateConnectedUsersAsync());
                }
            }
        }

        /// <summary>
        /// Contains the core logic for processing a list of users received from SignalR,
        /// updating the local collection, handling alerts, etc.
        /// </summary>
        private async Task ProcessUserUpdateList(List<EdgeUserViewModel> usersToProcess)
        {
            // Check if ODS and Room selections are valid before proceeding
            if (!CheckSelectedInputs())
            {
                ClearEdgeUsers();
                _log.LogWarning("ProcessUserUpdateList: Skipping processing because ODS/Room not selected.");
                return;
            }

            var locations = _jSONHandler.Data?.Locations;
            if (locations == null)
            {
                _log.LogError("ProcessUserUpdateList: No locations found in JSONHandler. Skipping screen population.");
                ClearEdgeUsers();
                return;
            }

            // Use the provided user list if available, otherwise fetch fresh data
            if (usersToProcess == null || !usersToProcess.Any())
            {
                _log.LogWarning("ProcessUserUpdateList: No user list provided. Fetching fresh list from database.");
                var fetchedUsers = await GetUsersAsync();
                if (fetchedUsers == null)
                {
                    _log.LogError("ProcessUserUpdateList: Failed to fetch users from database. Skipping.");
                    ClearEdgeUsers();
                    return;
                }
                _log.LogDebug("ProcessUserUpdateList: Fetched {Count} users from database.", fetchedUsers.Count);
                // Map fetched EdgeUser to EdgeUserViewModel
                usersToProcess = fetchedUsers.Select(user => new EdgeUserViewModel
                {
                    UserName = user.UserName,
                    HostName = user.HostName,
                    ODS = user.ODS,
                    Room = user.Room,
                    Alert = user.Alert,
                    LoggedIn = user.LoggedIn,
                    LastOnline = user.LastOnline,
                    Connected = user.Connected,
                    Version = user.Version,
                    Responders = new ObservableCollection<Responder>() // Initialize as needed
                }).ToList();
            }
            else
            {
                _log.LogDebug("ProcessUserUpdateList: Processing {Count} users provided directly.", usersToProcess.Count);
            }

            // --- Start of the main processing logic (moved from the original UpdateConnectedUsersAsync) ---
            bool alertFound = false;
            var edgeUsersViewModel = new ObservableCollection<EdgeUserViewModel>();

            foreach (var user in usersToProcess)
            {
                UpdateUserWithLocation(user, locations.ToList());
                var edgeUserViewModel = await CreateEdgeUserViewModel(user);
                if (edgeUserViewModel == null) continue;

                // First, we check for the existence of an alert.
                if (!string.IsNullOrEmpty(user.Alert))
                {
                    // Process the alert irrespective of the hostname.
                    var alertProcessed = await ProcessAlertForUser(user, edgeUserViewModel);

                    // But, update the alertFound variable only when hostname doesn't match.
                    if (user.HostName != HostName)
                    {
                        alertFound = alertProcessed;
                    }
                }
                else
                {
                    edgeUserViewModel.Responders.Add(new Responder());
                }

                edgeUsersViewModel.Add(edgeUserViewModel);
                if (edgeUserViewModel.IsCurrentUser)
                {
                    ProcessCurrentUserAlert(edgeUserViewModel);
                }
            }

            // Set the IsCurrentUser flag explicitly for every user
            foreach (var user in edgeUsersViewModel)
            {
                user.IsCurrentUser = string.Equals(user.UserName, UserName, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(user.HostName, HostName, StringComparison.OrdinalIgnoreCase);

                // Check if the current user has responded to this alert
                if (!string.IsNullOrEmpty(user.Alert) && user.Responders != null && user.Responders.Any())
                {
                    user.HasCurrentUserResponded = user.Responders.Any(r =>
                        string.Equals(r.ResponderName, UserName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.ResponderHostName, HostName, StringComparison.OrdinalIgnoreCase));

                    _log.LogInformation($"User {user.UserName} alert: HasCurrentUserResponded = {user.HasCurrentUserResponded}");
                }
                else
                {
                    user.HasCurrentUserResponded = false;
                }

                // Only process alert state for the current user's alert
                if (user.IsCurrentUser)
                {
                    ProcessCurrentUserAlert(user);
                }
            }

            // Check for responders specifically for the current user's alert only
            ResponderHasBeenSet = HasNonEmptyResponderForCurrentUser(edgeUsersViewModel);
            _alertStore.ResponderHasBeenSet = ResponderHasBeenSet;

            // Merge timestamps with EdgeUserViewModel
            await MergeTimestampWithEdgeUserViewModel(edgeUsersViewModel);
            UpdateEdgeUsers(edgeUsersViewModel);

            if (alertFound)
            {
                // Log details... (original logging code here)
                var sb = new StringBuilder("ProcessUserUpdateList: Alert Active: User List:");
                foreach (var item in edgeUsersViewModel)
                {
                    sb.AppendLine($"UserName: {item.UserName}, HostName: {item.HostName}, ODS: {item.ODS}, Room: {item.Room}, Alert: {item.Alert}");
                }
                _log.LogError(sb.ToString());
            }

            HandleAlerts(alertFound);
            // --- End of the main processing logic ---
        }

        //private async Task UpdateConnectedUsersAsync(List<EdgeUserViewModel> users = null)
        //{
        //    _log.LogInformation("UpdateConnectedUsersAsync: Received Hub message to Update/Refresh");

        //    if (!CheckSelectedInputs())
        //    {
        //        ClearEdgeUsers();
        //        return;
        //    }

        //    var locations = _jSONHandler.Data?.Locations;
        //    if (locations == null)
        //    {
        //        _log.LogError("UpdateConnectedUsersAsync: No locations found. Skipping screen population");
        //        ClearEdgeUsers();
        //        return;
        //    }

        //    // Overwrite `users` with data from `GetUsersAsync`
        //    var fetchedUsers = await GetUsersAsync();
        //    if (fetchedUsers == null)
        //    {
        //        _log.LogError("UpdateConnectedUsersAsync: No Users returned from Database. Skipping screen population");
        //        return;
        //    }

        //    // Map `fetchedUsers` to `List<EdgeUserViewModel>`
        //    users = fetchedUsers.Select(user => new EdgeUserViewModel
        //    {
        //        UserName = user.UserName,
        //        HostName = user.HostName,
        //        ODS = user.ODS,
        //        Room = user.Room,
        //        Alert = user.Alert,
        //        LoggedIn = user.LoggedIn,
        //        LastOnline = user.LastOnline,
        //        Connected = user.Connected,
        //        Responders = new ObservableCollection<Responder>() // Initialize as needed
        //    }).ToList();

        //    bool alertFound = false;
        //    var edgeUsersViewModel = new ObservableCollection<EdgeUserViewModel>();

        //    foreach (var user in users)
        //    {
        //        UpdateUserWithLocation(user, locations.ToList());
        //        var edgeUserViewModel = await CreateEdgeUserViewModel(user);
        //        if (edgeUserViewModel == null) continue;

        //        // First, we check for the existence of an alert.
        //        if (!string.IsNullOrEmpty(user.Alert))
        //        {
        //            // Process the alert irrespective of the hostname.
        //            var alertProcessed = await ProcessAlertForUser(user, edgeUserViewModel);

        //            // But, update the alertFound variable only when hostname doesn't match.
        //            if (user.HostName != HostName)
        //            {
        //                alertFound = alertProcessed;
        //            }
        //        }
        //        else
        //        {
        //            edgeUserViewModel.Responders.Add(new Responder());
        //        }

        //        edgeUsersViewModel.Add(edgeUserViewModel);
        //        ProcessCurrentUserAlert(edgeUserViewModel);
        //    }

        //    // Set the IsCurrentUser flag explicitly for every user
        //    foreach (var user in edgeUsersViewModel)
        //    {
        //        user.IsCurrentUser = string.Equals(user.UserName, UserName, StringComparison.OrdinalIgnoreCase) &&
        //                             string.Equals(user.HostName, HostName, StringComparison.OrdinalIgnoreCase);

        //        // Check if the current user has responded to this alert
        //        if (!string.IsNullOrEmpty(user.Alert) && user.Responders != null && user.Responders.Any())
        //        {
        //            user.HasCurrentUserResponded = user.Responders.Any(r =>
        //                string.Equals(r.ResponderName, UserName, StringComparison.OrdinalIgnoreCase) &&
        //                string.Equals(r.ResponderHostName, HostName, StringComparison.OrdinalIgnoreCase));

        //            _log.LogInformation($"User {user.UserName} alert: HasCurrentUserResponded = {user.HasCurrentUserResponded}");
        //        }
        //        else
        //        {
        //            user.HasCurrentUserResponded = false;
        //        }

        //        // Only process alert state for the current user's alert
        //        if (user.IsCurrentUser)
        //        {
        //            ProcessCurrentUserAlert(user);
        //        }
        //    }

        //    // Check for responders specifically for the current user's alert only
        //    ResponderHasBeenSet = HasNonEmptyResponderForCurrentUser(edgeUsersViewModel);
        //    _alertStore.ResponderHasBeenSet = ResponderHasBeenSet;

        //    // Merge timestamps with EdgeUserViewModel
        //    await MergeTimestampWithEdgeUserViewModel(edgeUsersViewModel);
        //    UpdateEdgeUsers(edgeUsersViewModel);

        //    // Log details if any alerts are found
        //    if (alertFound)
        //    {
        //        var sb = new StringBuilder("Alert Active: User List:");
        //        foreach (var item in edgeUsersViewModel)
        //        {
        //            sb.AppendLine($"UserName: {item.UserName}, HostName: {item.HostName}, ODS: {item.ODS}, Room: {item.Room}, Alert: {item.Alert}");
        //        }
        //        _log.LogError(sb.ToString());
        //    }

        //    HandleAlerts(alertFound);
        //}

        private bool HasNonEmptyResponderForCurrentUser(IEnumerable<EdgeUserViewModel> edgeUserViewModels)
        {
            if (edgeUserViewModels == null)
                return false;

            // Find the current user's view model
            var currentUserViewModel = edgeUserViewModels.FirstOrDefault(u =>
                string.Equals(u.UserName, UserName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(u.HostName, HostName, StringComparison.OrdinalIgnoreCase));

            if (currentUserViewModel == null)
                return false;

            // Check if the current user has an alert and if it has responders
            if (string.IsNullOrEmpty(currentUserViewModel.Alert))
                return false;

            if (currentUserViewModel.Responders == null || !currentUserViewModel.Responders.Any())
                return false;

            // Check if there are any non-empty responders
            return currentUserViewModel.Responders.Any(r => !IsResponderEmpty(r));
        }

        private bool CheckSelectedInputs()
        {
            _log.LogInformation($"CheckSelectedInputs: ODS: {SelectedBlankIPODS}");
            _log.LogInformation($"CheckSelectedInputs: Selected Room: {SelectedRoom}");

            if (string.IsNullOrEmpty(SelectedBlankIPODS) && (BlankIPAddress))
            {
                _log.LogInformation("CheckSelectedInputs: No ODS Selected. Skipping screen population");
                ODSIndex = -1;
                return false;
            }
            if (string.IsNullOrEmpty(SelectedRoom))
            {
                _log.LogInformation("CheckSelectedInputs: No Room Selected. Skipping screen population");
                RoomIndex = -1;
                return false;
            }

            return true;
        }


        private async Task<ObservableCollection<EdgeUser>> GetUsersAsync()
        {
            try
            {
                return await _edgeAlertTables.GetFilteredUsersAsync(LocationToODS(ODS)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError($"GetUsersAsync: Failed to get Filtered Users from: {LocationToODS(ODS)}. Error: {ex}");
                return null;
            }
        }

        private void UpdateUserWithLocation(EdgeUserViewModel user, List<Location> locations)
        {
            var location = locations.FirstOrDefault(l => l.ODS == user.ODS);
            if (location != null)
            {
                user.ODS = location.Practice;
            }
        }

        private async Task<EdgeUserViewModel> CreateEdgeUserViewModel(EdgeUser user)
        {
            var edgeUserViewModel = new EdgeUserViewModel
            {
                UserName = user.UserName,
                HostName = user.HostName,
                ODS = user.ODS,
                Room = user.Room,
                Alert = user.Alert,
                LoggedIn = user.LoggedIn,
                LastOnline = user.LastOnline,
                Connected = user.Connected,
                Responders = new ObservableCollection<Responder>(),
                // Set IsCurrentUser based on comparison with current user's credentials
                IsCurrentUser = string.Equals(user.UserName, UserName, StringComparison.OrdinalIgnoreCase) //&&
                                //string.Equals(user.HostName, HostName, StringComparison.OrdinalIgnoreCase)
            };

            return edgeUserViewModel;
        }

        private async Task<bool> ProcessAlertForUser(EdgeUser user, EdgeUserViewModel edgeUserViewModel)
        {
            _log.LogInformation("ProcessAlertForUser: Checking for Alerts and Responders");
            var responders = await GetRespondersForUser(user);
            if (responders != null)
            {
                foreach (var responder in responders)
                {
                    edgeUserViewModel.Responders.Add(responder);
                }

                _log.LogInformation($"ProcessAlertForUser: {user.UserName} has {responders.Count} responders.");
                return responders.Count == 0;
            }
            return false;
        }

        private async Task<ObservableCollection<Responder>> GetRespondersForUser(EdgeUser user)
        {
            try
            {
                return await _responderTables.GetRespondersByAlertTimeAsync(Utils.ConvertDateFormat(user.Alert)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError($"GetRespondersForUser: Failed to get Responders by Alert Time. Error: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Processes alert status flags (AlertSet, ResponderHasBeenSet) specifically for the current user's view model.
        /// This determines the border color in the UI for the user's own row.
        /// </summary>
        /// <param name="currentUserViewModel">The view model representing the current user.</param>
        private void ProcessCurrentUserAlert(EdgeUserViewModel currentUserViewModel)
        {
            // Ensure this is actually the current user - belt and braces check
            if (!currentUserViewModel.IsCurrentUser)
            {
                _log.LogWarning("ProcessCurrentUserAlert called with a non-current user view model. Skipping.");
                return;
            }

            _log.LogDebug($"ProcessCurrentUserAlert: Processing for current user. Alert='{currentUserViewModel.Alert}', ResponderCount={currentUserViewModel.Responders?.Count ?? 0}");

            if (!string.IsNullOrEmpty(currentUserViewModel.Alert))
            {
                // Current user has an alert set...
                bool hasValidResponders = currentUserViewModel.Responders != null &&
                                         currentUserViewModel.Responders.Any(r => !IsResponderEmpty(r));

                if (!hasValidResponders)
                {
                    // ...with no valid responders -> Red Border
                    _log.LogDebug("ProcessCurrentUserAlert: Setting AlertSet=true (Red Border)");
                    _alertStore.AlertSet = true;
                    _alertStore.ResponderHasBeenSet = false; // Ensure responder flag is false
                    AlertHasBeenSet = true; // Update VM property
                    ResponderHasBeenSet = false; // Update VM property
                }
                else
                {
                    // ...with valid responders -> Yellow Border
                    _log.LogDebug("ProcessCurrentUserAlert: Setting ResponderHasBeenSet=true (Yellow Border)");
                    _alertStore.AlertSet = false; // Ensure alert flag is false
                    _alertStore.ResponderHasBeenSet = true;
                    AlertHasBeenSet = false; // Update VM property
                    ResponderHasBeenSet = true; // Update VM property
                }
            }
            else
            {
                // Current user has no alert set -> No Border
                _log.LogDebug("ProcessCurrentUserAlert: Clearing alert flags (No Border)");
                _alertStore.AlertSet = false;
                _alertStore.ResponderHasBeenSet = false;
                AlertHasBeenSet = false; // Update VM property
                ResponderHasBeenSet = false; // Update VM property
            }
        }

        private bool IsResponderEmpty(Responder responder)
        {
            // Check if all properties of the responder are empty (null)
            return string.IsNullOrEmpty(responder.AlertTime) &&
                   string.IsNullOrEmpty(responder.ResponderName) &&
                   string.IsNullOrEmpty(responder.ResponderHostName) &&
                   string.IsNullOrEmpty(responder.AlertName) &&
                   string.IsNullOrEmpty(responder.ResponderCancelled);
        }

        // Update the HasNonEmptyResponder method to check if any responders are for the current user
        public bool HasNonEmptyResponder(IEnumerable<EdgeUserViewModel> edgeUserViewModels)
        {
            if (edgeUserViewModels == null)
                return false; // Return false if the collection is null

            // Check only the current user's alert (where IsCurrentUser is true)
            var currentUserViewModel = edgeUserViewModels.FirstOrDefault(vm => vm.IsCurrentUser);

            if (currentUserViewModel == null || currentUserViewModel.Responders == null)
                return false;

            // Check if the current user's alert has any non-empty responders
            return currentUserViewModel.Responders.Any(responder => !IsResponderEmpty(responder));
        }

        private bool HasCurrentUserResponded(EdgeUserViewModel alertUser)
        {
            if (alertUser == null || alertUser.Responders == null || !alertUser.Responders.Any())
                return false;

            // Check if any of the responders match the current user
            return alertUser.Responders.Any(r =>
                string.Equals(r.ResponderName, UserName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ResponderHostName, HostName, StringComparison.OrdinalIgnoreCase));
        }

        private async Task MergeTimestampWithEdgeUserViewModel(ObservableCollection<EdgeUserViewModel> edgeUserViewModels)
        {
            var timestamps = await _edgeAlertTables.GetAllUserTimestampsAsync(TrueODS);

            foreach (var edgeUserViewModel in edgeUserViewModels)
            {
                if (edgeUserViewModel.UserName != null && edgeUserViewModel.HostName != null)
                {
                    string uniqueKey = edgeUserViewModel.UserName.ToUpper() + "_" + edgeUserViewModel.HostName;
                    if (timestamps.TryGetValue(uniqueKey, out DateTime? timestamp))
                    {
                        if (timestamp.HasValue)
                        {
                            // Convert to local time including daylight saving time adjustment
                            DateTime localDateTime = timestamp.Value.ToLocalTime();

                            // Format the DateTime to "dd/MM/yyyy HH:mm:ss"
                            string formattedDateTime = localDateTime.ToString("dd/MM/yyyy HH:mm:ss");

                            edgeUserViewModel.DateTimeLoggedIn = formattedDateTime;
                        }
                        else
                        {
                            edgeUserViewModel.DateTimeLoggedIn = null;
                        }
                    }
                }
            }
        }

        private void UpdateEdgeUsers(ObservableCollection<EdgeUserViewModel> edgeUsersViewModel)
        {
            var sortedUsers = edgeUsersViewModel
                        .Where(u => !string.IsNullOrEmpty(u.Alert))
                        .OrderByDescending(u => DateTime.ParseExact(u.Alert, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture))
                        .Concat(edgeUsersViewModel.Where(u => string.IsNullOrEmpty(u.Alert)))
                        .ToList();

            // Check if the sortedUsers is null or empty
            if (sortedUsers == null || !sortedUsers.Any())
            {
                _log.LogError("UpdateEdgeUsers: sortedUsers is null or empty. No data to process.");

                // Log all items in edgeUsersViewModel
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("edgeUsersViewModel contains the following items:");
                foreach (var item in edgeUsersViewModel)
                {
                    sb.AppendLine($"UserName: {item.UserName}, HostName: {item.HostName}, Alert: {item.Alert}");
                }
                _log.LogError(sb.ToString());

                ClearEdgeUsers();

                return;
            }

            string? currentSelectedUserName = _roomStore.User?.UserName;

            // Clear list before populating
            ClearEdgeUsers();

            EdgeUsers = new ObservableCollection<EdgeUserViewModel>(sortedUsers);

            _log.LogInformation($"UpdateEdgeUsers: SortedUsers contains {sortedUsers.Count} items. EdgeUsers now contains {EdgeUsers.Count} items.");

            //SelectedEdgeUser = EdgeUsers.FirstOrDefault(user => user.UserName == currentSelectedUserName);

            //if (SelectedEdgeUser != null)
            //{
            //    _log.LogInformation($"UpdateEdgeUsers: Found and assigned SelectedEdgeUser: {SelectedEdgeUser.UserName}.");
            //}
            //else
            //{
            //    _log.LogError($"UpdateEdgeUsers: Could not find and assign SelectedEdgeUser from the name: {currentSelectedUserName}.");
            //}
        }

        private void HandleAlerts(bool alertFoundForOtherUsers)
        {
            if (alertFoundForOtherUsers)
            {
                PlayAlarmOnRepeat(); // This will show the StopButton (Mute button) if needed
                WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Open"));
            }
            else
            {
                // Call the main StopAlarm command's method, as it correctly stops sound
                // AND hides the StopButtonVisibility.
                StopAlarm();
            }
        }

        private void InitializeAudioPlayer()
        {
            try
            {
                _log.LogInformation($"InitializeAudioPlayer: Loading audio file.");
                // Only load the audio file, don't initialize WavePlayer here
                _audioFileReader = new AudioFileReader("Resources/alert.mp3");
                // Note: Consider making the path configurable or checking file existence.
            }
            catch (System.IO.FileNotFoundException fnfEx)
            {
                _log.LogError(fnfEx, "InitializeAudioPlayer: Alert audio file not found at '{Path}'. Alerts will not sound.", "Resources/alert.mp3");
                _audioFileReader = null; // Ensure it's null if file not found
            }
            catch (Exception e)
            {
                _log.LogError(e, "InitializeAudioPlayer: Error loading audio file.");
                _audioFileReader = null;
            }
        }

        private async void PlayAlarmOnRepeat()
        {
            if (_audioFileReader == null)
            {
                _log.LogError("PlayAlarmOnRepeat: Cannot play alert, audio file was not loaded.");
                return;
            }

            if (_wavePlayer?.PlaybackState == PlaybackState.Playing)
            {
                _log.LogDebug("PlayAlarmOnRepeat: Already playing.");
                return;
            }

            _log.LogInformation($"PlayAlarmOnRepeat: Attempting to play alert.");
            try
            {
                // Call internal StopAlarm to ensure the previous player is disposed
                StopAlarmInternal();

                _preferredAudioDeviceId = await _utils.GetPreferredAudioDeviceIDAsync();
                int targetDeviceNumber = _audioDeviceService.GetWaveOutDeviceNumberFromId(_preferredAudioDeviceId);

                _log.LogInformation("PlayAlarmOnRepeat: Preferred Device ID='{DeviceId}', Target WaveOut Number={DeviceNumber} (-1 is default)", _preferredAudioDeviceId, targetDeviceNumber);

                _wavePlayer = new WaveOutEvent();
                _wavePlayer.DeviceNumber = targetDeviceNumber;

                try
                {
                    _wavePlayer.Init(_audioFileReader);
                    _log.LogInformation("PlayAlarmOnRepeat: Initialized WaveOutEvent on DeviceNumber {DeviceNumber}.", targetDeviceNumber);
                }
                catch (NAudio.MmException mmEx) when (targetDeviceNumber != -1)
                {
                    _log.LogError(mmEx, "PlayAlarmOnRepeat: Failed to initialize WaveOutEvent on preferred DeviceNumber {PreferredDeviceNumber}. Falling back to default (-1).", targetDeviceNumber);
                    if (_wavePlayer != null) _wavePlayer.Dispose();
                    targetDeviceNumber = -1;
                    _wavePlayer = new WaveOutEvent { DeviceNumber = targetDeviceNumber };
                    _wavePlayer.Init(_audioFileReader);
                    _log.LogInformation("PlayAlarmOnRepeat: Initialized WaveOutEvent on fallback DeviceNumber {DeviceNumber}.", targetDeviceNumber);
                }
                catch (Exception initEx)
                {
                    _log.LogError(initEx, "PlayAlarmOnRepeat: Unexpected error initializing WaveOutEvent on DeviceNumber {DeviceNumber}. Cannot play sound.", targetDeviceNumber);
                    if (_wavePlayer != null) { _wavePlayer.Dispose(); _wavePlayer = null; }
                    return;
                }

                _alertStore.AlertActive = true;
                SelectedEdgeUser = null;

                bool hasExternalAlertsForButton = EdgeUsers?.Any(u =>
                    !string.IsNullOrEmpty(u.Alert) &&
                    !(string.Equals(u.UserName, UserName, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(u.HostName, HostName, StringComparison.OrdinalIgnoreCase))) ?? false;

                // If sound is playing due to external alerts, the "Mute Sound" button should be visible.
                StopButtonVisibility = hasExternalAlertsForButton ? Visibility.Visible : Visibility.Hidden;

                _stopRequested = false;
                if (_audioFileReader != null) _audioFileReader.Position = 0;
                if (_audioFileReader != null) _audioFileReader.Volume = 1.0f;
                _wavePlayer.PlaybackStopped += OnPlaybackStopped;
                _wavePlayer.Play();
                _log.LogInformation("PlayAlarmOnRepeat: Playback started on DeviceNumber {DeviceNumber}.", targetDeviceNumber);
            }
            catch (Exception e)
            {
                _log.LogError(e, "PlayAlarmOnRepeat: Error occurred during playback setup or start.");
                StopAlarmInternal();
            }
        }

        // Create an internal version of StopAlarm that doesn't affect the manual mute flag
        private void StopAlarmInternal()
        {
            // This is your existing StopAlarm logic, but without changing _isManuallyMutedByUser
            // or logging it as a user action.
            try
            {
                _log.LogDebug($"StopAlarmInternal: Stopping audio.");
                _alertStore.AlertActive = false;

                _stopRequested = true;
                if (_wavePlayer != null)
                {
                    if (_wavePlayer.PlaybackState != PlaybackState.Stopped)
                    {
                        _wavePlayer.Stop();
                    }
                    _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
                    _wavePlayer.Dispose();
                    _wavePlayer = null;
                }
                if (_audioFileReader != null)
                {
                    _audioFileReader.Position = 0;
                }
                // Do not send general "clear status" messages here as it's an internal stop.
                // UI for StopButtonVisibility is handled by PlayAlarmOnRepeat and HandleAlerts.
            }
            catch (Exception e)
            {
                _log.LogError(e, "StopAlarmInternal: Error stopping or disposing audio player.");
                if (_wavePlayer != null) { _wavePlayer.Dispose(); _wavePlayer = null; }
            }
        }

        /// <summary>
        /// Command to respond to an alert in a specific row
        /// </summary>
        [RelayCommand]
        public async void RespondToRow(EdgeUserViewModel selectedUser)
        {
            _log.LogInformation($"RespondToRow: Responding to alert from {selectedUser?.UserName}");

            if (selectedUser == null)
            {
                _log.LogWarning("RespondToRow: No user selected");
                return;
            }

            // Skip if it's the current user's alert
            if (selectedUser.IsCurrentUser)
            {
                _log.LogWarning("RespondToRow: Cannot respond to your own alert");
                return;
            }

            // Convert alert time format
            string alertTime;
            if (string.IsNullOrEmpty(selectedUser.Alert))
            {
                string nowInStringFormat = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                alertTime = Utils.ConvertDateFormat(nowInStringFormat);
            }
            else
            {
                alertTime = Utils.ConvertDateFormat(selectedUser.Alert);
            }

            // Create and send responder information
            await _edgeAlertService.RespondToAlertAsync(new Responder
            {
                AlertName = selectedUser.UserName,
                AlertTime = alertTime,
                ResponderName = UserName,
                ResponderHostName = HostName
            });
        }

        /// <summary>
        /// Command to stop an alert in a specific row
        /// </summary>
        [RelayCommand]
        public async void StopAlertForRow(EdgeUserViewModel selectedUser)
        {
            _log.LogInformation($"StopAlertForRow: Stopping alert from {selectedUser?.UserName}");

            if (selectedUser == null)
            {
                _log.LogWarning("StopAlertForRow: No user selected");
                return;
            }

            // Skip if it's the current user's alert
            if (selectedUser.IsCurrentUser)
            {
                _log.LogWarning("StopAlertForRow: Use the double-click method to cancel your own alert");
                return;
            }

            // Get selected User from the database
            EdgeUser user = await _edgeAlertTables.GetUserAsync(selectedUser.UserName, selectedUser.HostName);

            if (user == null)
            {
                _log.LogWarning($"StopAlertForRow: User {selectedUser.UserName} not found in database");
                return;
            }

            // Clear the alert
            user.Alert = string.Empty;

            // Update the user in the database
            await UpdateUserWithSemaphore(user);
        }

        [RelayCommand]
        private void StopAlarm()
        {
            try
            {
                _log.LogInformation($"StopAlarm: User Mute. Stopping and disposing WavePlayer.");
                _stopRequested = true;
                if (_wavePlayer != null)
                {
                    if (_wavePlayer.PlaybackState != PlaybackState.Stopped) { _wavePlayer.Stop(); }
                    _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
                    _wavePlayer.Dispose();
                    _wavePlayer = null;
                }
                if (_audioFileReader != null) { _audioFileReader.Position = 0; }
                _alertStore.AlertActive = false; // Sound is off locally

                DispatcherHelper.RunOnUIThread(() => {
                    WeakReferenceMessenger.Default.Send(new MainViewModelMessage("")); // Clear status text
                    StopButtonVisibility = Visibility.Hidden; // Hide the Mute button
                });
            }
            catch (Exception e)
            {
                _log.LogError(e, "StopAlarm: Error stopping or disposing audio player.");
                if (_wavePlayer != null)
                {
                    try { _wavePlayer.Dispose(); } catch { /* Best effort */ }
                    _wavePlayer = null;
                }
            }
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            try
            {
                // Check if stop wasn't explicitly requested AND if the player instance still exists
                if (!_stopRequested && _wavePlayer != null)
                {
                    if (_audioFileReader != null)
                    {
                        _audioFileReader.Position = 0; // Reset position
                        _wavePlayer.Play(); // Loop
                    }
                    else
                    {
                        _log.LogError("OnPlaybackStopped: Cannot loop, _audioFileReader is null.");
                        StopAlarm(); // Stop cleanly if file reader is gone
                    }
                }
                else
                {
                    _log.LogDebug("OnPlaybackStopped: Stop was requested or player is null. Not looping.");
                    // If stop was requested, StopAlarm should have already disposed the player.
                    // If player became null unexpectedly, log it.
                    if (_stopRequested && _wavePlayer != null)
                    {
                        _log.LogWarning("OnPlaybackStopped: Stop requested but _wavePlayer instance not disposed correctly by StopAlarm?");
                        StopAlarm(); // Attempt cleanup again
                    }
                }

                if (e.Exception != null)
                {
                    _log.LogError(e.Exception, "OnPlaybackStopped: Playback stopped due to an error.");
                    StopAlarm(); // Ensure cleanup on playback error
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "OnPlaybackStopped: Error during playback loop/stop handling.");
                StopAlarm(); // Ensure cleanup on handler error
            }
        }

        private void TriggerAlert(EdgeUser edgeUser)
        {
            HighlightedUserName = edgeUser.UserName;
            HighlightedHostName = edgeUser.HostName;
            if (HighlightedUserName == null || HighlightedUserName == "")
            {
                StopAlarm();
            }
            else
            {
                PlayAlarmOnRepeat();
                WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Open"));
            }
        }

        [RelayCommand]
        public async void DoubleClickAlert()
        {
            _log.LogInformation($"DoubleClickAlert:");
            // Ensure room is selected before being able to trigger alert
            if (string.IsNullOrEmpty(SelectedRoom))
            {
                _log.LogInformation($"DoubleClickAlert: Interrupted as no room selected");
                WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Please select a room before using Alert"));
                return;
            }

            _log.LogInformation($"DoubleClickAlert: Setting Alert");

            // Get any existing user data and set values
            EdgeUser user = await _edgeAlertTables.GetUserAsync(UserName, HostName);

            // Check if user.Alert is null or empty string, and set the value accordingly
            if (string.IsNullOrEmpty(user.Alert))
            {
                _alertStore.AlertSet = true;  // Make sure to set this explicitly
                AlertHasBeenSet = true;       // Also set the view model property directly
                user.Alert = _utils.ConvertUtcToGmtWithDst(DateTime.UtcNow);
                user.LastPing = _utils.ConvertUtcToGmtWithDst(DateTime.UtcNow);
            }
            else
            {
                _alertStore.AlertSet = false;  // Clear the flag
                AlertHasBeenSet = false;       // Also clear the view model property
                user.Alert = string.Empty;
                user.LastPing = _utils.ConvertUtcToGmtWithDst(DateTime.UtcNow);
            }

            // Trigger message
            await UpdateUserWithSemaphore(user);
        }


        [RelayCommand]
        public void RightClickClose()
        {
            _log.LogInformation($"RightClickClose:");
            if (_alertStore.AlertActive)
            {
                // Set Status Text
                WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Can't minimise while Alert is active"));
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Hide"));
            }
        }

        [RelayCommand]
        public async void Respond()
        {
            // Get current User
            EdgeUser user = await _edgeAlertTables.GetUserAsync(UserName, HostName);

            // Get current selected User that has an Alert
            EdgeUserViewModel selectedUser = SelectedEdgeUser;

            string alertTime;

            if (string.IsNullOrEmpty(selectedUser.Alert))
            {
                string nowInStringFormat = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                alertTime = Utils.ConvertDateFormat(nowInStringFormat);
            }
            else
            {
                alertTime = Utils.ConvertDateFormat(selectedUser.Alert);
            }

            await _edgeAlertService.RespondToAlertAsync(new Responder
            {
                AlertName = selectedUser.UserName,
                AlertTime = alertTime,
                ResponderName = UserName,
                ResponderHostName = HostName
            });
            SelectedEdgeUser = null;
        }

        [RelayCommand]
        public async void StopAlert()
        {
            // Get current selected User that has an Alert
            EdgeUserViewModel selectedUser = SelectedEdgeUser;

            // Get selected User
            EdgeUser user = await _edgeAlertTables.GetUserAsync(selectedUser.UserName, selectedUser.HostName);

            user.Alert = string.Empty;

            await UpdateUserWithSemaphore(user);
            SelectedEdgeUser = null;
        }

        private void ClearEdgeUsers()
        {
            // Ensure UI updates happen on the dispatcher thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Clear out the View
                EdgeUsers.Clear();
            });
        }

        async partial void OnAwayTimeoutMinutesChanged(int value)
        {
            // Add basic validation check before saving
            if (value >= 1 && value <= 60)
            {
                try
                {
                    string registryPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Edgebits\EdgeAlert";
                    Registry.SetValue(registryPath, "AwayTimeoutMinutes", value, RegistryValueKind.DWord); // Store as integer (DWORD)
                    _log.LogInformation($"Saved AwayTimeoutMinutes to registry: {value}");

                    // Optional: Notify UserStateMonitorService immediately if needed,
                    // otherwise it will pick up the new value on next application start.
                    // WeakReferenceMessenger.Default.Send(new AwayTimeoutChangedMessage(value));
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error writing AwayTimeoutMinutes to registry: {ErrorMessage}", ex.Message);
                    // Optionally revert the value back to the previous one or show an error to the user
                }
            }
            else
            {
                _log.LogWarning($"Invalid AwayTimeoutMinutes value ({value}) attempted. Not saving.");
                // You might want to reset the value back to the previous valid one or show user feedback
                // For simplicity, just logging for now. Consider adding user feedback mechanism.
                AwayTimeoutMinutes = await _utils.GetRegistryValueAsync<int>("AwayTimeoutMinutes", 3, defaultValue: 3);
            }
        }

        private async Task<UpdateUserResponse> UpdateUserWithSemaphore(EdgeUser user)
        {
            var result = new UpdateUserResponse();
            await _updateSemaphore.WaitAsync();
            try
            {
                result = await UpdateUserSafe(user);
            }
            catch (Exception ex)
            {
                _log.LogError($"UpdateUserWithSemaphore: Exception occurred while updating user {user.UserName}. Error: {ex.Message}");
                return new UpdateUserResponse { UserName = user.UserName, IsBlocked = false };
            }
            finally
            {
                _updateSemaphore.Release();
            }
            return result;
        }

        private async Task<UpdateUserResponse> UpdateUserSafe(EdgeUser user)
        {
            try
            {
                return await _edgeAlertService.UpdateUser(user);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _log.LogWarning($"UpdateUser: User not found. {ex.Message}");
                return new UpdateUserResponse { UserName = user.UserName, IsBlocked = false };
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                _log.LogWarning($"UpdateUser: Conflict error. {ex.Message}");
                return new UpdateUserResponse { UserName = user.UserName, IsBlocked = false };
            }
            catch (RequestFailedException ex)
            {
                _log.LogError($"UpdateUser: Request failed. {ex.Message}");
                // Handle retries or fallback logic here
                return new UpdateUserResponse { UserName = user.UserName, IsBlocked = false };
            }
            catch (Exception ex)
            {
                _log.LogError($"UpdateUser: Unexpected error. {ex.Message}");
                return new UpdateUserResponse { UserName = user.UserName, IsBlocked = false };
            }
        }

        // In MainViewModel.cs

        public async void Receive(ExitApplicationMessage message) // Changed to async void
        {
            try
            {
                await DispatcherHelper.RunOnUIThreadAsync(async () =>
                {
                    if (message.Value == "EXIT")
                    {
                        _log.LogInformation($"ExitApplicationMessage: Triggering Exit Logic");
                        // --- Get SerialNumber ---
                        string serialNumber = null;
                        try
                        {
                            // Assuming SystemInfoService.GetSystemInfo() is static or accessible
                            // Or resolve via DI if needed (_host.Services.GetService<SystemInfoService>() etc.)
                            var systemInfo = SystemInfoService.GetSystemInfo(); // Call static method
                                                                                // Use the retrieved serial number or generate a fallback if null/empty
                            serialNumber = systemInfo?.SerialNumber ?? SystemInfoService.GenerateUniqueMachineId();
                            _log.LogInformation($"ExitApplicationMessage: Retrieved SerialNumber: {serialNumber}");
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(ex, "Error getting serial number in ExitApplicationMessage. Using fallback.");
                            serialNumber = SystemInfoService.GenerateUniqueMachineId(); // Fallback ID generation
                        }
                        // --- End Get SerialNumber ---


                        // Call UserDisconnected service, now including the SerialNumber
                        if (_edgeAlertService != null && _utils != null)
                        {
                            _log.LogInformation("ExitApplicationMessage: Calling UserDisconnected service...");
                            await _edgeAlertService.UserDisconnected(new EdgeUser
                            {
                                // Ensure UserName and HostName properties are populated in the VM
                                UserName = this.UserName,
                                HostName = this.HostName,
                                SerialNumber = serialNumber
                            });
                            _log.LogInformation("ExitApplicationMessage: UserDisconnected service call completed.");
                        }
                        else
                        {
                            _log.LogWarning("ExitApplicationMessage: _edgeAlertService or _utils is null. Cannot call UserDisconnected.");
                        }

                        _log.LogInformation($"ExitApplicationMessage: UnregisterConnectionHandlers");

                        // Log user state as Offline when application exits
                        // Ensure _userStateMonitorService is not null before calling methods
                        _userStateMonitorService.LogStateTransition(UserState.Offline, "Application Exit");

                        // Explicitly send any pending logs, including the final "Offline" one.
                        await _userStateMonitorService.SendPendingLogs();
                        _log.LogInformation($"ExitApplicationMessage: Explicitly called SendPendingLogs.");

                        // Unregister SignalR handlers (this part can likely stay as is)
                        await UnregisterConnectionHandlers();

                        _log.LogInformation("MainViewModel: Cleanup complete.");
                        _log.LogInformation("MainViewModel: Initiating application shutdown.");
                        Application.Current.Shutdown();
                    }
                });
            }
            catch (Exception ex)
            {
                _log.LogError($"ExitApplicationMessage: {ex.Message}");
            }
        }

        /// <summary>
        /// Disposes SignalR HubConnection handlers.
        /// Ensure this method exists or add it if it was removed previously.
        /// </summary>
        public async Task UnregisterConnectionHandlers()
        {
            _log.LogInformation("Unregistering SignalR Hub Handlers...");
            _receiveEdgeAlertMessageSubscription?.Dispose();
            //_updateConnectedUsersSubscription?.Dispose(); // Uncomment if you still use this
            _updateConnectedUserGroupsSubscription?.Dispose();
            _pongSubscription?.Dispose();
            _edgeAlertBlockSubscription?.Dispose(); // Uncomment if you use this

            // Consider disposing the HubConnection itself if appropriate
            // if (_connection != null)
            // {
            //     await _connection.DisposeAsync();
            //     _connection = null;
            // }
            _log.LogInformation("SignalR Hub Handlers Unregistered.");
        }

        /// <summary>
        /// Plays the alert sound once on the specified device ID.
        /// Used for testing the audio output selection.
        /// Stops any currently playing alert first.
        /// </summary>
        /// <param name="targetDeviceId">The ID of the device to test. If null or SystemDefaultDeviceId, uses the system default.</param>
        /// <returns>True if playback was successfully initiated, false otherwise.</returns>
        public async Task<bool> PlayAlertSoundOnceAsync(string targetDeviceId)
        {
            if (_audioFileReader == null)
            {
                _log.LogError("PlayAlertSoundOnceAsync: Cannot play test, audio file not loaded.");
                return false;
            }

            // Ensure any repeating alert is stopped first
            _log.LogDebug("PlayAlertSoundOnceAsync: Stopping any existing alarm first...");
            StopAlarm(); // Use the existing StopAlarm to ensure proper cleanup

            // Optional slight delay to ensure resources are released, adjust if needed
            await Task.Delay(150);

            // Determine the WaveOut device number for the target ID
            int deviceNumber = _audioDeviceService.GetWaveOutDeviceNumberFromId(
                string.IsNullOrWhiteSpace(targetDeviceId) ? Utils.SYSTEM_DEFAULT_AUDIO_DEVICE_ID : targetDeviceId
            );

            _log.LogInformation("PlayAlertSoundOnceAsync: Attempting test playback on Device ID='{DeviceId}', WaveOut Number={DeviceNumber}",
                string.IsNullOrWhiteSpace(targetDeviceId) ? "System Default" : targetDeviceId,
                deviceNumber);

            WaveOutEvent tempWavePlayer = null; // Use a temporary player for the test sound
            TaskCompletionSource<bool> playbackFinishedTcs = new TaskCompletionSource<bool>(); // To signal completion/disposal

            try
            {
                tempWavePlayer = new WaveOutEvent { DeviceNumber = deviceNumber };

                // Event handler for when playback stops (either finished or error)
                tempWavePlayer.PlaybackStopped += (s, e) => {
                    _log.LogDebug("Test playback stopped. Disposing temporary player.");
                    (s as IWavePlayer)?.Dispose(); // Dispose the temporary player
                    tempWavePlayer = null; // Clear reference

                    if (e.Exception != null)
                    {
                        _log.LogError(e.Exception, "Error during test playback finalization.");
                        playbackFinishedTcs.TrySetResult(false); // Signal failure
                    }
                    else
                    {
                        playbackFinishedTcs.TrySetResult(true); // Signal success
                    }
                };

                // Reset position and volume
                // Ensure access to _audioFileReader is safe if potentially accessed by other threads,
                // though less likely for a simple file read position/volume.
                lock (_audioFileReader) // Basic lock for safety, review if contention is high
                {
                    _audioFileReader.Position = 0;
                    _audioFileReader.Volume = 1.0f;
                }


                // Initialize and play
                tempWavePlayer.Init(_audioFileReader);
                tempWavePlayer.Play();

                _log.LogInformation("PlayAlertSoundOnceAsync: Test playback initiated on device {DeviceNumber}.", deviceNumber);

                // Optionally, wait for playback to finish before returning true,
                // or return true immediately if initiation is enough.
                // Waiting ensures resources are cleaned up before next action.
                // Add a timeout to prevent waiting forever if PlaybackStopped doesn't fire.
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10))) // 10-second timeout
                {
                    var completedTask = await Task.WhenAny(playbackFinishedTcs.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token));
                    if (completedTask == playbackFinishedTcs.Task)
                    {
                        return await playbackFinishedTcs.Task; // Return the result (true/false) from the handler
                    }
                    else
                    {
                        _log.LogWarning("Test playback timed out after 10 seconds. Disposing player.");
                        tempWavePlayer?.Dispose(); // Ensure disposal on timeout
                        tempWavePlayer = null;
                        return false; // Indicate failure due to timeout
                    }
                }
                // return true; // Return true immediately after initiating playback (alternative)
            }
            catch (NAudio.MmException mmEx)
            {
                _log.LogError(mmEx, "PlayAlertSoundOnceAsync: NAudio MMException initializing/playing test sound on device {DeviceNumber}.", deviceNumber);
                tempWavePlayer?.Dispose(); // Ensure disposal on error
                return false;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "PlayAlertSoundOnceAsync: Unexpected error playing test sound on device {DeviceNumber}.", deviceNumber);
                tempWavePlayer?.Dispose(); // Ensure disposal on error
                return false;
            }
            // Note: 'finally' block is not strictly needed here as cleanup happens in PlaybackStopped or catch blocks.
        }

        public async void Receive(ApplicationRestartMessage message)
        {
            try
            {
                await DispatcherHelper.RunOnUIThreadAsync(async () =>
                {
                    if (message.Value == "RESTART")
                    {
                        // ... existing RESTART logic ...
                        _log.LogInformation($"ApplicationRestartMessage: Setting Room to Null and InitializeAsync");
                        WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Hide"));
                        _isFirstRun = true;
                        WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Connecting to Alert Server..."));
                        _roomStore.Room = _roomStore.Room ?? null;
                        EdgeUsers?.Clear(); // Clear existing users if list exists

                        await InitializeAsync();
                    }
                    if (message.Value == "SOFT_RESTART")
                    {
                        _log.LogInformation($"ApplicationRestartMessage: Soft Restart");
                        IsMachineLocked = false;
                        WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Hide"));
                        WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Connecting to Alert Server..."));
                        await SoftInitializeConnectionAsync();

                        // *** REVISED Feature Check for Away-Feature ***
                        string currentUser = _utils?.GetComputerName();
                        string currentOds = _roomStore?.GetTrueODS() ?? "UNKNOWN_ODS";
                        bool isAwayFeatureEnabled = false;

                        if (_featureSettingsCache == null || string.IsNullOrWhiteSpace(currentUser) || string.IsNullOrWhiteSpace(currentOds))
                        {
                            _log.LogWarning("Receive(SOFT_RESTART): FeatureSettingsCache or User/ODS info not available. Cannot check Away-Feature status.");
                        }
                        else
                        {
                            isAwayFeatureEnabled = _featureSettingsCache.GetEffectiveSetting(AwayFeatureName, currentUser, currentOds);
                            _log.LogDebug("Receive(SOFT_RESTART): Effective status for {FeatureName} is {Status}", AwayFeatureName, isAwayFeatureEnabled);
                        }

                        if (isAwayFeatureEnabled) // <<< Use the effective status variable
                        {
                            EdgeUser user = await _edgeAlertTables.GetUserAsync(UserName, HostName);
                            if (user != null) // Check if user exists before updating
                            {
                                user.LastOnline = "Online";
                                await UpdateUserWithSemaphore(user);
                                _log.LogInformation("Receive(SOFT_RESTART): Updated user status to Online as Away-Feature is enabled.");
                            }
                            else
                            {
                                _log.LogWarning("Receive(SOFT_RESTART): User not found in DB, cannot update LastOnline status.");
                            }
                        }
                        else
                        {
                            _log.LogInformation("Receive(SOFT_RESTART): Away-Feature is disabled. Skipping user status update.");
                        }
                        // *** END REVISED Feature Check ***
                    }
                    if (message.Value == "LOCK_MACHINE")
                    {
                        _log.LogInformation($"ApplicationRestartMessage: Lock Machine");
                        // Log state transition FIRST, regardless of feature enablement for Away status update
                        _userStateMonitorService?.LogStateTransition(UserState.Away, "Machine Locked");

                        IsMachineLocked = true;
                        await SoftInitializeConnectionAsync(); // Reconnect etc.

                        // *** REVISED Feature Check for Away-Feature ***
                        string currentUser = _utils?.GetComputerName();
                        string currentOds = _roomStore?.GetTrueODS() ?? "UNKNOWN_ODS";
                        bool isAwayFeatureEnabled = false;

                        if (_featureSettingsCache == null || string.IsNullOrWhiteSpace(currentUser) || string.IsNullOrWhiteSpace(currentOds))
                        {
                            _log.LogWarning("Receive(LOCK_MACHINE): FeatureSettingsCache or User/ODS info not available. Cannot check Away-Feature status.");
                        }
                        else
                        {
                            isAwayFeatureEnabled = _featureSettingsCache.GetEffectiveSetting(AwayFeatureName, currentUser, currentOds);
                            _log.LogDebug("Receive(LOCK_MACHINE): Effective status for {FeatureName} is {Status}", AwayFeatureName, isAwayFeatureEnabled);
                        }

                        if (isAwayFeatureEnabled) // <<< Use the effective status variable
                        {
                            EdgeUser user = await _edgeAlertTables.GetUserAsync(UserName, HostName);
                            if (user != null) // Check if user exists
                            {
                                user.LastOnline = $"Away {_utils.ConvertUtcToGmtWithDst(DateTime.UtcNow)}";
                                await UpdateUserWithSemaphore(user);
                                _log.LogInformation("Receive(LOCK_MACHINE): Updated user status to Away as Away-Feature is enabled.");
                            }
                            else
                            {
                                _log.LogWarning("Receive(LOCK_MACHINE): User not found in DB, cannot update LastOnline status.");
                            }
                        }
                        else
                        {
                            _log.LogInformation("Receive(LOCK_MACHINE): Away-Feature is disabled. Skipping user status update.");
                        }
                        // *** END REVISED Feature Check ***
                    }
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"ApplicationRestartMessage Error: {ex.Message}");
            }
        }

        //public async void Receive(IPAddressChangedMessage message)
        //{
        //    //await DispatcherHelper.RunOnUIThreadAsync(async () =>
        //    //{
        //        _log.LogInformation($"IPAddressChangedMessage: IP Address Update to {message.Value}");
        //        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        //        {
        //            _log.LogInformation($"IPAddressChangedMessage: Checking Internet Connectivity...");
        //            if (await _networkHandler.IsInternetAvailableAsync(cts.Token))
        //            {
        //                _log.LogInformation($"IPAddressChangedMessage: Internet Connected");
        //                await SetInitialLocationData();
        //                await SetUserDataAsync();
        //            }
        //            else
        //            {
        //                _log.LogInformation($"IPAddressChangedMessage: Internet Not Connected");
        //            }
        //        }
        //    //});
        //}

        public async void Receive(DoubleClickMessage message)
        {
            await DispatcherHelper.RunOnUIThreadAsync(async () =>
            {
                if (message.Value == "DoubleClick")
                {
                    DoubleClickAlert();
                }
            });
        }

        public void Receive(MainViewModelMessage message)
        {
            if (message.Value == "ReportView")
            {
                // This should only ever trigger once
                // Pass the data to ReportView
                FireDrill().Await(HandleError);
                AlertHistory().Await(HandleError);
                UpdateSystemInfo().Await(HandleError);
            }
        }       
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EdgeAlertSignalClient.Models; // For FeatureSettingInfo, Location etc.
using EdgeAlertSignalClient.Services; // For IEdgeAlertService
using EdgeAlertSignalClient.Handlers; // For JSONHandler
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Message; // For MessageBox

namespace EdgeAlertSignalClient.ViewModels
{
    /// <summary>
    /// Represents a single feature setting item for display in the UI,
    /// including its scope, identifier, explicit enabled status, and effective status.
    /// </summary>
    public partial class FeatureSettingDisplayItem : ObservableObject
    {
        [ObservableProperty]
        private string _scope; // "Global", "ODS", "User"

        [ObservableProperty]
        private string _identifier; // "GLOBAL:", ODS Code, User Name

        [ObservableProperty]
        private string _displayName; // Practice Name for ODS, User Name for User

        [ObservableProperty]
        private bool? _explicitIsEnabled; // Null if inherited, true/false if set explicitly

        [ObservableProperty]
        private bool _effectiveIsEnabled; // Calculated based on hierarchy

        [ObservableProperty]
        private bool _isToggleChecked; // Bound to the toggle switch

        public string ScopeIdentifier => _scope == "Global" ? "GLOBAL:" :
                                         _scope == "ODS" ? $"ODS_{_identifier}" :
                                         _scope == "User" ? $"USER_{_identifier.ToUpperInvariant()}" :
                                         string.Empty;
    }

    public partial class FeatureSwitchManagementViewModel : ObservableObject, IRecipient<MainViewModelInitialisedMessage>
    {
        // Define the set of feature names to exclude from the UI dropdown
        private static readonly HashSet<string> FeaturesToExcludeFromUI = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Away-Feature",
            "AzureSync-Feature",
            "CustomDomainAndIPFeature",
            "InternetCheck-Feature"
        };

        private readonly ILogger<FeatureSwitchManagementViewModel> _log;
        private readonly IEdgeAlertService _edgeAlertService;
        private readonly JSONHandler _jsonHandler; // To get Practice Names
        private readonly MainViewModel _mainViewModel; // To get User list

        [ObservableProperty]
        private ObservableCollection<string> _availableFeatures;

        [ObservableProperty]
        private ObservableCollection<FeatureSettingDisplayItem> _odsSettings;

        [ObservableProperty]
        private ObservableCollection<FeatureSettingDisplayItem> _userSettings;

        [ObservableProperty]
        private FeatureSettingDisplayItem _globalSetting;

        [ObservableProperty]
        private string _odsSearchText;

        [ObservableProperty]
        private string _userSearchText;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SetAllOdsOnCommand))]
        [NotifyCanExecuteChangedFor(nameof(SetAllOdsOffCommand))]
        [NotifyCanExecuteChangedFor(nameof(SetAllUsersOnCommand))]
        [NotifyCanExecuteChangedFor(nameof(SetAllUsersOffCommand))]
        [NotifyCanExecuteChangedFor(nameof(ClearAllOdsOverridesCommand))] // Add new
        [NotifyCanExecuteChangedFor(nameof(ClearAllUserOverridesCommand))] // Add new
        [NotifyCanExecuteChangedFor(nameof(ToggleSettingCommand))]
        [NotifyCanExecuteChangedFor(nameof(ClearOverrideCommand))]
        private string _selectedFeature;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SetAllOdsOnCommand))]
        [NotifyCanExecuteChangedFor(nameof(SetAllOdsOffCommand))]
        [NotifyCanExecuteChangedFor(nameof(SetAllUsersOnCommand))]
        [NotifyCanExecuteChangedFor(nameof(SetAllUsersOffCommand))]
        [NotifyCanExecuteChangedFor(nameof(ClearAllOdsOverridesCommand))] // Add new
        [NotifyCanExecuteChangedFor(nameof(ClearAllUserOverridesCommand))] // Add new
        [NotifyCanExecuteChangedFor(nameof(ToggleSettingCommand))]
        [NotifyCanExecuteChangedFor(nameof(ClearOverrideCommand))]
        [NotifyCanExecuteChangedFor(nameof(LoadFeatureSettingsCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage;

        // Full lists for filtering
        private List<FeatureSettingDisplayItem> _allOdsSettings;
        private List<FeatureSettingDisplayItem> _allUserSettings;

        public FeatureSwitchManagementViewModel(
            ILogger<FeatureSwitchManagementViewModel> log,
            IEdgeAlertService edgeAlertService,
            JSONHandler jsonHandler,
            MainViewModel mainViewModel) // Inject MainViewModel
        {
            _log = log;
            _edgeAlertService = edgeAlertService;
            _jsonHandler = jsonHandler;
            _mainViewModel = mainViewModel; // Store MainViewModel

            OdsSettings = new ObservableCollection<FeatureSettingDisplayItem>();
            UserSettings = new ObservableCollection<FeatureSettingDisplayItem>();
            _allOdsSettings = new List<FeatureSettingDisplayItem>();
            _allUserSettings = new List<FeatureSettingDisplayItem>();

            AvailableFeatures = new ObservableCollection<string>();

            // Initialize Global Setting placeholder
            _globalSetting = new FeatureSettingDisplayItem
            {
                Scope = "Global",
                Identifier = "GLOBAL:",
                DisplayName = "Global Default"
                // Explicit/Effective status will be loaded
            };

            WeakReferenceMessenger.Default.Register<MainViewModelInitialisedMessage>(this);

            StatusMessage = "Waiting for main application components..."; // Initial status
            _log.LogInformation("FeatureSwitchManagementViewModel created. Waiting for MainViewModel initialization message.");
        }

        /// <summary>
        /// Initializes the ViewModel by loading available features and then settings for the default feature.
        /// </summary>
        private async Task InitializeViewModelAsync()
        {
            await LoadAvailableFeaturesAsync();
            // Automatically load settings for the first available feature, if any
            if (AvailableFeatures.Any())
            {
                // Set SelectedFeature which will trigger OnSelectedFeatureChanged -> LoadFeatureSettingsCommand
                // Ensure this runs on UI thread if needed, but property change should handle it
                SelectedFeature = AvailableFeatures.FirstOrDefault(f => f.Equals("UserTimeTracking-Feature", StringComparison.OrdinalIgnoreCase))
                                  ?? AvailableFeatures.First();
            }
            else
            {
                StatusMessage = "No features found.";
                _log.LogWarning(StatusMessage);
            }
        }

        /// <summary>
        /// Loads the list of available features from the backend.
        /// </summary>
        private async Task LoadAvailableFeaturesAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Loading available features...";
            _log.LogInformation(StatusMessage);

            List<string> features = null;
            bool serviceCallSucceeded = false;

            try
            {
                // *** Added Logging Around Service Call ***
                _log.LogDebug("Calling _edgeAlertService.GetAvailableFeaturesAsync...");
                features = await _edgeAlertService.GetAvailableFeaturesAsync();
                serviceCallSucceeded = features != null; // Consider null as failure from service call
                _log.LogDebug("_edgeAlertService.GetAvailableFeaturesAsync completed. Result is null: {IsResultNull}, Count (if not null): {Count}",
                    features == null, features?.Count ?? 0);
                // *** End Added Logging ***

                // *** Dispatcher call remains, ensuring UI updates happen on correct thread ***
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (AvailableFeatures == null)
                    {
                        _log.LogError("LoadAvailableFeaturesAsync: AvailableFeatures collection is unexpectedly null inside Dispatcher.Invoke.");
                        return; // Cannot proceed
                    }

                    AvailableFeatures.Clear();

                    if (serviceCallSucceeded && features.Any()) // Check if call succeeded AND returned items
                    {
                        var filteredFeatures = features
                            .Where(f => !FeaturesToExcludeFromUI.Contains(f))
                            .ToList();

                        foreach (var feature in filteredFeatures)
                        {
                            AvailableFeatures.Add(feature);
                        }
                        _log.LogInformation($"Populated UI dropdown with {AvailableFeatures.Count} features.");
                        StatusMessage = $"Loaded {AvailableFeatures.Count} features."; // Update status on success
                    }
                    else
                    {
                        // Log specific reason for empty list
                        if (!serviceCallSucceeded)
                        {
                            _log.LogWarning("No available features loaded because the service call failed or returned null.");
                            StatusMessage = "Failed to load features from server.";
                        }
                        else // Service call succeeded but returned empty list
                        {
                            _log.LogWarning("No available features returned from service (list was empty).");
                            StatusMessage = "No configurable features found.";
                        }
                        // Keep AvailableFeatures empty
                    }
                });
            }
            catch (Exception ex)
            {
                // This catch block handles exceptions *during* the service call or dispatcher invoke
                StatusMessage = "Error loading available features.";
                _log.LogError(ex, StatusMessage);
                MessageBox.Show($"{StatusMessage}\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Ensure collection is cleared on error within dispatcher if possible
                await Application.Current.Dispatcher.InvokeAsync(() => AvailableFeatures?.Clear());
            }
            finally
            {
                IsBusy = false;
                // Trigger the loading of settings for the first feature *if* features were loaded
                // This needs to happen *after* IsBusy is false and potentially outside the dispatcher context
                // Let the OnSelectedFeatureChanged handler trigger the load as originally intended.
                if (AvailableFeatures != null && AvailableFeatures.Any() && string.IsNullOrEmpty(SelectedFeature)) // Check if SelectedFeature needs setting
                {
                    // Use Dispatcher to set SelectedFeature to avoid potential cross-thread access
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        SelectedFeature = AvailableFeatures.FirstOrDefault(f => f.Equals("UserTimeTracking-Feature", StringComparison.OrdinalIgnoreCase))
                                         ?? AvailableFeatures.First();
                        _log.LogInformation("Default feature selected after loading features: {SelectedFeature}", SelectedFeature);
                    });
                    // The OnSelectedFeatureChanged handler will then trigger LoadFeatureSettingsCommand
                }
                else if (AvailableFeatures == null || !AvailableFeatures.Any())
                {
                    // If no features were loaded, ensure status reflects that
                    StatusMessage = "No features available or failed to load.";
                }
            }
        }


        /// <summary>
        /// Triggered when the selected feature changes. Reloads settings for the new feature.
        /// </summary>
        /// <param name="value">The newly selected feature name.</param>
        partial void OnSelectedFeatureChanged(string value)
        {
            _log.LogInformation("Selected feature changed to: {SelectedFeature}", value);
            // Clear previous settings immediately for better UI feedback
            ClearDisplayedSettings();
            if (!string.IsNullOrWhiteSpace(value))
            {
                // Asynchronously execute the load command for the new feature
                // This command will handle the IsBusy state.
                _ = LoadFeatureSettingsCommand.ExecuteAsync(null);
            }
            else
            {
                StatusMessage = "No feature selected.";
            }
        }

        /// <summary>
        /// Clears the displayed settings lists and resets global setting display.
        /// </summary>
        private void ClearDisplayedSettings()
        {
            Application.Current.Dispatcher.Invoke(() => {
                OdsSettings.Clear();
                UserSettings.Clear();
            });
            _allOdsSettings.Clear();
            _allUserSettings.Clear();
            // Reset global setting display state
            GlobalSetting.ExplicitIsEnabled = null;
            GlobalSetting.EffectiveIsEnabled = false; // Default to false visually
            GlobalSetting.IsToggleChecked = false;
            OdsSearchText = string.Empty; // Clear search fields
            UserSearchText = string.Empty;
        }


        /// <summary>
        /// Loads the detailed settings (Global, ODS, User) for the currently SelectedFeature.
        /// </summary>
        [RelayCommand]
        private async Task LoadFeatureSettings()
        {
            // Check if a feature is selected
            if (string.IsNullOrWhiteSpace(SelectedFeature))
            {
                StatusMessage = "Please select a feature.";
                ClearDisplayedSettings();
                return;
            }

            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = $"Loading settings for {SelectedFeature}...";
            _log.LogInformation(StatusMessage);

            // Ensure collections are cleared before loading new data
            ClearDisplayedSettings();

            try
            {
                // 1. Fetch all explicit settings for the SELECTED feature
                // NOTE: GetAllFeatureSettingsAsync currently fetches ALL settings.
                // We might optimize backend to fetch only for one feature later.
                // For now, filter client-side.
                List<FeatureSettingInfo> explicitSettings = await _edgeAlertService.GetAllFeatureSettingsAsync();
                var featureSpecificSettings = explicitSettings?
                    .Where(s => s.FeatureName.Equals(SelectedFeature, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(s => s.ScopeIdentifier, s => s.IsEnabled, StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

                _log.LogInformation($"Fetched {featureSpecificSettings.Count} explicit settings for {SelectedFeature}.");

                // 2. Determine Global Setting state
                bool globalIsEnabled = featureSpecificSettings.TryGetValue("GLOBAL:", out bool globalExplicit) ? globalExplicit : false;
                GlobalSetting.ExplicitIsEnabled = featureSpecificSettings.ContainsKey("GLOBAL:");
                GlobalSetting.EffectiveIsEnabled = globalIsEnabled;
                GlobalSetting.IsToggleChecked = globalIsEnabled;

                // 3. Prepare ODS Settings (same logic as before)
                var allLocations = _jsonHandler.Data?.Locations ?? new ObservableCollection<Location>();
                foreach (var location in allLocations.OrderBy(l => l.Practice))
                {
                    if (string.IsNullOrEmpty(location.ODS) || string.IsNullOrEmpty(location.Practice)) continue;
                    string odsScope = $"ODS_{location.ODS}";
                    bool? explicitOdsEnabled = featureSpecificSettings.TryGetValue(odsScope, out bool odsExplicit) ? odsExplicit : (bool?)null;
                    bool effectiveOdsEnabled = explicitOdsEnabled ?? globalIsEnabled;

                    _allOdsSettings.Add(new FeatureSettingDisplayItem
                    {
                        Scope = "ODS",
                        Identifier = location.ODS,
                        DisplayName = location.Practice,
                        ExplicitIsEnabled = explicitOdsEnabled,
                        EffectiveIsEnabled = effectiveOdsEnabled,
                        IsToggleChecked = effectiveOdsEnabled
                    });
                }
                ApplyOdsFilter();
                _log.LogInformation($"Prepared {_allOdsSettings.Count} ODS settings placeholders for {SelectedFeature}.");


                // 4. Prepare User Settings (same logic as before)
                var allUsers = await _mainViewModel.EdgeAlertTablesInstance.GetAllUserAsync();
                var distinctUserNames = allUsers?.Select(u => u.UserName).Where(un => !string.IsNullOrEmpty(un)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(un => un).ToList() ?? new List<string>();
                foreach (var userName in distinctUserNames)
                {
                    if (string.IsNullOrWhiteSpace(userName)) continue;
                    string userScope = $"USER_{userName.ToUpperInvariant()}";
                    bool? explicitUserEnabled = featureSpecificSettings.TryGetValue(userScope, out bool userExplicit) ? userExplicit : (bool?)null;
                    bool effectiveUserEnabled = explicitUserEnabled ?? globalIsEnabled; // Simplified: User -> Global

                    _allUserSettings.Add(new FeatureSettingDisplayItem
                    {
                        Scope = "User",
                        Identifier = userName,
                        DisplayName = userName,
                        ExplicitIsEnabled = explicitUserEnabled,
                        EffectiveIsEnabled = effectiveUserEnabled,
                        IsToggleChecked = effectiveUserEnabled
                    });
                }
                ApplyUserFilter();
                _log.LogInformation($"Prepared {_allUserSettings.Count} user settings placeholders for {SelectedFeature}.");

                StatusMessage = $"Settings loaded for {SelectedFeature}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading settings for {SelectedFeature}.";
                _log.LogError(ex, StatusMessage);
                MessageBox.Show($"{StatusMessage}\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ClearDisplayedSettings(); // Clear display on error
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Handles toggling of any feature setting switch for the currently SelectedFeature.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteSingleToggle))]
        private async Task ToggleSetting(FeatureSettingDisplayItem item)
        {
            // Check if a feature is selected before allowing toggle
            if (item == null || string.IsNullOrWhiteSpace(SelectedFeature) || IsBusy) return;

            IsBusy = true;
            bool intendedState = item.IsToggleChecked;
            string currentFeature = SelectedFeature; // Capture selected feature at start

            StatusMessage = $"Updating setting for {item.DisplayName} ({currentFeature})...";
            _log.LogInformation($"ToggleSetting triggered for Feature='{currentFeature}', Scope='{item.Scope}', Identifier='{item.Identifier}', Intended State='{intendedState}'");

            bool success = false;
            try
            {
                // Always call SetFeatureSettingAsync.
                success = await _edgeAlertService.SetFeatureSettingAsync(currentFeature, item.ScopeIdentifier, intendedState);

                if (success)
                {
                    _log.LogInformation($"Successfully called SetFeatureSetting ({intendedState}) for Feature='{currentFeature}', Scope='{item.ScopeIdentifier}'");
                }
                else
                {
                    _log.LogError($"Failed to call SetFeatureSetting ({intendedState}) for Feature='{currentFeature}', Scope='{item.ScopeIdentifier}'");
                }

                if (success)
                {
                    StatusMessage = $"Setting for {item.DisplayName} ({currentFeature}) updated. Refreshing list...";
                    _log.LogInformation(StatusMessage);
                    IsBusy = false; // Allow load to run
                    await LoadFeatureSettingsCommand.ExecuteAsync(null); // Reloads for SelectedFeature
                    StatusMessage = $"Settings refreshed for {currentFeature}.";
                }
                else
                {
                    StatusMessage = $"Failed to update setting for {item.DisplayName} ({currentFeature}).";
                    _log.LogError(StatusMessage);
                    MessageBox.Show(StatusMessage, "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    item.IsToggleChecked = !intendedState; // Revert UI toggle
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error updating setting for {item.DisplayName} ({currentFeature}).";
                _log.LogError(ex, StatusMessage);
                MessageBox.Show($"{StatusMessage}\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                item.IsToggleChecked = !intendedState; // Revert UI toggle
                IsBusy = false;
            }
        }

        // CanExecute for the individual toggle command
        private bool CanExecuteSingleToggle()
        {
            return !string.IsNullOrWhiteSpace(SelectedFeature) && !IsBusy;
        }

        // Filtering methods (ApplyOdsFilter, ApplyUserFilter) remain the same as provided in Phase 3

        private void ApplyOdsFilter()
        {
            if (_allOdsSettings == null) return;
            try
            {
                Application.Current.Dispatcher.Invoke(() => {
                    OdsSettings.Clear();
                    var filter = OdsSearchText?.Trim();
                    var filtered = string.IsNullOrWhiteSpace(filter)
                        ? _allOdsSettings
                        : _allOdsSettings.Where(s => (s.DisplayName != null && s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                                                    (s.Identifier != null && s.Identifier.Contains(filter, StringComparison.OrdinalIgnoreCase)));
                    foreach (var item in filtered.OrderBy(s => s.DisplayName)) { OdsSettings.Add(item); }
                });
            }
            catch (Exception ex) { _log.LogError(ex, "Error applying ODS filter."); }
        }

        private void ApplyUserFilter()
        {
            if (_allUserSettings == null) return;
            try
            {
                Application.Current.Dispatcher.Invoke(() => {
                    UserSettings.Clear();
                    var filter = UserSearchText?.Trim();
                    var filtered = string.IsNullOrWhiteSpace(filter)
                        ? _allUserSettings
                        : _allUserSettings.Where(s => s.Identifier != null && s.Identifier.Contains(filter, StringComparison.OrdinalIgnoreCase));
                    foreach (var item in filtered.OrderBy(s => s.DisplayName)) { UserSettings.Add(item); }
                });
            }
            catch (Exception ex) { _log.LogError(ex, "Error applying User filter."); }
        }

        private bool CanExecuteBulkCommands()
        {
            // Can execute if a feature is selected and not currently busy
            return !string.IsNullOrWhiteSpace(SelectedFeature) && !IsBusy;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteBulkCommands))]
        private async Task SetAllOdsOn()
        {
            await ExecuteBulkOperation(
                serviceCall: () => _edgeAlertService.SetBulkOdsSettingsAsync(SelectedFeature, true),
                scopeType: "ODS",
                actionDescriptionFormat: "set all {0} scopes to {1}", // Pass format string
                targetState: true); // Pass target state
        }

        [RelayCommand(CanExecute = nameof(CanExecuteBulkCommands))]
        private async Task SetAllOdsOff()
        {
            await ExecuteBulkOperation(
               serviceCall: () => _edgeAlertService.SetBulkOdsSettingsAsync(SelectedFeature, false),
               scopeType: "ODS",
                actionDescriptionFormat: "set all {0} scopes to {1}",
               targetState: false);
        }

        [RelayCommand(CanExecute = nameof(CanExecuteBulkCommands))]
        private async Task SetAllUsersOn()
        {
            await ExecuteBulkOperation(
               serviceCall: () => _edgeAlertService.SetBulkUserSettingsAsync(SelectedFeature, true),
               scopeType: "User",
                actionDescriptionFormat: "set all {0} scopes to {1}",
               targetState: true);
        }

        [RelayCommand(CanExecute = nameof(CanExecuteBulkCommands))]
        private async Task SetAllUsersOff()
        {
            await ExecuteBulkOperation(
                serviceCall: () => _edgeAlertService.SetBulkUserSettingsAsync(SelectedFeature, false),
                scopeType: "User",
                 actionDescriptionFormat: "set all {0} scopes to {1}",
                targetState: false);
        }

        /// <summary>
        /// Executes a bulk update operation after user confirmation.
        /// </summary>
        /// <param name="serviceCall">A function representing the service call to execute.</param>
        /// <param name="scopeType">A string describing the scope ("ODS" or "User") for messages.</param>
        /// <param name="targetState">The target state (true for ON, false for OFF) for messages.</param>
        private async Task ExecuteBulkUpdate(Func<Task<bool>> serviceCall, string scopeType, bool targetState)
        {
            string stateText = targetState ? "ON" : "OFF";
            string feature = SelectedFeature; // Capture before potential async gap

            // Confirmation Dialog
            var result = MessageBox.Show(
                $"This will attempt to set the feature '{feature}' to '{stateText}' for ALL {scopeType} entries.\n\n" +
                $"This overrides any existing {scopeType}-specific settings for this feature.\n\n" +
                "Proceed?",
                $"Confirm Bulk {scopeType} Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                StatusMessage = $"Bulk {scopeType} update cancelled.";
                _log.LogInformation(StatusMessage);
                return;
            }

            // Execute the update
            IsBusy = true;
            StatusMessage = $"Setting all {scopeType} scopes to {stateText} for '{feature}'...";
            _log.LogInformation($"Executing bulk update: Scope={scopeType}, Feature='{feature}', State={targetState}");
            bool success = false;
            try
            {
                success = await serviceCall();

                if (success)
                {
                    StatusMessage = $"Bulk {scopeType} update request sent successfully. Refreshing list...";
                    _log.LogInformation(StatusMessage);
                    // Refresh UI - Must release IsBusy before awaiting the command
                    IsBusy = false;
                    await LoadFeatureSettingsCommand.ExecuteAsync(null);
                    StatusMessage = $"Bulk {scopeType} update complete. List refreshed.";
                    _log.LogInformation($"Refresh complete after bulk {scopeType} update.");
                }
                else
                {
                    StatusMessage = $"Failed to send bulk {scopeType} update request. Check logs.";
                    _log.LogError(StatusMessage);
                    MessageBox.Show(StatusMessage, "Bulk Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    IsBusy = false; // Release busy flag on failure
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during bulk {scopeType} update.";
                _log.LogError(ex, StatusMessage);
                MessageBox.Show($"{StatusMessage}\n{ex.Message}", "Bulk Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                IsBusy = false; // Ensure busy flag is released on exception
            }
            finally
            {
                // Although IsBusy is set within the try/catch, this ensures it's false if something unexpected happens
                // before the try block or if logic changes. Redundant but safe.
                if (IsBusy) IsBusy = false;
            }
        }

        /// <summary>
        /// Command to delete a specific setting override (User or ODS), allowing inheritance.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanClearOverride))]
        private async Task ClearOverride(FeatureSettingDisplayItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(SelectedFeature) || IsBusy || item.ExplicitIsEnabled == null)
            {
                // Cannot clear if no item, feature not selected, busy, or no explicit setting exists
                return;
            }

            // Confirmation Dialog
            string confirmationMessage = $"This will remove the specific '{SelectedFeature}' setting for '{item.DisplayName}' ({item.Scope}).\n\n" +
                                         $"The setting will then be inherited from a lower priority scope (e.g., Global default).\n\n" +
                                         "Proceed?";
            var result = MessageBox.Show(confirmationMessage, "Confirm Clear Override", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                StatusMessage = $"Clear override cancelled for '{item.DisplayName}'.";
                _log.LogInformation(StatusMessage);
                return;
            }

            // Execute the delete
            IsBusy = true;
            StatusMessage = $"Clearing override for '{item.DisplayName}' ({SelectedFeature})...";
            _log.LogInformation($"Executing clear override: Scope={item.ScopeIdentifier}, Feature='{SelectedFeature}'");
            bool success = false;
            try
            {
                success = await _edgeAlertService.DeleteFeatureSettingAsync(SelectedFeature, item.ScopeIdentifier);

                if (success)
                {
                    StatusMessage = $"Override cleared successfully for '{item.DisplayName}'. Refreshing list...";
                    _log.LogInformation(StatusMessage);
                    // Refresh UI - Must release IsBusy before awaiting the command
                    IsBusy = false;
                    await LoadFeatureSettingsCommand.ExecuteAsync(null); // Reloads for SelectedFeature
                    StatusMessage = $"Override cleared. List refreshed.";
                    _log.LogInformation($"Refresh complete after clearing override for '{item.DisplayName}'.");
                }
                else
                {
                    StatusMessage = $"Failed to send clear override request for '{item.DisplayName}'. Check logs.";
                    _log.LogError(StatusMessage);
                    MessageBox.Show(StatusMessage, "Clear Override Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    IsBusy = false; // Release busy flag on failure
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error clearing override for '{item.DisplayName}'.";
                _log.LogError(ex, StatusMessage);
                MessageBox.Show($"{StatusMessage}\n{ex.Message}", "Clear Override Error", MessageBoxButton.OK, MessageBoxImage.Error);
                IsBusy = false; // Ensure busy flag is released on exception
            }
            finally
            {
                // Ensure busy is false
                if (IsBusy) IsBusy = false;
            }
        }

        // --- Add NEW Bulk Clear Commands ---

        [RelayCommand(CanExecute = nameof(CanExecuteBulkCommands))] // Reuse existing CanExecute
        private async Task ClearAllOdsOverrides()
        {
            await ExecuteBulkOperation( // Use adapted helper method
                serviceCall: () => _edgeAlertService.DeleteAllOdsOverridesAsync(SelectedFeature),
                scopeType: "ODS",
                actionDescriptionFormat: "clear all {0} overrides for feature '{1}'");
        }

        [RelayCommand(CanExecute = nameof(CanExecuteBulkCommands))] // Reuse existing CanExecute
        private async Task ClearAllUserOverrides()
        {
            await ExecuteBulkOperation( // Use adapted helper method
               serviceCall: () => _edgeAlertService.DeleteAllUserOverridesAsync(SelectedFeature),
               scopeType: "User",
               actionDescriptionFormat: "clear all {0} overrides for feature '{1}'");
        }

        /// <summary>
        /// Executes a bulk operation (Update or Delete) after user confirmation.
        /// </summary>
        /// <param name="serviceCall">A function representing the service call to execute.</param>
        /// <param name="scopeType">A string describing the scope ("ODS" or "User") for messages.</param>
        /// <param name="actionDescriptionFormat">A format string for the action description (e.g., "set all {0} scopes to {1}" or "clear all {0} overrides for feature '{1}'").</param>
        /// <param name="targetState">Optional target state (true/false) for use in format string.</param>
        private async Task ExecuteBulkOperation(Func<Task<bool>> serviceCall, string scopeType, string actionDescriptionFormat, bool? targetState = null)
        {
            string feature = SelectedFeature; // Capture before potential async gap
            string stateText = targetState == null ? "" : (targetState.Value ? "ON" : "OFF"); // Only relevant for 'Set' operations
            string formattedActionDescription = string.Format(actionDescriptionFormat, scopeType, targetState.HasValue ? stateText : feature);

            // Confirmation Dialog
            var result = MessageBox.Show(
                $"This will {formattedActionDescription}.\n\n" +
                (actionDescriptionFormat.Contains("clear") ? "Settings will then be inherited from lower priority scopes.\n\n" : "This overrides any existing settings for this scope and feature.\n\n") +
                "Proceed?",
                $"Confirm Bulk {scopeType} Operation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                StatusMessage = $"Bulk {scopeType} operation cancelled.";
                _log.LogInformation(StatusMessage);
                return;
            }

            // Execute the update/delete
            IsBusy = true;
            StatusMessage = $"Performing bulk operation: {formattedActionDescription}...";
            _log.LogInformation($"Executing bulk operation: {formattedActionDescription}");
            bool success = false;
            try
            {
                success = await serviceCall();

                if (success)
                {
                    StatusMessage = $"Bulk {scopeType} operation request sent successfully. Refreshing list...";
                    _log.LogInformation(StatusMessage);
                    // Refresh UI - Must release IsBusy before awaiting the command
                    IsBusy = false;
                    await LoadFeatureSettingsCommand.ExecuteAsync(null);
                    StatusMessage = $"Bulk {scopeType} operation complete. List refreshed.";
                    _log.LogInformation($"Refresh complete after bulk {scopeType} operation.");
                }
                else
                {
                    StatusMessage = $"Failed to send bulk {scopeType} operation request. Check logs.";
                    _log.LogError(StatusMessage);
                    MessageBox.Show(StatusMessage, "Bulk Operation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    IsBusy = false; // Release busy flag on failure
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during bulk {scopeType} operation.";
                _log.LogError(ex, StatusMessage);
                MessageBox.Show($"{StatusMessage}\n{ex.Message}", "Bulk Operation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                IsBusy = false; // Ensure busy flag is released on exception
            }
            finally
            {
                if (IsBusy) IsBusy = false;
            }
        }

        /// <summary>
        /// Determines if the ClearOverride command can be executed for a specific item.
        /// </summary>
        private bool CanClearOverride(FeatureSettingDisplayItem item)
        {
            // Can clear if:
            // 1. A feature is selected
            // 2. Not currently busy
            // 3. An explicit setting actually exists for this item (it's not already inheriting)
            // 4. It's not the Global setting (Global cannot be "cleared" to inherit from nothing)
            return !string.IsNullOrWhiteSpace(SelectedFeature)
                   && !IsBusy
                   && item != null
                   && item.Scope != "Global" // Cannot clear Global setting
                   && item.ExplicitIsEnabled != null; // Only enable if there IS an explicit setting
        }

        // --- Start Change: Implement partial OnChanged methods ---
        partial void OnOdsSearchTextChanged(string value)
        {
            _log.LogDebug($"ODS search text changed: {value}");
            // Optional: Add debounce timer here if needed for performance on large lists
            ApplyOdsFilter(); // Call existing filter method
        }

        partial void OnUserSearchTextChanged(string value)
        {
            _log.LogDebug($"User search text changed: {value}");
            // Optional: Add debounce timer here
            ApplyUserFilter(); // Call existing filter method
        }

        public async void Receive(MainViewModelInitialisedMessage message)
        {
            _log.LogInformation("Received MainViewModelInitialisedMessage. Starting FeatureSwitchManagementViewModel initialization.");

            // *** Ensure execution is on the UI thread ***
            await Application.Current.Dispatcher.InvokeAsync(async () => // Use Dispatcher
            {
                _log.LogInformation("Executing InitializeViewModelAsync on UI thread.");
                // InitializeViewModelAsync calls LoadAvailableFeaturesAsync, which should now be safe
                // regarding UI collection updates if LoadAvailableFeaturesAsync uses Dispatcher correctly
                // for its *own* collection modifications.
                await InitializeViewModelAsync();
            });
        }
    }
}
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Handlers;
using EdgeAlertSignalClient.Message;
using EdgeAlertSignalClient.Models;
using EdgeAlertSignalClient.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections; // Needed for IList casting
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows; // Needed for MessageBox

namespace EdgeAlertSignalClient.ViewModels
{
    public partial class ReportAccessManagementViewModel : ObservableObject, IRecipient<MainViewModelInitialisedMessage>
    {
        private readonly ILogger<ReportAccessManagementViewModel> _log;
        private readonly JSONHandler _jsonHandler;
        private readonly IEdgeAlertService _edgeAlertService;
        private readonly MainViewModel _mainViewModel;

        [ObservableProperty]
        private ObservableCollection<string> _odsPractices;

        [ObservableProperty]
        private ObservableCollection<string> _filteredUserNames;

        private List<string> _allUserNames;

        [ObservableProperty]
        private string _userSearchText;

        [ObservableProperty]
        private ObservableCollection<ReportPermission> _currentPermissions;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AssignPermissionCommand))]
        [NotifyCanExecuteChangedFor(nameof(AssignAllOdsCommand))]
        [NotifyCanExecuteChangedFor(nameof(RevokeSelectedCommand))] // User selection affects revoke
        private string _selectedUserName;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AssignPermissionCommand))]
        private string _selectedOdsPractice;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AssignPermissionCommand))]
        [NotifyCanExecuteChangedFor(nameof(AssignAllOdsCommand))]
        private string _selectedPermissionLevel = "Viewer"; // Default selection

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        private bool _isBusy;

        // Constructor - Inject dependencies
        public ReportAccessManagementViewModel(
            ILogger<ReportAccessManagementViewModel> log,
            JSONHandler jsonHandler,
            IEdgeAlertService edgeAlertService,
            MainViewModel mainViewModel) // Request EdgeAlertTables directly
        {
            _log = log;
            _jsonHandler = jsonHandler;
            _edgeAlertService = edgeAlertService;
            _mainViewModel = mainViewModel;

            WeakReferenceMessenger.Default.Register<MainViewModelInitialisedMessage>(this);

            _log.LogInformation("ReportAccessManagementViewModel initializing...");

            OdsPractices = new ObservableCollection<string>();
            FilteredUserNames = new ObservableCollection<string>();
            _allUserNames = new List<string>();
            CurrentPermissions = new ObservableCollection<ReportPermission>();

            _log.LogInformation("ReportAccessManagementViewModel initialized.");
        }

        private async Task LoadInitialData()
        {
            IsBusy = true;
            StatusMessage = "Loading filters...";
            try
            {
                // Calls within here will now execute on the UI thread context
                LoadOdsPractices(); // This modifies OdsPractices - ensure UI thread
                await LoadUserNames(); // This modifies FilteredUserNames - ensure UI thread
                StatusMessage = "Filters loaded. Select user to view permissions.";
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error loading initial data for Report Access Management.");
                StatusMessage = "Error loading filters.";
                // Ensure collections are cleared safely on error
                OdsPractices?.Clear(); // UI thread safe
                FilteredUserNames?.Clear(); // UI thread safe
                _allUserNames?.Clear();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void LoadOdsPractices()
        {
            try
            {
                var practices = _jsonHandler.Data?.Locations?
                    .Select(l => l.Practice)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct()
                    .OrderBy(p => p)
                    .ToList();

                OdsPractices.Clear();
                // Do NOT add "*" wildcard anymore
                if (practices != null)
                {
                    foreach (var practice in practices) OdsPractices.Add(practice);
                }
                _log.LogInformation($"Loaded {OdsPractices.Count} ODS practices.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error loading ODS practices.");
                StatusMessage = "Error loading ODS list.";
            }
        }

        private async Task LoadUserNames()
        {
            _allUserNames.Clear();
            FilteredUserNames.Clear();

            try
            {
                var edgeAlertTables = _mainViewModel?.EdgeAlertTablesInstance;
                if (edgeAlertTables == null)
                {
                    _log.LogError("EdgeAlertTables instance is not yet available. Cannot load users.");
                    StatusMessage = "Error: User data service initializing...";
                    ApplyUserFilter();
                    return;
                }

                // Get ALL users without ODS filtering
                var allUsers = await edgeAlertTables.GetAllUserAsync();
                var userNames = allUsers?
                    .Select(u => u.UserName)
                    .Where(un => !string.IsNullOrEmpty(un))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(un => un)
                    .ToList();

                if (userNames != null)
                {
                    _allUserNames.AddRange(userNames);
                }
                _log.LogInformation($"Loaded {_allUserNames.Count} distinct user names into backing list.");

                ApplyUserFilter();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error loading user names.");
                StatusMessage = "Error loading user list.";
                ApplyUserFilter();
            }
        }

        // Partial method automatically called when UserSearchText changes
        partial void OnUserSearchTextChanged(string value)
        {
            _log.LogDebug($"User search text changed: {value}");
            ApplyUserFilter(); // Call the filter method
        }

        /// <summary>
        /// Filters the full user list based on UserSearchText and updates the FilteredUserNames collection.
        /// </summary>
        private void ApplyUserFilter()
        {
            if (_allUserNames == null) return;

            try
            {
                // Ensure UI update is on the correct thread if necessary
                // Application.Current.Dispatcher.Invoke(() => {
                FilteredUserNames.Clear();
                var filter = UserSearchText?.Trim();

                var filtered = string.IsNullOrWhiteSpace(filter)
                    ? _allUserNames // No filter applied, show all users
                    : _allUserNames.Where(name => name.Contains(filter, StringComparison.OrdinalIgnoreCase));

                // Add filtered items back to the observable collection
                foreach (var item in filtered.OrderBy(name => name)) // Keep sorted
                {
                    FilteredUserNames.Add(item);
                }
                // });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error applying user filter.");
                StatusMessage = "Error filtering user list.";
            }
            // If selection is lost due to filtering, update CanExecute for commands
            AssignPermissionCommand.NotifyCanExecuteChanged();
            AssignAllOdsCommand.NotifyCanExecuteChanged();
            RevokeSelectedCommand.NotifyCanExecuteChanged();
        }

        // --- CanExecute Methods for Commands ---
        private bool CanAssignPermission()
        {
            return !IsBusy &&
                   !string.IsNullOrEmpty(SelectedUserName) &&
                   !string.IsNullOrEmpty(SelectedOdsPractice) &&
                   !string.IsNullOrEmpty(SelectedPermissionLevel);
        }

        private bool CanAssignAllOds()
        {
            return !IsBusy &&
                   !string.IsNullOrEmpty(SelectedUserName) &&
                   !string.IsNullOrEmpty(SelectedPermissionLevel);
        }

        // CanExecute for Revoke is checked within the command based on the parameter

        // --- New Commands ---

        [RelayCommand(CanExecute = nameof(CanAssignPermission))]
        private async Task AssignPermission()
        {
            // Get required selections
            string targetUser = SelectedUserName;
            string selectedPractice = SelectedOdsPractice;
            string selectedLevel = SelectedPermissionLevel;

            // Look up True ODS for the selected practice
            var location = _jsonHandler.GetLocationByPractice(selectedPractice);
            if (location == null || string.IsNullOrEmpty(location.ODS))
            {
                StatusMessage = $"Could not find True ODS code for practice '{selectedPractice}'.";
                _log.LogError(StatusMessage);
                MessageBox.Show(StatusMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            string trueOds = location.ODS;

            _log.LogInformation($"Assigning {selectedLevel}: User='{targetUser}', ODS='{trueOds}' (Practice='{selectedPractice}')");

            // Prepare payload (list with one item)
            var permissionPayload = new ReportPermission
            {
                AccessorUserId = targetUser,
                TargetOds = trueOds, // Use the True ODS code
                PermissionLevel = selectedLevel,
                Remove = false
            };

            // Execute the change
            await ExecutePermissionChange(new List<ReportPermission> { permissionPayload });
        }

        [RelayCommand(CanExecute = nameof(CanAssignAllOds))]
        private async Task AssignAllOds()
        {
            string targetUser = SelectedUserName;
            string selectedLevel = SelectedPermissionLevel;

            // Confirmation dialog
            var result = MessageBox.Show($"Assign '{selectedLevel}' permission for ALL ODS practices to user '{targetUser}'?\n\n(This will add an entry for each practice)",
                                         "Confirm Assign All", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                StatusMessage = "Assign all cancelled.";
                return;
            }

            _log.LogInformation($"Assigning {selectedLevel} to ALL ODS: User='{targetUser}'");
            IsBusy = true; // Set busy indicator early

            List<ReportPermission> permissionsToAssign = new List<ReportPermission>();
            try
            {
                // Get all locations and map to True ODS codes
                var allLocations = _jsonHandler.Data?.Locations;
                if (allLocations == null || !allLocations.Any())
                {
                    throw new InvalidOperationException("Could not retrieve ODS locations from JSONHandler.");
                }

                var allTrueOdsCodes = allLocations
                                        .Where(l => !string.IsNullOrEmpty(l.ODS) && !string.IsNullOrEmpty(l.Practice)) // Ensure ODS and Practice exist
                                        .Select(l => l.ODS)
                                        .Distinct()
                                        .ToList();

                if (!allTrueOdsCodes.Any())
                {
                    throw new InvalidOperationException("No valid True ODS codes found to assign.");
                }

                _log.LogInformation($"Found {allTrueOdsCodes.Count} distinct True ODS codes to assign.");

                // Create permission object for each True ODS
                foreach (var trueOds in allTrueOdsCodes)
                {
                    permissionsToAssign.Add(new ReportPermission
                    {
                        AccessorUserId = targetUser,
                        TargetOds = trueOds,
                        PermissionLevel = selectedLevel,
                        Remove = false
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error preparing 'Assign All' list.";
                _log.LogError(ex, "Error getting ODS list for Assign All command.");
                MessageBox.Show($"{StatusMessage}\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                IsBusy = false;
                return;
            }

            // Execute the batch change
            await ExecutePermissionChange(permissionsToAssign);
        }

        [RelayCommand]
        private async Task RevokeSelected(object selectedItemsObj) // Parameter comes from CommandParameter
        {
            var selectedItems = selectedItemsObj as IList; // Cast from object

            // Check if anything is selected
            if (selectedItems == null || selectedItems.Count == 0)
            {
                StatusMessage = "Please select one or more permissions from the list to revoke.";
                MessageBox.Show(StatusMessage, "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Confirmation dialog
            var result = MessageBox.Show($"Revoke the selected {selectedItems.Count} permission(s) for user '{SelectedUserName}'?",
                                         "Confirm Revoke", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                StatusMessage = "Revoke cancelled.";
                return;
            }

            _log.LogInformation($"Revoking {selectedItems.Count} permissions: User='{SelectedUserName}'");
            IsBusy = true; // Set busy indicator

            List<ReportPermission> permissionsToRevoke = new List<ReportPermission>();
            try
            {
                // Iterate through the selected items and create payload for removal
                foreach (var item in selectedItems)
                {
                    if (item is ReportPermission selectedPerm) // Safely cast each item
                    {
                        permissionsToRevoke.Add(new ReportPermission
                        {
                            AccessorUserId = selectedPerm.AccessorUserId, // Use ID from the selected item
                            TargetOds = selectedPerm.TargetOds,       // Use True ODS from the selected item
                                                                      // PermissionLevel is not strictly needed for remove, but good practice
                            PermissionLevel = selectedPerm.PermissionLevel,
                            Remove = true                             // Set Remove flag to true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error preparing revoke list.";
                _log.LogError(ex, "Error iterating selected items for revoke.");
                MessageBox.Show($"{StatusMessage}\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                IsBusy = false;
                return;
            }

            if (!permissionsToRevoke.Any())
            {
                StatusMessage = "No valid permissions were selected for revoke.";
                _log.LogWarning("RevokeSelectedCommand: No valid ReportPermission objects found in selectedItems.");
                IsBusy = false;
                return;
            }

            // Execute the batch change
            await ExecutePermissionChange(permissionsToRevoke);
        }

        // Helper method for executing the batch call and handling response
        private async Task ExecutePermissionChange(List<ReportPermission> permissions)
        {
            if (permissions == null || !permissions.Any())
            {
                _log.LogWarning("ExecutePermissionChange called with empty permission list.");
                IsBusy = false;
                return;
            }

            // Set busy indicator if not already set by caller
            if (!IsBusy) IsBusy = true;
            string actionTypeLog = permissions.First().Remove ? "revoke" : "add/update"; // For logging
            StatusMessage = $"Processing {permissions.Count} permission change(s)...";

            try
            {
                // Call the updated service method that accepts a list
                // Ensure method name matches your service definition (ManageReportPermissionsAsync)
                bool success = await _edgeAlertService.ManageReportPermissionsAsync(permissions);

                if (success)
                {
                    StatusMessage = $"Successfully processed {permissions.Count} permission change(s). Refreshing list...";
                    _log.LogInformation(StatusMessage);
                    // Remove IsBusy to allow screen refresh
                    IsBusy = false;
                    await LoadPermissionsForSelectedUserAsync(); // Refresh list on success
                    StatusMessage = $"Successfully processed {permissions.Count} permission change(s). List refreshed.";
                }
                else
                {
                    StatusMessage = $"Failed to {actionTypeLog} permissions batch. Check logs.";
                    _log.LogError($"Failed backend call for {actionTypeLog} batch ({permissions.Count} items).");
                    MessageBox.Show(StatusMessage, "Operation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"An error occurred while trying to {actionTypeLog} permissions.";
                _log.LogError(ex, $"Exception during {actionTypeLog} batch ({permissions.Count} items).");
                MessageBox.Show($"{StatusMessage}\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                // Trigger CanExecute updates for commands
                AssignPermissionCommand.NotifyCanExecuteChanged();
                AssignAllOdsCommand.NotifyCanExecuteChanged();
                RevokeSelectedCommand.NotifyCanExecuteChanged();
            }
        }


        // --- Existing Methods ---

        // Called when user selection changes
        async partial void OnSelectedUserNameChanged(string value)
        {
            // Reload permissions for the newly selected user
            if (!string.IsNullOrEmpty(value))
            {
                await LoadPermissionsForSelectedUserAsync();
            }
            else
            {
                CurrentPermissions.Clear(); // Clear list if no user selected
                StatusMessage = "Select a user to view permissions.";
            }
            // Update CanExecute status of commands that depend on user selection
            AssignPermissionCommand.NotifyCanExecuteChanged();
            AssignAllOdsCommand.NotifyCanExecuteChanged();
            RevokeSelectedCommand.NotifyCanExecuteChanged();
        }

        // Called when ODS practice selection changes
        async partial void OnSelectedOdsPracticeChanged(string value)
        {
            if (string.IsNullOrEmpty(value)) return; // Ignore if cleared during init

            // Don't reset the user list when ODS changes
            // Just update any UI states as needed

            // Reset user selection when ODS changes
            SelectedUserName = null;

            // Update commands status
            AssignPermissionCommand.NotifyCanExecuteChanged();
            AssignAllOdsCommand.NotifyCanExecuteChanged();
            RevokeSelectedCommand.NotifyCanExecuteChanged();

            // Update status message
            StatusMessage = $"Selected {value}. Choose a user to manage permissions.";
        }

        // Called when permission level selection changes
        partial void OnSelectedPermissionLevelChanged(string value)
        {
            // Update CanExecute status of commands that depend on level selection
            AssignPermissionCommand.NotifyCanExecuteChanged();
            AssignAllOdsCommand.NotifyCanExecuteChanged();
        }

        // Method to load permissions (includes populating TargetPracticeName)
        private async Task LoadPermissionsForSelectedUserAsync()
        {
            if (string.IsNullOrEmpty(SelectedUserName))
            {
                CurrentPermissions.Clear();
                return;
            }

            // Prevent unnecessary reloads if already busy
            if (IsBusy) return;

            IsBusy = true;
            StatusMessage = $"Loading permissions for {SelectedUserName}...";
            CurrentPermissions.Clear();

            try
            {
                var permissions = await _edgeAlertService.GetUserReportPermissionsAsync(SelectedUserName);

                if (permissions != null)
                {
                    // Populate TargetPracticeName for display
                    foreach (var perm in permissions)
                    {
                        if (perm.TargetOds == "*") // Should not happen anymore if wildcard removed
                        {
                            perm.TargetPracticeName = "* (All Practices)";
                        }
                        else
                        {
                            var location = _jsonHandler.GetLocationByODS(perm.TargetOds);
                            perm.TargetPracticeName = location?.Practice ?? $"Unknown ODS ({perm.TargetOds})";
                        }
                    }

                    // Add processed permissions to the bound collection
                    foreach (var perm in permissions.OrderBy(p => p.TargetPracticeName))
                    {
                        CurrentPermissions.Add(perm);
                    }
                    StatusMessage = $"Loaded {CurrentPermissions.Count} permissions for {SelectedUserName}.";
                    // Add log after loading
                    _log.LogInformation($"LoadPermissionsForSelectedUserAsync: Loaded {CurrentPermissions.Count} permissions for {SelectedUserName}.");

                }
                else
                {
                    StatusMessage = $"Failed to load permissions for {SelectedUserName}. Check logs.";
                    _log.LogWarning($"LoadPermissionsForSelectedUserAsync: GetUserReportPermissionsAsync returned null for {SelectedUserName}.");
                    // MessageBox.Show($"Could not load permissions for {SelectedUserName}. Please check the logs or network connection.", "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Error loading permissions for user {SelectedUserName}.");
                StatusMessage = "Error loading permissions.";
                MessageBox.Show($"An unexpected error occurred while loading permissions: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                // Ensure CanExecute is updated after loading
                RevokeSelectedCommand.NotifyCanExecuteChanged(); // Revoke depends on list content indirectly
            }
        }

        public async void Receive(MainViewModelInitialisedMessage message) // Changed to async void
        {
            _log.LogInformation("Received MainViewModelInitialisedMessage. Starting ReportAccessManagementViewModel initialization.");

            // *** Ensure execution is on the UI thread ***
            await Application.Current.Dispatcher.InvokeAsync(async () => // Use Dispatcher
            {
                _log.LogInformation("Executing LoadInitialData on UI thread.");
                await LoadInitialData(); // Await the async method
            });
        }
    }
}
using CommunityToolkit.Mvvm.ComponentModel;
using EdgeAlertSignalClient.Stores;
using EdgeAlertSignalClient.Views;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Windows;
using EdgeAlertSignalClient.Message;
using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Handlers;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using EdgeAlertSignalClient.Services;
using System.Collections.ObjectModel;
using EdgeAlertSignalClient.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace EdgeAlertSignalClient.ViewModels
{
    public partial class AudioDeviceViewModel : ObservableObject
    {
        public AudioDevice Device { get; }

        [ObservableProperty]
        private bool _isSelected;

        public string Id => Device.Id;
        public string Name => Device.Name;
        // Expose IsAvailable from the underlying Device object
        public bool IsAvailable => Device.IsAvailable;
        // WaveOutDeviceNumber is still accessible if needed, but not directly used in style
        public int WaveOutDeviceNumber => Device.WaveOutDeviceNumber;

        public AudioDeviceViewModel(AudioDevice device)
        {
            Device = device;
        }
    }

    public partial class MainWindowViewModel : ObservableObject, IRecipient<MainViewModelMessage>,
                                                                 IRecipient<AdminLoginResultMessage>
    {
        [ObservableProperty]
        public string? _versionApp;

        [ObservableProperty]
        public object? _currentView;

        [ObservableProperty]
        public string? _mainWindowTitle;

        [ObservableProperty]
        public bool _mainWindowShowInTaskBar = false;

        [ObservableProperty]
        public Visibility _mainWindowVisibility = Visibility.Collapsed;

        [ObservableProperty]
        public WindowState _mainWindowState = WindowState.Minimized;

        [ObservableProperty]
        public string? _statusText;
        private readonly ILogger<MainWindowViewModel> _log;
        private NavigationStore _navigationStore;
        private MainView _mainView;
        private TaskbarIconService _taskbarIconService;
        private readonly IconView _iconView;
        private readonly ReportsView _reportsView;
        private readonly TimeReportsView _timeReportsView;
        private readonly RoomStore _roomStore;
        private readonly AlertStore _alertStore;
        private readonly NetworkHandler _networkHandler;
        private readonly IServiceProvider _serviceProvider;
        private readonly IEdgeAlertService _edgeAlertService;
        private readonly AdminLoginView _adminLoginView;
        private readonly AdminView _adminView;
        private readonly AdminViewModel _adminViewModel;
        private readonly TimeReportsViewModel _timeReportsViewModel;
        private readonly AudioDeviceService _audioDeviceService;
        private readonly MainViewModel _mainViewModel;
        private readonly Utils _utils;
        private bool _reportViewActivated = false;
        private bool _isAdminAuthenticated = false;

        // --- Audio Device Properties ---
        [ObservableProperty]
        private ObservableCollection<AudioDeviceViewModel> _availableAudioDevices;

        [ObservableProperty]
        private string _selectedAudioDeviceId = Utils.SYSTEM_DEFAULT_AUDIO_DEVICE_ID; // Initialize with default

        [ObservableProperty]
        private bool _isAudioMenuOpen = false;

        [ObservableProperty]
        private bool _isTimeReportVisible = false; // Default to hidden

        private readonly AssetManagementView _assetManagementView;

        public MainWindowViewModel(
            ILogger<MainWindowViewModel> log,
            NavigationStore navigationStore,
            MainView mainView,
            TaskbarIconService taskbarIconService,
            IconView iconView,
            ReportsView reportsView,
            TimeReportsView timeReportsView,
            RoomStore roomStore,
            AlertStore alertStore,
            NetworkHandler networkHandler,
            IServiceProvider serviceProvider,
            IEdgeAlertService edgeAlertService,
            AdminLoginView adminLoginView,
            AdminView adminView,
            AdminViewModel adminViewModel,
            TimeReportsViewModel timeReportsViewModel,
            AudioDeviceService audioDeviceService,
            MainViewModel mainViewModel,
            Utils utils,
            AssetManagementView assetManagementView // <-- Use correct view
        )
        {
            _log = log;
            _navigationStore = navigationStore;
            _mainView = mainView;
            _taskbarIconService = taskbarIconService;
            _iconView = iconView;
            _reportsView = reportsView;
            _timeReportsView = timeReportsView;
            _roomStore = roomStore;
            _alertStore = alertStore;
            _networkHandler = networkHandler;
            _serviceProvider = serviceProvider;
            _edgeAlertService = edgeAlertService;
            _adminLoginView = adminLoginView;
            _adminView = adminView;
            _adminViewModel = adminViewModel;
            _timeReportsViewModel = timeReportsViewModel;
            _audioDeviceService = audioDeviceService;
            _mainViewModel = mainViewModel;
            _utils = utils;
            _assetManagementView = assetManagementView;

            // Set version title
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionApp = $"Edge Alert v{(version != null ? version.ToString(4) : "1.0.0.0")}";
            //VersionApp = $"Edge Alert v1.4.0.2";
            MainWindowTitle = VersionApp;
            _log.LogInformation($"{VersionApp} has Started");

            // Initialize Collections
            AvailableAudioDevices = new ObservableCollection<AudioDeviceViewModel>();

            // Subscribe to Messages
            WeakReferenceMessenger.Default.Register<MainViewModelMessage>(this);
            // Add to constructor after initialization - register for admin login messages:
            WeakReferenceMessenger.Default.Register<AdminLoginResultMessage>(this);

            //Event Subscriptions
            _navigationStore.PropertyChanged += NavigationStore_PropertyChanged;

            // Set initial view
            _navigationStore.CurrentView = _mainView;

            // Check internet connection without awaiting
            var _ = CheckInternetConnectionAsync(); // Discard the result since we don't need to wait for it
        }

        public async Task CheckInternetConnectionAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                bool isInternetAvailable = await _networkHandler.IsInternetAvailableAsync(cts.Token);
                DispatcherHelper.RunOnUIThread(() =>
                {
                    if (!isInternetAvailable)
                    {
                        _log.LogWarning("Internet connection is not available.");
                        // Set Status Text safely on the UI thread
                        //WeakReferenceMessenger.Default.Send(new MainViewModelMessage("No Internet Connection."));
                    }
                    else
                    {
                        _log.LogInformation("Internet connection is available. Connecting to Alert Server...");
                        // Set Status Text safely on the UI thread
                        //WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Connecting to Alert Server..."));
                    }
                });
            }
        }

        private async Task UpdateButtonVisibility()
        {
            try
            {
                // Optional slight delay if MainViewModel initialization takes time. Test if needed.
                // await Task.Delay(500);

                // Ensure _edgeAlertService is not null before using it
                if (_edgeAlertService == null)
                {
                    _log.LogError("UpdateButtonVisibility: _edgeAlertService is null. Cannot check permissions.");
                    IsTimeReportVisible = false; // Default to hidden if service is unavailable
                    return;
                }

                IsTimeReportVisible = await _edgeAlertService.CheckHasAnyReportAccessAsync();
                _log.LogInformation($"Time Reports button visibility set to: {IsTimeReportVisible}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error checking report access for button visibility.");
                IsTimeReportVisible = false; // Default to hidden on error
            }
        }

        private void NavigationStore_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            CurrentView = _navigationStore.CurrentView;
        }

        [RelayCommand]
        public void EdgeAlertButton()
        {
            _navigationStore.CurrentView = _mainView;
        }

        [RelayCommand]
        public void ReportsButton()
        {
            if (_navigationStore.ReportChanged)
            {
                _navigationStore.ReportChanged = false;
                // Update Report now we are looking at it
                WeakReferenceMessenger.Default.Send(new MainViewModelMessage("ReportView"));
            }

            _navigationStore.CurrentView = _reportsView;
        }

        [RelayCommand]
        public void OpenUserStateDebug()
        {
            _log.LogInformation("Opening User State Debug window");

            // Get the debug view from the service provider
            var debugView = _serviceProvider.GetRequiredService<UserStateDebugView>();
            debugView.Show();
        }

        [RelayCommand]
        public void AdminButton()
        {
            if (_isAdminAuthenticated)
            {
                _navigationStore.CurrentView = _adminView;
            }
            else
            {
                _navigationStore.CurrentView = _adminLoginView;
            }
        }

        // Add this message handler method - updated to use Value property
        public void Receive(AdminLoginResultMessage message)
        {
            if (message.Value)
            {
                _isAdminAuthenticated = true;
                _navigationStore.CurrentView = _adminView;
                _adminViewModel.GrantAccess();
            }
        }

        [RelayCommand]
        public void WindowStateNormal()
        {
            _log.LogInformation($"WindowStateNormal: Making Visible");
            //MainWindowVisibility = Visibility.Visible;
            //MainWindowShowInTaskBar = true;
            //MainWindowState = WindowState.Normal;
            WeakReferenceMessenger.Default.Send(new MainViewModelMessage("MainWindowOpen"));
            WeakReferenceMessenger.Default.Send(new MainViewModelMessage("IconHide"));
        }

        [RelayCommand]
        public async void WindowStateMinimise()
        {
            bool isInternetAvailable;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                isInternetAvailable = await _networkHandler.IsInternetAvailableAsync(cts.Token);
            }

            if (!string.IsNullOrEmpty(_roomStore.Room) && (!_alertStore.AlertActive) || (!isInternetAvailable) || (_roomStore.ForceHide == true && _roomStore.Blocked == false))
            {
                _log.LogInformation($"WindowStateMinimise: RoomStore was {_roomStore.Room}, showing Icon, hiding Window");
                WeakReferenceMessenger.Default.Send(new MainViewModelMessage("MainWindowHide"));
                WeakReferenceMessenger.Default.Send(new MainViewModelMessage("IconShow"));
                //MainWindowState = WindowState.Minimized;
                //MainWindowVisibility = Visibility.Collapsed;
                //MainWindowShowInTaskBar = false;
            }
            else if (_roomStore.Blocked == true)
            {
                _log.LogInformation($"WindowStateMinimise: ODS Blocked, hiding Icon, hiding Window");
                WeakReferenceMessenger.Default.Send(new MainViewModelMessage("MainWindowHide"));
                WeakReferenceMessenger.Default.Send(new MainViewModelMessage("IconHide"));
            }
            else if (string.IsNullOrEmpty(_roomStore.Room))
            {
                StatusText = "Please select the room you are located before minimising";
                _log.LogInformation($"WindowStateMinimise: RoomStore was NOT Null, setting WindowStateNormal");
                WindowStateNormal();
            }
            else if (!string.IsNullOrEmpty(_roomStore.Room) && (_alertStore.AlertActive))
            {
                StatusText = "Can't minimise while Alert is active";
                _log.LogInformation($"WindowStateMinimise: RoomStore was NOT Null, setting WindowStateNormal");
                WindowStateNormal();
            }
        }

        [RelayCommand]
        public async void TimeReportsButton() // Changed to async Task
        {
            // Ensure the instance is available before calling
            if (_timeReportsViewModel != null)
            {
                // Call the initialization method which handles the "run once" logic
                // and loads filters/data on first activation.
                await _timeReportsViewModel.InitializeViewAsync();
            }
            else
            {
                // Log an error if the ViewModel instance is null (should not happen with DI)
                _log.LogError("TimeReportsButtonCommand: _timeReportsViewModelInstance is null. Cannot initialize view.");
                return;
            }
            // --- End New ---

            // Existing navigation logic
            _navigationStore.CurrentView = _timeReportsView;
        }

        // --- ADDED RelayCommand implementation for ViewLoaded ---
        [RelayCommand]
        private async Task ViewLoaded()
        {
            _log.LogInformation("MainWindowViewModel ViewLoadedCommand executed.");
            // Call the visibility check for Time Reports button
            await UpdateButtonVisibility();
            // Call the new command to load audio devices
            await LoadAudioDevicesCommand.ExecuteAsync(null);
        }

        [RelayCommand]
        private async Task LoadAudioDevices()
        {
            _log.LogInformation("Loading available audio devices...");
            // Consider adding IsBusy flag here if loading takes time
            // IsBusy = true;
            try
            {
                // Get devices from service
                List<AudioDevice> devices = _audioDeviceService.GetActiveOutputDevices();

                // Get saved preference
                SelectedAudioDeviceId = await _utils.GetPreferredAudioDeviceIDAsync(); // Update property which holds current state

                // Use Dispatcher for collection modification
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AvailableAudioDevices.Clear();
                    foreach (var device in devices)
                    {
                        var vm = new AudioDeviceViewModel(device)
                        {
                            // Set IsSelected based on comparison with the loaded preference
                            IsSelected = (device.Id == SelectedAudioDeviceId)
                        };
                        AvailableAudioDevices.Add(vm);
                    }
                });

                _log.LogInformation("Loaded {Count} audio devices. Selected ID: {SelectedId}", AvailableAudioDevices.Count, SelectedAudioDeviceId);

                // Ensure at least one item is marked as selected if the saved one wasn't found
                if (!AvailableAudioDevices.Any(vm => vm.IsSelected))
                {
                    var defaultVm = AvailableAudioDevices.FirstOrDefault(vm => vm.Id == Utils.SYSTEM_DEFAULT_AUDIO_DEVICE_ID);
                    if (defaultVm != null)
                    {
                        defaultVm.IsSelected = true;
                        SelectedAudioDeviceId = defaultVm.Id; // Update the stored ID if we defaulted
                        _log.LogWarning("Saved audio device not found, defaulting to System Default in UI.");
                        // Optionally save this default back to registry immediately?
                        // await _utils.SetPreferredAudioDeviceIDAsync(SelectedAudioDeviceId);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error loading audio devices.");
                // Clear list on error?
                await Application.Current.Dispatcher.InvokeAsync(() => AvailableAudioDevices.Clear());
                // Show error message?
            }
            finally
            {
                // IsBusy = false;
            }
        }

        // --- Command: Select Audio Device ---
        [RelayCommand]
        private async Task SelectAudioDevice(string selectedDeviceId)
        {
            if (string.IsNullOrEmpty(selectedDeviceId)) return;

            _log.LogInformation("Audio device selection changed. New Device ID: {DeviceId}", selectedDeviceId);
            // IsBusy = true; // Optional busy indicator

            try
            {
                // Save the selected ID to registry
                await _utils.SetPreferredAudioDeviceIDAsync(selectedDeviceId);
                SelectedAudioDeviceId = selectedDeviceId; // Update the ViewModel property

                // Update IsSelected state in the UI collection
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var vm in AvailableAudioDevices)
                    {
                        vm.IsSelected = (vm.Id == selectedDeviceId);
                    }
                });

                // Optional: Provide user feedback (e.g., StatusText)
                var selectedDeviceVm = AvailableAudioDevices.FirstOrDefault(vm => vm.IsSelected);
                StatusText = $"Alert audio device set to: {selectedDeviceVm?.Name ?? "Unknown"}";
                _log.LogInformation("Successfully saved and updated selection for Device ID: {DeviceId}", selectedDeviceId);

            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error selecting/saving audio device with ID: {DeviceId}", selectedDeviceId);
                StatusText = "Error saving audio device preference.";
                // Optionally reload devices to revert UI?
                // await LoadAudioDevicesCommand.ExecuteAsync(null);
            }
            finally
            {
                // IsBusy = false;
            }
        }

        // --- Command: Test Audio Device ---
        [RelayCommand]
        private async Task TestAudioDevice()
        {
            // Find the currently selected device in the list
            AudioDeviceViewModel? selectedVm = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                selectedVm = AvailableAudioDevices?.FirstOrDefault(vm => vm.IsSelected);
            });


            if (selectedVm == null)
            {
                _log.LogWarning("TestAudioDevice: No device selected in the list to test.");
                StatusText = "Please select a device from the list first.";
                return;
            }

            _log.LogInformation("Testing audio device: {DeviceName} ({DeviceId})", selectedVm.Name, selectedVm.Id);
            StatusText = $"Testing {selectedVm.Name}...";

            try
            {
                // Call a method (potentially in MainViewModel or a dedicated service)
                // to play the sound once on the specified device.
                // We pass the Device ID to the MainViewModel's method.
                if (_mainViewModel != null)
                {
                    bool success = await _mainViewModel.PlayAlertSoundOnceAsync(selectedVm.Id);
                    StatusText = success ? $"Test sound played on {selectedVm.Name}." : $"Failed to play test sound on {selectedVm.Name}.";
                    if (!success) _log.LogError("TestAudioDevice: PlayAlertSoundOnceAsync failed for {DeviceId}", selectedVm.Id);
                }
                else
                {
                    _log.LogError("TestAudioDevice: MainViewModel instance is null. Cannot play test sound.");
                    StatusText = "Error: Cannot access sound playback.";
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error testing audio device: {DeviceId}", selectedVm.Id);
                StatusText = $"Error testing {selectedVm.Name}.";
            }
        }

        [RelayCommand]
        private async Task ShowAudioDeviceMenu() // Changed to async Task for LoadAudioDevices call
        {
            _log.LogDebug("ShowAudioDeviceMenuCommand executed.");

            // Ensure the device list is up-to-date before showing
            // It might have changed since the window loaded
            await LoadAudioDevicesCommand.ExecuteAsync(null);

            // Set the property to open the ContextMenu
            // The actual opening is handled by the IsOpen binding in XAML
            IsAudioMenuOpen = true;
            // Note: IsAudioMenuOpen should automatically be set back to false by WPF
            // when the menu closes (e.g., user clicks away). If not, we might need
            // to handle the ContextMenu.Closed event or use a different approach.
        }

        public void ForceHideWindow()
        {
            _log.LogInformation("ForceHideWindow: Forcing application hidden due to a lack of network or valid location.");
            WeakReferenceMessenger.Default.Send(new MainViewModelMessage("MainWindowHide"));
            WeakReferenceMessenger.Default.Send(new MainViewModelMessage("IconShow"));
            StatusText = "Application hidden due to a lack of network or valid location.";
        }

        public void Receive(MainViewModelMessage message)
        {
            if (message.Value == "Open")
            {
                WindowStateNormal();
            }
            else if (message.Value == "Hide")
            {
                WindowStateMinimise();
            }
            else if (message.Value == "ForceHide")
            {
                WindowStateMinimise();
            }
            else if (message.Value == "ClearStatusText")
            {
                StatusText = "";
            }
            else if (message.Value == "IconHide" || message.Value == "IconShow" || message.Value == "MainWindowOpen" || message.Value == "MainWindowHide" || message.Value == "ReportView")
            {
                return;
            }
            else
            {
                StatusText = message.Value;
            }
        }

        [RelayCommand]
        public void AssetManagementButton()
        {
            _navigationStore.CurrentView = _assetManagementView;
        }
    }
}

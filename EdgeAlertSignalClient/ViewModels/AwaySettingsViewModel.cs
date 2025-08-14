using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EdgeAlertSignalClient.Services; // Assuming EdgeAlertService is here
using Microsoft.Extensions.Logging; // For logging
using System;
using System.Threading.Tasks;
using System.Windows; // For MessageBox
using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Message;
using EdgeAlertSignalClient.Handlers;

namespace EdgeAlertSignalClient.ViewModels
{
    public partial class AwaySettingsViewModel : ObservableObject, IRecipient<AdminTimeoutMessage>
    {
        private readonly ILogger<AwaySettingsViewModel> _log;
        private readonly IEdgeAlertService _edgeAlertService;
        private readonly AzureHandler _azureHandler;

        [ObservableProperty]
        private string _awayTimeoutMinutes = "3"; // Default value

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        private bool _isBusy;

        public AwaySettingsViewModel(ILogger<AwaySettingsViewModel> log, 
                                     IEdgeAlertService edgeAlertService,
                                     AzureHandler azureHandler)
        {
            _log = log;
            _edgeAlertService = edgeAlertService;
            _azureHandler = azureHandler;

            // Register for messages
            WeakReferenceMessenger.Default.Register<AdminTimeoutMessage>(this);

            InitializeViewModelAsync(); // Load initial value when VM is created
        }

        private async void InitializeViewModelAsync()
        {
            await LoadCurrentSettingAsync();
        }

        [RelayCommand]
        private async Task LoadCurrentSettingAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Loading current setting...";
            _log.LogInformation("Loading Away Timeout setting...");

            try
            {
                await _azureHandler.InitializationTask; // Wait for Azure keys to be loaded

                string currentValue = await _edgeAlertService.GetAwayTimeoutSettingAsync();
                AwayTimeoutMinutes = currentValue ?? "3"; // Use default if null comes back
                StatusMessage = $"Current setting loaded ({AwayTimeoutMinutes} minutes).";
                _log.LogInformation($"Loaded Away Timeout setting: {AwayTimeoutMinutes}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error loading Away Timeout setting.");
                StatusMessage = "Error loading setting.";
                // Keep the default value in AwayTimeoutMinutes
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task SaveSettingAsync()
        {
            if (IsBusy) return;

            // --- Validation ---
            if (!int.TryParse(AwayTimeoutMinutes, out int minutes) || minutes < 1 || minutes > 60)
            {
                StatusMessage = "Invalid value. Please enter a number between 1 and 60.";
                MessageBox.Show(StatusMessage, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning); // Added MessageBox feedback
                _log.LogWarning($"Validation failed for AwayTimeoutMinutes: {AwayTimeoutMinutes}");
                return;
            }
            // --- End Validation ---

            IsBusy = true;
            StatusMessage = "Saving setting...";
            _log.LogInformation($"Attempting to save Away Timeout setting: {minutes}");

            try
            {
                bool success = await _edgeAlertService.UpdateAwayTimeoutSettingAsync(minutes.ToString()); // Pass validated value
                if (success)
                {
                    StatusMessage = "Setting saved successfully.";
                    _log.LogInformation("Away Timeout setting saved successfully.");

                    // Notify other components via messaging
                    WeakReferenceMessenger.Default.Send(new AdminTimeoutMessage(minutes));
                }
                else
                {
                    StatusMessage = "Failed to save setting.";
                    _log.LogError("Failed to save Away Timeout setting via service.");
                    MessageBox.Show(StatusMessage, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error); // Added MessageBox feedback
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error saving Away Timeout setting.");
                StatusMessage = "An error occurred while saving.";
                MessageBox.Show(StatusMessage, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error); // Added MessageBox feedback
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Receive(AdminTimeoutMessage message)
        {
            // Update the UI if a timeout change comes from elsewhere
            if (message.Value > 0)
            {
                AwayTimeoutMinutes = message.Value.ToString();
                StatusMessage = $"Timeout value updated to {message.Value} minutes.";
            }
        }
    }
}
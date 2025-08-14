using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EdgeAlertSignalClient.Models;
using EdgeAlertSignalClient.Services;
using Microsoft.Extensions.Logging;

namespace EdgeAlertSignalClient.ViewModels
{
    public partial class UserStateDebugViewModel : ObservableObject
    {
        private readonly ILogger<UserStateDebugViewModel> _logger;
        private readonly UserStateMonitorService _userStateMonitorService;
        private readonly UserTimeLogService _userTimeLogService;

        [ObservableProperty]
        private ObservableCollection<UserStateLog> _logEntries = new ObservableCollection<UserStateLog>();

        [ObservableProperty]
        private string _currentStateText = "Active";

        [ObservableProperty]
        private Brush _currentStateBrush = Brushes.Green;

        public UserStateDebugViewModel(
            ILogger<UserStateDebugViewModel> logger,
            UserStateMonitorService userStateMonitorService,
            UserTimeLogService userTimeLogService)
        {
            _logger = logger;
            _userStateMonitorService = userStateMonitorService;
            _userTimeLogService = userTimeLogService;

            // Add sample data for testing
            AddSampleLogEntries();
        }

        [RelayCommand]
        public void SetActiveState()
        {
            _logger.LogInformation("User manually set state to Active");
            _userStateMonitorService.LogStateTransition(UserState.Active, "Manual override");
            UpdateCurrentState(UserState.Active);

            // Add a log entry to our view
            LogEntries.Insert(0, new UserStateLog
            {
                State = UserState.Active,
                StateStartTime = DateTime.UtcNow,
                DurationSeconds = 0,
                Context = "Manual override"
            });
        }

        [RelayCommand]
        public void SetAwayState()
        {
            _logger.LogInformation("User manually set state to Away");
            _userStateMonitorService.LogStateTransition(UserState.Away, "Manual override");
            UpdateCurrentState(UserState.Away);

            // Add a log entry to our view
            LogEntries.Insert(0, new UserStateLog
            {
                State = UserState.Away,
                StateStartTime = DateTime.UtcNow,
                DurationSeconds = 0,
                Context = "Manual override"
            });
        }

        [RelayCommand]
        public void SetOfflineState()
        {
            _logger.LogInformation("User manually set state to Offline");
            _userStateMonitorService.LogStateTransition(UserState.Offline, "Manual override");
            UpdateCurrentState(UserState.Offline);

            // Add a log entry to our view
            LogEntries.Insert(0, new UserStateLog
            {
                State = UserState.Offline,
                StateStartTime = DateTime.UtcNow,
                DurationSeconds = 0,
                Context = "Manual override"
            });
        }

        [RelayCommand]
        public void ClearLogs()
        {
            _logger.LogInformation("Clearing log entries from debug view");
            LogEntries.Clear();
        }

        [RelayCommand]
        public async Task SendLogs()
        {
            _logger.LogInformation("Attempting to send logs from debug view");

            // Create a collection to hold the logs
            var logs = new List<UserStateLog>(LogEntries);

            // Show a message based on the result
            bool success = await _userTimeLogService.SendLogsAsync(logs);

            string message = success ? "Logs sent successfully!" : "Failed to send logs. See application log for details.";
            string title = "Send Logs";
            MessageBoxImage icon = success ? MessageBoxImage.Information : MessageBoxImage.Error;

            MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        }

        private void UpdateCurrentState(UserState state)
        {
            CurrentStateText = state.ToString();

            // Update color based on state
            CurrentStateBrush = state switch
            {
                UserState.Active => Brushes.Green,
                UserState.Away => Brushes.Orange,
                UserState.Offline => Brushes.Red,
                _ => Brushes.Black
            };
        }

        private void AddSampleLogEntries()
        {
            // Add some sample log entries for testing
            LogEntries.Add(new UserStateLog
            {
                State = UserState.Active,
                StateStartTime = DateTime.UtcNow.AddMinutes(-10),
                DurationSeconds = 0,
                Context = "Application Start"
            });

            LogEntries.Add(new UserStateLog
            {
                State = UserState.Away,
                StateStartTime = DateTime.UtcNow.AddMinutes(-5),
                DurationSeconds = 300,
                Context = "User inactive"
            });

            LogEntries.Add(new UserStateLog
            {
                State = UserState.Active,
                StateStartTime = DateTime.UtcNow.AddMinutes(-2),
                DurationSeconds = 180,
                Context = "User returned"
            });
        }
    }
}
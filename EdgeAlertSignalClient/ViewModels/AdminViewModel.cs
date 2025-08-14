using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Message;
using EdgeAlertSignalClient.Views;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace EdgeAlertSignalClient.ViewModels
{
    public partial class AdminViewModel : ObservableObject
    {
        private readonly ILogger<AdminViewModel> _log;

        [ObservableProperty]
        private AwaySettingsView _awaySettingsView;

        [ObservableProperty]
        private FeatureSwitchManagementView _systemConfigView;

        [ObservableProperty]
        private ReportAccessManagementView _userManagementView;

        // This is used to enable/disable the admin panel access
        [ObservableProperty]
        private bool _isAdminAccessGranted = false;

        public AdminViewModel(
            ILogger<AdminViewModel> log,
            AwaySettingsView awaySettingsView,
            ReportAccessManagementView reportAccessManagementView,
            FeatureSwitchManagementView featureSwitchManagementView
            )
        {
            _log = log;
            _awaySettingsView = awaySettingsView;
            _userManagementView = reportAccessManagementView;
            _systemConfigView = featureSwitchManagementView;

            _log.LogInformation("AdminViewModel initialized");
        }

        [RelayCommand]
        public void GrantAccess()
        {
            IsAdminAccessGranted = true;
            _log.LogInformation("Admin access granted");
        }

        [RelayCommand]
        public void RevokeAccess()
        {
            IsAdminAccessGranted = false;
            _log.LogInformation("Admin access revoked");
        }
    }
}
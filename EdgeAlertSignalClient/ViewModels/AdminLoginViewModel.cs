using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Message;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.ViewModels
{
    public partial class AdminLoginViewModel : ObservableObject
    {
        private readonly ILogger<AdminLoginViewModel> _log;

        // This will be bound to the password box
        private string _password;

        // Property to bind to password box
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        [ObservableProperty]
        private string _errorMessage;

        [ObservableProperty]
        private bool _hasError;

        // The correct password
        private const string ADMIN_PASSWORD = "3dgeb1ts";

        public AdminLoginViewModel(ILogger<AdminLoginViewModel> log)
        {
            _log = log;
            _log.LogInformation("AdminLoginViewModel initialized");
        }

        [RelayCommand]
        private void Login()
        {
            if (string.IsNullOrEmpty(Password))
            {
                SetError("Password cannot be empty");
                return;
            }

            if (Password == ADMIN_PASSWORD)
            {
                // Clear any error
                HasError = false;
                ErrorMessage = string.Empty;

                // Send success message
                _log.LogInformation("Admin login successful");
                WeakReferenceMessenger.Default.Send(new AdminLoginResultMessage(true));
            }
            else
            {
                SetError("Invalid password");
                _log.LogWarning("Failed admin login attempt");
                WeakReferenceMessenger.Default.Send(new AdminLoginResultMessage(false));
            }
        }

        private void SetError(string message)
        {
            ErrorMessage = message;
            HasError = true;
        }
    }
}
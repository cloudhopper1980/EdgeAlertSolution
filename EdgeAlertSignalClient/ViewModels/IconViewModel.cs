using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Handlers;
using EdgeAlertSignalClient.Message;
using EdgeAlertSignalClient.Services;
using EdgeAlertSignalClient.Stores;
using EdgeAlertSignalClient.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace EdgeAlertSignalClient.ViewModels
{
    public partial class IconViewModel : ObservableObject, IRecipient<MainViewModelMessage>, IRecipient<ConnectivityStatusMessage>
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int HWND_TOPMOST = -1;
        private readonly ILogger<IconViewModel> _log;
        private readonly IEdgeAlertService _edgeAlertService;
        private readonly AlertStore _alertStore;
        private readonly Utils _utils;
        private readonly AzureHandler _azureHandler;

        private DispatcherTimer _flashingTimer;
        private DispatcherTimer _alertFlashingTimer;

        [ObservableProperty]
        public Visibility _iconVisibility = Visibility.Collapsed;

        [ObservableProperty]
        public bool _alertHasBeenSet;

        [ObservableProperty]
        public bool _responderHasBeenSet;

        [ObservableProperty]
        private string _borderColor = "#cd1f25"; // Default border color

        [ObservableProperty]
        private string _backgroundColor = "#ff3035"; // Default lighter background color

        [ObservableProperty]
        private string _backgroundColorDark = "#8a141a"; // Default darker background color

        public IconViewModel(ILogger<IconViewModel> log,
                             IEdgeAlertService edgeAlertService,
                             AlertStore alertStore,
                             Utils utils,
                             AzureHandler azureHandler)
        {
            WeakReferenceMessenger.Default.Register<MainViewModelMessage>(this);
            WeakReferenceMessenger.Default.Register<ConnectivityStatusMessage>(this);

            _log = log;
            _edgeAlertService = edgeAlertService;
            _alertStore = alertStore;
            _utils = utils;
            _azureHandler = azureHandler;

            _alertStore.PropertyChanged += _alertStore_PropertyChanged;
        }

        private void _alertStore_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            AlertHasBeenSet = _alertStore.AlertSet;
            ResponderHasBeenSet = _alertStore.ResponderHasBeenSet;
        }

        partial void OnAlertHasBeenSetChanged(bool value)
        {
            if (value)
            {
                StopFlashing();
                StartAlertFlashing();
            }
            else
            {
                StopAlertFlashing();
                if (ResponderHasBeenSet)
                    StartFlashing();
                else
                    BorderColor = "#cd1f25";
            }
        }

        partial void OnResponderHasBeenSetChanged(bool value)
        {
            if (value)
            {
                StopAlertFlashing();
                StartFlashing();
            }
            else
            {
                StopFlashing();
                if (AlertHasBeenSet)
                    StartAlertFlashing();
                else
                    BorderColor = "#cd1f25";
            }
        }

        private void StartAlertFlashing()
        {
            _alertFlashingTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(0.5)
            };
            _alertFlashingTimer.Tick += AlertFlashingTimer_Tick;
            _alertFlashingTimer.Start();
        }

        private void StopAlertFlashing()
        {
            if (_alertFlashingTimer != null)
            {
                _alertFlashingTimer.Stop();
                _alertFlashingTimer.Tick -= AlertFlashingTimer_Tick;
                BorderColor = ResponderHasBeenSet ? "#ffbf00" : "#ff3035";
            }
        }

        private void AlertFlashingTimer_Tick(object? sender, EventArgs e)
        {
            BorderColor = BorderColor == "#ff3035" ? "#6E1014" : "#ff3035";
        }

        private void StartFlashing()
        {
            _flashingTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(0.5)
            };
            _flashingTimer.Tick += ResponderFlashingTimer_Tick;
            _flashingTimer.Start();
        }

        private void StopFlashing()
        {
            if (_flashingTimer != null)
            {
                _flashingTimer.Stop();
                _flashingTimer.Tick -= ResponderFlashingTimer_Tick;
                BorderColor = "#cd1f25";
            }
        }

        private void ResponderFlashingTimer_Tick(object? sender, EventArgs e)
        {
            BorderColor = BorderColor == "#cd1f25" ? "#ffbf00" : "#cd1f25";
        }

        [RelayCommand]
        public async void DoubleClickAlert()
        {
            _log.LogInformation("IconViewModel: DoubleClickAlert:");
            WeakReferenceMessenger.Default.Send(new DoubleClickMessage("DoubleClick"));
            WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Open"));
            IconVisibility = Visibility.Collapsed;
        }

        [RelayCommand]
        public void RightClickClose()
        {
            _log.LogInformation("RightClickClose: Sending message to open Window and hide Icon");
            WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Open"));
            IconVisibility = Visibility.Collapsed;
        }

        public void Receive(MainViewModelMessage message)
        {
            if (message.Value == "IconShow")
            {
                _log.LogInformation("IconViewModel: Receive: show Icon");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IconVisibility = Visibility.Visible;
                });
            }
            else if (message.Value == "IconHide")
            {
                _log.LogInformation("IconViewModel: Receive: hide Icon");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IconVisibility = Visibility.Collapsed;
                });
            }
        }

        public void Receive(ConnectivityStatusMessage message)
        {
            if (!message.Value)
            {
                BorderColor = "#A9A9A9";
                BackgroundColor = "#D3D3D3";
                BackgroundColorDark = "#696969";
            }
            else
            {
                BorderColor = "#cd1f25";
                BackgroundColor = "#ff3035";
                BackgroundColorDark = "#8a141a";
            }
        }
    }
}

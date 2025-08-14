using System.Windows;
using System.Net.Http;
using System;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.ViewModels;
using EdgeAlertSignalClient.Message;
using System.Threading.Tasks;
using EdgeAlertSignalClient.Services;
using System.Windows.Controls;

namespace EdgeAlertSignalClient
{
    public partial class MainWindow : Window, IRecipient<MainViewModelMessage>
    {
        private readonly ILogger<MainWindow> _log;
        private MainWindowViewModel _mainWindowViewModel;
        private readonly UserStateMonitorService _userStateMonitorService;
        private readonly MainViewModel _mainViewModel;
        private readonly DispatcherTimer _repositionTimer;
        private bool _preventMinimize = false;  // Flag to control whether to prevent minimizing
        private bool _isShutdownLogicRunning = false; // Flag to prevent re-entrancy
        private readonly object _shutdownLock = new object(); // Lock object

        public MainWindow(ILogger<MainWindow> log, MainWindowViewModel mainWindowViewModel,
                          UserStateMonitorService userStateMonitorService, MainViewModel mainViewModel)
        {
            InitializeComponent();
            _log = log;
            _mainWindowViewModel = mainWindowViewModel;
            _userStateMonitorService = userStateMonitorService;
            _mainViewModel = mainViewModel;
            DataContext = _mainWindowViewModel;

            _log.LogInformation("MainWindow: Setting Listeners");
            this.StateChanged += MainWindow_StateChanged;
            this.LocationChanged += MainWindow_LocationChanged;
            this.Closing += MainWindow_Closing;

            // Subscribe to Messages
            WeakReferenceMessenger.Default.Register<MainViewModelMessage>(this);

            _log.LogInformation("MainWindow: Setting Reposition Timer");
            _repositionTimer = new DispatcherTimer
            {
#if DEBUG
                Interval = TimeSpan.FromSeconds(999999) // REMOVE DEBUG
#else
                Interval = TimeSpan.FromSeconds(15)
#endif
            };
            _repositionTimer.Tick += RepositionTimer_Tick;
            _log.LogInformation("MainWindow: Initialized");
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (_preventMinimize && this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Maximized;
            }

            if (this.WindowState == WindowState.Maximized)
            {
                var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                this.MaxHeight = screen.WorkingArea.Height;
            }
            else
            {
                this.MaxHeight = double.PositiveInfinity;
            }
        }

        // Make the handler async
        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            WeakReferenceMessenger.Default.Send(new MainViewModelMessage("MainWindow_Closing: Exiting"));
            WeakReferenceMessenger.Default.Send(new ExitApplicationMessage("EXIT"));
            await Task.Delay(3000); // Increase the delay time if necessary
            Application.Current.Shutdown();
        }

        public void Receive(MainViewModelMessage message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (message.Value == "MainWindowOpen")
                {
                    _preventMinimize = false;  // Allow the window to be restored
                    this.ShowInTaskbar = true;  // Ensures window visibility in the taskbar
                    this.WindowState = WindowState.Normal;  // Reset state if minimized

                    this.Left = SystemParameters.WorkArea.Left;
                    this.Top = SystemParameters.WorkArea.Top;
                    this.Width = SystemParameters.WorkArea.Width;
                    this.Height = SystemParameters.WorkArea.Height;

                    this.Show();
                    this.Activate();
                    this.Topmost = true;
                    _preventMinimize = true;  // Prevent further minimization
                }
                else if (message.Value == "MainWindowHide" || message.Value == "ForceHide")
                {
                    _preventMinimize = false;
                    this.Topmost = false;
                    this.Hide();  // Ensures complete hide state
                    _repositionTimer?.Stop();
                }
            });
        }

        // Add this method to MainWindow.xaml.cs
        private void AudioDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                // Ensure the ViewModel has loaded devices before showing
                // This might require slight adjustment depending on how you access the ViewModel here
                // Example if _mainWindowViewModel is accessible:
                _mainWindowViewModel?.LoadAudioDevicesCommand.ExecuteAsync(null).ConfigureAwait(false); // Load devices async but don't wait here

                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
                _log?.LogInformation("AudioDeviceButton_Click: Explicitly setting ContextMenu.IsOpen = true");
            }
            else
            {
                _log?.LogWarning("AudioDeviceButton_Click: Sender was not a Button or ContextMenu was null.");
            }
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (this.WindowState == WindowState.Maximized)
                {
                    // Convert the mouse position from relative-to-window to screen coordinates.
                    Point screenPosition = PointToScreen(e.GetPosition(this));

                    // Determine which screen we're on. 
                    var currentScreen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)screenPosition.X, (int)screenPosition.Y));

                    // Calculate the width percentage based on the current screen width.
                    double widthPercentage = screenPosition.X / currentScreen.WorkingArea.Width;

                    // Revert the window to the normal state.
                    this.WindowState = WindowState.Normal;

                    // Set the new position based on the screen's width and the width percentage.
                    this.Left = currentScreen.WorkingArea.Left + (currentScreen.WorkingArea.Width * widthPercentage) - (this.ActualWidth * widthPercentage);
                    this.Top = e.GetPosition(this).Y - (this.ActualHeight * e.GetPosition(this).Y / this.ActualHeight);
                }

                // Proceed with the drag operation.
                DragMove();

                if (this.Visibility == Visibility.Visible)
                {
                    _repositionTimer.Stop();
                    _repositionTimer.Start();
                }
            }
        }

        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            // If the window is maximized, toggle it to normal and then maximize again.
            // This helps in recalculating the dimensions when the window is moved to another monitor.
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                this.WindowState = WindowState.Maximized;
            }
        }

        private void RepositionTimer_Tick(object sender, EventArgs e)
        {
            _repositionTimer.Stop();
            if (this.Visibility == Visibility.Visible)
            {
                this.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
                this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
                this.Top = (SystemParameters.PrimaryScreenHeight - this.Height) / 2;
            }
        }
    }
}

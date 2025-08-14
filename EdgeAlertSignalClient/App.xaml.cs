using System;
using System.Windows;
using EdgeAlertSignalClient.Services;
using EdgeAlertSignalClient.ViewModels;
using EdgeAlertSignalClient.Views;
using EdgeAlertSignalClient.Stores;
using Microsoft.Extensions.DependencyInjection;
using EdgeAlertSignalClient.Handlers;
using EdgeAlertSignalClient.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Message;
using System.IO;
using Serilog;
using System.Threading;
using System.IO.Pipes;
using Microsoft.VisualBasic.Logging;
using System.Reflection;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Management;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace EdgeAlertSignalClient
{
    public partial class App : Application
    {
        private IHost _host;
        private readonly Serilog.ILogger _logger;
        private static Mutex _mutex = new Mutex(true, $"{{5D1E7B12-F9C3-41AE-8E54-0E0D67A1B09F}}_{Environment.UserName}");
        private NamedPipeServerStream _pipeServer;

        public App()
        {
            SplashScreen splashScreen = new SplashScreen("Resources/edgealert_splash.png");

            // Load the appsettings.json file
            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Get the user's Downloads folder path and create the "logs" subfolder
            string downloadsFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
            string logsFolderPath = Path.Combine(downloadsFolderPath, "EdgeDrivers", "EdgeAlertLogs");
            Directory.CreateDirectory(logsFolderPath);

            string logFilePath = Path.Combine(logsFolderPath, "EdgeAlertLog_.txt");

            // Configure Serilog
            Serilog.Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            _logger = Serilog.Log.Logger;

            // Set version title
            var VersionApp = $"Edge Alert v{Assembly.GetExecutingAssembly().GetName().Version.ToString(4)}";
            //var VersionApp = $"Edge Alert v1.4.0.2";
            _logger.Information($"{VersionApp} Initialising...");

#if RELEASE
            // Create a new named pipe and start listening for messages
            string currentUserName = Environment.UserName;

            // Check if the application is already running
            if (!_mutex.WaitOne(TimeSpan.Zero, true))
            {
                // Application is already running; exit this one
                _logger?.Information("Application instance already running. Exiting the second instance.");

                // Connect to the existing instance to bring it to the foreground
                // Check if the application is already running for this user
                using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", $"EdgeAlertPipe_{currentUserName}", PipeDirection.Out))
                {
                    pipeClient.Connect();
                }

                Shutdown();
                return;
            }

            _pipeServer = new NamedPipeServerStream($"EdgeAlertPipe_{currentUserName}", PipeDirection.In);
            Thread serverThread = new Thread(ServerThread);
            serverThread.IsBackground = true;
            serverThread.Start();
#endif
            // Show splash screen
            splashScreen.Show(true);

            _host = new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Views
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<MainView>();
                    services.AddSingleton<IconView>();
                    services.AddSingleton<ReportsView>();
                    // Register the ViewModel factory delegates for transient views
                    services.AddTransient<UserStateDebugView>();
                    services.AddSingleton<AdminLoginView>();
                    services.AddSingleton<AdminView>();
                    services.AddSingleton<AwaySettingsView>();
                    services.AddSingleton<ReportAccessManagementView>();
                    services.AddSingleton<TimeReportsView>();
                    services.AddTransient<FeatureSwitchManagementView>();

                    // ViewModels
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddSingleton<IconViewModel>();
                    services.AddSingleton<ReportsViewModel>();
                    services.AddSingleton<AdminLoginViewModel>();
                    services.AddSingleton<AdminViewModel>();
                    services.AddSingleton<AwaySettingsViewModel>();
                    services.AddSingleton<ReportAccessManagementViewModel>();
                    services.AddSingleton<TimeReportsViewModel>();
                    services.AddTransient<FeatureSwitchManagementViewModel>();

                    // Register services
                    services.AddSingleton<IEdgeAlertService, EdgeAlertService>();
                    services.AddSingleton<NavigationStore>();
                    services.AddSingleton<AlertStore>();
                    services.AddSingleton<RoomStore>();
                    services.AddSingleton<TaskbarIconService>();
                    services.AddSingleton<JSONHandler>();
                    services.AddSingleton<NetworkHandler>(); 
                    services.AddSingleton<AudioDeviceService>();
                    // Register User Time Tracking services
                    services.AddSingleton<UserStateMonitorService>();
                    services.AddSingleton<UserTimeLogService>();
                    // Register Feature Service
                    services.AddSingleton<FeatureSettingsCache>();


                    // Register CommandLineArgsHolder and Handlers
                    string[] args = Environment.GetCommandLineArgs();
                    services.AddSingleton<CommandLineArgsHolder>(provider => new CommandLineArgsHolder(Environment.GetCommandLineArgs()));
                    services.AddSingleton<Utils>();
                    services.AddSingleton<AzureHandler>();
                    services.AddSingleton<IPAddressMonitor>();

                    // Add Serilog to dependency injection
                    services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));


                })
                .Build();

            var argsHolder = _host.Services.GetService<CommandLineArgsHolder>();
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            _logger.Information("OnStartup: Initialising JSON Handler");
            // Get required services
            var argsHolder = _host.Services.GetService<CommandLineArgsHolder>();
            // var featureSwitchHandler = _host.Services.GetService<FeatureSwitchHandler>(); // Remove this line
            var jsonHandler = _host.Services.GetService<JSONHandler>();
            var logger = _host.Services.GetService<ILoggerFactory>().CreateLogger("IPAddressLogger");
            var networkHandler = _host.Services.GetService<NetworkHandler>();


            // <<< UPDATE the Initialize call >>>
            IPAddressHandler.Initialize(
                argsHolder,
                // featureSwitchHandler, // Remove
                networkHandler,
                jsonHandler,
                logger
                );

            // Get trusted domain or DNS suffix
            var domainName = networkHandler.GetTrustedDomainOrSuffix();

            if (string.IsNullOrEmpty(domainName))
            {
                MessageBox.Show("Can't run EdgeAlert on an untrusted network", "Untrusted Network", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }

            SystemEvents.SessionSwitch += SystemEvents_OnSessionSwitch;
            SystemEvents.SessionEnding += SystemEvents_SessionEnding;

            _logger.Information("Loading Main Window");

            // --- Asset Management Service/ViewModel/View Construction ---
            // Load Azure secrets (connection string) before constructing table services
            var azureHandler = _host.Services.GetService<AzureHandler>();
            var assetTableConnectionString = azureHandler?._settings?.DatabaseConnectionString;

            // Only construct if connection string is available
            AssetTableService? assetTableService = null;
            AssetManagementViewModel? assetManagementViewModel = null;
            AssetManagementView? assetManagementView = null;
            if (!string.IsNullOrEmpty(assetTableConnectionString))
            {
                assetTableService = new AssetTableService(assetTableConnectionString, "Assets");
                assetManagementViewModel = new AssetManagementViewModel(assetTableService);
                assetManagementView = new AssetManagementView(assetManagementViewModel);
            }
            else
            {
                _logger.Error("Asset table connection string is missing. Asset Management will not be available.");
                // Optionally: Show error or fallback
            }

            // --- Main Window Construction ---
            var mainWindow = _host.Services.GetService<MainWindow>();
            if (mainWindow != null && assetManagementView != null)
            {
                // Inject AssetManagementView into MainWindowViewModel if needed
                // (Assumes MainWindowViewModel is constructed with AssetManagementView)
                // If using DI, this may already be handled; otherwise, set property or pass as parameter
            }
        }

        // SystemEvents_SessionEnding: Triggered on system shutdown/restart
        private void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
        {
            _logger?.Information($"SystemEvents_SessionEnding: Triggered. Reason: {e.Reason}");

            // --- REVERTED: Send ExitApplicationMessage ---
            // MainViewModel will now handle UserDisconnected, Time Logging, etc.
            WeakReferenceMessenger.Default.Send(new ExitApplicationMessage("EXIT"));
            _logger?.Information($"SystemEvents_SessionEnding: Sent ExitApplicationMessage.");

            // --- REVERTED: Use Thread.Sleep ---
            // Provide a blocking delay to allow the message handler time to execute
            // before the OS terminates the process. Adjust duration as needed.
            // This is generally more reliable in SessionEnding than Task.Delay.
            _logger?.Information($"SystemEvents_SessionEnding: Sleeping for 1500ms to allow message processing...");
            Thread.Sleep(1500); // Original delay was 1000ms, slightly increased for safety.

            _logger?.Information($"SystemEvents_SessionEnding: Delay completed.");
            // Do NOT call UserDisconnected or Shutdown here directly.
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger.Information($"OnExit: Triggered");

            // Unsubscribe from the SessionEnded event
            SystemEvents.SessionEnding -= SystemEvents_SessionEnding;
            SystemEvents.SessionSwitch -= SystemEvents_OnSessionSwitch;
            _logger.Information($"OnExit: Unsubscribed to Events");

            try
            {
                // Release the mutex
                _mutex?.ReleaseMutex();
            }
            catch(Exception ex)
            {
                _logger.Error($"Failed to release mutex: {ex.Message}");
            }

            base.OnExit(e);
        }

        // SystemEvents_OnSessionSwitch: Handles Logoff, Disconnect, Lock, Unlock etc.
        private async void SystemEvents_OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            // No need to resolve services here anymore if MainViewModel handles the logic.
            _logger?.Information($"SystemEvents_OnSessionSwitch: Triggered. Reason: {e.Reason}");

            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    // Send Lock message (handled elsewhere for Away status etc.)
                    WeakReferenceMessenger.Default.Send(new ApplicationRestartMessage("LOCK_MACHINE"));
                    break;

                case SessionSwitchReason.SessionLogoff:
                case SessionSwitchReason.RemoteDisconnect:
                case SessionSwitchReason.ConsoleDisconnect:
                    // --- REVERTED: Send ExitApplicationMessage for logoff/disconnect ---
                    WeakReferenceMessenger.Default.Send(new ExitApplicationMessage("EXIT"));
                    _logger?.Information($"SystemEvents_OnSessionSwitch ({e.Reason}): Sent ExitApplicationMessage.");

                    // --- REVERTED: Use Task.Delay BEFORE Shutdown ---
                    // Allow time for message processing *before* explicitly shutting down.
                    _logger?.Information($"SystemEvents_OnSessionSwitch ({e.Reason}): Delaying for 1000ms before shutdown...");
                    await Task.Delay(1000);
                    break;

                // Other cases (Logon, Connect, Unlock) remain the same, sending ApplicationRestartMessage
                case SessionSwitchReason.SessionUnlock:
                    WeakReferenceMessenger.Default.Send(new ApplicationRestartMessage("SOFT_RESTART"));
                    break;
                case SessionSwitchReason.SessionLogon:
                case SessionSwitchReason.RemoteConnect:
                case SessionSwitchReason.ConsoleConnect:
                    WeakReferenceMessenger.Default.Send(new ApplicationRestartMessage("RESTART"));
                    break;
            }
        }

        private void ServerThread()
        {
            try
            {
                // Wait for a client to connect
                _pipeServer.WaitForConnection();

                // Handle the connected client, e.g., bring your application to the foreground.
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Send a message to your MainViewModel to open the application.
                    WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Open"));
                });
            }
            finally
            {
                // Always ensure to disconnect to allow new connections.
                _pipeServer.Disconnect();
            }

            // Wait for another client.
            ServerThread();
        }

        private string GetMachineSerialNumber()
        {
            try
            {
                var systemInfo = SystemInfoService.GetSystemInfo(); // Call the static method to get info
                return systemInfo?.SerialNumber ?? SystemInfoService.GenerateUniqueMachineId(); // Use fallback if needed
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error getting serial number in App.xaml.cs. Using fallback.");
                return SystemInfoService.GenerateUniqueMachineId(); // Fallback ID generation
            }
        }
    }
}

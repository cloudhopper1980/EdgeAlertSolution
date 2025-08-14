using CommunityToolkit.Mvvm.Input;
using Hardcodet.Wpf.TaskbarNotification;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows;
using System;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Messaging;
using System.Reflection;
using EdgeAlertSignalClient.Message;
using EdgeAlertSignalClient.Services;
using EdgeAlertSignalClient.Utilities;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EdgeAlertSignalClient.Stores;

public class TaskbarIconService : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private readonly IEdgeAlertService _edgeAlertService;
    private readonly Utils _utils;
    private readonly ILogger<TaskbarIconService> _log;
    private readonly RoomStore _roomStore;

    public TaskbarIconService(IEdgeAlertService edgeAlertService,
                              Utils utils,
                              ILogger<TaskbarIconService> log,
                              RoomStore roomStore)
    {
        var iconUri = new Uri("pack://application:,,,/Resources/EdgeAlertIcon.ico");
        var iconBitmap = new BitmapImage(iconUri);
        string version = $"Edge Alert v{Assembly.GetExecutingAssembly().GetName().Version.ToString(4)}";

        _taskbarIcon = new TaskbarIcon
        {
            IconSource = iconBitmap,
            ToolTipText = version,
            MenuActivation = PopupActivationMode.RightClick,
            DoubleClickCommand = new RelayCommand(ExecuteOpenCommand)
        };

        var contextMenu = new ContextMenu();

        var openMenuItem = new MenuItem
        {
            Header = "Open",
            Command = new RelayCommand(ExecuteOpenCommand)
        };
        contextMenu.Items.Add(openMenuItem);

        contextMenu.Items.Add(new Separator());

        var exitMenuItem = new MenuItem
        {
            Header = "Exit",
            Command = new RelayCommand(ExecuteExitCommand)
        };
        contextMenu.Items.Add(exitMenuItem);

        _taskbarIcon.ContextMenu = contextMenu;
        _edgeAlertService = edgeAlertService;
        _utils = utils;
        _log = log;
        _roomStore = roomStore;
    }

    private void ExecuteOpenCommand()
    {
        // Check if _roomStore is not null and not blocked, then send message
        if (_roomStore != null && !_roomStore.Blocked)
        {
            WeakReferenceMessenger.Default.Send(new MainViewModelMessage("Open"));
        }
    }

    private void ExecuteExitCommand()
    {
        _log.LogInformation("TaskbarIconService: ExecuteExitCommand: Exiting");
        // Unsubscribe to SignalR events
        WeakReferenceMessenger.Default.Send(new ExitApplicationMessage("EXIT"));
    }

    public void Dispose()
    {
        _taskbarIcon.Dispose();
    }
}
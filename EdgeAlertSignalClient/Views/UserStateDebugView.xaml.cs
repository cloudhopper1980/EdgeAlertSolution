using System.Windows;
using EdgeAlertSignalClient.ViewModels;
using Microsoft.Extensions.Logging;
using EdgeAlertSignalClient.Services;

namespace EdgeAlertSignalClient.Views
{
    /// <summary>
    /// Interaction logic for UserStateDebugView.xaml
    /// </summary>
    public partial class UserStateDebugView : Window
    {
        private readonly UserStateDebugViewModel _viewModel;

        public UserStateDebugView(
            ILogger<UserStateDebugViewModel> logger,
            UserStateMonitorService userStateMonitorService,
            UserTimeLogService userTimeLogService)
        {
            InitializeComponent();

            // Create the ViewModel
            _viewModel = new UserStateDebugViewModel(
                logger,
                userStateMonitorService,
                userTimeLogService);

            // Set the DataContext
            DataContext = _viewModel;
        }
    }
}
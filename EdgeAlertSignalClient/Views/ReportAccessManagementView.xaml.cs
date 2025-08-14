using EdgeAlertSignalClient.ViewModels; // Add this using directive
using System.Windows.Controls;

namespace EdgeAlertSignalClient.Views
{
    /// <summary>
    /// Interaction logic for ReportAccessManagementView.xaml
    /// </summary>
    public partial class ReportAccessManagementView : UserControl
    {
        // Keep the constructor parameterless for View registration if you register views directly
        // If you resolve views via DI, inject the ViewModel
        public ReportAccessManagementView(ReportAccessManagementViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel; // Set the DataContext
        }
    }
}
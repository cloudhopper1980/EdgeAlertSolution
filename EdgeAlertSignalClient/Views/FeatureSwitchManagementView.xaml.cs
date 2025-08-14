using System;
using System.Windows.Controls;
using EdgeAlertSignalClient.ViewModels;

namespace EdgeAlertSignalClient.Views
{
    /// <summary>
    /// Interaction logic for FeatureSwitchManagementView.xaml
    /// </summary>
    public partial class FeatureSwitchManagementView : UserControl
    {
        // Constructor now accepts the ViewModel via injection
        public FeatureSwitchManagementView(FeatureSwitchManagementViewModel viewModel)
        {
            InitializeComponent();
            // Set the DataContext directly to the injected ViewModel instance
            DataContext = viewModel;
        }
    }
}
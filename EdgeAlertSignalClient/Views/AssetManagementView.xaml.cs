using System.Windows.Controls;
using EdgeAlertSignalClient.ViewModels;

namespace EdgeAlertSignalClient.Views
{
    public partial class AssetManagementView : UserControl
    {
        public AssetManagementView(AssetManagementViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

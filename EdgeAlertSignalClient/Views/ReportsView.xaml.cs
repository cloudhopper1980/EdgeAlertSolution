using EdgeAlertSignalClient.Services;
using EdgeAlertSignalClient.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EdgeAlertSignalClient.Views
{
    /// <summary>
    /// Interaction logic for ReportsView.xaml
    /// </summary>
    public partial class ReportsView : UserControl
    {
        private readonly ReportsViewModel _reportsViewModel;

        public ReportsView(ReportsViewModel reportsViewModel)
        {
            InitializeComponent();
            _reportsViewModel = reportsViewModel;
            DataContext = _reportsViewModel;
        }
    }
}

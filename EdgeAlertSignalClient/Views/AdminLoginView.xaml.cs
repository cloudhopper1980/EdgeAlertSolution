using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Message;
using EdgeAlertSignalClient.ViewModels;
using System;
using System.Collections.Generic;
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
    /// Interaction logic for AdminLoginView.xaml
    /// </summary>
    public partial class AdminLoginView : UserControl, IRecipient<AdminLoginResultMessage>
    {
        private readonly AdminLoginViewModel _viewModel;

        public AdminLoginView(AdminLoginViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            // Subscribe to login result messages
            WeakReferenceMessenger.Default.Register<AdminLoginResultMessage>(this);
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (this.DataContext != null)
            {
                ((AdminLoginViewModel)this.DataContext).Password = ((PasswordBox)sender).Password;
            }
        }

        public void Receive(AdminLoginResultMessage message)
        {
            // Clear the password box regardless of authentication result
            passwordBox.Password = "";

            // Optional: Set focus back to password box if login fails
            if (!message.Value)
            {
                passwordBox.Focus();
            }
        }
    }
}
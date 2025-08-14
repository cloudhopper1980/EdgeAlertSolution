using CommunityToolkit.Mvvm.Messaging.Messages;
using EdgeAlertSignalClient.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EdgeAlertSignalClient.ViewModels.MainViewModel;

namespace EdgeAlertSignalClient.Message
{
    public class UserListMessage : ValueChangedMessage<ObservableCollection<EdgeUserViewModel>>
    {
        public UserListMessage(ObservableCollection<EdgeUserViewModel> value) : base(value)
        {
        }
    }
}

using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EdgeAlertSignalClient.Services.JoinTables;

namespace EdgeAlertSignalClient.Message
{
    public class AlertHistoryMessage : ValueChangedMessage<ObservableCollection<AlertHistory>>
    {
        public AlertHistoryMessage(ObservableCollection<AlertHistory> value) : base(value)
        {
        }
    }
}

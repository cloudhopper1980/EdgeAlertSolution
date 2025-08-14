using CommunityToolkit.Mvvm.Messaging.Messages;
using EdgeAlertSignalClient.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Message
{
    public class MainViewModelInitialisedMessage : ValueChangedMessage<string>
    {
        public MainViewModelInitialisedMessage(string value) : base(value)
        {
        }
    }
}

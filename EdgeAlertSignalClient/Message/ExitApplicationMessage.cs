using CommunityToolkit.Mvvm.Messaging.Messages;
using EdgeAlertSignalClient.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Message
{
    public class ExitApplicationMessage : ValueChangedMessage<string>
    {
        public ExitApplicationMessage(string value) : base(value)
        {
        }
    }
}

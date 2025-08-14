using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Message
{
    public class IPAddressChangedMessage : ValueChangedMessage<string>
    {
        public IPAddressChangedMessage(string value) : base(value)
        {
        }
    }
}

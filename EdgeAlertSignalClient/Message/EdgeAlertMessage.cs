using CommunityToolkit.Mvvm.Messaging.Messages;
using EdgeAlertSignalClient.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Message
{
    public class EdgeAlertMessage : ValueChangedMessage<EdgeUser>
    {
        public EdgeAlertMessage(EdgeUser value) : base(value)
        {
        }
    }
}

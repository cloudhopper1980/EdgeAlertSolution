using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Message
{
    public class AdminLoginResultMessage : ValueChangedMessage<bool>
    {
        public bool Success => Value;

        public AdminLoginResultMessage(bool success) : base(success)
        {
        }
    }
}
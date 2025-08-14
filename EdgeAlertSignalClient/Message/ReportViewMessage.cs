using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Message
{
    public class ReportViewMessage : ValueChangedMessage<string>
    {
        public ReportViewMessage(string value) : base(value)
        {
        }
    }
}

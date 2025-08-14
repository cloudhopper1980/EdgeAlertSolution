using CommunityToolkit.Mvvm.Messaging.Messages;
using EdgeAlertSignalClient.Services;
using System.Collections.ObjectModel;

namespace EdgeAlertSignalClient.Message
{
    public class SystemInfoMessage : ValueChangedMessage<ObservableCollection<SystemInfoService>>
    {
        public SystemInfoMessage(ObservableCollection<SystemInfoService> value) : base(value)
        {
        }
    }
}

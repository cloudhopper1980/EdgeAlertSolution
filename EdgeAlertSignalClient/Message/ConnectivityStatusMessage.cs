using CommunityToolkit.Mvvm.Messaging.Messages;

namespace EdgeAlertSignalClient.Message
{
    public class ConnectivityStatusMessage : ValueChangedMessage<bool>
    {
        public ConnectivityStatusMessage(bool value) : base(value)
        {
        }
    }

}

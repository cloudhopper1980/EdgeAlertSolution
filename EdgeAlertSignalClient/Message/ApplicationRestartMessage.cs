using CommunityToolkit.Mvvm.Messaging.Messages;

namespace EdgeAlertSignalClient.Message
{
    public class ApplicationRestartMessage : ValueChangedMessage<string>
    {
        public ApplicationRestartMessage(string value) : base(value)
        {
        }
    }
}

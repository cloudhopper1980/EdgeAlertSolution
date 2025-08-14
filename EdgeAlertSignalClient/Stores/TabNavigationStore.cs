using CommunityToolkit.Mvvm.ComponentModel;

namespace EdgeAlertSignalClient.Stores
{
    public partial class TabNavigationStore : ObservableObject
    {
        [ObservableProperty]
        public object _currentView;
    }
}

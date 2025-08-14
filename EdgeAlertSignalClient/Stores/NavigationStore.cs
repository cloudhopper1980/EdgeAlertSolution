using CommunityToolkit.Mvvm.ComponentModel;

namespace EdgeAlertSignalClient.Stores
{
    public partial class NavigationStore : ObservableObject
    {
        [ObservableProperty]
        public object _currentView;

        [ObservableProperty]
        public bool _reportChanged;

        public NavigationStore()
        {
        }
    }
}

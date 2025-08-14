using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EdgeAlertSignalClient.Services;
using EdgeAlertSignalClient.Views;
using EdgeAlertSignalClient.Stores;
using System.Windows.Input;

namespace EdgeAlertSignalClient.ViewModels
{
    public partial class AssetManagementTabViewModel : ObservableObject
    {
        private readonly NavigationStore _navigationStore;
        private readonly AssetManagementViewModel _assetManagementViewModel;
        private readonly AssetManagementView _assetManagementView;

        public ICommand ShowAssetManagementCommand { get; }

        public AssetManagementTabViewModel(NavigationStore navigationStore, AssetManagementViewModel assetManagementViewModel)
        {
            _navigationStore = navigationStore;
            _assetManagementViewModel = assetManagementViewModel;
            _assetManagementView = new AssetManagementView(_assetManagementViewModel);
            ShowAssetManagementCommand = new RelayCommand(ShowAssetManagement);
        }

        private void ShowAssetManagement()
        {
            _navigationStore.CurrentView = _assetManagementView;
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using EdgeAlertSignalClient.Models;
using EdgeAlertSignalClient.Services;

namespace EdgeAlertSignalClient.ViewModels
{
    public partial class AssetManagementViewModel : ObservableObject
    {
        private readonly AssetTableService _assetService;

        [ObservableProperty]
        private ObservableCollection<AssetItem> _assets = new();

        [ObservableProperty]
        private AssetItem? _selectedAsset;

        [ObservableProperty]
        private bool _isBusy;

        public AssetManagementViewModel(AssetTableService assetService)
        {
            _assetService = assetService;
            LoadAssetsCommand = new AsyncRelayCommand(LoadAssetsAsync);
        }

        public IAsyncRelayCommand LoadAssetsCommand { get; }

        private async Task LoadAssetsAsync()
        {
            IsBusy = true;
            try
            {
                var items = await _assetService.GetAssetsAsync();
                Assets = new ObservableCollection<AssetItem>(items);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}

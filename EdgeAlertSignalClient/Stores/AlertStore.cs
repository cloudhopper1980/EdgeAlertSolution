using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Stores
{
    public partial class AlertStore : ObservableObject
    {
        [ObservableProperty]
        public bool _alertSet = false;
        [ObservableProperty]
        public bool _alertActive = false;
        [ObservableProperty]
        public bool _responderHasBeenSet = false;
    }
}

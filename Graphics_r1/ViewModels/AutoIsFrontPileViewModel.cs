using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;


using PileDesign.Common;

namespace PileDesign.ViewModels
{
    partial class AutoIsFrontPileViewModel : BaseViewModel
    {
        [ObservableProperty]
        private double _angle = 30;

        [ObservableProperty]
        private ObservableCollection<bool> _isChecked = [true, true, true, true];
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;


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

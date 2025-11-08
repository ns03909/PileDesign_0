using System.Collections.ObjectModel;


namespace PileDesign.ViewModels
{
    class AutoIsFrontPileViewModel : BaseViewModel
    {
        private double _angle = 30;
        public double Angle
        {
            get => _angle;
            set => SetProperty(ref _angle, value);
        }

        private ObservableCollection<bool> isChecked = [true, true, true, true];
        public ObservableCollection<bool> IsChecked
        {
            get => isChecked;
            set => SetProperty(ref isChecked, value);
        }
    }
}

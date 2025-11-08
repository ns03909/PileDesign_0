//using CommunityToolkit.Mvvm.ComponentModel;
//using CommunityToolkit.Mvvm.Input;
//using System.Windows.Input;

//namespace PileDesign.ViewModels
//{
//    public partial class LoadCaseWindowViewModel : BaseViewModel
//    {
//        public LoadCaseViewModel LoadCaseViewModel { get; }

//        public ICommand OkCommand { get; }
//        public ICommand CancelCommand { get; }
//        public ICommand SliderValueChangedCommand { get; }

//        public LoadCaseWindowViewModel(MainWindowViewModel mainWindowViewModel)
//        {
//            // LoadCaseViewModelÇèâä˙âª
//            LoadCaseViewModel = new LoadCaseViewModel(mainWindowViewModel);

//            // ÉRÉ}ÉìÉhÇèâä˙âª
//            OkCommand = new RelayCommand(OnOk);
//            CancelCommand = new RelayCommand(OnCancel);
//            SliderValueChangedCommand = new RelayCommand(SliderValueChanged);
//        }

//        private void OnOk()
//        {
//            LoadCaseViewModel.OnOk();
//        }

//        private void OnCancel()
//        {
//            LoadCaseViewModel.OnCancel();
//        }

//        private void SliderValueChanged()
//        {
//            LoadCaseViewModel.SliderValueChanged();
//        }
//    }
//}
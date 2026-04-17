using CommunityToolkit.Mvvm.ComponentModel;
using PileDesign.Models.InputData;
using System.Collections.ObjectModel;

namespace PileDesign.ViewModels
{
    public partial class EmbedmentViewModel : BaseViewModel
    {
        //public InputModel InputModel => InputModel.Instance;
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        // 根入層
        [ObservableProperty]
        private ObservableCollection<EmbedmentDataItem> _embedmentCollection;

        // 根入層数
        [ObservableProperty]
        private int _embedmentLayersCount;

        // 根入部下端Z
        [ObservableProperty]
        private double _bottomAltitude;

        // EmbedmentCollectionの1行目のBottomAltitudeを同期する
        partial void OnBottomAltitudeChanged(double value)
        {
            if (EmbedmentCollection != null && EmbedmentCollection.Count > 0)
            {
                EmbedmentCollection[0].BottomAltitude = value;
            }
        }

        // 地盤番号
        [ObservableProperty]
        private int _groundNo;

        // コンストラクタ
        public EmbedmentViewModel()
        {
            EmbedmentCollection = [];
            EmbedmentLayersCount = EmbedmentCollection.Count;
            BottomAltitude = 0.00;
            GroundNo = 1;
        }
    }
}

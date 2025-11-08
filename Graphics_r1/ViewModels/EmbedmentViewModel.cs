using PileDesign.Models.InputData;
using System.Collections.ObjectModel;

namespace PileDesign.ViewModels
{
    public class EmbedmentViewModel : BaseViewModel
    {
        //public InputModel InputModel => InputModel.Instance;
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        // 根入層
        private ObservableCollection<EmbedmentDataItem> _embedmentCollection;
        public ObservableCollection<EmbedmentDataItem> EmbedmentCollection
        {
            get => _embedmentCollection;
            set => SetProperty(ref _embedmentCollection, value);
        }

        // 根入層数
        private int _embedmentLayersCount;
        public int EmbedmentLayersCount
        {
            get => _embedmentLayersCount;
            set => SetProperty(ref _embedmentLayersCount, value);
        }


        // 根入部下端Z
        private double _bottomAltitude;
        public double BottomAltitude
        {
            get => _bottomAltitude;
            set
            {
                if (SetProperty(ref _bottomAltitude, value))
                {
                    // EmbedmentCollectionの1行目のBottomAltitudeを更新
                    if (EmbedmentCollection.Count > 0)
                    {
                        EmbedmentCollection[0].BottomAltitude = value;
                    }
                }
            }
        }

        // 地盤番号
        private int groundNo;
        public int GroundNo
        {
            get => groundNo;
            set => SetProperty(ref groundNo, value);
        }

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
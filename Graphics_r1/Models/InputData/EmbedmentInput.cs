using PileDesign.ViewModels;
using System.Collections.ObjectModel;

namespace PileDesign.Models.InputData
{
    public class EmbedmentInput : BaseDataItem
    {
        // 根入れデータ
        private ObservableCollection<EmbedmentDataItem> _embedmentLayers = [];
        public ObservableCollection<EmbedmentDataItem> EmbedmentLayers
        {
            get => _embedmentLayers;
            set => SetProperty(ref _embedmentLayers, value);
        }

        // 根入層数
        private int _embedmentLayersCount = 0;
        public int EmbedmentLayersCount
        {
            get => _embedmentLayersCount;
            set => SetProperty(ref _embedmentLayersCount, value);
        }
        //public int EmbedmentLayersCount => EmbedmentLayers.Count;


        // 下端Z
        private double _bottomAltitude = 0.0;
        public double BottomAltitude
        {
            get => _bottomAltitude;
            set => SetProperty(ref _bottomAltitude, value);
        }

        // 地盤番号
        private int _groundNo = 1;
        public int GroundNo
        {
            get => _groundNo;
            set => SetProperty(ref _groundNo, value);
        }

        /// <summary>
        /// 新しいEmbedmentDataItemを生成するファクトリメソッド
        /// </summary>
        public EmbedmentDataItem CreateNewEmbedmentDataItem(int index)
        {
            EmbedmentDataItem newItem;
            if (EmbedmentLayers.Count > 0 && index > 0)
            {
                var lastItem = EmbedmentLayers[index - 1];
                newItem = new EmbedmentDataItem
                {
                    No = index + 1,
                    LayerThickness = lastItem.LayerThickness,
                    X1 = lastItem.X1,
                    X2 = lastItem.X2,
                    Y1 = lastItem.Y1,
                    Y2 = lastItem.Y2,
                };
            }
            else
            {
                newItem = new EmbedmentDataItem
                {
                    No = index + 1,
                    LayerThickness = 5.0,
                    X1 = 0.0,
                    X2 = 50.0,
                    Y1 = 0.0,
                    Y2 = 50.0,
                };
            }
            return newItem;
        }
    }
}

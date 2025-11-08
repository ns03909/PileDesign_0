using System.Collections.ObjectModel;

namespace PileDesign.Models.InputData
{
    public class SoilEmbedment : BaseModel
    {
        public int GroundNo { get; set; }

        // 根入部上端レベル
        private double _embedmentTopAltitude;
        public double EmbedmentTopAltitude
        {
            get => _embedmentTopAltitude;
            set => SetProperty(ref _embedmentTopAltitude, value);
        }

        // 根入部下端レベル
        private double _embedmentBottomAltitude;
        public double EmbedmentBottomAltitude
        {
            get => _embedmentBottomAltitude;
            set => SetProperty(ref _embedmentBottomAltitude, value);
        }

        // 節点
        private ObservableCollection<EmbedmentZDataItem> _zDataItems;
        public ObservableCollection<EmbedmentZDataItem> ZDataItems
        {
            get => _zDataItems;
            set => SetProperty(ref _zDataItems, value);
        }

        // 土層(総数：節点数-1)
        public ObservableCollection<GroundLayerInput> GroundLayers { get; set; }

        // ★★ ここを追加（パラメータなしコンストラクタ）★★
        public SoilEmbedment() { }

        // コンストラクタ
        public SoilEmbedment(int groundNo, double embedmentTopAltitude, ObservableCollection<EmbedmentZDataItem> zDataItems)
        {
            GroundNo = groundNo;
            EmbedmentTopAltitude = embedmentTopAltitude;
            ZDataItems = zDataItems;
            EmbedmentBottomAltitude = ZDataItems.Count == 0 ? EmbedmentTopAltitude : ZDataItems[^1].Z;
        }

        // 浅いコピーを作成するメソッド
        public SoilEmbedment ShallowCopy()
        {
            return (SoilEmbedment)this.MemberwiseClone();
        }

        // 深いコピーを作成するメソッド
        public SoilEmbedment DeepCopy()
        {
            return (SoilEmbedment)this.MemberwiseClone();
        }
    }
}

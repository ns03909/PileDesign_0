using System.Collections.ObjectModel;
using System.Linq;

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

        /// <summary>
        /// 深いコピー。以前は <c>ShallowCopy()</c> と同じで、節点と土層を元と
        /// 共有していた。<c>GroundLayers</c> は地盤側と同じ実体を指す
        /// (根入部が参照する土層そのもの) ので、要素は写さず入れ物だけ分ける。
        /// </summary>
        public SoilEmbedment DeepCopy()
        {
            var copy = (SoilEmbedment)this.MemberwiseClone();
            copy.ZDataItems = ZDataItems == null
                ? null! : [.. ZDataItems.Select(z => z.DeepCopy())];
            copy.GroundLayers = GroundLayers == null
                ? null! : [.. GroundLayers];
            return copy;
        }
    }
}

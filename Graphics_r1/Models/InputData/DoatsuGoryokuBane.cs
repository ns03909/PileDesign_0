using System.Collections.ObjectModel;

namespace PileDesign.Models.InputData
{
    public class DoatsuGoryokuBane : BaseModel
    {
        private ObservableCollection<DoatsuGoryokuBaneItem> _items = [];
        public ObservableCollection<DoatsuGoryokuBaneItem> Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
        }

        public double DeltaP;
        public double Ysp;

        // DeltaPYspの更新メソッド
        public void UpdateDeltaPYsp(double deltaPonEmbedmentDepth)
        {
            if (Items.Count == 0)
            { return; }
            double embedmentDepth = Items[0].ZTop - Items[^1].ZBtm;
            DeltaP = deltaPonEmbedmentDepth * embedmentDepth;
            Ysp = 0.1 * DeltaP;
            for (int i = 0; i < Items.Count; i++)
            {
                Items[i].SetDeltaPAndYsp(DeltaP, Ysp);
            }
        }

        // 深いコピーを作成するメソッド
        // 以前は MemberwiseClone のみで Items（ObservableCollection）が共有されるバグがあった。
        // JSON 往復で循環参照・ネスト要素ごと独立コピーを作成する。
        public DoatsuGoryokuBane DeepCopy()
        {
            return PileDesign.Common.DeepCopyUtil.CloneJson(this) ?? new DoatsuGoryokuBane();
        }
    }
}


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

        // 地盤反力が上限に達する変位 ΔP と降伏変位 ysp。土圧抵抗ばね図の理論曲線に使う。
        //
        // 以前は公開<b>フィールド</b>だった。保存 (System.Text.Json) はフィールドを書き出さないので、
        // 保存して開き直すと 0 に戻り、理論曲線が描かれなくなっていた。
        // Undo の複製 (DeepCopyUtil は IncludeFields) では写っていたため、
        // 編集中は正しく見えて保存往復でだけ消えた。
        public double DeltaP { get; set; }
        public double Ysp { get; set; }

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


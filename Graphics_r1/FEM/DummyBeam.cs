using System.Collections.ObjectModel;
using System.Linq;

namespace PileDesign.FEM
{
    public class DummyBeam
    {
        // setter は JSON 逆シリアライズ用。
        // get のみ + 引数付きコンストラクタだけだと System.Text.Json はコンストラクタ経由で
        // 復元しようとするが、ReferenceHandler.Preserve の $ref はコンストラクタ引数へ渡せず
        // 「Reference metadata is not supported when deserializing constructor parameters」で失敗する。
        // Node は他所と共有される (= 2 回目以降は $ref になる) ため、必ず setter 経由で復元させる。
        // Beam も同じ理由で既定コンストラクタ + setter の形になっている。
        public string Name { get; set; }
        public Node NodeI { get; set; }
        public Node NodeJ { get; set; }
        public double Length { get; set; }
        public ObservableCollection<DummyBeamResult> DummyBeamResults { get; set; } = [];

        public DummyBeam() { }

        public DummyBeam(string name, Node nodeI, Node nodeJ)
        {
            Name = name;
            NodeI = nodeI;
            NodeJ = nodeJ;
        }

        public DummyBeam DeepCopy()
        {
            // NodeI, NodeJは参照コピー。必要に応じてDeepCopy()に変更してください。
            var copy = new DummyBeam(Name, NodeI, NodeJ)
            {
                // 長さもコピー
                Length = this.Length,
                // DummyBeamResultsは各要素のDeepCopyで新しいコレクションを作成
                DummyBeamResults = new ObservableCollection<DummyBeamResult>(
                    this.DummyBeamResults.Select(r => r.DeepCopy()))
            };
            return copy;
        }


    }
}
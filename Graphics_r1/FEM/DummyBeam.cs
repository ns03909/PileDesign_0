using System.Collections.ObjectModel;
using System.Linq;

namespace PileDesign.FEM
{
    public class DummyBeam
    {
        public string Name { get; }
        public Node NodeI { get; }
        public Node NodeJ { get; }
        public double Length { get; set; }
        public ObservableCollection<DummyBeamResult> DummyBeamResults { get; set; } = [];

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
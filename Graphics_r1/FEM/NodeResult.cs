using PileDesign.Models.InputData;

namespace PileDesign.FEM
{
    //public class NodeResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step, Node node)
    //{
    public class NodeResult
    {
        public LoadCase LoadCase { get; set; }
        public LoadCombination LoadCombination { get; set; }
        public bool IsLiquefaction { get; set; }
        public int Step { get; set; }

        public NodeLoad CumulativedLoad { get; set; }
        public NodeDisp CumulativeDisp { get; set; }
        public NodeReaction CumulativeReaction { get; set; }
        public NodeDisp SoilDisp { get; set; }
        //public LoadCase LoadCase { get; } = loadCase;
        //public LoadCombination LoadCombination { get; } = loadCombination;
        //public bool IsLiquefaction { get; } = isLiquefaction;
        //public int Step { get; } = step;

        //public NodeLoad CumulativedLoad { get; } = node.CumulativedLoad != null ? node.CumulativedLoad.Clone() : null;
        //public NodeDisp CumulativeDisp { get; } = node.CumulativeDisp != null ? node.CumulativeDisp.Clone() : null;
        //public NodeReaction CumulativeReaction { get; } = node.CumulativeReaction != null ? node.CumulativeReaction.Clone() : null;
        //public NodeDisp SoilDisp { get; } = node.SoilDisp != null ? node.SoilDisp.Clone() : null;

        // パラメータなしコンストラクタ（必須）
        public NodeResult() { }
        // 既存の生成用コンストラクタ
        public NodeResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step, Node node)
        {
            LoadCase = loadCase;
            LoadCombination = loadCombination;
            IsLiquefaction = isLiquefaction;
            Step = step;

            CumulativedLoad = node.CumulativedLoad?.Clone();
            CumulativeDisp = node.CumulativeDisp?.Clone();
            CumulativeReaction = node.CumulativeReaction?.Clone();
            SoilDisp = node.SoilDisp?.Clone();
        }

        public NodeResult DeepCopy()
        {
            // node引数は不要なのでnullやダミーでOK

            Node nodeDummy = new()
            {
                CumulativedLoad = this.CumulativedLoad.Clone(),
                CumulativeDisp = this.CumulativeDisp.Clone(),
                CumulativeReaction = this.CumulativeReaction.Clone(),
                SoilDisp = this.SoilDisp.Clone()
            };
            nodeDummy.SetNodeInfo("dummy", 0, 0, 0);

            return new NodeResult(
                this.LoadCase.DeepCopy(),
                this.LoadCombination.DeepCopy(),
                this.IsLiquefaction,
                this.Step,
                nodeDummy

            );
        }
        //{
        //    // 元のNodeをDeepCopyして渡す
        //    return new NodeResult(
        //        this.LoadCase.DeepCopy(),
        //        this.LoadCombination.DeepCopy(),
        //        this.IsLiquefaction,
        //        this.Step,
        //        this.Node.DeepCopy() // Nodeプロパティがある前提
        //    );
        //}
    }
}

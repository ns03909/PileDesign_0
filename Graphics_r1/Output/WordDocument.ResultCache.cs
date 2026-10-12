using PileDesign.FEM;
using PileDesign.Models.InputData;

namespace PileDesign.Output
{
    // 計算書の出力で結果を引く入口。
    //
    // 以前はここに計算書専用の Dictionary (荷重ケースを "L{レベル}-{番号}" の文字列にした鍵) を持っていた。
    // 画面の Beam.GetBeamResult などとは別の書き方で、しかも指定した液状化の状態に結果が無いと
    // 逆の状態の結果を返していた ([液] の行に非液状化の値が出得た)。
    // 結果の索引は FEM の ResultLookup に 1 つにまとめたので、ここはそれを呼ぶだけにする。
    internal partial class WordDocument
    {
        /// <summary>
        /// 出力の前に索引を作っておく。グラフ・表は Parallel.For から引くので、
        /// 各スレッドが同時に同じ索引を作り始めないよう先に作る (作り直しが重なっても結果は同じ)。
        /// </summary>
        private void BuildResultLookupCaches()
        {
            if (anaModel == null) return;
            anaModel.StepIndex.EnsureBuilt();
            if (anaModel.Beams != null) foreach (var beam in anaModel.Beams) beam?.ResultLookup.EnsureBuilt();
            if (anaModel.Nodes != null) foreach (var node in anaModel.Nodes) node?.ResultLookup.EnsureBuilt();
        }

        /// <summary>指定した荷重条件の最終ステップ。結果が無ければ -1。</summary>
        private int GetLastStepCached(LoadCase? lc, LoadCombination? comb, bool isLiq)
            => anaModel?.StepIndex.Latest(lc, comb, isLiq)?.Step ?? -1;

        private BeamResult? GetBeamResultCached(Beam beam, LoadCase? lc, LoadCombination? comb, bool isLiq, int step = -1)
        {
            if (step == -1) step = GetLastStepCached(lc, comb, isLiq);
            if (step < 0) return null;
            return beam.ResultLookup.At(lc, comb, isLiq, step);
        }

        private NodeResult? GetNodeResultCached(Node node, LoadCase? lc, LoadCombination? comb, bool isLiq, int step = -1)
        {
            if (step == -1) step = GetLastStepCached(lc, comb, isLiq);
            if (step < 0) return null;
            return node.ResultLookup.At(lc, comb, isLiq, step);
        }
    }
}

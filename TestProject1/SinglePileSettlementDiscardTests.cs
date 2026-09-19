using Microsoft.VisualStudio.TestTools.UnitTesting;
using MathNet.Numerics.LinearAlgebra;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 「沈下解析結果が削除されます」で、<b>単杭沈下の結果も消えること</b>。
    ///
    /// <para>2026-09-20 まで消えていたのは群杭沈下の記録だけで、土層-杭セットが持つ
    /// 荷重-沈下曲線と節点別の履歴は残っていた。画面・計算書・グラフは
    /// <c>IsVerticalAnalysisDone</c> が false になるので「未実行」と扱うのに、
    /// 解析の入口は残った値をそのまま使っていた。</para>
    ///
    /// <list type="bullet">
    /// <item>水平解析の杭節点 Z ばね (P-S ばね) は節点別履歴の有無だけを見る</item>
    /// <item>基礎梁考慮沈下の杭頭ばねは曲線の有無だけを見る</item>
    /// <item>保存して開き直すと、曲線の有無から「実行済み」に戻る</item>
    /// </list>
    ///
    /// <para>曲線の置き場所は <see cref="SoilPile"/> のままでよい (結果でありながら次の解析の
    /// 入力でもある)。置き場所が入力側にあることと、消さなくてよいことは別の話。</para>
    /// </summary>
    [TestClass]
    public class SinglePileSettlementDiscardTests
    {
        /// <summary>単杭沈下を終えた状態の土層-杭セットを 1 つ持つ入力を作る。</summary>
        private static (MainWindowViewModel vm, InputModel input, SoilPile soilPile) Build()
        {
            var input = new InputModel();
            input.ElementDivision ??= new ElementDivision();
            var sp = new SoilPile { GroundNo = 1, PileBodyNo = 1, Z = 0.0 };

            // 荷重-沈下曲線 (基礎梁考慮沈下の杭頭ばねが読む)
            sp.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 0, DD0s = 0 });
            sp.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 1000, DD0s = 5 });
            sp.LoadDisplacementsLimit.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 1500, DD0s = 20 });

            // 節点別の履歴 (水平解析の杭節点 Z ばねが読む)
            sp.NodeDisplacements = [Vector<double>.Build.Dense(4, 0.0), Vector<double>.Build.Dense(4, 0.001)];
            sp.NodeReactions = [Vector<double>.Build.Dense(4, 0.0), Vector<double>.Build.Dense(4, 100.0)];

            input.ElementDivision.SoilPiles.Add(sp);

            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);
            vm.IsVerticalAnalysisDone = true;
            return (vm, input, sp);
        }

        /// <summary>破棄の対象として数えられること (単杭沈下だけでも確認ダイアログが出る前提)。</summary>
        [TestMethod]
        public void TheSinglePileResultCountsAsSomethingToDiscard()
        {
            var (vm, _, _) = Build();
            Assert.IsTrue(vm.HasSettlementResultsForTest(), "単杭沈下だけでも破棄対象になること");
        }

        /// <summary>
        /// 破棄で曲線が消えること。残っていると基礎梁考慮沈下の杭頭ばねが古い曲線を使う。
        /// </summary>
        [TestMethod]
        public void DiscardingClearsTheLoadDisplacementCurves()
        {
            var (vm, _, sp) = Build();

            vm.ClearSettlementResultsForTest();

            Assert.AreEqual(0, sp.LoadDisplacements.Count, "荷重-沈下曲線が残っている");
            Assert.AreEqual(0, sp.LoadDisplacementsLimit.Count, "極限の曲線が残っている");
        }

        /// <summary>
        /// 破棄で節点別の履歴が消えること。残っていると水平解析の杭節点 Z ばねが古い履歴を使う
        /// (<c>ShouldApplyVerticalSpringsToPile</c> は履歴の有無しか見ない)。
        /// </summary>
        [TestMethod]
        public void DiscardingClearsThePerNodeHistory()
        {
            var (vm, _, sp) = Build();

            vm.ClearSettlementResultsForTest();

            Assert.AreEqual(0, sp.NodeDisplacements.Count, "節点変位の履歴が残っている");
            Assert.AreEqual(0, sp.NodeReactions.Count, "節点反力の履歴が残っている");
        }

        /// <summary>
        /// 破棄したら、保存ファイルの単杭沈下の節も空になること。
        /// 残ると開き直したときに「実行済み」として復活する。
        /// </summary>
        [TestMethod]
        public void DiscardingLeavesNothingForTheFileToCarry()
        {
            var (vm, input, _) = Build();

            Assert.IsNotNull(SinglePileSettlementResult.Capture(input), "前提: 保存する曲線がある");

            vm.ClearSettlementResultsForTest();

            Assert.IsNull(SinglePileSettlementResult.Capture(input),
                "曲線が保存ファイルに残る (開き直すと単杭沈下が実行済みに戻る)");
        }

        /// <summary>破棄したあとは、もう破棄対象が無いと答えること (旗と中身が揃っていること)。</summary>
        [TestMethod]
        public void AfterDiscardingNothingIsLeftToDiscard()
        {
            var (vm, _, _) = Build();

            vm.ClearSettlementResultsForTest();

            Assert.IsFalse(vm.IsVerticalAnalysisDone, "旗が立ったまま");
            Assert.IsFalse(vm.HasSettlementResultsForTest(), "まだ破棄対象が残っている");
        }

        /// <summary>
        /// 節点別の履歴は、空のリストを<b>入れ直して</b>消すこと (<c>Clear()</c> ではない)。
        ///
        /// <c>SoilPile.DeepCopy</c> は節点別履歴のリストを写し先と共有するので、
        /// その場で <c>Clear()</c> すると写し先 (スナップショット) の履歴まで消える。
        /// 呼び出し側は現在の入力とスナップショットの両方に対して破棄を走らせるので、
        /// 入れ直しであれば片方ずつ確実に消える。
        /// </summary>
        [TestMethod]
        public void ClearingReplacesTheListInsteadOfEmptyingItInPlace()
        {
            var (vm, _, sp) = Build();
            var sharedList = sp.NodeDisplacements;   // 写し先が共有しているつもりのリスト

            vm.ClearSettlementResultsForTest();

            Assert.AreEqual(0, sp.NodeDisplacements.Count, "こちらは消えていること");
            Assert.AreEqual(2, sharedList.Count,
                "共有しているリストの中身まで消えている (Clear() ではなく入れ直しであること)");
            Assert.AreNotSame(sharedList, sp.NodeDisplacements);
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.ViewModels;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 解析結果の組 (控え) を、水平解析をやり直さずに取り直しても、控えの杭と解析の要素のつながりが残ること (2026-11-04)。
    ///
    /// <para>1 回目に組を取ると、表示中の解析結果 (CurrentModel) は控えの複製に切り替わるが、編集中の入力の杭は
    /// 元の解析モデルの要素を指したまま。沈下解析などで組を取り直すと、つながりを編集中の入力から写していたので、
    /// 控えの複製の中に 1 つも見つからず、控えの杭の要素 (Beams) が全部空になった。グラフ (杭応力の深さ分布) が
    /// Beams[0] で範囲外の例外を出して止まり、利用者が Visual Studio のデバッグ実行で遭遇した。</para>
    /// </summary>
    [TestClass]
    public class ResultSetRecaptureTests
    {
        [TestMethod]
        public void RecapturingWithoutANewAnalysis_KeepsThePileElementLinks()
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);
            var modelling = new AnalysisModelling(input);
            vm.CurrentModel = new AnaModel(
                input, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            vm.IsHorizontalAnalysisDone = true;

            vm.CaptureAnalysisResultSet();
            var first = vm.ResultInputModel.PileLayoutItems.Select(p => p.Beams?.Count ?? 0).ToList();
            Assert.IsTrue(vm.HasAnalysisResultSet, "(前提) 組を取れていない");
            Assert.IsTrue(first.All(n => n > 0), "(前提) 1 回目の組で杭の要素がつながっていない");

            // 沈下解析などで組を取り直す (水平解析はやり直さない)
            vm.CaptureAnalysisResultSet();
            var second = vm.ResultInputModel.PileLayoutItems.Select(p => p.Beams?.Count ?? 0).ToList();
            CollectionAssert.AreEqual(first, second, "組を取り直したら控えの杭と解析の要素のつながりが消えた");

            // つながった先は、表示中の解析結果の要素 (控えの複製) であること
            var model = vm.CurrentModel;
            Assert.IsTrue(vm.ResultInputModel.PileLayoutItems.SelectMany(p => p.Beams).All(b => model.Beams.Contains(b)),
                "控えの杭が、表示中の解析結果ではない要素を指している");
        }
    }
}

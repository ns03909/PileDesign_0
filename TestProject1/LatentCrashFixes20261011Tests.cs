using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// Null 許容の警告を片付ける途中で見つかった、落ちうる箇所の修正 (2026-10-11)。
    /// </summary>
    [TestClass]
    public class LatentCrashFixes20261011Tests
    {
        /// <summary>
        /// 土圧合力ばねは、根入部と区間の両方があるときだけ解析モデルに入れる。モデルを組む側と剛性を入れる側が
        /// 同じ条件 (ElementDivision.HasEarthPressureSprings) を使う。
        /// </summary>
        [TestMethod]
        public void EarthPressureSprings_NeedBothTheEmbedmentAndTheSegments()
        {
            var division = new ElementDivision { DoatsuGoryokuBane = new DoatsuGoryokuBane(), SoilEmbedment = null };
            Assert.IsFalse(division.HasEarthPressureSprings(), "根入部が無いのに土圧合力ばねを入れようとしています");

            division.DoatsuGoryokuBane = null;
            division.SoilEmbedment = new SoilEmbedment(1, 0.0, []);
            Assert.IsFalse(division.HasEarthPressureSprings());

            division.DoatsuGoryokuBane = new DoatsuGoryokuBane();
            Assert.IsTrue(division.HasEarthPressureSprings());

            string modelling = TestSource.Read("Graphics_r1", "FEM", "AnalysisModelling.cs");
            string solver = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Solver.cs");
            StringAssert.Contains(TestSource.MethodBody(modelling, "private void AddDoatsuGoryokuBane()"), "HasEarthPressureSprings()",
                "解析モデルを組む側が、土圧合力ばねの条件を自前で書いています");
            StringAssert.Contains(TestSource.MethodBody(solver, "private void PrepareKmat("), "HasEarthPressureSprings()",
                "剛性を入れる側が、土圧合力ばねの条件を自前で書いています (組む側と食い違うと、節点の無い区間を読んで落ちます)");
        }

        /// <summary>
        /// 杭体の写しは、失敗を握り潰して null を返さない (例外のまま知らせる)。以前は null を返し、呼び出し側
        /// (元に戻す操作の控え・履歴の写し) が杭体の一覧へそのまま入れて、戻したときに別の場所で落ちた。
        /// </summary>
        [TestMethod]
        public void PileBodyCopy_DoesNotSwallowFailures()
        {
            string src = TestSource.Read("Graphics_r1", "Models", "InputData", "PileBodyInput.cs");
            int at = src.IndexOf("public PileBodyInput DeepCopy()", System.StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, "PileBodyInput.DeepCopy が見つかりません");
            int end = src.IndexOf("return copy;", at, System.StringComparison.Ordinal);
            Assert.IsTrue(end > at, "PileBodyInput.DeepCopy の本体が見つかりません");
            // 本体の後ろ (次のメンバーの手前まで) に、例外を受けて null を返す形が無いこと
            int next = src.IndexOf("/// <summary>", end, System.StringComparison.Ordinal);
            string tail = src.Substring(end, (next > end ? next : src.Length) - end);
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(tail, @"catch\s*\(|return\s+null"),
                "杭体の写しが失敗を握り潰して null を返しています");

            // 通常の写しは今までどおり作れる
            var (model, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive("例題を組めません"); return; }
            var body = model.PileBodies[0];
            var copy = body.DeepCopy();
            Assert.AreNotSame(body, copy);
            Assert.AreEqual(body.PileBodySegments.Count, copy.PileBodySegments.Count);
        }

        /// <summary>
        /// 根入部だけが無く土圧合力ばねの区間が残っている入力 (群杭沈下の計算例の読込が根入部だけを外す) でも、
        /// 水平解析が最後まで走る。以前は剛性を入れる側が区間だけを見て、節点の作られていない区間の節点を読んで落ちた。
        /// </summary>
        [TestMethod]
        public void HorizontalAnalysis_RunsWithSegmentsButNoEmbedment()
        {
            int segments = 0;
            var vm = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9",
                new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 2,
                    Level2Steps = 2,
                    Customize = input =>
                    {
                        // 区間は要素分割の画面で作るので、読込直後には無い。根入れの範囲に 2 区間を置く
                        var bane = input.ElementDivision.DoatsuGoryokuBane ??= new DoatsuGoryokuBane();
                        if (bane.Items.Count == 0)
                        {
                            bane.Items.Add(new DoatsuGoryokuBaneItem
                            {
                                Gamma = 18, Phi = 30, C = 0, ZTop = 0.0, ZBtm = -1.0, K0 = 0.5,
                                StressTop = 0, StressBtm = 18, X1 = -1, X2 = 1, Y1 = -1, Y2 = 1,
                            });
                            bane.Items.Add(new DoatsuGoryokuBaneItem
                            {
                                Gamma = 18, Phi = 30, C = 0, ZTop = -1.0, ZBtm = -2.0, K0 = 0.5,
                                StressTop = 18, StressBtm = 36, X1 = -1, X2 = 1, Y1 = -1, Y2 = 1,
                            });
                        }
                        segments = bane.Items.Count;
                        input.ElementDivision.SoilEmbedment = null;
                    },
                });

            if (segments == 0) { Assert.Inconclusive("例題に土圧合力ばねの区間がありません"); return; }
            Assert.IsNotNull(vm.CurrentModel, "根入部が無く区間だけがある入力で、水平解析が最後まで走りませんでした");
            // 解析中の例外は解析の側で受け止められ、モデルだけは返る。結果があることまで見る
            Assert.IsTrue(vm.CurrentModel!.Nodes.Any(n => n.NodeResults.Count > 0),
                "根入部が無く区間だけがある入力で、水平解析の結果がありません (途中で止まりました)");
            Assert.IsTrue(vm.CurrentModel!.Nodes.All(n => n.Name != "根入部節点"),
                "根入部が無いのに土圧合力ばねの節点を作っています");
        }
    }
}

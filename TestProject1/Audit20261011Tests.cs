using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-11 の横断監査 (画面の確定・取り消し、検定の欠け、残差の NaN、写しの漏れ) で直したものの再発防止。
    /// </summary>
    [TestClass]
    public class Audit20261011Tests
    {
        // ── 解析 ─────────────────────────────

        /// <summary>
        /// 残差の比に NaN・∞ が混じったら「収束していない」(大きな値) を返す。
        /// 以前は Math.Max(x, NaN) = NaN のあと比較がどちらも偽になり、0 (= 収束) を返していた。
        /// </summary>
        [TestMethod]
        public void ANonFiniteResidual_IsNotConvergence()
        {
            foreach (var mode in Enum.GetValues<ResidualReferenceMode>())
            {
                Assert.IsTrue(AnaModel.ResidualRatio(mode, double.NaN, 1.0, 1.0, 1.0) > 1.0, $"{mode}: 残差が NaN で収束扱い");
                Assert.IsTrue(AnaModel.ResidualRatio(mode, double.NaN, 1.0, double.NaN, 1.0) > 1.0, $"{mode}: 反力が NaN で収束扱い");
                Assert.IsTrue(AnaModel.ResidualRatio(mode, 1.0, double.PositiveInfinity, 1.0, 1.0) > 1.0, $"{mode}: 外力が ∞ で収束扱い");
            }
            Assert.IsTrue(AnaModel.ResidualRatio(ResidualReferenceMode.InternalForce, 1e-8, 1.0, 1.0, double.NaN) > 1.0,
                "内力の大きさが NaN で収束扱い");
            // 有限の値は今までどおり
            Assert.AreEqual(1e-8, AnaModel.ResidualRatio(ResidualReferenceMode.ExternalForceOrReaction, 1e-8, 1.0, 0.5, 0.0), 1e-20);
        }

        // ── 写し・保存 ─────────────────────────────

        /// <summary>
        /// 土層-杭セットの写しが「既定のプリセットを当て終えた」印を写す。写し忘れていたので、単杭沈下の画面を開くたびに
        /// 利用者が入れた α・n が既定のプリセットで上書きされていた。
        /// </summary>
        [TestMethod]
        public void SoilPileCopy_KeepsThePresetInitializedFlag()
        {
            var (model, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive("例題を組めません"); return; }
            var sp = model.ElementDivision.SoilPiles[0];
            sp.IsSettlementPresetInitialized = true;
            Assert.IsTrue(sp.DeepCopy().IsSettlementPresetInitialized, "写しで印が消え、開くたびにプリセットで α・n が上書きされます");
        }

        /// <summary>
        /// 矩形荷重は JSON の往復 (保存・入力の写し・解析結果の控え) で座標が変わらない。
        /// 以前は保存される幅 DX・DY のセッターが中心から座標を引き直し、0.1 が 0.09999999999999998 になっていた。
        /// </summary>
        [TestMethod]
        public void RectLoad_SurvivesAJsonRoundTripExactly()
        {
            var rnd = new Random(20261011);
            int checkedLoads = 0;
            for (int i = 0; i < 200; i++)
            {
                double x1 = Math.Round(rnd.NextDouble() * 20 - 10, 3), y1 = Math.Round(rnd.NextDouble() * 20 - 10, 3);
                var load = new RectLoad
                {
                    X1 = x1, X2 = x1 + Math.Round(0.1 + rnd.NextDouble() * 5, 3),
                    Y1 = y1, Y2 = y1 + Math.Round(0.1 + rnd.NextDouble() * 5, 3),
                    QA = 100,
                };
                var copy = PileDesign.Common.DeepCopyUtil.CloneJson(load)!;
                Assert.AreEqual(load.X1, copy.X1, 0.0, $"X1 が往復で変わりました ({load.X1:R} → {copy.X1:R})");
                Assert.AreEqual(load.X2, copy.X2, 0.0);
                Assert.AreEqual(load.Y1, copy.Y1, 0.0, $"Y1 が往復で変わりました ({load.Y1:R} → {copy.Y1:R})");
                Assert.AreEqual(load.Y2, copy.Y2, 0.0);
                checkedLoads++;
            }
            TestSource.AssertScanned(checkedLoads, 200, "矩形荷重");
        }

        /// <summary>
        /// 解析結果の控えへ、単杭沈下の各荷重段階の全節点の変位・反力も写す。写していなかったので、単杭沈下を
        /// 実行した直後でも結果の図 (単杭の反力・基礎梁考慮の反力・単杭の変形) が空になり、変形が 0 で描かれていた。
        /// </summary>
        [TestMethod]
        public void ResultSnapshot_CarriesTheSinglePileNodeHistories()
        {
            var (live, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (live == null) { Assert.Inconclusive("例題を組めません"); return; }
            var sp = live.ElementDivision.SoilPiles[0];
            var vtm = new VerticalLoadTransferMethod(live, sp);
            sp.LoadDisplacements = vtm.LoadDisplacements;
            sp.NodeDisplacements = vtm.Ds;
            sp.NodeReactions = vtm.Rs;
            Assert.IsTrue(sp.NodeDisplacements is { Count: > 0 }, "(前提) 単杭沈下の節点の履歴があること");

            var set = PileDesign.Models.AnalysisResultSet.Capture(live, null, null, false, true, false, false, true);
            Assert.IsNotNull(set);
            var snap = set!.InputSnapshot.ElementDivision.SoilPiles[0];
            Assert.IsNotNull(snap.NodeDisplacements, "解析結果の控えに、単杭沈下の節点の変位が写っていません");
            Assert.IsNotNull(snap.NodeReactions, "解析結果の控えに、単杭沈下の節点の反力が写っていません");
            Assert.IsNotNull(snap.GetFullDisplacementForLoad(sp.LoadDisplacements[^1].PileTopLoad), "控えから節点の変位を引けません");
        }

        // ── 検定・計算書 ─────────────────────────────

        /// <summary>
        /// 水平解析の杭頭変形角は、杭頭の変位が得られない杭が 1 本でもあれば判定しない (沈下による変形角と同じ)。
        /// 以前は残りの杭で OK / NG を出していた。
        /// </summary>
        [TestMethod]
        public void HorizontalDeformationAngle_DoesNotDropPilesSilently()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "EvaluationService.cs");
            string body = TestSource.MethodBody(src, "private List<EvaluationItem> CheckPileHeadDeformationAngle(");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(body, @"if \(head == null\) continue;|if \(nr\?\.CumulativeDisp == null\) continue;"),
                "杭頭の変位が得られない杭を黙って除いています");
            StringAssert.Contains(body, "missing.Count > 0", "欠けた杭があるときに判定をやめていません");
        }

        /// <summary>
        /// FT-Pile の許容回転角が 0 以下・数でない・諸元が無いときも、回転角の項目を消さずに検定不能として出す。
        /// 以前は null (規定なし) を返して項目ごと消え、FT キャップが圧縮で耐力を超えるときに限って検定が出なかった。
        /// </summary>
        [TestMethod]
        public void FtPileRotation_IsNotDroppedWhenThetaAIsNotPositive()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "EvaluationService.cs");
            int ft = src.IndexOf("if (top.Contains(\"FT-Pile構法\"))", StringComparison.Ordinal);
            Assert.IsTrue(ft > 0, "FT-Pile の照査条件が見つかりません");
            string block = src.Substring(ft, src.IndexOf("// ── 杭頭固定 ──", ft, StringComparison.Ordinal) - ft);
            Assert.IsFalse(block.Contains("if (!(thetaA > 0) || !double.IsFinite(thetaA)) return null;", StringComparison.Ordinal),
                "許容回転角が 0 以下のとき、回転角の項目を消しています");
            Assert.IsTrue(System.Text.RegularExpressions.Regex.Matches(block, @"new RotationCriterion\(double\.NaN").Count >= 3,
                "諸元が無い・θa が数でない・θa が 0 以下のそれぞれで、検定不能の照査条件を返していません");
            StringAssert.Contains(src, "criterion.UnavailableReason != null", "限界値を決められない照査条件を検定不能として出していません");
        }

        /// <summary>
        /// 計算書の水平解析の検定で長期の項目を省くときも、NG・未収束・検定不能の長期の項目は省かない。
        /// 以前はまとめて省いてから「NG 項目なし」と書き、画面の総括と食い違うことがあった。
        /// </summary>
        [TestMethod]
        public void TheReport_OmitsOnlyLongTermItemsThatAreOk()
        {
            string src = TestSource.Read("Graphics_r1", "Output", "WordDocument.SummaryTables.cs");
            StringAssert.Contains(src, "IsLongTermSectionCheck(i) && i.IsJudged && i.IsOk",
                "長期の項目を省くときに、OK のものだけに絞っていません");
        }

        // ── 画面 ─────────────────────────────

        /// <summary>解析中・取り消し・失敗のあとに確定しない、閉じたら解析を止める (群杭沈下 基礎梁あり・単杭沈下 基礎梁考慮・水平解析)。</summary>
        [TestMethod]
        public void AnalysisWindows_DoNotCommitWhileRunning_AndStopOnClose()
        {
            string group = TestSource.Read("Graphics_r1", "ViewModels", "GroupSettlementWithBeamCalculationViewModel.cs");
            StringAssert.Contains(group, "RelayCommand(_ => SaveAndClose(), _ => CanSave)", "群杭沈下 (基礎梁あり) の保存が解析中にも実行できます (Ctrl+Enter)");
            StringAssert.Contains(TestSource.MethodBody(group, "private void SaveAndClose()"), "if (!CanSave) return;");
            StringAssert.Contains(TestSource.MethodBody(group, "private void DiscardAndClose()"), "CancelRunningAnalysis();");

            string vb = TestSource.Read("Graphics_r1", "ViewModels", "VerticalBeamCalculationViewModel.cs");
            StringAssert.Contains(TestSource.MethodBody(vb, "private void DiscardAndClose()"), "CancelRunningAnalysis();");
            int done = vb.IndexOf("IsAnalysisExecuted = true;", StringComparison.Ordinal);
            int check = vb.LastIndexOf("token.ThrowIfCancellationRequested();", done, StringComparison.Ordinal);
            int run = vb.LastIndexOf("await RunAsync(settings, token);", done, StringComparison.Ordinal);
            Assert.IsTrue(run > 0 && check > run, "単杭沈下 (基礎梁考慮) で、最後の反復の途中で取り消しても完了にしています");

            foreach (var (file, vm) in new[] { ("VerticalBeamCalculationWindow.xaml.cs", "VerticalBeamCalculationViewModel"),
                                                ("GroupSettlementWithBeamWindow.xaml.cs", "GroupSettlementWithBeamCalculationViewModel") })
                StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", file), $"Closing += (_, _) => (DataContext as ViewModels.{vm})?.CancelRunningAnalysis();",
                    $"{file}: × ボタンで閉じても解析が止まりません");

            string hz = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs");
            string ok = TestSource.MethodBody(hz, "private void OnOk()");
            int running = ok.IndexOf("if (IsAnalysisRunning)", StringComparison.Ordinal);
            Assert.IsTrue(running >= 0 && running < ok.IndexOf("CurrentModel", StringComparison.Ordinal),
                "水平解析で、追加実行の途中に OK で確定できます");
        }
    }
}

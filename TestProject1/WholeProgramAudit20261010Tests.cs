using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-10 のプログラム全体の見直し (第 4 回) で直した、解析の数値を変えない不具合の再発防止。
    /// </summary>
    [TestClass]
    public class WholeProgramAudit20261010Tests
    {
        /// <summary>
        /// 基礎梁の鉛直解析のモデルは、基礎梁につながっていない一般節点を入れない (水平解析のモデルと同じ)。
        /// 入れると剛性の入らない自由な節点が残り、剛性行列が特異になって求解に失敗した。
        /// </summary>
        [TestMethod]
        public void VerticalBeamModel_LeavesOutUnconnectedGeneralNodes()
        {
            var piles = Enumerable.Range(0, 2).Select(i => new PileLayoutDataItem { No = i + 1, X = i * 5.0, Y = 0, Z = 0 }).ToArray();
            var input = new InputModel
            {
                PileLayoutItems = new ObservableCollection<PileLayoutDataItem>(piles),
                FoundationBeamInput = new FoundationBeamInput
                {
                    Materials = [new BeamMaterial { YoungModulus = 2.5e7, PoissonRatio = 0.2 }],
                    Sections = [],
                    Beams = [],
                },
                InputNodes = [new InputNode { No = 7, Type = NodeType.General, X = 20, Y = 20, Z = 0 }],
                FundamentalInput = new FundamentalInput(),
            };
            input.FoundationBeamInput.Beams.Add(new FoundationBeam
            {
                NodeI_Type = NodeReferenceType.PileLayout, NodeI_Id = piles[0].UniqueId,
                NodeJ_Type = NodeReferenceType.PileLayout, NodeJ_Id = piles[1].UniqueId,
                MaterialNo = 1, Width = 0.5, Height = 1.0, YoungModulus = 2.5e7,
            });
            Assert.AreEqual(1, input.GetUnconnectedGeneralNodes().Count, "前提: 一般節点 7 は基礎梁につながっていない");

            var modelling = new VerticalBeamModelling(input);
            Assert.IsFalse(modelling.Nodes.Any(n => n.Name == "InputNode-7"),
                "基礎梁につながっていない一般節点が鉛直解析のモデルに入っています (剛性行列が特異になる)");
            Assert.IsTrue(modelling.Nodes.Count > 0, "前提: モデルが組めている");
        }

        /// <summary>
        /// 解析結果の控えは、結果や入力に NaN があっても作れる (保存・DeepCopy と同じく NaN・∞ を書く)。
        /// 以前は書き出しが例外になって控えを作れず、結果が編集中の入力で描かれ、編集すると計算書を出せなくなった。
        /// </summary>
        [TestMethod]
        public void ResultSnapshot_IsCapturedEvenWithNaN()
        {
            var input = new InputModel
            {
                PileLayoutItems = [new PileLayoutDataItem { No = 1, PileNo = 1, SinglePileSettlementVL = double.NaN }],
            };
            var set = PileDesign.Models.AnalysisResultSet.Capture(input, null, null,
                hasHorizontal: false, hasVertical: true, hasGroupPileSettlement: false, hasVerticalBeam: false,
                isElementSplit: false, out var failure);
            Assert.IsNotNull(set, $"NaN を含むと解析結果の控えを作れません: {failure?.Message}");
            Assert.IsTrue(double.IsNaN(set.InputSnapshot.PileLayoutItems[0].SinglePileSettlementVL), "NaN が控えに残っていません");
        }

        /// <summary>
        /// 解く荷重ケースの判定 (慣性力 0 でも地盤変位を与えるなら解く) を、ケースループと件数の数え方で共有する。
        /// 以前は数える側が慣性力だけを見ており、地盤変位だけのケースしかないと実行できなかった。
        /// </summary>
        [TestMethod]
        public void CaseCounting_SharesTheLoopsSkipRule()
        {
            string vm = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs");
            string run = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs");
            Assert.IsFalse(vm.Contains("x.UpperMassForce != 0 || x.FoundationMassForce != 0", System.StringComparison.Ordinal),
                "件数の数え方が慣性力だけで解くケースを決めています");
            Assert.IsFalse(vm.Contains("lc.UpperMassForce == 0 && lc.FoundationMassForce == 0", System.StringComparison.Ordinal),
                "追加実行の未計算件数が慣性力だけで解くケースを決めています");
            StringAssert.Contains(run, "!HasLoadToRun(loadCase, null)");
            StringAssert.Contains(run, "!HasLoadToRun(loadCase, loadCombination)");
            foreach (var sig in new[] { "public int TotalCalculationCount", "public int TotalLoadCaseCount" })
                StringAssert.Contains(TestSource.Region(vm, sig, 1500), "CountCaseCombinationsToRun(", $"{sig} が共通の判定を使っていません");
        }

        /// <summary>計算書の水平解析サマリーは、OK で登録した解析のもの (完了しただけの解析で書き換えない)。</summary>
        [TestMethod]
        public void AnalysisSummaryText_IsPublishedOnlyWhenTheResultIsRegistered()
        {
            string summary = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Summary.cs");
            Assert.IsFalse(summary.Contains("DocxOutput.LastAnalysisSummaryText = summaryText", System.StringComparison.Ordinal),
                "解析が完了しただけで計算書のサマリーを書き換えています");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs"),
                "vm.DocxOutput.LastAnalysisSummaryText = LastRunSummaryText;");
        }

        /// <summary>計算書出力ウィンドウの「解析済み」は、荷重ケース・組合せを番号で照合する (名前は係数や改名で変わる)。</summary>
        [TestMethod]
        public void DocxAnalyzedFlags_MatchByNumber()
        {
            string body = TestSource.MethodBody(
                TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.ResultsWindows.cs"),
                "private void UpdateDocxOutputAnalyzedFlags()");
            Assert.IsFalse(body.Contains(".Name)", System.StringComparison.Ordinal) || body.Contains("LoadName)", System.StringComparison.Ordinal),
                "解析済みの照合に名前を使っています");
            StringAssert.Contains(body, "analyzedCombNos.Contains(comb.No)");
        }

        /// <summary>
        /// P-S ばねの出所の設定は複製で落ちない (保存ファイルの値を消さない)。
        /// 解析はこの設定を使わないので、計算書は「常時」「極限」を書き分けない。
        /// </summary>
        [TestMethod]
        public void PsSpringSource_SurvivesDeepCopy_AndTheReportDoesNotClaimIt()
        {
            var input = new InputModel { PsSpringSource = PsSpringSourceMode.Ultimate, UsePsSpringAtPileTip = true };
            Assert.AreEqual(PsSpringSourceMode.Ultimate, input.CopyForHistory().PsSpringSource, "複製で既定へ落ちています");
            var row = PileDesign.Output.WordDocument.BuildDesignConditionRows(input).Single(r => r.Item == "杭先端の鉛直ばね");
            Assert.IsFalse(row.Value.Contains("極限", System.StringComparison.Ordinal), "解析が使っていない「極限」を計算書に書いています");
        }

        // ── 2026-10-10 利用者の指示で足した設定・扱い ─────────────────

        /// <summary>
        /// 単杭沈下 (基礎梁考慮) の杭ばねは、浮き上がる (沈下が負) と単杭沈下解析の引抜き側の曲線で抵抗する。
        /// 以前は押込みの初期剛性で抵抗し、報告する反力は 0 だった (要素の内力と反力が食い違った)。
        /// </summary>
        [TestMethod]
        public void VerticalPileSpring_UsesTheTensionSideWhenUplifted()
        {
            static PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement P(double dd0s_mm, double load_kN)
                => new() { DD0s = dd0s_mm, PileTopLoad = load_kN };
            var curve = new VerticalPileSpringCurve(new ObservableCollection<PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement>
            {
                P(0, 0), P(1, 100), P(2, 180), P(-1, -50), P(-3, -80),
            });

            // 押込み側は従来どおり
            Assert.AreEqual(100.0, curve.GetForce(0.001), 1e-9);
            Assert.AreEqual(100_000.0, curve.InitialTangentStiffness, 1e-6);

            // 引抜き側: 曲線の上で補間 (浮き上がり 2 mm → 50 + 0.5×30 = 65 kN の引張)
            Assert.AreEqual(-65.0, curve.GetForce(-0.002), 1e-9, "浮き上がったときの反力が引抜き側の曲線になっていません");
            Assert.AreEqual(15_000.0, curve.GetTangentStiffness(-0.002), 1e-6, "引抜き側の接線剛性 (30 kN / 2 mm) ではありません");
            Assert.AreEqual(65.0 / 0.002, curve.GetSecantStiffness(-0.002), 1e-6);
            Assert.AreEqual(curve.GetSecantStiffness(-0.002) * -0.002, curve.GetForce(-0.002), 1e-9,
                "要素の内力 (割線 × 変位) と報告する反力が食い違っています");
            Assert.AreNotEqual(curve.InitialTangentStiffness, curve.GetTangentStiffness(-0.0005), "浮き上がりに押込みの初期剛性を使っています");

            // 引抜き抵抗の限界を超えたら力は一定
            Assert.AreEqual(-80.0, curve.GetForce(-0.010), 1e-6);

            // 引抜き側の点が無い (引抜き抵抗なし) なら、ほとんど抵抗しない
            var noTension = new VerticalPileSpringCurve(new ObservableCollection<PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement>
            {
                P(0, 0), P(1, 100),
            });
            Assert.IsTrue(noTension.GetTangentStiffness(-0.001) <= 1.0, "引抜き抵抗が無いのに浮き上がりに抵抗しています");
            Assert.AreEqual(noTension.GetSecantStiffness(-0.001) * -0.001, noTension.GetForce(-0.001), 1e-12);
        }

        /// <summary>
        /// 基本設定「場所打ち杭・鋼管杭の杭頭回転角」: 鉄筋定着工法の場所打ちRC杭・鋼管杭の杭頭 M-θ は、
        /// 杭の非線形の ON/OFF によらずこの設定に従う。以前は杭の非線形 OFF で剛になり、鋼管杭の Kθ が消えた。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example3_5", "PileExample3_5")]
        [DataRow("Example10", "PileExample10")]
        [DataRow("Example9", "PileExample9")]
        public void PileHeadRotation_FollowsTheFundamentalSetting_NotThePileNonlinearity(string ground, string pileName)
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel(ground, pileName);
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            if (input.PileBodies.All(b => b == null || !b.HasSelectablePileHeadRotation()))
            {
                Assert.Inconclusive($"{ground} に鉄筋定着工法の場所打ちRC杭・鋼管杭が無い");
                return;
            }

            var mainVm = new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input };
            var vm = new PileDesign.ViewModels.HorizontalCalculationViewModel(mainVm) { BypassUiPromptsForTesting = true };
            var modelling = new AnalysisModelling(input);
            var model = new AnaModel(input, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            model.InitializeStates();
            var lc = input.LoadCasesInput.AnalysisTargetSeismicLoadCases.First();

            const double KBig = 1e10;
            int Check(bool nonlinear, bool consider)
            {
                lc.IsPileNonLinear = nonlinear;
                input.FundamentalInput.ConsiderPileHeadRotation = consider;
                vm.SetupNonlinearMThetaForLoadCase(model, lc);
                int n = 0;
                foreach (var spring in model.RotationalSprings)
                {
                    var body = spring.PileBodyNo is int pb ? input.PileBodyAt(pb) : null;
                    if (body == null || !body.HasSelectablePileHeadRotation()) continue;
                    bool rigid = spring.CurveXY == null && spring.KthetaXY >= KBig;
                    if (consider)
                    {
                        // 軸力が範囲外の場所打ちRC杭は指針どおり剛 (GetMThetaRelationship)。それ以外は M-θ が付く
                        if (spring.LastSetupReason?.StartsWith("Rigid(def.Mode=Rigid", System.StringComparison.Ordinal) == true) continue;
                        Assert.IsFalse(rigid, $"{ground} {spring.Name}: 杭の非線形 {(nonlinear ? "ON" : "OFF")} で、考慮するのに杭頭が剛です ({spring.LastSetupReason})");
                    }
                    else
                    {
                        Assert.IsTrue(rigid, $"{ground} {spring.Name}: 杭の非線形 {(nonlinear ? "ON" : "OFF")} で、考慮しないのに杭頭が剛ではありません ({spring.LastSetupReason})");
                    }
                    n++;
                }
                return n;
            }

            TestSource.AssertScanned(Check(nonlinear: false, consider: true), 1, "杭体線形・考慮する");
            TestSource.AssertScanned(Check(nonlinear: true, consider: true), 1, "杭体非線形・考慮する");
            TestSource.AssertScanned(Check(nonlinear: false, consider: false), 1, "杭体線形・考慮しない");
            TestSource.AssertScanned(Check(nonlinear: true, consider: false), 1, "杭体非線形・考慮しない");
        }

        [TestMethod]
        public void PileHeadRotation_DefaultsToConsidered()
            => Assert.IsTrue(new FundamentalInput().ConsiderPileHeadRotation, "既定は「考慮する」");

        // ── 杭頭接合部 (2026-10-10 利用者の了解) ─────────────────

        /// <summary>F.T.Pile (引抜き対応タイプ) の Me は鋼棒の項を N・mm 系の係数 0.5 で持つ。以前は 5×10⁻⁴ で 1/1000 だった。</summary>
        [TestMethod]
        public void FTPile_TensionType_KeepsTheTensionBarMoment()
        {
            var ft = new PileDesign.Models.FTPile(36.0, 28_000.0);
            ft.SetDimensionsFromSection(600.0, 100.0);
            ft.FTPileTensionBars.IsTensionType = true;
            var bars = ft.FTPileTensionBars;
            Assert.IsTrue(bars.Ag > 0 && bars.Fu > 0, "前提: 引抜き抵抗用鋼棒がある");

            // 引張 100 kN でも鋼棒が曲げを負担する (以前は Me ≒ 0 で M ≡ 0)
            double nTension = -100_000.0;   // N
            var (_, ms) = ft.GetMThetaRelationship(nTension);
            double barTerm = 0.5 * bars.Ag * bars.Fu * bars.Ds + 0.5 * nTension * bars.Ds;   // N·mm
            Assert.IsTrue(barTerm > 0, "前提");
            Assert.IsTrue(ms.Max() > 0.05 * barTerm, $"引張時の M-θ が鋼棒の分を持っていません (最大 {ms.Max():E3} N·mm、鋼棒の項 {barTerm:E3})");
        }

        /// <summary>
        /// F.T.Pile の許容回転角 θa: 標準タイプは θac、引抜き対応タイプは min(θac, θas)。
        /// 検定は軸力を N で渡す (σN = N/Ap、Ap は mm²)。
        /// </summary>
        [TestMethod]
        public void FTPile_AllowableRotation_FollowsTheType()
        {
            var ft = new PileDesign.Models.FTPile(36.0, 28_000.0);
            ft.SetDimensionsFromSection(600.0, 100.0);
            // キャップを選ぶ (画面ではカタログから選ぶ。未選択だと Phi = 0 で支圧係数 φc が 0 になる)
            Assert.IsTrue(ft.FTCaps?.Count > 0, "前提: FT キャップのカタログが読める");
            ft.FTCap = ft.FTCaps.First(c => c.Phi > 0);
            ft.Update();
            double thetaAs = ft.FTPileTensionBars.ThetaAS;
            Assert.IsTrue(thetaAs > 0 && thetaAs < 0.03, "前提: 鋼棒の既定値から θas が決まっている");

            ft.FTPileTensionBars.IsTensionType = false;
            Assert.AreEqual(0.03, ft.GetAllowableRotationAngle(0.0), 1e-12, "標準タイプが鋼棒の θas で頭打ちになっています");
            ft.FTPileTensionBars.IsTensionType = true;
            Assert.AreEqual(Math.Min(0.03, thetaAs), ft.GetAllowableRotationAngle(0.0), 1e-12);

            string body = TestSource.Read("Graphics_r1", "ViewModels", "EvaluationService.cs");
            StringAssert.Contains(body, "GetAllowableRotationAngle(axialN_kN * 1000.0)", "検定が軸力を kN のまま渡しています");
        }

        private sealed class ConstantStress(double sigma) : PileDesign.Models.CTPMaterial
        {
            public override double GetStress(double epsilon) => sigma;
        }

        /// <summary>キャプテンパイルの正方形配置は、n 本の定着筋をすべて数える (以前は中間の列を 1 本と数えた)。</summary>
        [DataTestMethod]
        [DataRow(4)]
        [DataRow(8)]
        [DataRow(12)]
        [DataRow(16)]
        public void CaptainPile_SquareArrangement_CountsEveryBar(int n)
        {
            var section = new PileDesign.Models.CTPSquareDotSection(400.0, n, 100.0);
            var (axial, _) = section.GetForceAndMoment(new ConstantStress(1.0), 0.0, 0.0);
            Assert.AreEqual(n * 100.0, axial, 1e-9, $"{n} 本の正方形配置で、数えた本数が {axial / 100.0} 本です");
        }

        /// <summary>キャプテンパイル (定着筋なし) で引張なら、曲げを負担しない (Mu = 0、M-θ ≡ 0)。以前は負の外挿値だった。</summary>
        [TestMethod]
        public void CaptainPile_WithoutTensionBars_CarriesNoMomentInTension()
        {
            var cp = new PileDesign.Models.CaptainPile(24.0, 22669.0);
            var ring = cp.PCRings.FirstOrDefault(r => (r.Name ?? "").EndsWith("N")) ?? cp.PCRings.FirstOrDefault();
            Assert.IsNotNull(ring, "PC リングのカタログが読めていません");
            cp.PCRing = ring;
            cp.D = ring.D;
            cp.Nu = 1.0;
            cp.CTPTensionRebars.HasTensionRebars = false;
            cp.Update();
            Assert.IsTrue(cp.UltimateNs!.Count > 2, "前提: N-M 曲線がある");
            Assert.AreEqual(0.0, cp.UltimateNs[0], 1e-6, "前提: 定着筋なしの引張側の端は 0");
            Assert.IsTrue(cp.UltimateNs.Zip(cp.UltimateNs.Skip(1)).All(p => p.First <= p.Second), "前提: N-M 曲線は引張側から並ぶ");

            Assert.AreEqual(0.0, cp.GetMu(-300_000.0), 1e-9, "引張なのに Mu が 0 ではありません");
            var (thetas, ms) = cp.GetMThetaRelationship(-300_000.0);
            Assert.IsTrue(ms.All(m => m == 0.0), "引張で曲げを負担できないのに M-θ に曲げがあります");
            Assert.IsTrue(thetas.Zip(thetas.Skip(1)).All(p => p.First < p.Second), "M-θ の回転角が増えていきません");

            Assert.IsTrue(cp.GetMu(1_000_000.0) > 0, "圧縮では曲げを負担する");
        }

        /// <summary>動かす瞬間に項目を 1 つ消すコレクション (別の書き手とのすれ違いの再現)。</summary>
        private sealed class ShrinksOnMove : ObservableCollection<string>
        {
            public ShrinksOnMove(params string[] items) : base(items) { }
            protected override void MoveItem(int oldIndex, int newIndex)
            {
                RemoveAt(Count - 1);
                base.MoveItem(oldIndex, newIndex);
            }
        }

        /// <summary>
        /// 解析結果の表示内容の並べ替えは、位置を調べてから動かすまでに候補が減っても例外を外へ出さない (諦めて次の変更でやり直す)。
        /// 以前は Dispatcher の未処理例外になった (2026-10-10 の全体テストで発生)。
        /// </summary>
        [TestMethod]
        public void ContentReorder_GivesUpQuietlyWhenTheListShrinksMidMove()
        {
            var live = new ShrinksOnMove("A", "B", "C");
            PileDesign.ViewModels.MainWindowViewModel.ApplyContentOrder(live, ["C", "A", "B"]);
        }

        // ── F.T.Pile の FT キャップ未選択 (2026-10-10 利用者の依頼) ─────────────────

        /// <summary>例題の最初の杭体を F.T.Pile 構法にして、FT キャップを選んでいない状態にする。</summary>
        private static (InputModel Input, PileBodyInput Body)? FTPileWithoutCap()
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example3_4", "PileExample3_4");
            if (input == null) return null;
            var body = input.PileBodies[0];
            body.PileTopType = "FT-Pile構法";
            body.PileTop.FTPile = new PileDesign.Models.FTPile(24.0, 22669.0);
            Assert.IsTrue(body.PileTop.FTPile.IsCapUnselected(), "前提: キャップを選んでいない");
            return (input, body);
        }

        /// <summary>
        /// FT キャップは杭径から自動で決める (杭径と一致するキャップ。カタログに無ければ未選択にして知らせる)。
        /// 未選択・違う径のキャップは、水平解析の前に決め直して続ける。カタログに杭径が無ければ止める。
        /// </summary>
        [TestMethod]
        public void FTCap_IsChosenFromThePileDiameter()
        {
            var built = FTPileWithoutCap();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (input, body) = built.Value;
            var ft = body.PileTop.FTPile!;
            var section = body.PileBodySegments[0].PileSection;
            double diameter = section.PileDiameter;
            Assert.IsNotNull(ft.CapForPileDiameter(diameter), "前提: 例題の杭径のキャップがカタログにある");

            // 杭頭部の自動選定: 杭径と一致するキャップ
            Assert.IsTrue(body.PileTop.AutoSelectFTCap(section));
            Assert.AreEqual(diameter, ft.FTCap.Phi, 1e-9);
            Assert.IsTrue(double.IsFinite(ft.GetAllowableRotationAngle(1_000_000.0)), "キャップを決めても許容回転角が求まりません");

            // 違う径のキャップを持っていたら、解析の前に杭径へ決め直して続ける
            Assert.IsTrue(ft.SelectCap(ft.FTCaps.First(c => System.Math.Abs(c.Phi - diameter) > 1).Phi));
            Assert.AreEqual(1, PileDesign.Services.FTCapSelection.FindMismatched(input).Count, "杭径と違うキャップを見つけていません");
            var mainVm = new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input };
            var vm = new PileDesign.ViewModels.HorizontalCalculationViewModel(mainVm) { BypassUiPromptsForTesting = true };
            Assert.IsTrue(vm.EnsureFTCapsMatchPileDiameter());
            Assert.AreEqual(diameter, ft.FTCap.Phi, 1e-9, "解析の前に杭径のキャップへ決め直していません");
            Assert.AreEqual(0, PileDesign.Services.FTCapSelection.FindMismatched(input).Count);

            StringAssert.Contains(TestSource.MethodBody(
                TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs"), "private async Task OnExecuteAnalysisCore(bool additive)"),
                "EnsureFTCapsMatchPileDiameter()", "解析の入口で FT キャップを確かめていません");
        }

        /// <summary>杭径に合う FT キャップがカタログに無ければ、別の径で黙って計算せず、未選択にして解析を止める。</summary>
        [TestMethod]
        public void FTCap_MissingFromTheCatalog_StopsTheAnalysis()
        {
            var built = FTPileWithoutCap();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (input, body) = built.Value;
            var section = body.PileBodySegments[0].PileSection;
            section.PileDiameter = 1500.0;   // FT キャップは 300〜1200 mm
            Assert.IsNull(body.PileTop.FTPile!.CapForPileDiameter(1500.0), "前提: 杭径 1500 のキャップは無い");

            Assert.IsFalse(body.PileTop.AutoSelectFTCap(section));
            Assert.IsTrue(body.PileTop.FTPile.IsCapUnselected(), "カタログに無いのに別の径のキャップを選んでいます");
            StringAssert.Contains(string.Join(Environment.NewLine, PileDesign.Services.CheckInputData.CollectInputWarnings(input)),
                "FT キャップがカタログにありません", "入力の注意の一覧に出ていません");

            var mainVm = new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input };
            var vm = new PileDesign.ViewModels.HorizontalCalculationViewModel(mainVm) { BypassUiPromptsForTesting = true };
            Assert.IsFalse(vm.EnsureFTCapsMatchPileDiameter(), "キャップを決められないのに解析を続けています");
        }

        /// <summary>FT キャップの選び方は 1 か所 (PileTop.AutoSelectFTCap)。杭頭部の画面の選択欄は表示だけ。</summary>
        [TestMethod]
        public void FTCap_SelectionLivesInOnePlace()
        {
            foreach (var (dir, file) in new[] { ("ViewModels", "PileBodyViewModel.cs"), ("Views", "PileTopWindow.xaml.cs") })
            {
                string src = TestSource.Read("Graphics_r1", dir, file);
                StringAssert.Contains(src, "AutoSelectFTCap(", file);
                Assert.IsFalse(src.Contains(".Where(c => c.Phi >= pileDia)", StringComparison.Ordinal), $"{file}: キャップの選び方の写しが残っています");
            }
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "PileTopWindow.xaml"), "IsEnabled=\"False\" ToolTip=\"FT キャップは杭径ごとの製品");
        }

        /// <summary>
        /// 杭周面の引抜き側: 単杭の荷重伝達法は押込みと同じ τ-s (τ2 まで) で、杭全体の引抜き荷重を τT から求めた Rt で止める。
        /// 水平解析の P-S ばねは荷重を止めないので、τT で頭打ちにする (勾配 τ1/S1 で sT = |τT|·S1/τ1 まで、その先は一定)。
        /// 以前は P-S ばねも τ2 まで上がり、引き抜かれる杭の抵抗が Rtu を超えていた (2026-10-10 利用者の指示)。
        /// 沈下の画面の τ-s 図は単杭の荷重伝達法と同じ形に描く。
        /// </summary>
        [TestMethod]
        public void PilePerimeter_UpliftSide_CappedAtTauTOnlyForThePsSpring()
        {
            const double tau1 = 40, tau2 = 50, tauT = -33.3, S1 = 0.005, S2 = 0.020, psiL = 1.0;
            double Force(string state, double s, double t) => VerticalLoadTransferMethod.GetSecantStiffnessPilePerimeter(
                state, s, true, true, tau1, tau2, S1, S2, psiL, t) * s;

            // P-S ばね (τT を渡す): 引抜き 4 mm は勾配 τ1/S1 の上 (32)、20 mm は |τT| で頭打ち
            Assert.AreEqual(-32.0, Force("negative", -0.004, tauT), 1e-9);
            Assert.AreEqual(-33.3, Force("negative", -0.020, tauT), 1e-9, "P-S ばねの引抜き側が |τT| で頭打ちになっていません");
            // 単杭の荷重伝達法 (τT を渡さない): 押込みと同じ曲線で τ2 まで
            Assert.AreEqual(-50.0, Force("negative", -0.030, double.NaN), 1e-9, "単杭の荷重伝達法の引抜き側が押込みと同じ曲線ではありません");
            Assert.AreEqual(50.0, Force("positive", 0.030, double.NaN), 1e-9);

            string vlt = TestSource.Read("Graphics_r1", "FEM", "VerticalLoadTransferMethod.cs");
            StringAssert.Contains(vlt, "GetTangentStiffnessPilePerimeter(state, s, aPC, aPT, tau1, tau2, S1, S2, psiL);",
                "単杭の荷重伝達法が τT を渡しています (元のコードのとおり、荷重を Rt で止める)");
            StringAssert.Contains(vlt, "GetSecantStiffnessPilePerimeter(state, s, aPC, aPT, tau1, tau2, S1, S2, psiL) * s;");
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(
                TestSource.Read("Graphics_r1", "FEM", "PileVerticalSoilSpringModel.cs"), @"psiLHalf, pcv\.TauT\)").Count,
                "水平解析の P-S ばねが τT を渡していません");

            // τ-s 図: 引抜き側は押込み側を反転した形 (τT で頭打ちに描かない)
            var pcv = new PileDesign.Models.InputData.PileCircumVertical
            {
                Tau1 = tau1, Tau2 = tau2, TauT = tauT, S1 = 5, S2 = 20,
                PileBodySegment = new PileDesign.Models.InputData.PileBodySegment { PileSection = new PileDesign.Models.InputData.PileSection { PileDiameter = 1000.0 } },
            };
            var pts = PileDesign.ViewModels.SettlementViewModel.CircumCurvePoints(pcv);
            double psi = pcv.Psi;
            CollectionAssert.Contains(pts, (-20.0, -tau2 * psi), "τ-s 図の引抜き側が単杭の荷重伝達法と同じ形ではありません");
            CollectionAssert.Contains(pts, (-5.0, -tau1 * psi));
            Assert.IsTrue(psi > 0, "前提: 周長がある");
            Assert.IsFalse(pts.Any(p => p.X < 0 && System.Math.Abs(p.Y - tauT * psi) < 1e-9),
                "τ-s 図の曲線を τT で頭打ちに描いています");
        }

        /// <summary>
        /// 水平解析の P-S ばねで剛性が 0 (引抜き抵抗を外した層で引き抜かれる等) のときは、抵抗しない (行列を特異にしない下限だけ)。
        /// 以前は押込みの初期剛性に置き換え、外した層や杭先端が引抜きに抵抗していた。
        /// </summary>
        [TestMethod]
        public void PsSpring_ZeroStiffness_StaysNearZero()
        {
            string solver = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Solver.cs");
            Assert.IsFalse(solver.Contains("kz = md.InitialTangentStiffness;", StringComparison.Ordinal), "剛性 0 を押込みの初期剛性に置き換えています");
            StringAssert.Contains(solver, "kz = Math.Max(md.InitialTangentStiffness * 1e-6, 1e-3);");
        }

        /// <summary>
        /// 基礎梁の傾斜角の限界値は設計者が決める値 (基本設定「杭頭 2 点間の変形角の限界値」)。記録のレベルに応じて、
        /// 常時は使用限界、レベル1 は損傷限界、レベル2 は安全限界。以前は全記録を固定の 1/300 で常時として判定していた。
        /// </summary>
        [TestMethod]
        public void FoundationBeamInclination_UsesTheDesignersLimitsByLevel()
        {
            var f = new FundamentalInput { ServiceDeformationAngleLimit = 0.002, DamageDeformationAngleLimit = 0.004, UltimateDeformationAngleLimit = 0.008 };
            PileDesign.Models.Results.GroupSettlementCaseRecord Rec(string name) => new() { LoadCaseName = name, IsBeamAware = true };
            Assert.AreEqual((0, "使用限界", 0.002), PileDesign.Services.FoundationBeamInclinationEvaluator.InclinationLimitFor(Rec("杭軸力 VL"), f));
            Assert.AreEqual((1, "損傷限界", 0.004), PileDesign.Services.FoundationBeamInclinationEvaluator.InclinationLimitFor(Rec("L1-1: 杭軸力"), f));
            Assert.AreEqual((2, "安全限界", 0.008), PileDesign.Services.FoundationBeamInclinationEvaluator.InclinationLimitFor(Rec("L2-3: 杭軸力"), f));
            // 性能グレード S ではレベル2 も損傷限界 (基本設定の説明・水平解析の杭頭変形角と同じ。以前はグレードを見ていなかった)
            f.SeismicGrade = "S";
            Assert.AreEqual((2, "損傷限界", 0.004), PileDesign.Services.FoundationBeamInclinationEvaluator.InclinationLimitFor(Rec("L2-3: 杭軸力"), f),
                "性能グレード S なのに、レベル2 の傾斜角を安全限界で照査しています");
            Assert.AreEqual((1, "損傷限界", 0.004), PileDesign.Services.FoundationBeamInclinationEvaluator.InclinationLimitFor(Rec("L1-1: 杭軸力"), f));
            Assert.IsFalse(TestSource.Read("Graphics_r1", "Services", "FoundationBeamInclinationEvaluator.cs").Contains("1.0 / 300.0", StringComparison.Ordinal),
                "固定の 1/300 が残っています");
        }

        /// <summary>
        /// PHC 杭の M-φ で、PC 鋼材がコンクリート圧壊より先に降伏する経路 (a) の終点は β1·β2·Mu0、
        /// Mu0 = min(Mu1, Mu2) (Mu1: コンクリートが εcu、Mu2: PC 鋼材が εpu = 0.02、断面の曲げ解析)。
        /// 以前は Mu0 = min(MCf, MY) = MY (鋼材の降伏) で、天井が検定の耐力より低かった (2026-10-10 利用者の指示)。
        /// </summary>
        [TestMethod]
        public void PhcMPhi_PathA_EndsAtTheMinimumOfConcreteCrushAndTendonRupture()
        {
            var section = new PileSection { PileBodyType = PileDesign.Constants.PileTypeNames.PrecastConcrete, PileSectionType = PileDesign.Constants.PileTypeNames.Phc };
            section.SelectedPrecastPile.Name = "PHC-600-標準-85-A";
            section.RecalculateSelectedPrecastPile();
            if (section.CreateSectionCalculator() is not PHCSection phc) { Assert.Inconclusive("PHC の断面を組めません"); return; }

            const double n = 0.0;
            var (my, phiY) = phc.GetMomentCurvatureForN(n, "TendonYield");
            var (mcf, phiCf) = phc.GetMomentCurvatureForN(n, "ConcreteCompressiveFailure");
            var (mu2, _) = phc.GetMomentCurvatureForN(n, "TendonTensileFailure");
            var (_, phiCr) = phc.GetCrackMoment(n);
            if (!(phiCr < phiY && phiY < phiCf)) { Assert.Inconclusive("前提: 経路 a (ひび割れ → PC 鋼材の降伏 → 圧壊) の断面"); return; }

            double mu0 = Math.Min(mcf, mu2);
            Assert.IsTrue(mu0 > my, "前提: Mu0 は鋼材の降伏モーメントより大きい");
            var (_, ms) = phc.GetMPhiRelationshipInternal(n, 0.8);
            Assert.AreEqual(0.8 * phc.GetBeta2(n) * mu0, ms[^1], 1e-6 * mu0,
                "M-φ の終点が β1·β2·min(Mu1, Mu2) ではありません (鋼材の降伏モーメントで頭打ち?)");
        }

        /// <summary>
        /// せん断耐力の M/(Q·d) は、杭・荷重ケース・液状化の考慮/非考慮ごとの最大 M・最大 Q で求める (荷重組合せはまたぐ)。
        /// 以前は液状化の考慮・非考慮を混ぜて最大を取っていた (2026-10-10 利用者の指示)。
        /// </summary>
        [TestMethod]
        public void MonQd_IsResolvedSeparatelyForLiquefaction()
        {
            var lc = new LoadCase { No = 1, Level = 2 };
            var beam = new Beam();
            BeamResult R(bool liq, double m, double q) => new()
            {
                LoadCase = lc, IsLiquefaction = liq,
                CumulativeForce = new BeamForce(0, q, 0, 0, 0, m, 0, 0, 0, 0, 0, 0),
            };
            beam.BeamResults = [R(true, 900, 100), R(false, 300, 400)];
            var pile = new PileLayoutDataItem { No = 1, PileNo = 1, Beams = [beam] };
            var input = new InputModel { PileLayoutItems = [pile] };

            var map = PileDesign.ViewModels.EvaluationService.BuildMaxForcesByPileCase(input);
            var liq = map[(pile, PileDesign.ViewModels.EvaluationService.MaxForceKeyOf(lc, true))];
            var dry = map[(pile, PileDesign.ViewModels.EvaluationService.MaxForceKeyOf(lc, false))];
            Assert.AreEqual((900.0, 100.0), liq, "液状化考慮の最大断面力に、非考慮の Q が混ざっています");
            Assert.AreEqual((300.0, 400.0), dry, "液状化非考慮の最大断面力に、考慮の M が混ざっています");
        }

        /// <summary>
        /// 鋼管杭の引張軸力側の耐力は、原文どおり圧縮側の量で計算しているが、原文の誤りの可能性がある (2026-10-10 利用者の判断)。
        /// 他の文献で確認して直すまで、ヘルプとコードに「要注意」を残す。直したら、このテストと注記を一緒に消すこと。
        /// </summary>
        [TestMethod]
        public void SteelPipeTensionSide_CautionIsDocumentedUntilFixed()
        {
            string help = TestSource.Read("Graphics_r1", "Help", "help.html");
            StringAssert.Contains(help, "id=\"steel-pipe-tension-caution\"", "ヘルプの要注意の注記が消えています");
            StringAssert.Contains(help, "href=\"#steel-pipe-tension-caution\"");
            string code = TestSource.Read("Graphics_r1", "Models", "InputData", "SteelPipeSection.cs");
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(code, @"要注意 \(引張軸力側\)").Count, "コードの要注意の注記が消えています");
        }
    }
}

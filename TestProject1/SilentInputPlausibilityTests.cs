using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 値としては受け付けられるが、もっともらしい誤った結果を黙って出していた入力の検査 (2026-10-11)。
    /// <list type="bullet">
    /// <item>kh0 の手入力は土層名で引くので、同じ名前の層が 2 つあると当たる層が決まらない (止める)。名前を変えると取り残される (注意)。</item>
    /// <item>杭先端 ±D に土質点が無いと平均 N 値が 0 になり、先端支持力が 0 のまま計算が進む (注意)。</item>
    /// <item>地表面加速度・単位体積重量・変形係数の単位の取り違え (注意)。有効上載圧が負で水平地盤ばねが消える区間 (注意)。</item>
    /// <item>杭間隔比 R/B が 1 未満 (止める)・杭配置の中心間隔 ÷ 杭径と大きく違う (注意)。</item>
    /// </list>
    /// 検査は入力を書き換えず、解析の値は変えない。どれも解析の入口 (<see cref="CheckInputData.CollectAnalysisBlockers"/>・
    /// <see cref="CheckInputData.CollectInputWarningDiagnostics"/>) から確かめる (検査を作っても入口に繋がなければ効かない)。
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class SilentInputPlausibilityTests
    {
        private bool _unattended;
        [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
        [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

        private static InputModel Example9()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) throw new AssertInconclusiveException("例題ロード失敗: " + error);
            return model;
        }

        private static List<string> Blockers(InputModel m) => CheckInputData.CollectAnalysisBlockers(m).Select(d => d.Message).ToList();
        private static List<string> Warnings(InputModel m) => CheckInputData.CollectInputWarningDiagnostics(m)
            .Where(d => d.Severity == PileDesign.Common.DiagnosticSeverity.Warning).Select(d => d.Message).ToList();
        private static string Show(IEnumerable<string> lines) => string.Join("\n", lines);

        // ── kh0 の手入力 ──

        [TestMethod]
        public void Kh0Override_OnADuplicatedLayerName_BlocksTheAnalysis()
        {
            var m = Example9();
            var sp = m.ElementDivision!.SoilPiles[0];
            var layers = m.GroundAt(sp.GroundNo)!.GroundLayers;
            for (int i = 0; i < layers.Count; i++) layers[i].Name = $"層{i + 1}";
            string name = layers[1].Name;
            sp.Kh0LayerOverrides.Add(new Kh0LayerOverride { LayerName = name, Kh0 = 12345 });
            Assert.IsFalse(Blockers(m).Any(b => b.Contains("kh0")), "名前が重ならなければ止めない:\n" + Show(Blockers(m)));

            layers[0].Name = name; // 既定の名前「(AS#)」のまま 2 層を作ったのと同じ
            var blockers = Blockers(m);
            Assert.IsTrue(blockers.Any(b => b.Contains($"土層名「{name}」の土層が 2 層あり") && b.Contains("kh0 の手入力をどの層に使うか決まりません")),
                "同じ名前の層が 2 つあるのに止めていません:\n" + Show(blockers));
            Assert.AreEqual(12345, sp.Kh0LayerOverrides.Single().Kh0, "検査が手入力を書き換えています");
        }

        [TestMethod]
        public void Kh0Override_LeftBehindByARename_IsWarned_NotBlocked()
        {
            var m = Example9();
            var sp = m.ElementDivision!.SoilPiles[0];
            sp.Kh0LayerOverrides.Add(new Kh0LayerOverride { LayerName = "名前を変える前の層", Kh0 = 12345 });
            Assert.IsFalse(Blockers(m).Any(b => b.Contains("kh0")), "取り残された手入力は使われないだけなので止めない");
            var warnings = Warnings(m);
            Assert.IsTrue(warnings.Any(w => w.Contains("土層名「名前を変える前の層」") && w.Contains("当たる土層がありません")),
                "取り残された kh0 の手入力を知らせていません:\n" + Show(warnings));
            Assert.IsTrue(PileDesign.Output.WordDocument.InputNoticeLines(m).Any(l => l.Contains("当たる土層がありません")), "計算書に書かれません");
        }

        // ── 杭先端の平均 N 値 ──

        /// <summary>キャプテンパイル工法の例題 (AIJ2018) は杭先端 ±D に土質点が無く、先端の平均 N 値が 0 になっている。値はそのまま知らせる。</summary>
        [TestMethod]
        public void ToeNValueWithoutSoilPoints_IsWarned_AndTheValueIsUnchanged()
        {
            // 杭先端の上下 D から土質点を取り除いて、点の無い状態を作る (同梱のキャプテンパイルの例題は 2026-10-12 まで
            // 土質点が無くこの状態だったが、層の N 値から土質点を作って直した)
            var m = Example9();
            var sp0 = m.ElementDivision!.SoilPiles[0];
            var ground = m.GroundsInput[sp0.GroundNo - 1];
            foreach (var mass in sp0.PileToeNValuePoints().ToList()) ground.GroundMassesData.Remove(mass);
            var sp = m.ElementDivision!.SoilPiles.First(s => s.PileToeNValuePoints().Count == 0);
            Assert.AreEqual(0.0, sp.PileToeNValue, "平均 N 値の値は変えない (0 のまま)");
            var warnings = Warnings(m);
            Assert.IsTrue(warnings.Any(w => w.Contains($"杭体{sp.PileBodyNo}・地盤{sp.GroundNo}") && w.Contains("土質点がありません")
                                            && w.Contains("先端支持力を 0 として計算します")),
                "杭先端の平均 N 値が 0 になることを解析の前に知らせていません:\n" + Show(warnings));
        }

        [TestMethod]
        public void ToeNValueWithSoilPoints_IsNotWarned()
        {
            var m = Example9();
            Assert.IsTrue(m.ElementDivision!.SoilPiles.All(s => s.PileToeNValuePoints().Count > 0), "前提: 計算例9 は杭先端 ±D に土質点がある");
            Assert.IsFalse(Warnings(m).Any(w => w.Contains("平均 N 値")), Show(Warnings(m)));
        }

        // ── 地表面加速度 ──

        [TestMethod]
        public void GroundAcceleration_OutsideAPlausibleRange_IsWarned()
        {
            var m = Example9();
            var g = m.GroundsInput[0];
            Assert.IsFalse(Warnings(m).Any(w => w.Contains("地表面加速度")), "前提: 例題の加速度は m/s² の範囲:\n" + Show(Warnings(m)));

            g.GroundAcceleration1 = 0.35; // g のつもり
            Assert.IsTrue(Warnings(m).Any(w => w.Contains("レベル1 の地表面加速度 0.35 m/s² は単位が違う可能性があります")), Show(Warnings(m)));

            g.GroundAcceleration2 = 350; // gal のつもり。入力欄は 100 に丸める
            Assert.AreEqual(100.0, g.GroundAcceleration2, "前提: 入力欄は 100 m/s² に丸める");
            var w2 = Warnings(m).FirstOrDefault(w => w.Contains("レベル2 の地表面加速度 100 m/s²"));
            Assert.IsNotNull(w2, "丸めた加速度を知らせていません:\n" + Show(Warnings(m)));
            StringAssert.Contains(w2, "丸めた値になっています");
            StringAssert.Contains(w2, "350 gal = 3.5 m/s²");
        }

        // ── 単位体積重量・変形係数 ──

        [TestMethod]
        public void SoilDensityAndEs_WithAnImplausibleMagnitude_AreWarned()
        {
            var m = Example9();
            var layer = m.GroundsInput[0].GroundLayers.First(l => l.NValue > 0);
            int no = m.GroundsInput[0].GroundLayers.IndexOf(layer) + 1;
            layer.Density = 1.8;            // t/m³ のつもり
            layer.Es = 0.7 * layer.NValue;  // N/mm² のつもり
            var warnings = Warnings(m);
            Assert.IsTrue(warnings.Any(w => w.StartsWith($"地盤1 層{no}: 単位体積重量 γ = 1.8 kN/m³ は土としては小さすぎます")), Show(warnings));
            Assert.IsTrue(warnings.Any(w => w.StartsWith($"地盤1 層{no}: 変形係数 Es") && w.Contains("小さすぎます")), Show(warnings));
            Assert.IsFalse(Blockers(m).Any(b => b.Contains("単位体積重量") || b.Contains("変形係数")), "桁の疑いは止めない");

            layer.Density = 18; layer.Es = 700 * layer.NValue;
            Assert.IsFalse(Warnings(m).Any(w => w.StartsWith($"地盤1 層{no}: 単位体積重量") || w.StartsWith($"地盤1 層{no}: 変形係数 Es")), Show(Warnings(m)));
        }

        /// <summary>
        /// 水中重量を入れると地下水位より下で有効上載圧が負になり、砂質土・礫質土の py が 0 以下 (ばねが消える) になる。
        /// その区間を知らせる。
        /// </summary>
        [TestMethod]
        public void NegativeEffectiveStress_RemovesTheSprings_AndIsAnnounced()
        {
            var m = Example9();
            var g = m.GroundsInput[0];
            foreach (var l in g.GroundLayers) { l.Density = 8; l.GranularityClass = "砂質土"; }
            g.GroundWaterTableAltitude = g.GroundTopAltitude;
            m.GenerateSoilPiles();
            Assert.IsTrue(m.ElementDivision!.SoilPiles.Any(s => s.HorizontalSoilReactions.Any(r => r.SigmaZPrimeBtm < 0)), "前提: 有効上載圧が負の要素がある");
            var warnings = Warnings(m);
            Assert.IsTrue(warnings.Any(w => w.Contains("砂質土・礫質土で有効上載圧が負になり") && w.Contains("水平地盤ばねが 0 になります")), Show(warnings));
        }

        // ── 杭間隔比 R/B ──

        [TestMethod]
        public void SpacingRatioBelowOne_BlocksTheAnalysis_ButZeroAndOneDoNot()
        {
            var m = Example9();
            var pile = m.PileLayoutItems[0];
            pile.PileSpacingFactor = 0.5;
            Assert.IsTrue(Blockers(m).Any(b => b.StartsWith($"杭 No.{pile.No}: 杭間隔比 R/B = 0.5 が 1 未満です")), Show(Blockers(m)));

            foreach (double ok in new[] { 0.0, 1.0 })
            {
                pile.PileSpacingFactor = ok;
                Assert.IsFalse(Blockers(m).Any(b => b.Contains("杭間隔比")), $"R/B = {ok} で止めています:\n" + Show(Blockers(m)));
            }
        }

        [TestMethod]
        public void SpacingRatioFarFromTheLayout_IsWarned_AndAMatchingOneIsNot()
        {
            // 計算例10 は R/B = 4.3 で、杭配置の中心間隔 ÷ 杭径と合っている
            var (m, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (m == null) { Assert.Inconclusive(error); return; }
            Assert.IsTrue(m.PileLayoutItems.All(p => p.PileSpacingFactor == 4.3), "前提: 計算例10 の R/B は 4.3");
            Assert.IsFalse(Warnings(m).Any(w => w.Contains("杭配置から求めた中心間隔")), Show(Warnings(m)));

            foreach (var p in m.PileLayoutItems) p.PileSpacingFactor = 10;
            var warnings = Warnings(m).Where(w => w.Contains("杭配置から求めた中心間隔")).ToList();
            Assert.AreEqual(1, warnings.Count, "同じ R/B の杭は 1 行にまとめる:\n" + Show(warnings));
            StringAssert.Contains(warnings[0], "杭間隔比 R/B = 10 が");
            Assert.IsFalse(Blockers(m).Any(b => b.Contains("杭間隔比")), "食い違いは止めない");
        }

        // ── 例題 ──

        /// <summary>同梱の計算例は、新しい検査で止まらない (止まるなら検査か例題のどちらかが誤り)。</summary>
        [TestMethod]
        [Timeout(600000)]
        public void NoExampleIsBlockedByTheNewChecks()
        {
            int scanned = 0;
            var blocked = new List<string>();
            foreach (var (ground, pile) in PileExamples)
            {
                var (m, error) = IntegrationTests.BuildExampleInputModel(ground, pile);
                Assert.IsNotNull(m, error);
                scanned++;
                foreach (var d in CheckInputData.CollectAnalysisBlockers(m))
                    blocked.Add($"{pile}: {d.Message}");
            }
            TestSource.AssertScanned(scanned, PileExamples.Length, "計算例");
            Assert.AreEqual(0, blocked.Count, "計算例が解析の前に止まります:\n" + Show(blocked));
        }
        internal static readonly (string Ground, string Pile)[] PileExamples =
        [
            ("Example3_1", "PileExample3_1"), ("Example3_2", "PileExample3_2"), ("Example3_3", "PileExample3_3"),
            ("Example3_4", "PileExample3_4"), ("Example3_5", "PileExample3_5"), ("Example3_8_1", "PileExample3_8"),
            ("Example5", "PileExample5"), ("Example7", "PileExample7"), ("Example9", "PileExample9"),
            ("Example10", "PileExample10"), ("ExampleCAP3_7", "PileExampleCAP3_7"), ("ExampleCAP3_7_3", "PileExampleCAP3_7_3"),
            ("ExampleCPT2018", "PileExampleCPT2018"), ("ExampleK8", "PileExampleK8"),
        ];

        internal static readonly string[] GroupSettlementExamples = ["GroupSettlementK7", "GroupSettlement5", "GroupSettlement2_1"];

        /// <summary>群杭沈下の計算例を、実機の読み込み (杭配置・杭体・地盤を例題から組む) に沿って組む。</summary>
        internal static (InputModel? Model, string? Error) BuildGroupSettlementExample(string name)
        {
            try
            {
                var vm = new MainWindowViewModel();
                var model = vm.CurrentInputModel;
                if (model == null) return (null, "既定の入力がありません");
                var data = GroupSettlementExampleLoader.LoadFromFile(name);
                if (!string.IsNullOrEmpty(data.GroundExampleName))
                {
                    var groundVm = new GroundLayerViewModel(vm);
                    var groundData = GroundExampleLoader.LoadFromFile(data.GroundExampleName);
                    GroundExampleLoader.ApplyToGroundInput(groundVm.GroundInput, groundData);
                    model.GroundsInput[0] = groundVm.GroundInput.DeepCopy();
                }
                GroupSettlementExampleLoader.ApplyToInputModel(model, data, null);
                model.MigratePileZSemantics_v1_to_v2();
                foreach (var pb in model.PileBodies)
                {
                    foreach (var seg in pb.PileBodySegments)
                    {
                        var sec = seg?.PileSection;
                        if (sec == null) continue;
                        if (!string.IsNullOrWhiteSpace(sec.SelectedPrecastPile?.Name)) sec.RecalculateSelectedPrecastPile();
                        sec.RecalculatePileDia();
                        sec.RecalculateConcreteE();
                        sec.SetSpecs();
                    }
                }
                model.UpdateCountLists();
                vm.UpdatePileLayoutNo();
                model.GenerateSoilPiles();
                return (model, null);
            }
            catch (Exception ex)
            {
                return (null, $"{name}: {ex.Message}");
            }
        }
    }
}

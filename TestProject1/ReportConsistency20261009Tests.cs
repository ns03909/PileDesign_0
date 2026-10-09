using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Output;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-09 の計算書 (docx) の見直し: 計算書の式・表・図が、解析と検定の実装と食い違っていたものを直した。
    /// 解析の数値は変えていない。計算書の記載を実装に合わせただけ。
    /// </summary>
    [TestClass]
    public class ReportConsistency20261009Tests
    {
        private static string Sections() => TestSource.Read("Graphics_r1", "Output", "WordDocument.Sections.cs");

        /// <summary>コメント行を除いたソース (古い式がコメントアウトして残っている)。</summary>
        private static string WithoutCommentLines(string src)
            => string.Join("\n", src.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        /// <summary>SC 杭の β (基本設定) を一時的に変え、終わったら戻す。</summary>
        private sealed class ScBetaScope : IDisposable
        {
            private readonly double _b1 = ConcreteModelOptions.ScUltimateShearBeta1;
            private readonly double _b2 = ConcreteModelOptions.ScUltimateShearBeta2;
            private readonly bool _fiber = ConcreteModelOptions.UseFiberMPhi;
            public void Dispose()
            {
                ConcreteModelOptions.ScUltimateShearBeta1 = _b1;
                ConcreteModelOptions.ScUltimateShearBeta2 = _b2;
                ConcreteModelOptions.UseFiberMPhi = _fiber;
            }
        }

        // ===== 1. SC 杭の安全限界せん断の β は基本設定の値 =====

        [TestMethod]
        public void ScShearBetaRows_FollowTheOptions()
        {
            using var _ = new ScBetaScope();
            ConcreteModelOptions.UseFiberMPhi = false;
            ConcreteModelOptions.ScUltimateShearBeta1 = 0.95;
            ConcreteModelOptions.ScUltimateShearBeta2 = 0.6;

            var body = new Body();
            WordDocument.AddBetaFactorTables(body);
            string text = body.InnerText;
            StringAssert.Contains(text, "0.95", "β1 の表の SC 杭・せん断・安全限界が基本設定の β1 になっていません");
            StringAssert.Contains(text, "安全限界: 0.60", "β2 の表の SC 杭・せん断が基本設定の β2 になっていません");

            var row = WordDocument.BuildMaterialOptionRows().Single(r => r.Item.Contains("SC杭の安全限界せん断"));
            Assert.AreEqual("β1 = 0.95、β2 = 0.60", row.Choice);
            StringAssert.Contains(row.Note, "β = 0.570");
        }

        [TestMethod]
        public void ScShearBetaRow_DefaultsAreMarked()
        {
            using var _ = new ScBetaScope();
            ConcreteModelOptions.ScUltimateShearBeta1 = ConcreteModelOptions.DefaultScUltimateShearBeta1;
            ConcreteModelOptions.ScUltimateShearBeta2 = ConcreteModelOptions.DefaultScUltimateShearBeta2;

            var row = WordDocument.BuildMaterialOptionRows().Single(r => r.Item.Contains("SC杭の安全限界せん断"));
            Assert.AreEqual("β1 = 1.00、β2 = 0.75（既定）", row.Choice, "既定の β2 = 0.75 を「低減しない」と読めない形で書くこと");
        }

        // ===== 4. SC 杭の M-φ の β (検定の表の値にそろえた) =====

        [TestMethod]
        public void ScMPhiBetaNote_MatchesTheImplementation()
        {
            string precast = TestSource.Read("Graphics_r1", "Models", "InputData", "PrecastPileSection.cs");
            // 計算書の値は計算の定数そのもの。M-φ の β は検定の表 (SC 杭の曲げ: 安全限界 β1 = β2 = 1.0) と同じ
            Assert.AreEqual(1.0, WordDocument.ScMPhiBeta1);
            Assert.AreEqual(1.0, WordDocument.ScMPhiBeta2);
            StringAssert.Contains(precast, "return GetMPhiRelationshipInternal(axialN, MPhiBeta1);");
            Assert.IsFalse(precast.Contains("double beta2 = 0.75;"), "SC 杭の M-φ に表に無い β2 = 0.75 が残っています");

            using var _ = new ScBetaScope();
            ConcreteModelOptions.UseFiberMPhi = false;
            var body = new Body();
            WordDocument.AddBetaFactorTables(body);
            StringAssert.Contains(body.InnerText, "解析用 M-φ 関係", "SC 杭の M-φ の β を書き添えていません");
        }

        // ===== 2. 引抜き抵抗力の式 =====

        [TestMethod]
        public void PullOutFormulas_MatchTheImplementation()
        {
            string src = Sections();
            // 実装: Rt_DLS = Rty (1/3 は掛けない)
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Models", "InputData", "SoilPile.cs"), "Rt_DLS = Rty;");
            Assert.IsFalse(src.Contains(@"\frac{1}{3} R_{TY}"), "損傷限界引抜力の式が 1/3·R_TY のままです (実装は R_TY)");
            StringAssert.Contains(src, @"R_{d} = R_{TY} = \frac{2}{3}\left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) \psi + W");

            // 実装: 周面の抵抗に周長 ψ を掛ける (PileCircumVertical.Rtu/Rtr/Rty)
            string pcv = TestSource.Read("Graphics_r1", "Models", "InputData", "PileCircumVertical.cs");
            StringAssert.Contains(pcv, "public double Rtu => TauT * EffectiveL * Psi;");
            foreach (var name in new[] { "R_{TU} =", "R_{TR} =", "R_{TY} =" })
            {
                var m = Regex.Match(src, Regex.Escape($"AddEq(body, @\"{name}") + "[^\"]*\"");
                Assert.IsTrue(m.Success, $"{name} の式が見つかりません");
                StringAssert.Contains(m.Value, @"\psi + W", $"{name} の式に周長 ψ がありません");
            }
        }

        // ===== 3. 土圧合力ばねの式 =====

        [TestMethod]
        public void EarthPressureSpringFormula_MatchesTheImplementation()
        {
            string src = WithoutCommentLines(Sections());
            Assert.IsFalse(src.Contains(@"(\Delta_{p}-3)y_{sp}"), "分母が (Δp−3)·ysp のままです (実装は (Δp−3·ysp)·|y|)");
            StringAssert.Contains(src, @"\left(\Delta_{p}-3y_{sp}\right)\left|y\right|");
            // 土被り圧は区間の厚さ γh ではなく、区間の代表深さの有効土被り圧 σz' (2026-10-09 修正。
            // EarthPressureSpringDepth20261009Tests)
            StringAssert.Contains(src, @"\p_{p} = \left(q + \sigma_{z}'\right) K_{p} + 2c\sqrt{K_{p}}");
            Assert.IsFalse(src.Contains(@"q + \gamma h"), "土被り圧が区間の厚さ (γh) のままです");

            // 書いた式で実装の値を再現できること (式と実装の照合)
            // 地表面から深さ 0〜2 m の区間 (γ=18、地下水位より上): σz' = (0 + 36)/2 = 18
            var item = new DoatsuGoryokuBaneItem { Gamma = 18, Phi = 30, C = 10, ZTop = 0, ZBtm = -2, StressTop = 0, StressBtm = 36, K0 = 0.5 };
            item.SetDeltaPAndYsp(0.1, 0.01);
            double sigma = (0 + 36) / 2.0;
            double kp = Math.Pow(Math.Tan((45 + 30 / 2.0) * Math.PI / 180), 2);
            double p0 = sigma * 0.5;
            double pp = sigma * kp + 2 * 10 * Math.Sqrt(kp);
            double y = 0.03, dp = 0.1, ysp = 0.01;
            double expected = (pp - p0) * (dp - ysp) * y / (2 * dp * ysp + (dp - 3 * ysp) * Math.Abs(y));
            Assert.AreEqual(expected, item.GetPressure(y), 1e-9);
            Assert.AreEqual(pp - p0, item.GetPressure(0.2), 1e-9);
        }

        // ===== 19. F.T.Pile の説明は杭頭の種類も見る =====

        [TestMethod]
        public void HasFTPile_RequiresThePileTopType()
        {
            var body = new PileBodyInput { PileTopType = "鉄筋定着工法" };
            body.PileTop.FTPile = new FTPile();
            var input = new InputModel { PileBodies = new ObservableCollection<PileBodyInput> { body } };

            Assert.IsFalse(WordDocument.HasFTPile(input), "杭頭の種類が F.T.Pile でないのに F.T.Pile の説明を出します");
            body.PileTopType = "FT-Pile構法";
            Assert.IsTrue(WordDocument.HasFTPile(input));
            body.PileTop.FTPile = null;
            Assert.IsFalse(WordDocument.HasFTPile(input));
            Assert.IsFalse(WordDocument.HasFTPile(null));
        }

        // ===== 15. 設計条件に杭軸力の扱いと杭先端の鉛直ばね =====

        [TestMethod]
        public void DesignConditionRows_IncludeAxialForceAndPileTipSpring()
        {
            var input = new InputModel();
            var rows = WordDocument.BuildDesignConditionRows(input);
            Assert.AreEqual("入力値", rows.Single(r => r.Item == "杭軸力の扱い").Value);
            Assert.AreEqual("杭先端支持", rows.Single(r => r.Item == "杭先端の鉛直ばね").Value);

            input.UseAnalysisAxialForce = true;
            input.UsePsSpringAtPileTip = true;
            input.PsSpringSource = PsSpringSourceMode.Ultimate;
            rows = WordDocument.BuildDesignConditionRows(input);
            Assert.AreEqual("入力値＋応力解析結果", rows.Single(r => r.Item == "杭軸力の扱い").Value);
            Assert.AreEqual("P-S 非線形ばね（極限）", rows.Single(r => r.Item == "杭先端の鉛直ばね").Value);
        }

        [TestMethod]
        public void Kh0ManualNote_SaysXiAndBetaLAreStillApplied()
        {
            // 実装: 手入力の kh0 にも GroupPileEffect.Kh0Factor (= ξ·βL) が掛かる
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Models", "InputData", "HorizontalSoilReaction.cs"),
                "public double GetKh0For(in GroupPileEffect effect) => Kh0 * effect.Kh0Factor;");
            string src = TestSource.Read("Graphics_r1", "Output", "WordDocument.Assumptions.cs");
            StringAssert.Contains(src, "手入力した kh0 にも、群杭係数 ξ と");
        }

        // ===== 17. 杭検討結果まとめの N_Max / N_Min は載せるケースだけ =====

        [TestMethod]
        public void SummaryAxialForces_TakeOnlyReportedCases()
        {
            var pile = new PileLayoutDataItem
            {
                AxialForceVL0 = 50,
                AxialForceLevel1s = new ObservableCollection<double> { 100, 900, 0 },
                AxialForceLevel2s = new ObservableCollection<double> { -300 },
            };
            var cases = new[]
            {
                new LoadCase { Level = 1, No = 1, IsApplicable = true },
                new LoadCase { Level = 1, No = 2, IsApplicable = true, IsIncludedInReport = false }, // 載せない
                new LoadCase { Level = 1, No = 3, IsApplicable = true },                             // 未入力 (0) → 常時
                new LoadCase { Level = 1, No = 4, IsApplicable = true },                             // 欄が無い → 常時 (例外で止めない)
                new LoadCase { Level = 2, No = 1, IsApplicable = false },                            // 適用しない
            };
            var got = WordDocument.ReportedSeismicAxialForces(pile, cases).ToList();
            CollectionAssert.AreEqual(new[] { (1, 100.0), (1, 50.0), (1, 50.0) }, got,
                "載せないケースの軸力・未入力の 0 が N_Max / N_Min に入っています");
        }

        // ===== 18. 杭体諸元の杭先端径 =====

        [TestMethod]
        public void PileToeDiameter_UsesPileToeDia()
        {
            var body = new PileBodyInput { PileToeDia = 1500 };
            Assert.AreEqual("1,500", WordDocument.FormatPileToeDiameter(body));
            Assert.AreEqual("-", WordDocument.FormatPileToeDiameter(new PileBodyInput { PileToeDia = 0 }));
        }

        // ===== 14. 基礎梁の表は接続仮定によらず出す・解析に渡る値を載せる =====

        [TestMethod]
        public void FoundationBeamTables_AreOutputWheneverBeamsExist()
        {
            var fb = new FoundationBeamInput { ConnectionMode = FoundationBeamConnectionMode.RigidBody };
            Assert.IsFalse(WordDocument.ShouldOutputFoundationBeamTables(fb));
            fb.Beams.Add(new FoundationBeam());
            Assert.IsTrue(WordDocument.ShouldOutputFoundationBeamTables(fb), "剛体連結でも基礎梁は解析に入ります");
            Assert.IsFalse(WordDocument.ShouldOutputFoundationBeamTables(null));

            string src = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Output", "WordDocument.cs"),
                "private static void AddFoundationBeamInputTables(");
            foreach (var used in new[] { "sec.ShearAreaY", "sec.ShearAreaZ", "sec.TorsionalMoment", "beam.AngleBeta" })
                StringAssert.Contains(src, used, $"解析に渡る {used} が基礎梁の表にありません");
        }

        // ===== 5. 杭頭 M-θ の曲線は画面と同じくケースの控えから =====

        [TestMethod]
        public void MThetaReport_UsesSnapshotsOnlyAndMarksRigid()
        {
            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Output", "WordDocument.Charts.cs"),
                "private void AddMThetaCurves(");
            Assert.IsFalse(body.Contains("GetMThetaRelationship("), "計算書が入力からの理論曲線を控えより優先しています (画面と違う)");
            StringAssert.Contains(body, "IsRigidMThetaStiffness(");

            Assert.IsTrue(WordDocument.IsRigidMThetaStiffness(1e10));
            Assert.IsFalse(WordDocument.IsRigidMThetaStiffness(1e8));
            Assert.AreEqual(1e9, WordDocument.MThetaRigidThreshold);
            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "GraphViewModel.CurveGraphs.cs"),
                "const double KRigidThreshold = 1e9;", "画面の剛結の閾値と計算書の閾値が違います");
        }

        // ===== 9. 自動計算の地盤変位は液状化の曲線も描く =====

        [TestMethod]
        public void GroundDisplacement_DrawsLiquefactionCurves()
        {
            var ground = new GroundInput();
            ground.GroundMassesData = new ObservableCollection<GroundMassDataInput>
            {
                new() { DmaxUStar = new ObservableCollection<double> { 10, 20 }, DmaxUStarSigmaGammaCyH = new ObservableCollection<double> { 30, 60 } },
                new() { DmaxUStar = new ObservableCollection<double> { 5, 8 },  DmaxUStarSigmaGammaCyH = new ObservableCollection<double> { 12, 25 } },
            };
            double[] depths = [0.0, -5.0];
            var (xs, ys) = WordDocument.CollectAutoDisplacement(ground, depths, level: 1, isLiq: true);
            CollectionAssert.AreEqual(new[] { 60.0, 25.0 }, xs, "液状化ケースの地盤変位 (DmaxUStarSigmaGammaCyH) を描いていません");
            CollectionAssert.AreEqual(depths, ys);
            Assert.AreEqual(20.0, WordDocument.CollectAutoDisplacement(ground, depths, 1, isLiq: false).Xs[0]);

            // 液状化を算定していない (全点 0) なら描かない
            foreach (var m in ground.GroundMassesData) m.DmaxUStarSigmaGammaCyH = new ObservableCollection<double> { 0, 0 };
            Assert.AreEqual(0, WordDocument.CollectAutoDisplacement(ground, depths, 0, isLiq: true).Xs.Length);

            Assert.IsFalse(WordDocument.GroundDisplacementCaptionMode(isCustom: false).Contains("任意"),
                "自動計算の図を「任意地盤変位」と題しています");
        }

        // ===== 12. 図を作れなかったら省いたことを書く =====

        [TestMethod]
        public void FailedDiagrams_AreNotedNotSilentlyDropped()
        {
            string renderer = TestSource.Read("Graphics_r1", "Output", "DiagramRenderer.cs");
            Assert.IsFalse(Regex.IsMatch(renderer, @"catch \(Exception ex\)\s*\{\s*Log\.Warning\([^;]*\);\s*return (\[\]|Array\.Empty<byte>\(\));"),
                "描画の失敗を空の配列で握りつぶしています (計算書に図の無い見出し・キャプションが残る)");

            string doc = TestSource.Read("Graphics_r1", "Output", "WordDocument.cs");
            Assert.AreEqual(5, Regex.Matches(doc, Regex.Escape("{ if (AddPilingLayoutDiagramByMm(")).Count,
                "杭配置・応力マップのキャプションを、図を置けたときだけに付けていません");
            Assert.AreEqual(2, Regex.Matches(doc, Regex.Escape("if (AddPileForceDiagramByMm(")).Count);

            string diagrams = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Output", "WordDocument.Diagrams.cs"),
                "private void AddGroupPileSettlementContourDiagram(");
            Assert.IsFalse(Regex.IsMatch(diagrams, @"settlementData\.Count == 0\)\s*return;"),
                "群杭沈下の分布図を黙って省いています");
        }

        // ===== 20. 慣性力の作用位置 =====

        [TestMethod]
        public void InertiaForceText_SaysItActsAtTheInertiaCenter()
        {
            string doc = TestSource.Read("Graphics_r1", "Output", "WordDocument.cs");
            Assert.IsFalse(doc.Contains("杭頭位置に \", Tex(@\"\\beta_{U} P_{s}"), "慣性力が杭頭位置に作用すると書いています");
            StringAssert.Contains(doc, "荷重ケースごとに入力した慣性力中心（X・Y・標高）に");
        }

        // ===== 小さなもの: F.T.Pile の記号の単位 =====

        [TestMethod]
        public void FTPileSymbolUnits_AreMillimetres()
        {
            // 実装は mm (FTPile.Ls = 600、Ds = 500、δa = εa·Ls)
            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Output", "WordDocument.Descriptions.cs"),
                "private void AddDescriptionFTPile(");
            foreach (var old in new[] { "鋼棒の配置距離[m]", "鋼棒の有効長さ[m]", "鋼棒の許容伸び[m]" })
                Assert.IsFalse(body.Contains(old), $"F.T.Pile の記号の単位が m のままです: {old}");
            Assert.AreEqual(600.0, new FTPileTensionBars().Ls);
        }
    }
}

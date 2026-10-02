using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace TestProject1
{
    /// <summary>
    /// 入力の検査の網羅表: 入力ごとに、おかしな値を入れたとき<b>どの段階で</b>捕まるか
    /// (画面で受け付けない・解析の前に止める・解析の前に注意する・計算書に書く) を、実際に入れて確かめる。
    ///
    /// <para>表 (Docs/入力の検査一覧.md) は、ここで<b>観測した結果から</b>作る。手で書いた一覧は実装から取り残されるので、
    /// 表の各行は「その値を入れたら実際にこうなった」ことを毎回確かめた行になる。期待と違う段階で捕まった
    /// (または捕まらなくなった) ら落ちる。表を作り直すときは UPDATE_INPUT_CHECK_TABLE=1。</para>
    ///
    /// <para>土台は計算例9 (場所打ち RC 杭 18 本)。1 つの行につき 1 か所だけ壊す。</para>
    /// </summary>
    [TestClass]
    public class InputCheckCoverageTests
    {
        [Flags]
        public enum Stage
        {
            None = 0,
            /// <summary>画面 (入力の値の設定) で受け付けない・範囲に収める</summary>
            Screen = 1,
            /// <summary>解析の前に止める</summary>
            Block = 2,
            /// <summary>解析の前に注意する (止めない)</summary>
            Warn = 4,
            /// <summary>計算書の「入力についての注意」に書く</summary>
            Report = 8,
        }

        /// <param name="Input">入力の名前 (画面の呼び方)</param>
        /// <param name="Bad">入れた値</param>
        /// <param name="Break">土台のモデルに、おかしな値を入れる。値を受け付けなかったら true を返す (画面で直した)</param>
        /// <param name="Expected">捕まる段階</param>
        /// <param name="Settlement">群杭の沈下の土層 (解析前の検査が別)</param>
        public sealed record Case(string Screen, string Input, string Bad, Func<InputModel, bool> Break, Stage Expected, bool Settlement = false);

        private static PileSection Section(InputModel m) => m.PileBodies[0].PileBodySegments[0].PileSection;

        /// <summary>値を入れ、入れた値のまま残ったか (false なら画面で受け付けなかった)。</summary>
        private static bool Rejected(Func<double> read, Action write, double bad)
        {
            write();
            double now = read();
            return !(now.Equals(bad) || (double.IsNaN(bad) && double.IsNaN(now)));
        }

        internal static IReadOnlyList<Case> Cases { get; } =
        [
            // ── 地盤 ──
            new("地盤", "土層の層厚", "0", m => Rejected(() => m.GroundsInput[0].GroundLayers[0].LayerThickness, () => m.GroundsInput[0].GroundLayers[0].LayerThickness = 0, 0), Stage.Block),
            new("地盤", "土層の N 値", "0", m =>
            {
                var layer = m.GroundsInput[0].GroundLayers.First(l => l.NValue > 0);
                return Rejected(() => layer.NValue, () => layer.NValue = 0, 0);
            }, Stage.Warn | Stage.Report),
            new("地盤", "土質点の N 値", "0", m =>
            {
                var point = m.GroundsInput[0].GroundMassesData.First(p => p.NValue > 0);
                return Rejected(() => point.NValue, () => point.NValue = 0, 0);
            }, Stage.Warn | Stage.Report),
            new("地盤", "土層の N 値", "−5", m => Rejected(() => m.GroundsInput[0].GroundLayers[0].NValue, () => m.GroundsInput[0].GroundLayers[0].NValue = -5, -5), Stage.Screen | Stage.Warn | Stage.Report),
            new("地盤", "粘性土の粘着力 Cu", "0", m =>
            {
                var l = m.GroundsInput[0].GroundLayers[0];
                l.GranularityClass = "粘性土";
                return Rejected(() => l.Cohesive, () => l.Cohesive = 0, 0);
            }, Stage.Warn | Stage.Report),
            new("地盤", "土層の変形係数 Es", "0", m => Rejected(() => m.GroundsInput[0].GroundLayers[0].Es, () => m.GroundsInput[0].GroundLayers[0].Es = 0, 0), Stage.Warn | Stage.Report),
            new("地盤", "地表面の加速度 (レベル1)", "200", m => Rejected(() => m.GroundsInput[0].GroundAcceleration1, () => m.GroundsInput[0].GroundAcceleration1 = 200, 200), Stage.Screen),

            // ── 杭配置 ──
            new("杭配置", "杭体の番号", "99 (無い杭体)", m => Rejected(() => m.PileLayoutItems[0].PileBodyNo, () => m.PileLayoutItems[0].PileBodyNo = 99, 99), Stage.Block),
            new("杭配置", "地盤の番号", "99 (無い地盤)", m => Rejected(() => m.PileLayoutItems[0].GroundNo, () => m.PileLayoutItems[0].GroundNo = 99, 99), Stage.Block),
            new("杭配置", "群杭係数 ξ", "0", m => Rejected(() => m.PileLayoutItems[0].GroupPileFactor, () => m.PileLayoutItems[0].GroupPileFactor = 0, 0), Stage.Block),
            new("杭配置", "群杭係数 ξ", "1.5", m => Rejected(() => m.PileLayoutItems[0].GroupPileFactor, () => m.PileLayoutItems[0].GroupPileFactor = 1.5, 1.5), Stage.Warn | Stage.Report),
            new("杭配置", "杭間隔比 R/B", "0 (未入力)", m => Rejected(() => m.PileLayoutItems[0].PileSpacingFactor, () => m.PileLayoutItems[0].PileSpacingFactor = 0, 0), Stage.Warn | Stage.Report),
            new("杭配置", "杭の位置", "隣の杭と同じ", m =>
            {
                var (a, b) = (m.PileLayoutItems[0], m.PileLayoutItems[1]);
                b.X = a.X; b.Y = a.Y;
                return false;
            }, Stage.Warn | Stage.Report),

            // ── 杭体 ──
            new("杭体", "杭区間の長さ", "0", m => Rejected(() => m.PileBodies[0].PileBodySegments[0].SegmentLength, () => m.PileBodies[0].PileBodySegments[0].SegmentLength = 0, 0), Stage.Block),
            new("杭体", "コンクリートの外径", "0", m => Rejected(() => Section(m).ConcreteOutDia, () => Section(m).ConcreteOutDia = 0, 0), Stage.Block),
            new("杭体", "コンクリートの設計基準強度 Fc", "0", m => Rejected(() => Section(m).ConcreteFc, () => Section(m).ConcreteFc = 0, 0), Stage.Screen),
            new("杭体", "コンクリートのヤング係数 Ec", "5,000 N/mm²", m => Rejected(() => Section(m).ConcreteE, () => Section(m).ConcreteE = 5000, 5000), Stage.Warn | Stage.Report),
            new("杭体", "コンクリートのヤング係数 Ec", "500 N/mm²", m => Rejected(() => Section(m).ConcreteE, () => Section(m).ConcreteE = 500, 500), Stage.Block),

            // ── 荷重 ──
            // 収束判定の基準が既定 (外力または反力) なら慣性力 0 でも解けるので止めない。外力だけを基準にしたときに止める
            new("荷重", "荷重の組合せ係数 βU・βL", "両方 0 (収束判定の基準が外力)", m =>
            {
                m.FundamentalInput.ResidualReference = ResidualReferenceMode.ExternalForce;
                foreach (var c in m.LoadCasesInput.AllLoadCombinations) { c.Beta1 = 0; c.Beta2 = 0; }
                return false;
            }, Stage.Block),

            // ── 基礎梁 ──
            new("基礎梁", "材料のポアソン比", "0.7", m =>
            {
                var mat = new BeamMaterial();
                return Rejected(() => mat.PoissonRatio, () => mat.PoissonRatio = 0.7, 0.7);
            }, Stage.Screen),

            // ── 群杭の沈下 ──
            new("群杭の沈下", "沈下用土層の変形係数 Ek", "0", m => SettlementLayer(m, l => l.Ek = 0), Stage.Block, Settlement: true),
            new("群杭の沈下", "沈下用土層のポアソン比", "0.6", m => SettlementLayer(m, l => l.PoissonsRatio = 0.6), Stage.Block, Settlement: true),
            new("群杭の沈下", "沈下用土層の層厚", "0", m => SettlementLayer(m, l => l.Thickness = 0), Stage.Block, Settlement: true),
        ];

        private static bool SettlementLayer(InputModel m, Action<SettlementSoilLayer> breakIt)
        {
            var layer = new SettlementSoilLayer { Thickness = 5, Ek = 20000, PoissonsRatio = 0.3 };
            m.PileGroupSettlement.SettlementSoilLayers = [layer];
            breakIt(layer);
            return false;
        }

        private static string Key(Diagnostic d) => $"{d.Severity}|{d.Message}";

        /// <summary>1 つの行を実際に入れて、捕まった段階を返す。</summary>
        private static (Stage Observed, string Detail) Observe(Case c)
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) throw new AssertInconclusiveException("例題ロード失敗: " + error);

            if (c.Settlement)
            {
                bool rejectedS = c.Break(model);
                var problems = PileDesign.Models.InputData.Steinnbrener.DescribeLayerProblems(model.PileGroupSettlement.SettlementSoilLayers);
                return ((rejectedS ? Stage.Screen : Stage.None) | (problems.Count > 0 ? Stage.Block : Stage.None), string.Join(" / ", problems));
            }

            var blockersBefore = CheckInputData.CollectAnalysisBlockers(model).Select(Key).ToHashSet();
            var warningsBefore = CheckInputData.CollectInputWarningDiagnostics(model).Select(Key).ToHashSet();
            var reportBefore = PileDesign.Output.WordDocument.InputNoticeLines(model).ToHashSet();

            bool rejected = c.Break(model);

            var newBlockers = CheckInputData.CollectAnalysisBlockers(model).Where(d => !blockersBefore.Contains(Key(d))).ToList();
            var newWarnings = CheckInputData.CollectInputWarningDiagnostics(model).Where(d => !warningsBefore.Contains(Key(d))).ToList();
            var newReport = PileDesign.Output.WordDocument.InputNoticeLines(model).Where(l => !reportBefore.Contains(l)).ToList();

            Stage s = (rejected ? Stage.Screen : Stage.None)
                | (newBlockers.Count > 0 ? Stage.Block : Stage.None)
                | (newWarnings.Count > 0 ? Stage.Warn : Stage.None)
                | (newReport.Count > 0 ? Stage.Report : Stage.None);
            string detail = string.Join(" / ", newBlockers.Concat(newWarnings).Select(d => d.Message).Distinct().Take(2));
            return (s, detail);
        }

        internal static string Describe(Stage s)
        {
            if (s == Stage.None) return "検査なし";
            var parts = new List<string>();
            if (s.HasFlag(Stage.Screen)) parts.Add("画面で受け付けない");
            if (s.HasFlag(Stage.Block)) parts.Add("解析の前に止める");
            if (s.HasFlag(Stage.Warn)) parts.Add("解析の前に注意");
            if (s.HasFlag(Stage.Report)) parts.Add("計算書に書く");
            return string.Join("・", parts);
        }

        /// <summary>計算書の「計算条件・仮定」章が入力の注意を書くこと (表の「計算書に書く」はこの経路を前提にしている)。</summary>
        [TestMethod]
        public void TheReportWritesTheInputWarnings()
        {
            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Output", "WordDocument.Assumptions.cs"), "AddCalculationAssumptionsSection");
            StringAssert.Contains(body, "NoteInputWarnings(body, inputModel);");

            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }
            model.PileLayoutItems[0].GroupPileFactor = 1.5;
            var lines = PileDesign.Output.WordDocument.InputNoticeLines(model);
            foreach (var l in lines) Console.WriteLine(l);
            Assert.IsTrue(lines.Any(l => l.Contains("群杭係数", StringComparison.Ordinal)), "群杭係数の注意が計算書に書かれません");
        }

        internal static string TablePath => Path.Combine(TestSource.Dir(), "Docs", "入力の検査一覧.md");

        [TestMethod]
        [Timeout(600000)]
        public void EveryBadInput_IsCaughtAtTheExpectedStage_AndTheTableIsCurrent()
        {
            var rows = new List<(Case Case, Stage Observed, string Detail)>();
            var problems = new List<string>();
            foreach (var c in Cases)
            {
                var (observed, detail) = Observe(c);
                rows.Add((c, observed, detail));
                if (observed != c.Expected)
                    problems.Add($"{c.Screen} / {c.Input} = {c.Bad}: 期待「{Describe(c.Expected)}」、実際「{Describe(observed)}」 {detail}");
            }
            TestSource.AssertScanned(rows.Count, 20, "入力の検査の行");

            var md = new StringBuilder();
            md.AppendLine("# 入力の検査一覧");
            md.AppendLine();
            md.AppendLine("入力におかしな値を入れたとき、どの段階で捕まるかの一覧です。");
            md.AppendLine("各行は試験 (`TestProject1/InputCheckCoverageTests.cs`) が計算例9 に実際にその値を入れて確かめた結果で、");
            md.AppendLine("実装が変わると試験が落ちます。作り直しは `UPDATE_INPUT_CHECK_TABLE=1` を付けて試験を走らせます。");
            md.AppendLine();
            md.AppendLine("- **画面で受け付けない**: 入力した値を受け付けず、範囲に収めるか元の値に戻す");
            md.AppendLine("- **解析の前に止める**: 解析を始める前の検査で止め、直す場所を示す");
            md.AppendLine("- **解析の前に注意**: 解析は止めないが、解析の前の確認と「入力の診断」に出す");
            md.AppendLine("- **計算書に書く**: 計算書の「計算条件・仮定」章の「入力についての注意」に書く");
            md.AppendLine();
            md.AppendLine("| 画面 | 入力 | 入れた値 | 捕まる段階 |");
            md.AppendLine("|---|---|---|---|");
            foreach (var (c, observed, _) in rows)
                md.AppendLine($"| {c.Screen} | {c.Input} | {c.Bad} | {Describe(observed)} |");
            string table = md.ToString().Replace("\r\n", "\n");

            if (Environment.GetEnvironmentVariable("UPDATE_INPUT_CHECK_TABLE") == "1" && problems.Count == 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TablePath)!);
                File.WriteAllText(TablePath, table, new UTF8Encoding(true));
            }

            Assert.AreEqual(0, problems.Count, "入力の検査の段階が期待と違います:\n  " + string.Join("\n  ", problems));
            Assert.IsTrue(File.Exists(TablePath), $"入力の検査一覧がありません: {TablePath} (UPDATE_INPUT_CHECK_TABLE=1 で作る)");
            Assert.AreEqual(table, File.ReadAllText(TablePath).Replace("\r\n", "\n"),
                "入力の検査一覧が実装と食い違っています。UPDATE_INPUT_CHECK_TABLE=1 で作り直してください");
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// NewJ-BAR (開先付き異形棒鋼を鋼管に溶接する杭頭補強) の計算を、
    /// 「NewJ-BAR WSD390 WSD490 設計マニュアル (杭頭補強編)」(2026 年 9 月改訂) の値と突き合わせる。
    /// </summary>
    [TestClass]
    public class NewJBarTests
    {
        /// <summary>図 4.22 の出力例: Φ600 SKK490 t=9、WSD390 10-WD32J、中詰補強筋なし、Fc24。</summary>
        private static NewJBar ManualExample()
        {
            var j = new NewJBar { Grade = "WSD390", Size = "WD32J", Count = 10 };
            j.SetPile(600, 9, "SKK490");
            j.SetPileCapConcrete(24, 23);
            return j;
        }

        private static void AreClose(double expected, double actual, double relTol, string what)
            => Assert.IsTrue(Math.Abs(actual - expected) <= relTol * Math.Abs(expected),
                $"{what}: 期待 {expected}、実際 {actual} (許容 {relTol:P1})");

        [TestMethod]
        public void TheVirtualSectionFollowsTheManual()
        {
            var j = ManualExample();
            // 図 4.22「RC 断面 D = 600 + 264」(= 2 × 32 + 200)
            Assert.AreEqual(864.0, j.VirtualDiameter, 1e-9);
            Assert.IsTrue(j.UsesRing, "10 本は円環に置き換える");
            Assert.AreEqual(15.0, j.YoungRatio, "Fc24 の n");

            // 4.3.1 1) b): 500 未満は D + 200、500 以上は D + 2·d + 200
            j.SetPile(499, 9, "SKK490");
            Assert.AreEqual(699.0, j.VirtualDiameter, 1e-9);
            j.SetPile(500, 9, "SKK490");
            Assert.AreEqual(500.0 + 64.0 + 200.0, j.VirtualDiameter, 1e-9);
        }

        /// <summary>
        /// 図 4.22 の「応力度の検討」(短期、Md = 670 kN·m)。Nmin = 589 kN と Nmax = 1,767 kN のときの
        /// NewJ-BAR の引張・圧縮とコンクリートの圧縮の応力度 (N/cm² を N/mm² に直した値)。
        /// 出力例の補強筋の半径は 31.67 cm で、こちらは D/2 + 公称直径/2 = 31.59 cm のため 0.5% の幅で見る。
        /// </summary>
        [TestMethod]
        public void TheShortTermStressesMatchTheManualExample()
        {
            var j = ManualExample();
            var a = j.GetElasticStresses(589e3, 670e6);
            AreClose(15.519, a.Concrete, 0.005, "Nmin コンクリート");
            AreClose(316.898, a.BarTension, 0.005, "Nmin 引張");
            AreClose(148.133, a.BarCompression, 0.005, "Nmin 圧縮");
            var b = j.GetElasticStresses(1767e3, 670e6);
            AreClose(14.775, b.Concrete, 0.005, "Nmax コンクリート");
            AreClose(168.956, b.BarTension, 0.005, "Nmax 引張");
            AreClose(161.475, b.BarCompression, 0.005, "Nmax 圧縮");

            // 出力例ではコンクリートが 15.5 < 16 (= 2/3·24) で OK。許容曲げモーメントはそれより少し大きい
            double ma = j.GetAllowableMoment(589e3) * 1e-6;
            Assert.IsTrue(ma > 670.0 && ma < 670.0 * 16.0 / 15.519 * 1.01, $"Ma(589) = {ma}");
            // 許容値に達したときはコンクリートか補強筋のどちらかがちょうど許容値になる
            var at = j.GetElasticStresses(589e3, ma * 1e6);
            Assert.AreEqual(16.0, at.Concrete, 1e-3, "コンクリートで決まる");
        }

        /// <summary>
        /// 図 4.22 の終局曲げモーメント (N = 0 で 1,021.3、N = 2,356 kN で 1,478.1 kN·m)。
        /// 層分割の細部 (補強筋の半径・層の面積の近似) の違いで、こちらは 0.7〜0.9% 大きい。1.5% の幅で見る。
        /// </summary>
        [TestMethod]
        public void TheUltimateMomentsMatchTheManualExample()
        {
            var j = ManualExample();
            AreClose(1021.3, j.GetUltimateMoment(0.0) * 1e-6, 0.015, "Mu(N=0)");
            AreClose(1478.1, j.GetUltimateMoment(2356e3) * 1e-6, 0.015, "Mu(N=2356)");
            // 純引張は補強筋の終局強度 × 全断面積、それを超える引張は範囲の外
            double pureTension = -j.TotalBarArea * j.UltimateStrength;
            Assert.IsTrue(double.IsNaN(j.GetUltimateMoment(pureTension * 1.01)));
            Assert.AreEqual(1.1 * 390.0, j.UltimateStrength, 1e-9, "WSD390 は 1.1F");
            j.Grade = "WSD490";
            Assert.AreEqual(490.0, j.UltimateStrength, 1e-9, "WSD490 は F");
        }

        /// <summary>主筋 8 本未満は配置角度を振って最小を取る (弱軸方向)。本数を増やせば耐力は下がらない。</summary>
        [TestMethod]
        public void FewerThanEightBarsTakeTheWeakAxis()
        {
            var j = ManualExample();
            j.Count = 4;
            Assert.IsFalse(j.UsesRing);
            double mu4 = j.GetUltimateMoment(0.0);
            double ma4 = j.GetAllowableMoment(0.0);
            j.Count = 6;
            double mu6 = j.GetUltimateMoment(0.0);
            j.Count = 8;
            double mu8 = j.GetUltimateMoment(0.0);
            Assert.IsTrue(mu4 > 0 && ma4 > 0);
            Assert.IsTrue(mu4 < mu6 && mu6 < mu8, $"{mu4} {mu6} {mu8}");
        }

        /// <summary>(4.6) 式の必要定着長さ。4.2.3 解説の表 (付着割裂の場合) と一致する。</summary>
        [TestMethod]
        public void TheRequiredAnchorageLengthMatchesTheManualTable()
        {
            var a = new NewJBar { Grade = "WSD390", Size = "WD32J", Count = 8 };
            a.SetPileCapConcrete(21, 23);
            Assert.AreEqual(1.425, a.Fb, 1e-12);
            Assert.AreEqual(1.0 * 1.25 * 390 * 32 / 10 / 1.425, a.RequiredAnchorageLength, 1e-9);
            Assert.AreEqual(1095.0, a.RequiredAnchorageLength, 0.5, "1.0×1.25×390×32/10/1.425 = 1095 mm");
            Assert.AreEqual(35.0 * 32, a.AnchorageLength, 1e-9, "標準品は 35d");

            var b = new NewJBar { Grade = "WSD490", Size = "WD41J", Count = 8 };
            b.SetPileCapConcrete(24, 23);
            Assert.AreEqual(1674.0, b.RequiredAnchorageLength, 0.5, "1.0×1.25×490×41/10/1.50 = 1674 mm");
            Assert.AreEqual(42.0 * 41, b.AnchorageLength, 1e-9, "標準品は 42d");
            b.IsConfined = false;
            Assert.AreEqual(1.25 * 1674.0, b.RequiredAnchorageLength, 1.0, "拘束なしは α = 1.25");
        }

        /// <summary>1.2 3) の鋼管の最小板厚と表 4.3 の標準埋込み深さ。</summary>
        [TestMethod]
        public void TheTablesFollowTheManual()
        {
            (string grade, string size, string pipe, double tmin, double l0)[] rows =
            [
                ("WSD390", "WD32J", "SKK400", 8, 220), ("WSD390", "WD32J", "SKK490", 6, 220),
                ("WSD390", "WD35J", "SKK400", 9, 240), ("WSD390", "WD35J", "SKK490", 7, 240),
                ("WSD390", "WD38J", "SKK400", 10, 240), ("WSD390", "WD38J", "SKK490", 8, 240),
                ("WSD490", "WD32J", "SKK400", 8, 260), ("WSD490", "WD32J", "SKK490", 8, 230),
                ("WSD490", "WD35J", "SKK400", 9, 280), ("WSD490", "WD35J", "SKK490", 8, 260),
                ("WSD490", "WD38J", "SKK400", 10, 280), ("WSD490", "WD38J", "SKK490", 8, 260),
                ("WSD490", "WD41J", "SKK400", 11, 300), ("WSD490", "WD41J", "SKK490", 9, 270),
            ];
            foreach (var r in rows)
            {
                var j = new NewJBar { Grade = r.grade, Size = r.size, Count = 8 };
                j.SetPile(800, 12, r.pipe);
                Assert.AreEqual(r.tmin, j.MinimumPipeThickness, $"{r.grade} {r.size} {r.pipe} 最小板厚");
                Assert.AreEqual(r.l0, j.StandardEmbedmentLength, $"{r.grade} {r.size} {r.pipe} 標準埋込み深さ");
            }
        }

        /// <summary>(4.7) 式: Qa = D·L·fcc、fcc = 2 × (2/3)·Fc。終局は 1.5 倍。</summary>
        [TestMethod]
        public void TheBearingShearFollowsTheManual()
        {
            var j = ManualExample();
            Assert.AreEqual(220.0, j.EffectiveEmbedmentLength, "未入力は標準埋込み深さ");
            Assert.AreEqual(600.0 * 220.0 * 2.0 * 2.0 / 3.0 * 24.0, j.ShortTermAllowableShear, 1e-6);
            Assert.AreEqual(1.5 * j.ShortTermAllowableShear, j.UltimateShear, 1e-6);
            j.EmbedmentLength = 300;
            Assert.AreEqual(600.0 * 300.0 * 32.0, j.ShortTermAllowableShear, 1e-6);
        }

        [TestMethod]
        public void OutOfRangeInputsAreNamed()
        {
            var j = ManualExample();
            Assert.AreEqual(0, j.GetApplicabilityProblems().Count, string.Join("\n", j.GetApplicabilityProblems()));

            j.Size = "WD41J";
            Assert.IsFalse(j.IsComputable, "WD41J は WSD390 に無い");
            Assert.IsTrue(j.GetApplicabilityProblems().Any(p => p.Contains("WD41J")));

            j = ManualExample();
            j.Grade = "WSD490";
            j.SetPileCapConcrete(21, 23);
            Assert.IsTrue(j.GetApplicabilityProblems().Any(p => p.Contains("Fc")), "WSD490 は Fc24 以上");

            j = ManualExample();
            j.SetPile(600, 5, "SKK490");
            Assert.IsTrue(j.GetApplicabilityProblems().Any(p => p.Contains("板厚")));

            j = ManualExample();
            j.AnchorageLengthInD = 25;
            Assert.IsTrue(j.GetApplicabilityProblems().Any(p => p.Contains("定着長さ")));

            j = ManualExample();
            j.Count = 3;
            Assert.IsTrue(j.GetApplicabilityProblems().Any(p => p.Contains("4 本以上")));
        }

        /// <summary>入力だけを保存し、杭とパイルキャップから受け取る値・派生値は書き出さない。写しは別の実体。</summary>
        [TestMethod]
        public void OnlyTheInputsAreSavedAndCopied()
        {
            var j = ManualExample();
            j.EmbedmentLength = 260; j.AnchorageLengthInD = 40; j.IsConfined = false;
            j.InfillBarCount = 6; j.InfillBarSize = "D25"; j.InfillBarGrade = "SD390"; j.InfillBarCover = 80; j.InfillDepth = 1300;
            string json = System.Text.Json.JsonSerializer.Serialize(j);
            foreach (var derived in new[] { "VirtualDiameter", "PileOuterDiameter", "Fc", "BarArea", "UltimateStrength",
                         "InfillOuterDiameter", "InfillRadius", "InfillTotalArea", "HasSteelPipe", "TopSectionType", "PileBodyType" })
                Assert.IsFalse(json.Contains($"\"{derived}\""), $"{derived} を書き出している: {json}");
            var back = System.Text.Json.JsonSerializer.Deserialize<NewJBar>(json)!;
            Assert.AreEqual(("WSD390", "WD32J", 10, 260.0, 40.0, false),
                (back.Grade, back.Size, back.Count, back.EmbedmentLength, back.AnchorageLengthInD, back.IsConfined));
            Assert.AreEqual((6, "D25", "SD390", 80.0, 1300.0),
                (back.InfillBarCount, back.InfillBarSize, back.InfillBarGrade, back.InfillBarCover, back.InfillDepth));

            var top = new PileTop { NewJBar = j };
            var copy = top.DeepCopy();
            Assert.IsNotNull(copy.NewJBar);
            Assert.AreNotSame(j, copy.NewJBar);
            copy.NewJBar!.Count = 12;
            Assert.AreEqual(10, j.Count, "写しを変えても元は変わらない");
        }

        /// <summary>鋼管杭の杭頭タイプに出し、水平解析では剛接合にする。名前に「鉄筋定着工法」を含めない (Contains の分岐に入る)。</summary>
        [TestMethod]
        public void ItIsASteelPilePileTopTypeSolvedAsRigid()
        {
            Assert.IsTrue(PileBodyInput.SteelPileTopTypeOption.Contains(NewJBar.PileTopTypeName));
            Assert.IsFalse(NewJBar.PileTopTypeName.Contains("鉄筋定着工法"));
            var body = new PileBodyInput { PileBodyType = PileDesign.Constants.PileTypeNames.SteelPipe };
            body.PileTopType = NewJBar.PileTopTypeName;
            Assert.IsFalse(body.HasSelectablePileHeadRotation(), "基本設定の杭頭回転角の対象ではない");
            var def = body.GetMThetaRelationship(1000.0);
            Assert.AreEqual(PileDesign.FEM.PileHeadRotationMode.Rigid, def.Mode);
        }

        /// <summary>
        /// SC 杭・場所打ち鋼管コンクリート杭 (外殻鋼管) の杭頭タイプにも出し、どれも剛接合で解く (マニュアル 1.2 1) の対象杭種)。
        /// </summary>
        [TestMethod]
        public void ItIsAlsoOfferedForScAndInsituSteelPipePiles()
        {
            Assert.IsTrue(PileBodyInput.PrecastConcretePileTopTypeOption.Contains(NewJBar.PileTopTypeName));
            Assert.IsTrue(PileBodyInput.InsituSteelPipedConcretePileTopTypeOption.Contains(NewJBar.PileTopTypeName));
            Assert.IsFalse(PileBodyInput.InsituReinforcedConcretePileTopTypeOption.Contains(NewJBar.PileTopTypeName),
                "場所打ち RC 杭には鋼管が無い");
            foreach (var type in new[] { PileDesign.Constants.PileTypeNames.PrecastConcrete, PileDesign.Constants.PileTypeNames.InsituSteelPipeConcrete })
            {
                var body = new PileBodyInput { PileBodyType = type };
                body.PileTopType = NewJBar.PileTopTypeName;
                Assert.IsFalse(body.HasSelectablePileHeadRotation(), type);
                Assert.AreEqual(PileDesign.FEM.PileHeadRotationMode.Rigid, body.GetMThetaRelationship(1000.0).Mode, type);
            }
        }

        private static PileSection ScSection(string productName)
        {
            var s = new PileSection
            {
                PileBodyType = PileDesign.Constants.PileTypeNames.PrecastConcrete,
                PileSectionType = PileDesign.Constants.PileTypeNames.Sc,
                PipeGrade = "SKK490",
            };
            s.SelectedPrecastPile.Name = productName;
            s.RecalculateSelectedPrecastPile();
            return s;
        }

        /// <summary>
        /// SC 杭はカタログの鋼管 (外径・板厚) に溶接する。SC-600 t9 (SKK490) は図 4.22 の出力例と同じ鋼管なので、同じ値になる
        /// (マニュアル 4.2 解説: SC 杭も中詰めの有無に関わらず充実の円形断面として計算する)。
        /// </summary>
        [TestMethod]
        public void AnScPileUsesItsSteelPipe()
        {
            var top = new PileTop { PileCapFc = 24, PileCapGamma = 23 };
            var j = top.NewJBarFor(NewJBar.PileTopTypeName, ScSection("SC-600-標準-80-9"))!;
            j.Grade = "WSD390"; j.Size = "WD32J"; j.Count = 10;
            Assert.IsTrue(j.HasSteelPipe);
            Assert.AreEqual((600.0, 9.0, "SKK490"), (j.PileOuterDiameter, j.PipeThickness, j.PipeGrade));
            Assert.AreEqual(PileDesign.Constants.PileTypeNames.Sc, j.TopSectionType);
            var manual = ManualExample();
            Assert.AreEqual(manual.VirtualDiameter, j.VirtualDiameter);
            Assert.AreEqual(manual.GetUltimateMoment(0), j.GetUltimateMoment(0), 1e-6);
            Assert.AreEqual(manual.GetAllowableMoment(589e3), j.GetAllowableMoment(589e3), 1e-6);
            Assert.AreEqual(0, j.GetApplicabilityProblems().Count, string.Join(" / ", j.GetApplicabilityProblems()));
        }

        /// <summary>
        /// 外面に鋼管の無い最上段 (PHC 杭・場所打ち鋼管コンクリート杭の鉄筋コンクリート部) では断面を組まず、理由を名指す。
        /// 既製杭は PHC 杭でも PipeDia に杭径が入っているので、径だけを見ると鋼管杭として組んでしまう。
        /// </summary>
        [TestMethod]
        public void ATopSectionWithoutASteelPipeIsNamed()
        {
            var phc = new PileSection
            {
                PileBodyType = PileDesign.Constants.PileTypeNames.PrecastConcrete,
                PileSectionType = PileDesign.Constants.PileTypeNames.Phc,
            };
            phc.SelectedPrecastPile.Name = PileSection.PHCOption.First();
            phc.RecalculateSelectedPrecastPile();
            var top = new PileTop { PileCapFc = 24, PileCapGamma = 23 };
            var j = top.NewJBarFor(NewJBar.PileTopTypeName, phc)!;
            Assert.IsFalse(j.HasSteelPipe);
            Assert.IsFalse(j.IsComputable);
            StringAssert.Contains(j.NotComputableReason, "PHC杭");
            StringAssert.Contains(j.NotComputableReason, "鋼管を持たない");

            var rc = new PileSection { PileBodyType = PileDesign.Constants.PileTypeNames.InsituSteelPipeConcrete };
            rc.PileSectionType = PileDesign.Constants.PileTypeNames.RcSection;
            j = top.NewJBarFor(NewJBar.PileTopTypeName, rc)!;
            Assert.IsFalse(j.IsComputable);
            StringAssert.Contains(j.NotComputableReason, PileDesign.Constants.PileTypeNames.RcSection);

            // 解析前の確認も同じ理由で知らせる
            var input = new InputModel();
            var body = new PileBodyInput { PileBodyType = PileDesign.Constants.PileTypeNames.PrecastConcrete };
            body.PileTopType = NewJBar.PileTopTypeName;
            body.PileBodySegments = [new PileBodySegment { No = 1, SegmentLength = 10, SegmentDepth = 10, PileSection = phc }];
            body.PileTop ??= new PileTop { PileCapFc = 24, PileCapGamma = 23 };
            input.PileBodies = [body];
            var notices = PileDesign.Services.CheckInputData.CollectNewJBarNotices(input);
            Assert.IsTrue(notices.Any(n => n.Message.Contains("鋼管を持たない")), string.Join(" / ", notices.Select(n => n.Message)));
        }

        /// <summary>
        /// 外殻鋼管 (場所打ち鋼管コンクリート杭の鋼管コンクリート部) は鋼管の公称外径・板厚に溶接する。
        /// 杭径 1,500 mm 以上は、フーチングを杭と基礎梁の接合部として検討するよう知らせる (4.2.4。範囲外ではない)。
        /// </summary>
        [TestMethod]
        public void AnInsituSteelPipePileUsesItsOuterPipe()
        {
            var s = new PileSection { PileBodyType = PileDesign.Constants.PileTypeNames.InsituSteelPipeConcrete };
            s.PileSectionType = PileDesign.Constants.PileTypeNames.SteelPipeConcreteSection;
            s.PipeDia = 1600; s.PipeTs = 22; s.PipeGrade = "SKK490";
            var top = new PileTop { PileCapFc = 30, PileCapGamma = 23 };
            var j = top.NewJBarFor(NewJBar.PileTopTypeName, s)!;
            j.Grade = "WSD490"; j.Size = "WD41J"; j.Count = 24;
            Assert.IsTrue(j.IsComputable);
            Assert.AreEqual(1600.0, j.PileOuterDiameter, "腐食代を差し引かない公称外径");
            Assert.AreEqual(1600.0 + 2 * 41 + 200, j.VirtualDiameter);
            Assert.AreEqual(0, j.GetApplicabilityProblems().Count, string.Join(" / ", j.GetApplicabilityProblems()));
            var notes = j.GetDesignNotes();
            Assert.AreEqual(1, notes.Count);
            StringAssert.Contains(notes[0], "4.2.4");

            s.PipeDia = 1400;
            j = top.NewJBarFor(NewJBar.PileTopTypeName, s)!;
            Assert.AreEqual(0, j.GetDesignNotes().Count, "1,500 mm 未満は知らせない");
        }

        /// <summary>
        /// 中詰補強筋は NewJ-BAR と別の円環として、それぞれの材料で数える。SD390 の D32 を NewJ-BAR と同じ半径に置けば、
        /// 強度 (短期 390・長期 195・終局 1.1×390) も面積も同じなので、NewJ-BAR を同じ本数だけ増やした円環と一致する。
        /// </summary>
        [TestMethod]
        public void InfillBarsAtTheSameRadiusActLikeMoreJBars()
        {
            var j = ManualExample();
            j.InfillBarCount = 6; j.InfillBarSize = "D32"; j.InfillBarGrade = "SD390"; j.InfillBarCover = 0;
            // 中詰めの外径を NewJ-BAR の配置径にして、rn = r' にする
            j.SetTopSection(PileDesign.Constants.PileTypeNames.SteelPipe, PileDesign.Constants.PileTypeNames.SteelPipeSection,
                600, 9, "SKK490", infillOuterDiameter: 2.0 * j.BarRadius);
            Assert.AreEqual(j.BarRadius, j.InfillRadius, 1e-9);

            var more = ManualExample();
            more.Count = 16;
            foreach (double n in new[] { -1000e3, 0.0, 589e3, 2356e3 })
            {
                AreClose(more.GetAllowableMoment(n), j.GetAllowableMoment(n), 1e-9, $"短期 Ma (N = {n * 1e-3} kN)");
                AreClose(more.GetAllowableMoment(n, shortTerm: false), j.GetAllowableMoment(n, shortTerm: false), 1e-9, $"長期 Ma (N = {n * 1e-3} kN)");
                AreClose(more.GetUltimateMoment(n), j.GetUltimateMoment(n), 1e-9, $"Mu (N = {n * 1e-3} kN)");
            }
        }

        /// <summary>
        /// 中詰補強筋は内側にあるので、同じ曲げで NewJ-BAR より応力度が小さく、入れれば耐力は上がる。
        /// 終局強度は 1.1F (SD490 は F)、長期は RC 規準の min(F/1.5, 215 または 195)。
        /// </summary>
        [TestMethod]
        public void InfillBarsAddStrengthWithTheirOwnMaterial()
        {
            var plain = ManualExample();
            var j = ManualExample();
            j.InfillBarCount = 8; j.InfillBarSize = "D22"; j.InfillBarGrade = "SD345"; j.InfillBarCover = 100;
            Assert.AreEqual(600 - 2 * 9, j.InfillOuterDiameter, "鋼管杭の中詰めの外径は鋼管の内径");
            Assert.AreEqual(291.0 - 100.0, j.InfillRadius, 1e-9);
            Assert.AreEqual((345.0, 215.0, 1.1 * 345.0), (j.InfillF, j.InfillLongTermAllowableStress, j.InfillUltimateStrength));
            j.InfillBarGrade = "SD490"; j.InfillBarSize = "D29";
            Assert.AreEqual((490.0, 195.0, 490.0), (j.InfillF, j.InfillLongTermAllowableStress, j.InfillUltimateStrength));
            j.InfillBarGrade = "SD345"; j.InfillBarSize = "D22";

            Assert.IsTrue(j.GetAllowableMoment(589e3) > plain.GetAllowableMoment(589e3));
            Assert.IsTrue(j.GetUltimateMoment(0) > plain.GetUltimateMoment(0));
            var sj = j.GetElasticStresses(589e3, 670e6);
            var si = j.GetInfillElasticStresses(589e3, 670e6);
            Assert.IsTrue(si.Tension > 0 && si.Tension < sj.BarTension, $"中詰 {si.Tension} / NewJ-BAR {sj.BarTension}");
            Assert.IsTrue(sj.BarTension < plain.GetElasticStresses(589e3, 670e6).BarTension, "中詰補強筋が引張を分け持つ");
            var none = plain.GetInfillElasticStresses(589e3, 670e6);
            Assert.AreEqual((0.0, 0.0), none, "中詰補強筋が無ければ 0");
        }

        /// <summary>SC 杭の中詰めは中空部 (SC-600 t90 の中空径 420)。中詰めの深さは定着長さ + 200 以上 (4.2.1)。配置半径が無ければ組まない。</summary>
        [TestMethod]
        public void InfillGeometryAndDepthFollowTheManual()
        {
            var top = new PileTop { PileCapFc = 24, PileCapGamma = 23 };
            var j = top.NewJBarFor(NewJBar.PileTopTypeName, ScSection("SC-600-標準-80-9"))!;
            j.Grade = "WSD390"; j.Size = "WD32J"; j.Count = 10;
            j.InfillBarCount = 6; j.InfillBarSize = "D22"; j.InfillBarGrade = "SD345"; j.InfillBarCover = 60;
            Assert.AreEqual(420.0, j.InfillOuterDiameter, 1e-9, "SC 杭は中空部の径 (600 − 2×90)");
            Assert.AreEqual(150.0, j.InfillRadius, 1e-9);
            Assert.AreEqual(35 * 22 + 200, j.InfillRequiredDepth);
            Assert.IsTrue(j.GetDesignNotes().Any(n => n.Contains("970")), "深さが未入力なら下限を知らせる");
            j.InfillDepth = 900;
            Assert.IsTrue(j.GetApplicabilityProblems().Any(p => p.Contains("中詰めコンクリートの深さ")));
            j.InfillDepth = 1000;
            Assert.IsFalse(j.GetApplicabilityProblems().Any(p => p.Contains("中詰めコンクリートの深さ")));
            j.InfillBarGrade = "SD490";
            Assert.AreEqual(42 * 22 + 200, j.InfillRequiredDepth);

            j.InfillBarCover = 300;   // 配置半径 210 − 300 < 0
            Assert.IsFalse(j.IsComputable);
            StringAssert.Contains(j.NotComputableReason, "中詰補強筋の配置半径");
        }

        /// <summary>
        /// 例題「設計例集3.5」(鋼管杭 Φ800) の杭頭を NewJ-BAR にして解き、検定に杭頭接合部の曲げ・せん断が
        /// レベル1 (短期許容) とレベル2 (終局) で出ること、限界値が計算クラスの値と一致することを見る。
        /// </summary>
        [TestMethod]
        [Timeout(600000)]
        public void TheEvaluationChecksTheJointOfEveryPile()
        {
            MainWindowViewModel vm;
            try
            {
                vm = TestProject1.ConvergenceRegression.HeadlessHorizontalRunner.RunExampleForViewModel("Example3_5", "PileExample3_5",
                    new TestProject1.ConvergenceRegression.HeadlessHorizontalRunner.RunOptions
                    {
                        Level1Steps = 4, Level2Steps = 8, Parallelism = 1,
                        Customize = im =>
                        {
                            foreach (var pb in im.PileBodies)
                            {
                                pb.PileTopType = NewJBar.PileTopTypeName;
                                pb.PileTop.PileCapFc = 30;
                                pb.PileTop.NewJBar = new NewJBar { Grade = "WSD490", Size = "WD38J", Count = 16 };
                            }
                        },
                    });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }
            var result = EvaluationService.BuildEvaluationResult(vm, factored: true);
            var input = vm.ResultInputModel!;
            var moments = result.Items.Where(i => i.Kind == EvaluationKind.PileHeadJointMoment).ToList();
            var shears = result.Items.Where(i => i.Kind == EvaluationKind.PileHeadJointShear).ToList();
            Assert.IsFalse(moments.Concat(shears).Any(i => i.IsUnavailable),
                string.Join("\n", moments.Concat(shears).Where(i => i.IsUnavailable).Select(i => i.UnavailableReason)));
            Assert.IsTrue(moments.Any(i => i.Level == 1 && i.LimitName == "短期許容"), "レベル1 の曲げ");
            Assert.IsTrue(moments.Any(i => i.Level == 2 && i.LimitName == "終局"), "レベル2 の曲げ");
            Assert.IsTrue(shears.Any(i => i.Level == 1) && shears.Any(i => i.Level == 2), "せん断");

            var piles = input.PileLayoutItems.ToList();
            TestSource.AssertScanned(piles.Count, 24, "NewJ-BAR の杭");
            var jbar = input.PileBodies[0].PileTop.NewJBarFor(NewJBar.PileTopTypeName, input.PileBodies[0].PileBodySegments[0].PileSection)!;
            Assert.AreEqual(800.0, jbar.PileOuterDiameter, "鋼管の公称外径");
            foreach (var item in moments.Take(40))
            {
                double expected = item.LimitName == "終局"
                    ? jbar.GetUltimateMoment(item.AxialForce!.Value * 1000.0)
                    : jbar.GetAllowableMoment(item.AxialForce!.Value * 1000.0, shortTerm: item.LimitName == "短期許容");
                Assert.AreEqual(expected * 1e-6, item.Limit, 1e-6, $"杭 No.{item.PileNo} {item.LoadCaseName} {item.LimitName}");
            }
            foreach (var item in shears)
            {
                double qa = jbar.ShortTermAllowableShear * 1e-3;
                Assert.AreEqual(item.LimitName == "終局" ? 1.5 * qa : qa, item.Limit, 1e-6);
            }

            // 文章化 (画面と計算書)
            var sb = new System.Text.StringBuilder();
            EvaluationTextFormatter.AppendItem(sb, moments.First(i => i.LimitName == "終局"));
            EvaluationTextFormatter.AppendItem(sb, shears.First(i => i.LimitName == "短期許容"));
            string text = sb.ToString();
            StringAssert.Contains(text, "Mu=");
            StringAssert.Contains(text, "Qa=");
        }
    }
}

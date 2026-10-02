using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace TestProject1
{
    /// <summary>
    /// 支持力・単杭の沈下・群杭の沈下を、代表ケースについて<b>入力と式だけから</b>手で書き下し、プログラムの値と突き合わせる。
    ///
    /// <para>期待値はこのテストの中で、地盤・杭の入力値 (土層の N 値・粘着力・層厚、杭径、杭長) と指針の式から求める。
    /// プログラムの中間の量 (平均 N 値・τ2・杭区間) は使わない。既存の試験 (工法ごとの係数の確認など) は
    /// プログラムが出した平均 N 値に係数を掛けて比べているので、平均の範囲・上限の取り方・層の区切りの誤りは捕まらない。
    /// ここはそれを捕まえる。</para>
    ///
    /// 式の出典:
    ///  - 支持力: 建築基礎構造設計指針 2019 6.3 (先端 qp = 係数 × N̄、N̄ は杭先端 ±D の平均、N ≤ 100 で平均し 60 で頭打ち) と
    ///    周面 τ2 (工法・土質ごとの係数と上限)。Ru = Rp + Rf、使用限界 Ru/3。
    ///  - 単杭の沈下: 荷重伝達法。先端は Sp = 0.1·Dp·{α(Rp/Rpu) + (1-α)(Rp/Rpu)^n}、杭体は軸方向の弾性縮み。
    ///  - 群杭の沈下: Steinbrenner の式 (矩形載荷面の隅角部、多層は層ごとの差し引き)。
    ///    影響係数 F1・F2 の値は Bowles, Foundation Analysis and Design の表 (L/B = 1, H/B = 1 で F1 = 0.142, F2 = 0.083) と、
    ///    半無限弾性体の隅角部 (H → ∞) の閉じた式で確かめる。
    /// </summary>
    [TestClass]
    public class BearingSettlementHandCalculationTests
    {
        private const string Sand = "砂質土";
        private const string Clay = "粘性土";

        // ── 組み立て ─────────────────────────────────────────────

        /// <summary>
        /// 杭頭 GL±0、杭長 20 m (杭先端 -20 m)、軸径 1000 mm の PHC 杭 1 本 (杭先端径は引数)。
        /// 要素の境界は杭頭・土層の境界・杭先端に置く。
        /// </summary>
        private static SoilPile Build(
            string constructionType,
            (double bottom, string cls, double n, double cu)[] layers,
            (double altitude, double n)[] masses,
            double toeDiaMm = 1200,
            double eta = 1.0,
            bool friction = true,
            double settleAlpha = 0.3, double settleN = 2.0)
        {
            var section = new PileSection
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileSectionType = PileTypeNames.Phc,
                PileDiameter = 1000,
            };
            var body = new PileBodyInput
            {
                PileBodyType = PileTypeNames.PrecastConcrete,
                PileConstructionType = constructionType,
                PileToeDia = toeDiaMm,
                SettlePileToeDia = toeDiaMm,
                TipNonPermability = eta,
                SettleAlpha = settleAlpha,
                SettleN = settleN,
            };
            body.PileBodySegments = [new PileBodySegment { No = 1, SegmentLength = 20, SegmentDepth = 20, PileSection = section }];
            section.PileBodyType = PileTypeNames.PrecastConcrete;
            section.PileSectionType = PileTypeNames.Phc;
            section.PileDiameter = 1000;
            section.SelectedPrecastPile.Name = "PHC-1000-標準-80-A";

            var ground = new GroundInput
            {
                GroundLayers = [.. layers.Select((l, i) => new GroundLayerInput
                {
                    No = i + 1,
                    BottomAltitude = l.bottom,
                    GranularityClass = l.cls,
                    NValue = l.n,
                    Cohesive = l.cu,
                    IsPositiveCircumResistance = friction,
                    IsNegativeCircumResistance = friction,
                })],
                GroundMassesData = [.. masses.Select((m, i) => new GroundMassDataInput { No = i + 1, AltitudeDepth = m.altitude, NValue = m.n })],
            };

            var z = new[] { 0.0 }.Concat(layers.Select(l => l.bottom).Where(b => b > -20.0)).Append(-20.0).ToArray();
            ObservableCollection<PileZDataItem> zItems = [.. z.Select(v => new PileZDataItem { Z = v })];
            var soilPile = new SoilPile();
            soilPile.Initialize(no: 1, groundNo: 1, groundInput: ground, pileBodyNo: 1, pileBodyInput: body, z: 0.0, zDataItems: zItems);
            soilPile.UpdateProperties();
            return soilPile;
        }

        /// <summary>杭先端 -20 m、杭先端径 1.2 m なので平均の範囲は -21.2〜-18.8 m。範囲外の 2 点 (-18.5・-21.5) は入れない。</summary>
        private static readonly (double, double)[] ToeMasses =
            [(-18.5, 5), (-19.0, 30), (-20.0, 36), (-21.0, 42), (-21.5, 100)];

        private static readonly (double, string, double, double)[] ThreeLayers =
            [(-12.0, Sand, 10, 0), (-18.0, Clay, 4, 60), (-30.0, Sand, 40, 0)];

        private static void AssertBearing(SoilPile p, double qp, double rfSum, double toeDia, string label)
        {
            double ap = Math.PI * toeDia * toeDia / 4.0;
            double rp = qp * ap;
            double rf = Math.PI * 1.0 * rfSum; // 周長 = π × 軸径 1.0 m
            double ru = rp + rf;
            Assert.AreEqual(qp, p.Qpu, 1e-9 * qp, $"{label}: 極限先端支持力度 qp");
            Assert.AreEqual(rp, p.Rpu, 1e-9 * rp, $"{label}: 極限先端支持力 Rp");
            Assert.AreEqual(rf, p.Rfu, 1e-9 * rf, $"{label}: 極限周面抵抗力 Rf");
            Assert.AreEqual(ru, p.Ru, 1e-9 * ru, $"{label}: 極限鉛直支持力 Ru");
            Assert.AreEqual(ru / 3.0, p.R_SLS, 1e-9 * ru, $"{label}: 使用限界支持力 Ru/3");
        }

        // ── 支持力 ─────────────────────────────────────────────

        /// <summary>
        /// 場所打ちコンクリート杭、先端は砂質土。
        /// N̄ = (30 + 36 + 42)/3 = 36 → qp = 120 × 36 = 4,320 kN/m²。
        /// τ2: 砂 N=10 → 3.3×10 = 33、粘土 Cu=60 → 60、砂 N=40 → 3.3×40 = 132。
        /// Rf = π×1.0×(33×12 + 60×6 + 132×2) = π×1,020。
        /// </summary>
        [TestMethod]
        public void Insitu_SandToe()
            => AssertBearing(Build(PileConstructionTypeNames.Insitu, ThreeLayers, ToeMasses),
                qp: 4320, rfSum: 33 * 12 + 60 * 6 + 132 * 2, toeDia: 1.2, "場所打ち");

        /// <summary>
        /// プレボーリング杭、上限に掛かる場合。
        /// 先端: N は 100 で頭打ちしてから平均 (80, 100, 50) → 76.7、平均は 60 で頭打ち → qp = 150 × 60 = 9,000 (上限と同じ)。
        /// τ2: 砂 N=60 → 2.5×60 = 150 → 上限 125、粘土 Cu=300 → 上限 125、砂 N=40 → 100。
        /// </summary>
        [TestMethod]
        public void Preboring_CapsOnNAndTau()
            => AssertBearing(Build(PileConstructionTypeNames.Preboring,
                    [(-12.0, Sand, 60, 0), (-18.0, Clay, 4, 300), (-30.0, Sand, 40, 0)],
                    [(-19.0, 80), (-20.0, 150), (-21.0, 50)]),
                qp: 9000, rfSum: 125 * 12 + 125 * 6 + 100 * 2, toeDia: 1.2, "プレボーリング");

        /// <summary>
        /// 中掘り杭、先端は粘性土 → qp = 6·Cu = 6 × 250 = 1,500 (N 値は使わない)。
        /// τ2: 砂 N=20 → 1.5×20 = 30、粘土 Cu=250 → 0.4×250 = 100 → 上限 50。
        /// </summary>
        [TestMethod]
        public void Chubori_ClayToe_UsesCohesion()
            => AssertBearing(Build(PileConstructionTypeNames.Chubori,
                    [(-12.0, Sand, 20, 0), (-30.0, Clay, 8, 250)],
                    [(-19.0, 8), (-20.0, 8), (-21.0, 8)]),
                qp: 1500, rfSum: 30 * 12 + 50 * 8, toeDia: 1.2, "中掘り");

        /// <summary>
        /// 場所打ちコンクリート杭、先端は粘性土で上限に掛かる → qp = min(6 × 1,500, 7,500) = 7,500。
        /// τ2: 砂 N=10 → 33、粘土 Cu=1,500 → 上限 100。
        /// </summary>
        [TestMethod]
        public void Insitu_ClayToe_CappedAt7500()
            => AssertBearing(Build(PileConstructionTypeNames.Insitu,
                    [(-12.0, Sand, 10, 0), (-30.0, Clay, 30, 1500)],
                    [(-19.0, 30), (-20.0, 30), (-21.0, 30)]),
                qp: 7500, rfSum: 33 * 12 + 100 * 8, toeDia: 1.2, "場所打ち (粘土)");

        /// <summary>
        /// 回転貫入杭、閉塞率 η = 0.8。qp = 150 × η × N̄ = 150 × 0.8 × 36 = 4,320 (上限 9,000η = 7,200)。
        /// τ2: 砂 N=10 → 2×10 = 20、粘土 Cu=60 → 0.5×60 = 30、砂 N=40 → 80。
        /// </summary>
        [TestMethod]
        public void Rotary_AppliesTheBlockageRatio()
            => AssertBearing(Build(PileConstructionTypeNames.Rotary, ThreeLayers, ToeMasses, eta: 0.8),
                qp: 4320, rfSum: 20 * 12 + 30 * 6 + 80 * 2, toeDia: 1.2, "回転貫入");

        /// <summary>
        /// 打込み杭、閉塞率 η = 0.8。qp = 300 × η × N̄ = 300 × 0.8 × 36 = 8,640 (基礎指針'19 表6.3.1)。
        /// τ2: 砂 N=10 → 2×10 = 20、粘土 Cu=60 → 0.8×60 = 48、砂 N=40 → 80。
        /// 以前は係数が 3 で、先端支持力が 100 分の 1 (86.4) になっていた。
        /// </summary>
        [TestMethod]
        public void Driven_Uses300EtaN()
            => AssertBearing(Build(PileConstructionTypeNames.Driven, ThreeLayers, ToeMasses, eta: 0.8),
                qp: 8640, rfSum: 20 * 12 + 48 * 6 + 80 * 2, toeDia: 1.2, "打込み");

        // ── 単杭の沈下 ─────────────────────────────────────────────

        /// <summary>
        /// 周面抵抗を考えない杭: 杭頭荷重 P と自重 W はすべて先端が受ける。杭頭の沈下 (自重だけの状態からの増分) は
        ///   ΔS0 = Sp(P + W) − Sp(W) + P·L/EA、Sp(R) = 0.1·Dp·{α(R/Rpu) + (1−α)(R/Rpu)^n}
        /// (自重による杭体の縮みは自重の状態に含まれ、増分には P による縮みだけが出る)。
        /// α = 0.3, n = 2 で非線形の範囲まで、荷重-沈下曲線の各点を照合する。
        /// </summary>
        [TestMethod]
        public void SinglePile_ToeOnly_MatchesTheClosedForm()
        {
            var pile = Build(PileConstructionTypeNames.Insitu, ThreeLayers, ToeMasses, friction: false);
            foreach (var pcv in pile.PileCircumVerticals) { pcv.IsPositiveCircumResistance = false; pcv.IsNegativeCircumResistance = false; }

            var section = pile.PileCircumVerticals[0].PileBodySegment.PileSection;
            double ea = section.EA, w = section.W * 20.0;
            Assert.IsTrue(ea > 0 && w > 0, "杭の EA・単位重量が組めていません");
            double rpu = Math.PI * 1.2 * 1.2 / 4.0 * 4320; // 支持力の試験と同じ先端
            double dp = 1.2, alpha = 0.3, n = 2.0;
            double Sp(double r) => 0.1 * dp * (alpha * (r / rpu) + (1 - alpha) * Math.Pow(r / rpu, n));

            var method = new VerticalLoadTransferMethod(new InputModel(), pile);
            var points = method.LoadDisplacements.Where(d => d.PileTopLoad > 0 && d.PileTopLoad + w < 0.95 * rpu).ToList();
            TestSource.AssertScanned(points.Count, 5, "荷重-沈下曲線の圧縮側の点");

            foreach (var d in points)
            {
                double p = d.PileTopLoad;
                double expectedMm = (Sp(p + w) - Sp(w) + p * 20.0 / ea) * 1000.0;
                Assert.AreEqual(expectedMm, d.DD0s, 2e-3 * expectedMm,
                    $"P = {p:F0} kN の杭頭沈下 (手計算 {expectedMm:F3} mm、プログラム {d.DD0s:F3} mm)");
                Assert.AreEqual(p + w, d.RzToe, 1e-3 * (p + w), $"P = {p:F0} kN の先端反力");
            }
        }

        // ── 群杭の沈下 (Steinbrenner) ─────────────────────────────────

        private static ObservableCollection<SettlementSoilLayer> Layers(params (double h, double e, double nu)[] layers)
            => [.. layers.Select(l => new SettlementSoilLayer { Thickness = l.h, Ek = l.e, PoissonsRatio = l.nu })];

        private static ObservableCollection<RectLoad> Rect(double x1, double x2, double y1, double y2, double qa)
            => [new RectLoad { X1 = x1, X2 = x2, Y1 = y1, Y2 = y2, QA = qa }];

        /// <summary>
        /// 正方形 (L/B = 1)、層厚 H = B (H/B = 1) の隅角部。
        /// F1 = (2/π)·ln{(1+√2)√2 / (1+√3)} = 0.1419、F2 = (1/2π)·atan(1/√3) = 1/12 = 0.0833 (Bowles の表の 0.142・0.083)。
        /// s = q·B·{(1−ν²)F1 + (1−ν−2ν²)F2}/E。
        /// </summary>
        [TestMethod]
        public void Steinbrenner_SquareCorner_MatchesTheTable()
        {
            double f1 = 2.0 / Math.PI * Math.Log((1 + Math.Sqrt(2)) * Math.Sqrt(2) / (1 + Math.Sqrt(3)));
            double f2 = 1.0 / 12.0;
            Assert.AreEqual(0.142, f1, 5e-4, "手計算の F1 が表と合いません (式の書き写しの確認)");
            Assert.AreEqual(0.083, f2, 5e-4);

            const double b = 4.0, qa = 1600.0, e = 20000.0, nu = 0.3;
            double q = qa / (b * b);
            double expected = q * b * ((1 - nu * nu) * f1 + (1 - nu - 2 * nu * nu) * f2) / e;
            double actual = Steinnbrener.CalcSettlement(new Point(0, 0), Rect(0, b, 0, b, qa), Layers((b, e, nu)));
            Assert.AreEqual(expected, actual, 1e-9 * expected);
        }

        /// <summary>
        /// 十分に厚い 1 層 (半無限弾性体) の正方形の中心: s = q·B·(1−ν²)·I/E、I = (4/π)·ln(1+√2) = 1.122 (たわみ性載荷の中心。
        /// Timoshenko–Goodier)。中心は 1/4 の正方形 4 つの隅角部の和なので、F2 の項は厚さとともに消える。
        /// </summary>
        [TestMethod]
        public void Steinbrenner_HalfSpaceSquareCenter_Is1_122()
        {
            const double b = 6.0, qa = 3600.0, e = 30000.0, nu = 0.35;
            double q = qa / (b * b);
            double centerFactor = 4.0 / Math.PI * Math.Log(1 + Math.Sqrt(2)); // 隅角部の F1(∞) = (2/π)ln(1+√2) を半幅で 4 つ → q·B·(1−ν²)/E に掛かる係数
            Assert.AreEqual(1.122, centerFactor, 5e-4);
            double expected = q * b * (1 - nu * nu) * centerFactor / e;
            double actual = Steinnbrener.CalcSettlement(new Point(b / 2, b / 2), Rect(0, b, 0, b, qa), Layers((1e6, e, nu)));
            Assert.AreEqual(expected, actual, 1e-4 * expected);
        }

        /// <summary>
        /// 2 層 (上層 H = B、下層は十分に厚い) の隅角部:
        ///   s = q·B·Is(H/B=1)/E1 + q·B·{Is(∞) − Is(H/B=1)}/E2、Is(∞) = (1−ν²)·(2/π)·ln(1+√2)。
        /// 層ごとの差し引き (上の層の深さの影響係数を引く) の取り違えを捕まえる。
        /// </summary>
        [TestMethod]
        public void Steinbrenner_TwoLayers_SubtractTheUpperLayer()
        {
            const double b = 4.0, qa = 1600.0, e1 = 10000.0, e2 = 50000.0, nu1 = 0.3, nu2 = 0.4;
            double q = qa / (b * b);
            double f1 = 2.0 / Math.PI * Math.Log((1 + Math.Sqrt(2)) * Math.Sqrt(2) / (1 + Math.Sqrt(3)));
            double f2 = 1.0 / 12.0;
            double f1Inf = 2.0 / Math.PI * Math.Log(1 + Math.Sqrt(2));
            double Is(double nu, double a1, double a2) => (1 - nu * nu) * a1 + (1 - nu - 2 * nu * nu) * a2;

            double expected = q * b * Is(nu1, f1, f2) / e1 + q * b * (Is(nu2, f1Inf, 0) - Is(nu2, f1, f2)) / e2;
            double actual = Steinnbrener.CalcSettlement(new Point(0, 0), Rect(0, b, 0, b, qa), Layers((b, e1, nu1), (1e6, e2, nu2)));
            Assert.AreEqual(expected, actual, 1e-4 * expected);
        }

        /// <summary>
        /// 載荷面を 4 つに分けても (荷重は面積で按分)、どの点の沈下も変わらない。
        /// 点の位置 (載荷面の内・外、上下左右・斜め) ごとに隅角部の組み合わせ方が違うので、
        /// 9 つの区画すべてに点を置き、組み合わせの符号の誤りを捕まえる。
        /// </summary>
        [TestMethod]
        public void Steinbrenner_SplittingTheLoad_KeepsEveryPoint()
        {
            var layers = Layers((3.0, 8000, 0.3), (5.0, 20000, 0.35), (20.0, 60000, 0.3));
            const double x1 = 2, x2 = 8, y1 = 1, y2 = 5, qa = 2400;
            var whole = Rect(x1, x2, y1, y2, qa);
            double xm = 5.5, ym = 2.5, q = qa / ((x2 - x1) * (y2 - y1));
            ObservableCollection<RectLoad> parts =
            [
                new RectLoad { X1 = x1, X2 = xm, Y1 = y1, Y2 = ym, QA = q * (xm - x1) * (ym - y1) },
                new RectLoad { X1 = xm, X2 = x2, Y1 = y1, Y2 = ym, QA = q * (x2 - xm) * (ym - y1) },
                new RectLoad { X1 = x1, X2 = xm, Y1 = ym, Y2 = y2, QA = q * (xm - x1) * (y2 - ym) },
                new RectLoad { X1 = xm, X2 = x2, Y1 = ym, Y2 = y2, QA = q * (x2 - xm) * (y2 - ym) },
            ];

            int checkedPoints = 0;
            foreach (double x in new[] { -1.0, 4.0, 11.0 })
                foreach (double y in new[] { -2.0, 3.7, 9.0 })
                {
                    double a = Steinnbrener.CalcSettlement(new Point(x, y), whole, layers);
                    double c = Steinnbrener.CalcSettlement(new Point(x, y), parts, layers);
                    Assert.IsTrue(a > 0, $"({x}, {y}) の沈下が正になっていません: {a}");
                    Assert.AreEqual(a, c, 1e-9 + 1e-7 * a, $"({x}, {y}) で、載荷面を分けると沈下が変わります");
                    checkedPoints++;
                }
            Assert.AreEqual(9, checkedPoints);
        }
    }
}

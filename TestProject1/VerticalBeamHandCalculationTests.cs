using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 基礎梁を考えた沈下 (鉛直の梁モデル) を、杭 3 本を 1 列に並べた連続梁で<b>手で解いた値</b>と突き合わせる。
    ///
    /// <para>杭はばね k、基礎梁は曲げ剛性 EI・せん断剛性 G·As の梁。杭の間隔 L で並べた 3 本の杭の反力は、
    /// 両端の杭を支点とする単純梁 (スパン 2L) の中央のたわみと、ばねの縮みの適合から閉じた形で決まる:
    ///   f = (2L)³/(48EI) + 2L/(4·G·As)   (中央の集中荷重 1 に対する、スパン 2L の単純梁の中央のたわみ。
    ///   第 2 項がせん断変形で、基本設定で「含めない」にすると無くなる)
    ///   中央に P: 端の反力 R = P/(3 + 2kf)、中央の反力 Rc = R(1 + 2kf)
    ///   端に P:   反対の端 R3 = −P/(6 + 4kf)、中央 R2 = −2R3、載荷点 R1 = P − R2 − R3
    /// 梁が剛 (f = 0) なら 1/3 ずつ・5/6, 1/3, −1/6 になる (剛体の釣り合い)。杭の沈下は R/k。</para>
    ///
    /// <para>反復沈下解析 (IterativeBeamSettlementService) が組むのと同じ手順 (ばねの上書き → 集中荷重 → 1 回の線形の解) で解く。</para>
    ///
    /// <para>せん断断面積は矩形の (5/6)bh、G = E / (2(1 + ν))。ここの寸法 (梁せい 1 m・スパン 10 m) では、
    /// せん断変形で f が 3% ほど大きくなる。</para>
    /// </summary>
    [TestClass]
    public class VerticalBeamHandCalculationTests
    {
        private const double Span = 5.0, Width = 0.5, Height = 1.0, E = 2.5e7, Nu = 0.2, K = 2.0e5;

        private static (InputModel Model, PileLayoutDataItem[] Piles) Build(double nu1 = Nu, double nu2 = Nu, bool shear = true)
        {
            var piles = Enumerable.Range(0, 3).Select(i => new PileLayoutDataItem { No = i + 1, X = i * Span, Y = 0, Z = 0 }).ToArray();
            var input = new InputModel
            {
                PileLayoutItems = new ObservableCollection<PileLayoutDataItem>(piles),
                FoundationBeamInput = new FoundationBeamInput
                {
                    Materials = [new BeamMaterial { YoungModulus = E, PoissonRatio = nu1 }, new BeamMaterial { YoungModulus = E, PoissonRatio = nu2 }],
                    Sections = [],
                    Beams = [],
                },
            };
            for (int i = 0; i < 2; i++)
                input.FoundationBeamInput.Beams.Add(new FoundationBeam
                {
                    NodeI_Type = NodeReferenceType.PileLayout, NodeI_Id = piles[i].UniqueId,
                    NodeJ_Type = NodeReferenceType.PileLayout, NodeJ_Id = piles[i + 1].UniqueId,
                    MaterialNo = i + 1, Width = Width, Height = Height, YoungModulus = E,
                });
            input.FundamentalInput = new FundamentalInput { IncludeFoundationBeamShearDeformation = shear };
            return (input, piles);
        }

        /// <summary>反復沈下解析と同じ手順で、杭頭の集中荷重 (下向き正) に対する杭頭の沈下 (下向き正) を解く。</summary>
        private static Dictionary<int, double> Solve(InputModel input, Dictionary<int, double> loads)
        {
            var modelling = new VerticalBeamModelling(input);
            var anaModel = modelling.BuildAnaModel();
            anaModel.InitializeStates();
            foreach (var spring in modelling.PileSpringMap.Values)
            {
                spring.SetKe(0, 0, K, 0, 0, 0, true);
                spring.SetKe(0, 0, K, 0, 0, 0, false);
            }
            foreach (var (no, node) in modelling.ConnectionNodes)
            {
                var load = new NodeLoad(0, 0, -(loads.TryGetValue(no, out double p) ? p : 0.0), 0, 0, 0);
                node.SetIncrementalLoad(load);
                node.SetCumulativeLoad(load);
            }
            anaModel.MapOnVectorDF();
            anaModel.MapOnVectorF();
            anaModel.InitializeNormsqR_onNormsqFint();
            anaModel.SetR();
            anaModel.MapOnKtanMat();
            Solver.SolveDisp(anaModel, 1.0);
            return modelling.ConnectionNodes.ToDictionary(kv => kv.Key, kv => -kv.Value.CumulativeDisp.Uz);
        }

        /// <summary>中央の集中荷重 1 に対する、スパン 2L の単純梁の中央のたわみ (曲げ + せん断)。</summary>
        private static double Flexibility(bool shear)
        {
            double ei = E * Width * Height * Height * Height / 12.0;
            double gas = E / (2 * (1 + Nu)) * (5.0 / 6.0) * Width * Height;
            return Math.Pow(2 * Span, 3) / (48 * ei) + (shear ? 2 * Span / (4 * gas) : 0.0);
        }

        [TestMethod]
        public void TheDefault_IncludesShearDeformation()
            => Assert.IsTrue(new FundamentalInput().IncludeFoundationBeamShearDeformation, "基礎梁のせん断変形は既定で含める");

        /// <summary>
        /// 設定が基礎梁の要素に届く。鉛直梁のモデルは要素で確かめ、水平解析のモデル (同梱の例題に基礎梁が無く、組むのが重い) は
        /// 基礎梁の要素を組む所で設定を読んでいることを確かめる。杭の要素には効かせない。
        /// </summary>
        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void TheSetting_ReachesTheFoundationBeamElements(bool shear)
        {
            var modelling = new VerticalBeamModelling(Build(shear: shear).Model);
            Assert.IsTrue(modelling.Beams.Count > 0);
            Assert.IsTrue(modelling.Beams.All(b => b.IncludeShearDeformation == shear), "鉛直梁のモデルの基礎梁に設定が届いていません");
            Assert.IsFalse(new Beam().IncludeShearDeformation, "要素の既定 (杭の要素) はせん断変形を含めない");

            string src = TestSource.Read("Graphics_r1", "FEM", "AnalysisModelling.cs");
            int at = src.IndexOf("new Beam($\"FoundationBeam-", StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, "水平解析のモデルで基礎梁の要素を組む所が見つかりません");
            string around = src.Substring(at, Math.Min(600, src.Length - at));
            StringAssert.Contains(around, "IncludeFoundationBeamShearDeformation");
            StringAssert.Contains(around, "EnableShearDeformation()");
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void LoadAtTheCenter_SharesByTheContinuousBeam(bool shear)
        {
            const double p = 3000;
            double kf = K * Flexibility(shear);
            double rEnd = p / (3 + 2 * kf), rCenter = rEnd * (1 + 2 * kf);
            Assert.IsTrue(rCenter / p > 0.5, "梁の柔らかさが効く条件になっていません (中央の杭が 1/3 しか受けないなら梁は剛)");

            var s = Solve(Build(shear: shear).Model, new() { [2] = p });
            Assert.AreEqual(rEnd / K, s[1], 1e-6 * rCenter / K, "端の杭の沈下");
            Assert.AreEqual(rCenter / K, s[2], 1e-6 * rCenter / K, "中央の杭の沈下");
            Assert.AreEqual(rEnd / K, s[3], 1e-6 * rCenter / K, "端の杭の沈下 (反対側)");
        }

        /// <summary>端の杭の上に P。反対の端の杭は引き上げられる (沈下が負)。</summary>
        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void LoadAtTheEnd_LiftsTheFarPile(bool shear)
        {
            const double p = 3000;
            double kf = K * Flexibility(shear);
            double r3 = -p / (6 + 4 * kf), r2 = -2 * r3, r1 = p - r2 - r3;

            var s = Solve(Build(shear: shear).Model, new() { [1] = p });
            Assert.AreEqual(r1 / K, s[1], 1e-6 * r1 / K, "載荷した杭の沈下");
            Assert.AreEqual(r2 / K, s[2], 1e-6 * r1 / K, "中央の杭の沈下");
            Assert.AreEqual(r3 / K, s[3], 1e-6 * r1 / K, "反対の端の杭の沈下 (浮き上がり)");
            Assert.IsTrue(s[3] < 0, "反対の端の杭が浮き上がっていません");
        }

        /// <summary>
        /// せん断変形を含めない式は、端の固定度 r (1 = 剛、0 = ピン) をどう取っても、以前の要素剛性の式と一致する
        /// (含める式は、端を回転ばねにして梁の端の回転を消去する形で組んでいる。その組み方の確かめ)。
        /// </summary>
        [TestMethod]
        public void WithoutShear_TheBendingStiffnessMatchesTheEulerFormula()
        {
            const double ei = 2.0, length = 1.5;
            foreach (var (ri, rj) in new[] { (1.0, 1.0), (0.3, 0.7), (0.2, 0.2), (1.0, 0.0), (0.0, 1.0), (0.9, 0.01), (0.0, 0.0) })
            {
                double rb = 1 + ri + rj, l1 = ei / length, l2 = l1 / length, l3 = l2 / length;
                double a = 6 * l3 * (ri + rj + 4 * ri * rj) / rb, b = 6 * l2 * ri * (1 + 2 * rj) / rb, c = 6 * l2 * (1 + 2 * ri) * rj / rb;
                double d = 6 * l1 * ri * (1 + rj) / rb, e = 6 * l1 * (1 + ri) * rj / rb, f = 6 * l1 * ri * rj / rb;
                double[,] euler = { { a, b, -a, c }, { b, d, -b, f }, { -a, -b, a, -c }, { c, f, -c, e } };
                var k = Beam.PlaneBendingStiffness(ei, length, 0.0, ri, rj);
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 4; j++)
                        Assert.AreEqual(euler[i, j], k[i, j], 1e-9 * (Math.Abs(a) + Math.Abs(d) + Math.Abs(e)), $"r = ({ri}, {rj}) の [{i},{j}]");
            }
        }

        /// <summary>片持ち梁 (端 i 固定) の先端に P: たわみは P·L³/(3EI) + P·L/(G·As)。</summary>
        [TestMethod]
        public void WithShear_TheCantileverTipDeflectionAddsTheShearTerm()
        {
            const double ei = 3.0e5, gas = 4.0e5, length = 2.0, p = 10.0;
            double phi = 12 * ei / (gas * length * length);
            var k = Beam.PlaneBendingStiffness(ei, length, phi, 1.0, 1.0);
            // 端 j の変位と回転だけが動く (端 i は固定)
            double k11 = k[2, 2], k12 = k[2, 3], k22 = k[3, 3];
            double tip = p * k22 / (k11 * k22 - k12 * k12);
            double expected = p * length * length * length / (3 * ei) + p * length / gas;
            Assert.AreEqual(expected, tip, 1e-12 * expected);
        }

        /// <summary>
        /// 寸法とヤング係数が同じでポアソン比だけ違う基礎梁 2 本は、それぞれのせん断弾性係数で解く。
        /// 以前は断面の使い回しの鍵に E しか入っておらず、2 本目が 1 本目の材料 (G) で解かれていた。
        /// </summary>
        [TestMethod]
        public void BeamsWithDifferentPoissonRatios_KeepTheirOwnShearModulus()
        {
            var (model, _) = Build(nu1: 0.0, nu2: 0.5);
            var modelling = new VerticalBeamModelling(model);
            Assert.AreEqual(2, modelling.Beams.Count);
            double g1 = modelling.Beams[0].Section.Material.G, g2 = modelling.Beams[1].Section.Material.G;
            Assert.AreEqual(E / 2.0, g1, 1e-9 * E, "1 本目の梁のせん断弾性係数");
            Assert.AreEqual(E / 3.0, g2, 1e-9 * E, "2 本目の梁のせん断弾性係数 (1 本目の材料で解かれています)");
        }
    }
}

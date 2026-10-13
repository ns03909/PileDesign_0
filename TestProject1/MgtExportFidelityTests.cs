using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Output;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media.Media3D;

namespace TestProject1
{
    /// <summary>
    /// MGT 出力のばね・荷重が解析モデルと同じであること (2026-10-14 のレビュー)。「ファイルを作れた」ではなく、
    /// ばねの向き・荷重-変形関係・荷重の力とモーメントを、解析と同じ式で求めた値と突き合わせる。
    ///
    /// 以前の出力は、
    /// <list type="bullet">
    /// <item>杭の鉛直ばね (Z 方向) を X 方向のばねとして書き、力が 0 になっていた。</item>
    /// <item>杭頭の回転ばね (キャップと杭頭の連結) を書いていなかった。</item>
    /// <item>杭の水平地盤ばねの反力を、節点の片側の要素の分しか入れていなかった (内部節点で約半分)。</item>
    /// <item>慣性力を全ケース代表節点に掛け、荷重ケースごとの作用点の違い (偏心) のモーメントを落としていた。</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class MgtExportFidelityTests
    {
        // ── MGT を読む ─────────────────────────────

        private sealed class Mgt
        {
            public Dictionary<int, Point3D> Nodes = [];
            public Dictionary<int, (double Disp, double Force)[]> Functions = [];
            public Dictionary<int, bool> Symmetric = [];
            public List<(int I, int J, int Func)> MultiLinear = [];
            public List<(int I, int J, double[] K)> Gen = [];
            public Dictionary<string, List<double[]>> ConLoads = [];   // 荷重ケース → [節点, FX, FY, FZ, MX, MY, MZ]
            public Dictionary<string, List<(int Node, string Flag, double Dx, double Dy)>> SpDisp = [];   // 荷重ケース → 強制変位
            public string Text = "";

            public static Mgt Parse(string text)
            {
                var m = new Mgt { Text = text };
                string section = "", loadCase = "";
                int func = -1;
                var funcPairs = new List<(double, double)>();
                void EndFunc() { if (func > 0) m.Functions[func] = funcPairs.ToArray(); func = -1; funcPairs = []; }
                foreach (var raw in text.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";")) continue;
                    if (line.StartsWith("*"))
                    {
                        EndFunc();
                        section = line.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)[0];
                        if (section == "*USE-STLD") loadCase = line.Split(',')[1].Trim();
                        continue;
                    }
                    var f = line.Split(',').Select(s => s.Trim()).ToArray();
                    double D(int i) => double.Parse(f[i], CultureInfo.InvariantCulture);
                    switch (section)
                    {
                        case "*NODE":
                            m.Nodes[int.Parse(f[0])] = new Point3D(D(1), D(2), D(3));
                            break;
                        case "*FORCES-DEFORMATION":
                            if (line.StartsWith("FUNC="))
                            {
                                EndFunc();
                                func = int.Parse(f[0].Substring(5));
                                m.Symmetric[func] = f[2] == "YES";
                            }
                            else
                                for (int i = 0; i + 1 < f.Length; i += 2)
                                    if (f[i].Length > 0) funcPairs.Add((D(i), D(i + 1)));
                            break;
                        case "*ELASTICLINK":
                            if (f[3] == "MULTI LINEAR") m.MultiLinear.Add((int.Parse(f[1]), int.Parse(f[2]), int.Parse(f[6])));
                            else if (f[3] == "GEN") m.Gen.Add((int.Parse(f[1]), int.Parse(f[2]), Enumerable.Range(11, 6).Select(D).ToArray()));
                            break;
                        case "*SPDISP":
                            if (!m.SpDisp.TryGetValue(loadCase, out var sp)) m.SpDisp[loadCase] = sp = [];
                            sp.Add((int.Parse(f[0]), f[1], D(2), D(3)));
                            break;
                        case "*CONLOAD":
                            if (!m.ConLoads.TryGetValue(loadCase, out var list)) m.ConLoads[loadCase] = list = [];
                            list.Add(Enumerable.Range(0, 7).Select(D).ToArray());
                            break;
                    }
                }
                EndFunc();
                return m;
            }

            /// <summary>荷重-変形関数の値 (折れ線の補間。対称なら負側は正側の反転)。</summary>
            public double ForceAt(int func, double d)
            {
                var c = Functions[func];
                if (Symmetric[func] && d < 0) return -ForceAt(func, -d);
                for (int i = 1; i < c.Length; i++)
                    if (d <= c[i].Disp + 1e-12)
                    {
                        var (d0, f0) = c[i - 1]; var (d1, f1) = c[i];
                        return f0 + (f1 - f0) * (d - d0) / (d1 - d0);
                    }
                throw new ArgumentOutOfRangeException(nameof(d));
            }

            /// <summary>解析モデルの節点の番号 (出力は解析モデルの節点の順に 1 から振る)。</summary>
            public static int IdOf(AnaModel model, Node node)
            {
                int i = model.Nodes.IndexOf(node);
                Assert.IsTrue(i >= 0, $"節点 {node.Name} が解析モデルにありません");
                return i + 1;
            }
        }

        private static string Export(AnaModel model)
        {
            string file = Path.Combine(Path.GetTempPath(), $"MgtFidelity_{Guid.NewGuid():N}.mgt");
            try
            {
                new MgtExporter(model).Export(file);
                return File.ReadAllText(file);
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static LoadCombination Comb() => new(1, 1, 1, 1);

        private static AnaModel Build(InputModel input)
        {
            var m = new AnalysisModelling(input);
            return new AnaModel(input, m.Nodes, m.Beams, m.DummyBeams, m.RigidBodies, m.HorizontalSoilSprings, m.RotationalSprings);
        }

        private static (InputModel Input, AnaModel Model) Example(string folder, string file)
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel(folder, file);
            if (input == null) Assert.Inconclusive($"例題 {file} を組めません");
            return (input!, Build(input!));
        }

        /// <summary>MGT の X 方向ばね (杭の節点 → X に +1 m の地盤節点) の荷重-変形関数。</summary>
        private static int XFunctionOf(Mgt mgt, AnaModel model, Node pileNode)
        {
            int id = Mgt.IdOf(model, pileNode);
            var links = mgt.MultiLinear.Where(l => l.I == id && Math.Abs(mgt.Nodes[l.J].X - pileNode.Coord.X - 1.0) < 1e-9
                                                    && Math.Abs(mgt.Nodes[l.J].Z - pileNode.Coord.Z) < 1e-9).ToList();
            Assert.AreEqual(1, links.Count, $"節点 {pileNode.Name} の X 方向の地盤ばねが 1 本ではありません");
            return links[0].Func;
        }

        // ── 3. 杭の水平地盤ばね ─────────────────────────────

        /// <summary>
        /// 杭の水平地盤ばねの力は、節点の上の要素 (下端の py) と下の要素 (上端の py) の地盤反力の和 (解析と同じ)。
        /// 以前は最初に見つかった 1 要素の区間長の半分だけで、内部節点の反力が約半分になっていた。
        /// 内部節点・両端・土層の境界 (上下で反力の性質が違う節点) を確かめる。
        /// </summary>
        [TestMethod]
        public void PileSoilSprings_AddBothNeighbouringElements()
        {
            var (input, model) = Example("Example3_4", "PileExample3_4");
            var mgt = Mgt.Parse(Export(model));
            var rep = input.LoadCasesInput.LoadCasesLevel1[0];
            int checkedNodes = 0, boundaries = 0;
            foreach (var pile in input.PileLayoutItems)
            {
                var reactions = pile.SoilPileAt(input)!.HorizontalSoilReactions;
                var effect = GroupPileEffect.For(pile);
                bool isFront = pile.IsFrontFor(rep);
                int n = pile.PileNodes.Count;
                for (int i = 0; i < n; i++)
                {
                    int func = XFunctionOf(mgt, model, pile.PileNodes[i]);
                    foreach (double y in new[] { 0.002, 0.01, 0.05 })
                    {
                        double expected = 0;
                        if (i > 0 && i - 1 < reactions.Count)
                            expected += reactions[i - 1].GetSoilSecantReactionCoefficient(y, false, isFront, effect, rep.SoilNonlinearityMode) * y;
                        if (i < n - 1 && i < reactions.Count)
                            expected += reactions[i].GetSoilSecantReactionCoefficient(y, true, isFront, effect, rep.SoilNonlinearityMode) * y;
                        if (expected <= 0 && i == 0) expected = 1e-3 * y;
                        Assert.AreEqual(expected, mgt.ForceAt(func, y), Math.Max(1e-3, 1e-4 * Math.Abs(expected)),
                            $"杭 No.{pile.No} 節点 {i} (y = {y} m): MGT の反力が解析と違います");
                    }
                    if (i > 0 && i < n - 1 && i < reactions.Count && reactions[i - 1].Kh0 != reactions[i].Kh0) boundaries++;
                    checkedNodes++;
                }
            }
            TestSource.AssertScanned(checkedNodes, 10, "杭の節点");
            Assert.IsTrue(boundaries > 0, "(前提) 上下で地盤反力の違う節点 (土層の境界) を確かめていません");
        }

        /// <summary>
        /// 全節点のばねの力の和は、要素分割によらず杭全体の地盤反力に等しい (線形の地盤なら Σ kh·B·L·y)。
        /// 片側の要素しか数えていなかった以前の出力は、分割を細かくしても合計が約半分のままだった。
        /// </summary>
        [TestMethod]
        public void PileSoilSprings_SumToTheWholePileReaction()
        {
            var (input, model) = Example("Example3_4", "PileExample3_4");
            foreach (var lc in input.LoadCasesInput.LoadCasesLevel1) lc.SoilNonlinearityMode = SoilNonlinearityMode.Linear;
            var mgt = Mgt.Parse(Export(model));
            const double y = 0.01;
            var pile = input.PileLayoutItems[0];
            var reactions = pile.SoilPileAt(input)!.HorizontalSoilReactions;
            double sum = pile.PileNodes.Sum(node => mgt.ForceAt(XFunctionOf(mgt, model, node), y));
            double whole = reactions.Take(pile.PileNodes.Count - 1)
                .Sum(r => r.Kh0 * GroupPileEffect.For(pile).Kh0Factor * r.B * (r.ZTop - r.ZBtm) * y);
            Assert.IsTrue(whole > 0, "(前提) 杭に地盤反力があること");
            Assert.AreEqual(whole, sum, 1e-6 * whole, "MGT のばねの合計が杭全体の地盤反力と違います");
        }

        // ── 1. 杭の鉛直ばね ─────────────────────────────

        /// <summary>
        /// 杭の鉛直ばね (Z 方向) は Z 方向のばねとして書く。鉛直剛性 1,000 kN/m のばねで、沈下 1 mm の反力は 1 kN。
        /// 以前は X 方向のばねとして書き、X の剛性 (0) を使って力が 0 になっていた。
        /// 鉛直ばねを使うときは、解析と同じく杭の軸力も荷重ケースとして書く。
        /// </summary>
        [TestMethod]
        public void VerticalSprings_AreWrittenInZ_WithTheirStiffness_AndTheAxialForce()
        {
            var (input, model) = Example("Example3_4", "PileExample3_4");
            var pile = input.PileLayoutItems[0];
            var head = pile.PileNodes[0];
            var tip = pile.PileNodes[^1];
            var ground = new Node { Name = "VZ", Coord = tip.Coord, Boundary = new Boundary(true, true, true, true, true, true) };
            model.Nodes.Add(ground);
            var spring = new HorizontalSoilSpring("鉛直ばね", tip, ground);
            spring.SetKe(0, 0, 1000, 0, 0, 0, true);
            model.HorizontalSoilSprings.Add(spring);
            pile.VerticalNodeSprings.Add(spring);
            pile.VerticalNodeSpringCurves.Add(new VerticalPileSpringCurve([(-0.1, -100.0), (0.0, 0.0), (0.1, 100.0)]));
            var lc = input.LoadCasesInput.LoadCasesLevel1[0];
            model.AnalysisStepResults.Add(new AnalysisStepResult(lc, Comb(), false, 1, 1, 0));

            var mgt = Mgt.Parse(Export(model));
            int tipId = Mgt.IdOf(model, tip);
            var z = mgt.MultiLinear.Where(l => l.I == tipId && (mgt.Nodes[l.J] - tip.Coord - new Vector3D(0, 0, 1)).Length < 1e-9).ToList();
            Assert.AreEqual(1, z.Count, "鉛直ばねが Z 方向のばね (杭の節点の 1 m 上の地盤節点へ) として書かれていません");
            Assert.AreEqual(1.0, mgt.ForceAt(z[0].Func, 0.001), 1e-6, "沈下 1 mm の反力が 1 kN になっていません");
            Assert.AreEqual(-1.0, mgt.ForceAt(z[0].Func, -0.001), 1e-6, "引抜き側の反力が書かれていません");
            Assert.IsFalse(mgt.Symmetric[z[0].Func], "押込みと引抜きで違う鉛直ばねを対称として書いています");
            XFunctionOf(mgt, model, tip);   // X 方向のばねは水平地盤ばねの 1 本だけ (鉛直ばねを X に書いていない)

            string ax = $"AX_L{lc.Level}_{lc.No}";
            Assert.IsTrue(mgt.ConLoads.ContainsKey(ax), "杭の軸力の荷重ケースがありません");
            int jointId = Mgt.IdOf(model, model.PileJointNodeOf(pile.No)!);
            double n = pile.AxialForceLevel1s[lc.No - 1];
            Assert.IsTrue(Math.Abs(n) > 1, "(前提) 杭の地震時軸力があること");
            Assert.AreEqual(-n, mgt.ConLoads[ax].Where(r => (int)r[0] == jointId).Sum(r => r[3]), 1e-3, "杭の軸力が接合節点に下向きに掛かっていません");
            StringAssert.Contains(mgt.Text, $"ST, {ax,-14}, 1.0000", "荷重組合せに杭の軸力が入っていません");
        }

        // ── 2. 杭頭の回転ばね ─────────────────────────────

        /// <summary>
        /// 杭頭の回転ばね (キャップ節点と杭頭節点) を GEN の連結として書く。以前は書いておらず、MGT では杭頭がキャップにつながっていなかった。
        /// 回転を考えない杭頭は剛 (1e10)、水平 2 方向とねじりは解析のペナルティ (1e8)、鉛直は剛体連結に任せて 0。
        /// </summary>
        [TestMethod]
        public void PileHeadRotationalSprings_AreWritten()
        {
            var (input, model) = Example("Example3_4", "PileExample3_4");
            Assert.IsTrue(model.RotationalSprings.Count > 0, "(前提) 杭頭の回転ばねがあること");
            var mgt = Mgt.Parse(Export(model));
            Assert.AreEqual(model.RotationalSprings.Count, mgt.Gen.Count, "杭頭の回転ばねの数だけ GEN の連結がありません");
            foreach (var rs in model.RotationalSprings)
            {
                var link = mgt.Gen.Single(g => g.I == Mgt.IdOf(model, rs.NodeI) && g.J == Mgt.IdOf(model, rs.NodeJ));
                CollectionAssert.AreEqual(new[] { 1e8, 1e8, 0, link.K[3], link.K[3], 1e8 }, link.K, $"{rs.Name}: 並進・ねじりの連結が解析と違います");
                Assert.IsTrue(link.K[3] > 0, $"{rs.Name}: 回転剛性がありません");
            }
        }

        /// <summary>杭頭で M-θ を使うときは、近似 (常時軸力での初期剛性) であることを出力前に知らせ、ファイルの先頭にも書く。</summary>
        [TestMethod]
        public void ApproximationsAreListed_AndWrittenAtTheTop()
        {
            var (_, model) = Example("Example3_4", "PileExample3_4");
            var exporter = new MgtExporter(model);
            var notes = exporter.DescribeApproximations();
            Assert.IsTrue(notes.Any(n => n.Contains("液状化を考慮しない", StringComparison.Ordinal)), "地盤ばねを 1 組で書くことを知らせていません");
            string text = Export(model);
            int header = text.IndexOf("解析モデルと違うところ", StringComparison.Ordinal);
            Assert.IsTrue(header >= 0 && header < text.IndexOf("*UNIT", StringComparison.Ordinal), "ファイルの先頭に注記がありません");
        }

        // ── 4. 荷重ケースごとの作用点 ─────────────────────────────

        /// <summary>
        /// 荷重ケースごとの作用点 (慣性力中心) は、代表節点の力と、位置の差から生じるモーメントに置き換えて書く。
        /// 原点まわりの力とモーメントの合計が、作用点に力を掛けたものと一致すること。
        /// 以前は作用点 Y = 0 m と Y = 10 m の 2 ケースが同じ節点・同じ荷重・モーメント 0 で出力されていた。
        /// </summary>
        [TestMethod]
        public void EachCaseActionPoint_IsKeptAsAnEquivalentMoment()
        {
            var (input, model) = Example("Example3_4", "PileExample3_4");
            // 作用点だけが違う 2 つのケース (2 つ目は 1 つ目の写しに別の番号を付ける)
            var first = input.LoadCasesInput.LoadCasesLevel1[0];
            var second = first.DeepCopy();
            second.No = 99;
            var cases = new[] { first, second };
            var master = model.Nodes[0].Coord;
            cases[0].ForceActionPointX = master.X; cases[0].ForceActionPointY = master.Y;
            cases[1].ForceActionPointX = master.X + 3; cases[1].ForceActionPointY = master.Y + 10;
            cases[1].ForceActionPointAltitude = master.Z + 2;
            foreach (var lc in cases.Take(2))
            {
                lc.UpperMassForce = 100; lc.LoadAngle = 30;
                model.AnalysisStepResults.Add(new AnalysisStepResult(lc, Comb(), false, 1, 1, 0));
            }

            var mgt = Mgt.Parse(Export(model));
            int masterId = Mgt.IdOf(model, model.Nodes[0]);
            foreach (var lc in cases.Take(2))
            {
                var row = mgt.ConLoads[$"UF_L{lc.Level}_{lc.No}"].Single();
                Assert.AreEqual(masterId, (int)row[0]);
                var f = new Vector3D(row[1], row[2], row[3]);
                var mAtMaster = new Vector3D(row[4], row[5], row[6]);
                // 原点まわり: MGT = M + r_master × F、解析 = r_P × F
                var p = new Vector3D(lc.ForceActionPointX, lc.ForceActionPointY, lc.ForceActionPointAltitude);
                double rad = lc.LoadAngle * Math.PI / 180;
                var fExpected = new Vector3D(100 * Math.Cos(rad), 100 * Math.Sin(rad), 0);
                Assert.AreEqual(0, (f - fExpected).Length, 1e-3, $"{lc.LoadName}: 力が違います");
                var mgtMoment = mAtMaster + Vector3D.CrossProduct((Vector3D)master, f);
                var expected = Vector3D.CrossProduct(p, fExpected);
                Assert.AreEqual(0, (mgtMoment - expected).Length, 1e-2, $"{lc.LoadName}: 原点まわりのモーメントが作用点に掛けたものと違います");
            }
            var r0 = mgt.ConLoads[$"UF_L{cases[0].Level}_{cases[0].No}"].Single();
            var r1 = mgt.ConLoads[$"UF_L{cases[1].Level}_{cases[1].No}"].Single();
            Assert.IsTrue(Math.Abs(r1[6] - r0[6]) > 1, "作用点の違う 2 ケースのねじりモーメントが同じです");
        }

        // ── 土圧合力ばね・地盤変位 ─────────────────────────────

        /// <summary>
        /// 土圧合力ばねの力は、その節点を上端・下端に持つ区間の割線剛性 (区間の半分) の和 × 変位 (解析と同じ)。X と Y で受ける面が違う。
        /// 以前は杭のばねとして扱って反力の情報が引けず、解析前の剛性 (0) で書いていた。
        /// 根入部の地盤節点にも地盤変位を与える (以前は杭周の地盤節点にだけ与えていた)。
        /// </summary>
        [TestMethod]
        public void EarthPressureSprings_KeepTheirForce_AndGetTheGroundDisplacement()
        {
            // 計算例9 の根入部の区間に土圧合力ばねを置く (区間・土質は手で与える)
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            var embedment = input?.ElementDivision?.SoilEmbedment;
            if (input == null || embedment == null) { Assert.Inconclusive("(前提) 根入部のある例題"); return; }
            var zds = new System.Collections.ObjectModel.ObservableCollection<EmbedmentZDataItem>(
                new[] { 0.0, -0.8, -1.5, -2.5 }.Select(z => new EmbedmentZDataItem { Z = z }));
            embedment.ZDataItems = zds;
            var items = new System.Collections.ObjectModel.ObservableCollection<DoatsuGoryokuBaneItem>();
            for (int i = 0; i + 1 < zds.Count; i++)
            {
                var item = new DoatsuGoryokuBaneItem
                {
                    ZTop = zds[i].Z, ZBtm = zds[i + 1].Z, X1 = -2, X2 = 2, Y1 = -3, Y2 = 3,
                    Gamma = 18, Phi = 30, C = 10, K0 = 0.5, StressTop = -18 * zds[i].Z, StressBtm = -18 * zds[i + 1].Z,
                };
                item.SetDeltaPAndYsp(0.3, 0.03);
                items.Add(item);
            }
            input.ElementDivision.DoatsuGoryokuBane = new DoatsuGoryokuBane { Items = items };
            for (int i = 0; i < zds.Count; i++) zds[i].GroundDisp1 = 5 + i;
            Assert.IsTrue(input.ElementDivision.HasEarthPressureSprings(), "(前提) 土圧合力ばねが入ること");
            var model = Build(input);
            var lc = input.LoadCasesInput.LoadCasesLevel1[0];
            model.AnalysisStepResults.Add(new AnalysisStepResult(lc, Comb(), false, 1, 1, 0));
            var mgt = Mgt.Parse(Export(model));

            var springs = items.SelectMany(it => new[] { it.TopHorizontalSoilSpring, it.BtmHorizontalSoilSpring }).Where(x => x != null)
                .Distinct(ReferenceEqualityComparer.Instance).Cast<HorizontalSoilSpring>().ToList();
            Assert.AreEqual(zds.Count, springs.Count, "(前提) 区間の境界ごとに 1 本のばね");
            const double u = 0.005;
            foreach (var sp in springs)
            {
                var mine = items.Where(it => ReferenceEquals(it.TopHorizontalSoilSpring, sp) || ReferenceEquals(it.BtmHorizontalSoilSpring, sp)).ToList();
                double expectedX = mine.Sum(it => it.GetSecantStiffness(u) * it.DY * it.DZ * 0.5) * u;
                Assert.IsTrue(expectedX > 0, "(前提) 土圧合力ばねに剛性があること");
                Assert.AreEqual(expectedX, mgt.ForceAt(XFunctionOf(mgt, model, sp.NodeI), u), Math.Max(1e-4, 1e-6 * expectedX),
                    $"Z = {sp.NodeI.Coord.Z}: 力が解析と違います (区間 {mine.Count} つ分)");
            }

            var gd = mgt.SpDisp.GetValueOrDefault($"GD_L{lc.Level}_{lc.No}") ?? [];
            double rad = lc.LoadAngle * Math.PI / 180;
            int withDisp = 0;
            foreach (var zd in zds)
            {
                double d = zd.GroundDisp1 / 1000 * Math.Cos(rad);
                if (Math.Abs(d) <= 1e-9) continue;
                var node = springs.Select(x => x.NodeJ).First(n => Math.Abs(n.Coord.Z - zd.Z) < 1e-6);
                var row = gd.SingleOrDefault(r => r.Node == Mgt.IdOf(model, node));
                Assert.AreEqual("100000", row.Flag, $"根入部の地盤節点 (Z = {zd.Z}) に地盤変位を与えていません");
                Assert.AreEqual(d, row.Dx, 1e-6);
                withDisp++;
            }
            Assert.IsTrue(withDisp > 0, "(前提) 根入部に地盤変位があること");
        }

        /// <summary>
        /// Y 方向の地盤変位は、Y 方向の地盤ばねがつながる仮想節点に与える。以前は X 方向用の地盤節点に X・Y を与えていたので、
        /// Y 方向のばねには地盤変位が伝わらなかった。常時 (VL) のケースには地盤変位を与えない (解析と同じ)。
        /// </summary>
        [TestMethod]
        public void YGroundDisplacement_GoesToTheYSpringGroundNode()
        {
            var (input, model) = Example("Example3_4", "PileExample3_4");
            var lc = input.LoadCasesInput.LoadCasesLevel1[0];
            lc.LoadAngle = 90;   // Y 方向だけ
            model.AnalysisStepResults.Add(new AnalysisStepResult(lc, Comb(), false, 1, 1, 0));
            var vl = lc.DeepCopy();
            vl.LoadName = "VL"; vl.No = 98;
            model.AnalysisStepResults.Add(new AnalysisStepResult(vl, Comb(), false, 1, 1, 0));
            var mgt = Mgt.Parse(Export(model));
            var gd = mgt.SpDisp[$"GD_L{lc.Level}_{lc.No}"];
            Assert.IsTrue(gd.Count > 0, "(前提) 地盤変位があること");
            int checkedRows = 0;
            foreach (var pile in input.PileLayoutItems)
            {
                var zData = pile.SoilPileAt(input)!.ZDataItems;
                for (int i = 0; i < pile.PileNodes.Count && i < zData.Count; i++)
                {
                    double d = zData[i].GroundDisp1 / 1000;
                    if (Math.Abs(d) <= 1e-9) continue;
                    var pileNode = pile.PileNodes[i];
                    var yLink = mgt.MultiLinear.Single(l => l.I == Mgt.IdOf(model, pileNode)
                        && Math.Abs(mgt.Nodes[l.J].Y - pileNode.Coord.Y - 1.0) < 1e-9 && Math.Abs(mgt.Nodes[l.J].X - pileNode.Coord.X) < 1e-9);
                    var row = gd.Single(r => r.Node == yLink.J);
                    Assert.AreEqual("010000", row.Flag);
                    Assert.AreEqual(d, row.Dy, 1e-6, $"杭 No.{pile.No} 節点 {i}: Y 方向のばねの地盤節点に地盤変位が入っていません");
                    checkedRows++;
                }
            }
            TestSource.AssertScanned(checkedRows, 3, "地盤変位のある杭の節点");
            Assert.IsFalse(gd.Any(r => r.Flag != "010000"), "Y 方向だけの荷重で X 方向の地盤変位を与えています");
            Assert.IsFalse(mgt.SpDisp.ContainsKey($"GD_L{vl.Level}_{vl.No}"), "常時 (VL) のケースに地盤変位を与えています");
        }

        // ── 書けないばね ─────────────────────────────

        /// <summary>杭・土圧合力の区間のどれにも属さないばねは、向きと荷重-変形関係が決まらないので、理由を示して出力を止める。</summary>
        [TestMethod]
        public void SpringsOfUnknownKind_StopTheExport()
        {
            var (_, model) = Example("Example3_4", "PileExample3_4");
            var a = model.Nodes[^1];
            model.HorizontalSoilSprings.Add(new HorizontalSoilSpring("謎のばね", a, a));
            var ex = Assert.ThrowsException<InvalidOperationException>(() => new MgtExporter(model).DescribeApproximations());
            StringAssert.Contains(ex.Message, "「謎のばね」");
            StringAssert.Contains(ex.Message, "どれでもなく");
        }
    }
}

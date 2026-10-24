using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Windows.Media.Media3D;
using Material = PileDesign.FEM.Material;

namespace TestProject1
{
    /// <summary>
    /// 中間に地盤ばねを持つ有限長の片持ち杭を、剛性行列を使わない柔性法の独立解と比較する。
    /// 本番の土層からのばね生成・非線形材料則は対象外で、FEM の組立・座標変換・解法・断面力を検査する。
    /// </summary>
    [TestClass]
    public class HorizontalIndependentSolutionTests
    {
        [DataTestMethod]
        [DataRow(0.0, 0.0)]
        [DataRow(-3.5, 30.0)]
        [DataRow(12.0, 90.0)]
        public void FinitePileWithIntermediateSpring_MatchesFlexibilitySolution(double headZ, double angle)
        {
            const double length = 10, a = 4, ei = 100000, k = 2000, load = 10;
            Node Make(string name, double height, bool fixedNode) => new()
            {
                Name = name,
                Coord = new Point3D(2, -3, headZ - length + height),
                Boundary = new Boundary(fixedNode, fixedNode, fixedNode, fixedNode, fixedNode, fixedNode)
            };
            var tip = Make("杭先端", 0, true);
            var mid = Make("中間節点", a, false);
            var head = Make("杭頭", length, false);
            var soil = Make("地盤節点", a, true);
            var material = new Material(30000000, 0.2);
            var section = new Section(material, 1, 1, 1, 0.01, ei / material.E, ei / material.E);
            var upper = new Beam("上側", section, head, mid, 1, 1);
            var lower = new Beam("下側", section, mid, tip, 1, 1);
            var spring = new HorizontalSoilSpring("地盤ばね", mid, soil);
            spring.SetKe(k, k, 0, 0, 0, 0, true);
            spring.SetKe(k, k, 0, 0, 0, 0, false);
            var model = new AnaModel(new InputModel(), [head, mid, tip, soil],
                [upper, lower], [], [], [spring], []);
            double radians = angle * Math.PI / 180;
            var direction = new Vector3D(Math.Cos(radians), Math.Sin(radians), 0);
            head.SetIncrementalLoad(new NodeLoad(load * direction.X, load * direction.Y, 0, 0, 0, 0));
            head.UpdateCumulativeLoad();
            model.MapOnVectorF();
            model.UpdateVectorDOFForcedDisp();
            model.MapOnKtanMat();
            model.InitializeVectorR();
            for (int i = 0; i < model.CountFree; i++) model.VectorR[i] = model.VectorF[i];
            Solver.SolveDisp(model);

            // 中間変位 = P f(a,L) − R f(a,a)、R = k × 中間変位。
            double fAL = a * a * (3 * length - a) / (6 * ei);
            double fAA = a * a * a / (3 * ei);
            double reaction = k * load * fAL / (1 + k * fAA);
            double headDisp = load * Math.Pow(length, 3) / (3 * ei) - reaction * fAL;
            double rotation = load * length * length / (2 * ei) - reaction * a * a / (2 * ei);
            foreach (var (node, expected) in new[] { (head, headDisp), (mid, reaction / k), (tip, 0.0) })
            {
                Assert.AreEqual(expected * direction.X, node.CumulativeDisp.Ux, 1e-11, $"{node.Name}: X変位");
                Assert.AreEqual(expected * direction.Y, node.CumulativeDisp.Uy, 1e-11, $"{node.Name}: Y変位");
            }
            Assert.AreEqual(-rotation * direction.Y, head.CumulativeDisp.Rx, 1e-11, "杭頭回転X");
            Assert.AreEqual(rotation * direction.X, head.CumulativeDisp.Ry, 1e-11, "杭頭回転Y");
            spring.SetBeamDispAndForce();
            Assert.AreEqual(reaction * direction.X, spring.CumulativeForce.Fxi, 1e-8, "ばね反力X");
            Assert.AreEqual(reaction * direction.Y, spring.CumulativeForce.Fyi, 1e-8, "ばね反力Y");

            foreach (var beam in new[] { upper, lower })
            {
                beam.SetBeamDispAndForce();
                var global = beam.GetCachedCoordTransform().Transpose() * beam.CumulativeForce.GetVector();
                double shear = beam == upper ? load : load - reaction;
                double mi = beam == upper ? 0 : load * (length - a);
                double mj = beam == upper ? load * (length - a) : load * length - reaction * a;
                foreach (var (offset, sign, moment) in new[] { (0, 1.0, mi), (6, -1.0, mj) })
                {
                    Assert.AreEqual(sign * shear * direction.X, global[offset], 1e-8, $"{beam.Name}: せん断力X");
                    Assert.AreEqual(sign * shear * direction.Y, global[offset + 1], 1e-8, $"{beam.Name}: せん断力Y");
                    Assert.AreEqual(-sign * moment * direction.Y, global[offset + 3], 1e-8, $"{beam.Name}: 曲げX");
                    Assert.AreEqual(sign * moment * direction.X, global[offset + 4], 1e-8, $"{beam.Name}: 曲げY");
                }
            }
        }
    }
}

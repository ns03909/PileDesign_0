using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 「基礎のねじれを拘束」オプション。
    ///
    /// 慣性力作用点 (基礎の代表節点) の Z 軸まわり回転を拘束する。
    /// 境界条件は方程式番号による縮約なので、Rz の式が剛性行列から消え、
    /// ねじれが厳密にゼロになる。剛体の cross-term (Ux += −Rz·ΔY 等) も
    /// master の Rz が消えることで落ちるため、<b>杭頭の水平変位が全杭で揃う</b>。
    ///
    /// 既定は OFF (従来どおり)。ON/OFF で結果が変わるので、既定では
    /// 1 ビットも動かないことが要。
    /// </summary>
    [TestClass]
    public class FoundationTorsionOptionTests
    {
        private static InputModel? Example() =>
            IntegrationTests.BuildExampleInputModel("Example9", "PileExample9").Item1;

        /// <summary>代表節点は Nodes[0]（ActionPoint）。</summary>
        private static Node ActionPointOf(InputModel input) =>
            new AnalysisModelling(input).Nodes[0];

        [TestMethod]
        public void DefaultIsOff()
        {
            Assert.IsFalse(new InputModel().RestrainFoundationTorsion,
                "既定で ON になっていると、既存モデルの結果が黙って変わる");
        }

        /// <summary>
        /// 既定 (OFF) では代表節点の回転を拘束しないこと。
        /// </summary>
        [TestMethod]
        public void Off_LeavesTheActionPointFree()
        {
            var input = Example();
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            if (IsSingleRow(input)) { Assert.Inconclusive("1 列配置の例題では元から拘束される"); return; }

            var ap = ActionPointOf(input);

            Assert.IsFalse(ap.Boundary.Rz, "既定でねじれが拘束されている");
            Assert.IsFalse(ap.Boundary.Rx);
            Assert.IsFalse(ap.Boundary.Ry);
        }

        /// <summary>
        /// ON では Z 軸まわりだけ拘束すること。
        /// Rx / Ry（基礎の傾き）まで止めると別の解析になってしまう。
        /// </summary>
        [TestMethod]
        public void On_FixesOnlyTheRotationAboutZ()
        {
            var input = Example();
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            if (IsSingleRow(input)) { Assert.Inconclusive("1 列配置の例題では元から拘束される"); return; }

            input.RestrainFoundationTorsion = true;
            var ap = ActionPointOf(input);

            Assert.IsTrue(ap.Boundary.Rz, "Z 軸まわりが拘束されていない");
            Assert.IsFalse(ap.Boundary.Rx, "X 軸まわりまで拘束している");
            Assert.IsFalse(ap.Boundary.Ry, "Y 軸まわりまで拘束している");
            Assert.IsFalse(ap.Boundary.Ux, "水平変位まで拘束している");
            Assert.IsFalse(ap.Boundary.Uy);
            Assert.IsFalse(ap.Boundary.Uz);
        }

        /// <summary>
        /// 拘束した Rz は剛性行列から取り除かれること（ペナルティではなく縮約）。
        /// 自由度の総数が 1 つ減る。
        /// </summary>
        [TestMethod]
        public void On_RemovesOneDegreeOfFreedom()
        {
            var input = Example();
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            if (IsSingleRow(input)) { Assert.Inconclusive("1 列配置の例題では元から拘束される"); return; }

            int free = FreeDofCount(input, ignoreTorsion: false);
            int fixedRz = FreeDofCount(input, ignoreTorsion: true);

            Assert.AreEqual(free - 1, fixedRz,
                "拘束しても自由度が減っていない (剛性行列から消えていない)");
        }

        /// <summary>
        /// 杭配置が 1 列のモデルでは、元から Rz が拘束されるので ON/OFF で変わらないこと。
        /// </summary>
        [TestMethod]
        public void SingleRowLayoutIsUnaffected()
        {
            var input = Example();
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            // 全杭を Y=0 の 1 列に寄せる
            foreach (var pile in input.PileLayoutItems) pile.Y = 0.0;

            input.RestrainFoundationTorsion = false;
            Assert.IsTrue(ActionPointOf(input).Boundary.Rz, "1 列配置では元から拘束されるはず");

            input.RestrainFoundationTorsion = true;
            Assert.IsTrue(ActionPointOf(input).Boundary.Rz);
        }

        private static bool IsSingleRow(InputModel input)
        {
            var piles = input.PileLayoutItems;
            return piles.Max(p => p.X) - piles.Min(p => p.X) < 1e-6
                || piles.Max(p => p.Y) - piles.Min(p => p.Y) < 1e-6;
        }

        /// <summary>組み上げた FEM モデルの自由度数。</summary>
        private static int FreeDofCount(InputModel input, bool ignoreTorsion)
        {
            input.RestrainFoundationTorsion = ignoreTorsion;
            var modelling = new AnalysisModelling(input);
            var ana = new AnaModel(
                input, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            return ana.CountFree;
        }
    }
}

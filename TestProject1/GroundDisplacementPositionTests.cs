using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 土質点の地盤変位を、層の上端 (地表から層厚 H を積んだ位置) に置いていること。
    ///
    /// <para>地盤の質量・モード形の漸化式は、土質点 i を層 i の上端に置く (U0 = 地表、最後の土質点が
    /// 基盤面 U* = 0)。一方、解析の強制変位は土質点を土質データの深度 (AltitudeDepth。N 値の深度で、
    /// 層のほぼ中央) に置いて補間しており、分布が半層ほど深くなっていた。画面・計算書のグラフと
    /// 「自動→カスタム」は GLDepth + 間隔×(1 / 0.5 / 0) という第 3 の置き方だった。
    /// 例表 (基礎指針'19 計算例2 例表2.2) が層厚だけを与えることも踏まえ、2026-09-12 に
    /// 利用者の判断で層の上端に揃えた。</para>
    /// </summary>
    [TestClass]
    public class GroundDisplacementPositionTests
    {
        private static readonly double[] H = [0.8, 0.7, 1.0, 1.0];
        private static readonly double[] D = [10.0, 8.0, 5.0, 0.0];   // 最後の土質点 = 基盤面

        private static GroundInput Ground()
        {
            var g = new GroundInput { GroundTopAltitude = 0.0 };
            g.GroundMassesData = [];
            for (int i = 0; i < H.Length; i++)
            {
                // 土質データの深度 (GLDepth / AltitudeDepth) は層の中ほど。変位の位置ではない
                var m = new GroundMassDataInput { H = H[i], GLDepth = -(0.6 + i), AltitudeDepth = -(0.6 + i) };
                m.DmaxUStar = [D[i], 2 * D[i]];
                m.DmaxUStarSigmaGammaCyH = [3 * D[i], 4 * D[i]];
                g.GroundMassesData.Add(m);
            }
            return g;
        }

        /// <summary>
        /// 液状化分 ΣγcyH は「その質点より下にある層の γcy·H の総和」であること。
        ///
        /// <para>これが層の上端に置く<b>物理の根拠</b>。ΣγcyH は基盤面から上へ積んだせん断ひずみの
        /// 積分なので、値が属する位置は積分を止めた位置＝質点そのもの（層の上端）である。
        /// 層厚 H は質点 i と質点 i+1 の間の層のもの（ばね剛性 K = ρVse²/H と同じ区間）なので、
        /// 質点 i の総和には自分の層 i が丸ごと入る。土質データの深度（層のほぼ中央）に置くと
        /// 半層ぶん深くへずれる。</para>
        /// </summary>
        [TestMethod]
        public void TheLiquefactionShearStrainIsIntegratedUpToTheNode()
        {
            var vm = new GroundLayerViewModel(new MainWindowViewModel());
            var g = new GroundInput { GroundTopAltitude = 0.0 };
            g.GroundMassesData = [];

            double[] h = [2.0, 3.0, 1.5];
            double[] gammaCy = [4.0, 2.0, 1.0];   // %
            for (int i = 0; i < h.Length; i++)
            {
                var m = new GroundMassDataInput { H = h[i], GLDepth = -(1.0 + i), IsEngineeringBedrock = false };
                m.GammaCy = [gammaCy[i], gammaCy[i]];
                g.GroundMassesData.Add(m);
            }
            // 最後の質点は工学的基盤面 (変位 0 の基準)
            var bedrock = new GroundMassDataInput { H = 0.0, GLDepth = -10.0, IsEngineeringBedrock = true };
            bedrock.GammaCy = [0.0, 0.0];
            g.GroundMassesData.Add(bedrock);

            vm.GroundInput = g;
            vm.RecalculateSigmaGammaCyH();

            // 期待値: 質点 i から下の層の γcy[%]/100 × H[m] × 1000 [mm] の総和
            for (int level = 0; level < 2; level++)
            {
                for (int i = 0; i < h.Length; i++)
                {
                    double expected = 0.0;
                    for (int j = i; j < h.Length; j++)
                        expected += gammaCy[j] / 100.0 * h[j] * 1000.0;

                    Assert.AreEqual(expected, g.GroundMassesData[i].SigmaGammaCyH[level], 1e-9,
                        $"レベル{level + 1} 質点{i + 1}: ΣγcyH がその質点より下の層の総和になっていません。"
                        + "積分を止めた位置 (= 質点 = 層の上端) の値であることが、変位をそこに置く根拠です");
                }
            }

            Assert.AreEqual(0.0, g.GroundMassesData[^1].SigmaGammaCyH[0], 0.0,
                "工学的基盤面の変位は 0 であること (積分の起点)");
        }

        [TestMethod]
        public void MassesSitAtLayerTopsStackedFromTheSurface()
        {
            double[] expected = [0.0, -0.8, -1.5, -2.5];
            double[] tops = Ground().MassTopAltitudes();
            Assert.AreEqual(expected.Length, tops.Length);
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], tops[i], 1e-12, $"土質点 {i} の位置");
        }

        [DataTestMethod]
        [DataRow(1.0, 10.0)]    // 地上: 地表の値
        [DataRow(0.0, 10.0)]
        [DataRow(-0.4, 9.0)]
        [DataRow(-0.8, 8.0)]    // 2 番目の層の上端 (土質データの深度 −1.6 ではない)
        [DataRow(-1.15, 6.5)]
        [DataRow(-2.0, 2.5)]
        [DataRow(-3.0, 0.0)]    // 基盤面より深い: 0
        public void AnalysisInterpolatesBetweenLayerTops(double z, double expected)
        {
            var item = new ZDataItem { GroundInput = Ground(), Z = z };
            item.SetSoilDisplacement();
            Assert.AreEqual(expected, item.GroundDisp1, 1e-9, "L1");
            Assert.AreEqual(2 * expected, item.GroundDisp2, 1e-9, "L2");
            Assert.AreEqual(3 * expected, item.GroundDisp1L, 1e-9, "L1 液状化");
            Assert.AreEqual(4 * expected, item.GroundDisp2L, 1e-9, "L2 液状化");
        }

        /// <summary>
        /// FL のグラフは土質点の深度 (N 値などの土質データの深度) に描く。以前は地盤ウィンドウと
        /// 計算書だけ GLDepth + 間隔×(1 / 0.5 / 0) で、杭姿図 (土質点の深度) と食い違っていた。
        /// </summary>
        [TestMethod]
        public void FlGraphsUseTheSoilDataDepth()
        {
            foreach (var (path, marker, label) in new[]
            {
                (new[] { "Graphics_r1", "ViewModels", "GroundLayerViewModel.Graphs.cs" }, "void DrawFLGraph(", "地盤ウィンドウの FL"),
                (new[] { "Graphics_r1", "Output", "WordDocument.GroundGraphs.cs" }, "bool AddFLDataToPlot(", "計算書の FL"),
            })
            {
                string src = Regex.Replace(TestSource.Read(path), "//.*", "");
                int a = src.IndexOf(marker, StringComparison.Ordinal);
                int b = src.IndexOf("private ", a + marker.Length, StringComparison.Ordinal);
                Assert.IsTrue(a >= 0 && b > a, $"{marker} が見つかりません");
                string body = src[a..b];
                Assert.IsFalse(Regex.IsMatch(body, @"\.Spacing\s*\*"), $"{label}のグラフに GLDepth + 間隔×係数 の置き方が残っています");
                StringAssert.Contains(body, "GLDepth", $"{label}のグラフが土質点の深度を使っていません");
            }
        }

        [TestMethod]
        public void EveryDisplacementViewUsesTheLayerTops()
        {
            foreach (var (path, label) in new[]
            {
                (new[] { "Graphics_r1", "ViewModels", "GroundLayerViewModel.Graphs.cs" }, "地盤ウィンドウのグラフ"),
                (new[] { "Graphics_r1", "Output", "WordDocument.GroundGraphs.cs" }, "計算書のグラフ"),
                (new[] { "Graphics_r1", "Views", "GroundWindow.xaml.cs" }, "自動→カスタム"),
            })
            {
                string src = Regex.Replace(TestSource.Read(path), "//.*", "");
                // 地盤ウィンドウと計算書は FL のグラフも同じファイルにある (FL は土質データの深度の話なので対象外)。
                // 地盤変位のグラフのメソッドだけを見る。「自動→カスタム」はファイル全体 (ボタンが 2 つある)
                string? method = label switch
                {
                    "地盤ウィンドウのグラフ" => "void DrawGroundDisplacementGraph(",
                    "計算書のグラフ" => "void AddGroundDisplacementGraph(",
                    _ => null,
                };
                if (method != null)
                {
                    int a = src.IndexOf(method, StringComparison.Ordinal);
                    int b = src.IndexOf("private ", a + method.Length, StringComparison.Ordinal);
                    Assert.IsTrue(a >= 0 && b > a, $"{method} が見つかりません");
                    src = src[a..b];
                }
                StringAssert.Contains(src, "MassTopAltitudes()", $"{label}が土質点の変位の位置を共通の関数で求めていません");
                Assert.IsFalse(Regex.IsMatch(src, @"\.Spacing\s*\*"), $"{label}に GLDepth + 間隔×係数 の置き方が残っています");
            }

            string zData = Regex.Replace(TestSource.Read("Graphics_r1", "Models", "InputData", "ZDataItem.cs"), "//.*", "");
            int at = zData.IndexOf("void SetSoilDisplacement(", StringComparison.Ordinal);
            int end = zData.IndexOf("GetInterpolatedValue(double x", at, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0 && end > at, "SetSoilDisplacement が見つかりません");
            Assert.IsFalse(zData[at..end].Contains("AltitudeDepth", StringComparison.Ordinal),
                "解析の強制変位が土質データの深度 (AltitudeDepth) で補間しています");
        }
    }
}

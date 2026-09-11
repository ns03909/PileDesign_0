using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 地盤変位を描く箇所が、解析と同じ値・同じ深さの向きで描いていること。
    ///
    /// <para>2026-09-11 に 2 件見つかった。</para>
    /// <para>1. 計算書の地盤変位グラフで、<b>任意入力の曲線だけ深さの符号が逆</b>だった
    /// (<c>topAlt - p.Z</c>)。同じグラフの土層と自動計算の曲線は「地表 0・地中で負」で
    /// 描いているので、任意入力の曲線だけが地上側に鏡映しで出ていた。画面側は正しかった。</para>
    /// <para>2. 杭体ウィンドウの地盤変位の重ね描きが、地盤変位の「考慮しない」「任意入力」を
    /// 見ずに自動計算値 (<c>DmaxUStar</c>) を描いていた。解析 (<c>ZDataItem</c>) と
    /// メイン画面はモードに従うので、その画面だけが解析と違う値を見せていた。</para>
    ///
    /// <para>どちらも「同じ変換・同じ判定を各所が自前で書いていた」のが根なので、
    /// <c>GroundInput</c> に 1 つずつ置いて全員がそれを通るようにした。</para>
    /// </summary>
    [TestClass]
    public class GroundDisplacementDisplayTests
    {
        [TestMethod]
        public void CustomProfileDepthUsesOneConversion()
        {
            foreach (var (path, label) in new[]
            {
                (new[] { "Graphics_r1", "Output", "WordDocument.GroundGraphs.cs" }, "計算書"),
                (new[] { "Graphics_r1", "ViewModels", "GroundLayerViewModel.Graphs.cs" }, "地盤ウィンドウ"),
            })
            {
                string src = StripComments(TestSource.Read(path));
                StringAssert.Contains(src, "ToGLDepth(p.Z)",
                    $"{label}の任意入力の曲線が、標高→GL 基準深さの変換を自前で書いています");
                Assert.IsFalse(Regex.IsMatch(src, @"\w+\s*-\s*p\.Z\b|p\.Z\s*-\s*\w+"),
                    $"{label}に標高と地表面の引き算が残っています (向きの取り違えの元)");
            }
        }

        [TestMethod]
        public void PileBodyOverlayFollowsTheDisplacementMode()
        {
            string body = MethodBody(
                StripComments(TestSource.Read("Graphics_r1", "Common", "DrawPileElevation.cs")),
                "void DrawDisplacementValues(");
            StringAssert.Contains(body, "GetMassDisplacement(",
                "杭体ウィンドウの地盤変位の重ね描きが、地盤変位のモードを通っていません");
            Assert.IsFalse(body.Contains("DmaxUStar", StringComparison.Ordinal),
                "杭体ウィンドウの地盤変位の重ね描きが、自動計算値を直に読んでいます");
        }

        [TestMethod]
        public void MainScreenUsesTheSameModeLogic()
        {
            string body = MethodBody(
                StripComments(TestSource.Read("Graphics_r1", "Views", "MainWindow.CanvasUpdate.cs")),
                "double GetDispFromGroundMass(");
            StringAssert.Contains(body, "GetMassDisplacement(",
                "メイン画面の質点の地盤変位が、共通の判定を通っていません");
        }

        [TestMethod]
        public void ToGLDepthIsZeroAtTheSurfaceAndNegativeBelow()
        {
            var ground = new GroundInput { GroundTopAltitude = 5.0 };
            Assert.AreEqual(0.0, ground.ToGLDepth(5.0), 1e-12);
            Assert.AreEqual(-3.0, ground.ToGLDepth(2.0), 1e-12);
        }

        [TestMethod]
        public void MassDisplacementFollowsTheMode()
        {
            var mass = new GroundMassDataInput { AltitudeDepth = -2.0 };
            mass.DmaxUStar = [10.0, 20.0];
            mass.DmaxUStarSigmaGammaCyH = [30.0, 40.0];
            var ground = new GroundInput();

            // 自動計算: レベル・液状化で 4 つを取り分ける
            Assert.AreEqual(10.0, ground.GetMassDisplacement(mass, 0, false));
            Assert.AreEqual(20.0, ground.GetMassDisplacement(mass, 1, false));
            Assert.AreEqual(30.0, ground.GetMassDisplacement(mass, 0, true));
            Assert.AreEqual(40.0, ground.GetMassDisplacement(mass, 1, true));

            // 任意入力: 質点の標高で補間する (自動計算値は見ない)
            ground.CustomDisplacementProfile = new CustomDisplacementProfile { IsEnabled = true };
            ground.CustomDisplacementProfile.Level2NonLiq.Add(new DisplacementPoint(0.0, 100.0));
            ground.CustomDisplacementProfile.Level2NonLiq.Add(new DisplacementPoint(-4.0, 0.0));
            Assert.AreEqual(50.0, ground.GetMassDisplacement(mass, 1, false), 1e-12);

            // 考慮しない: 任意入力が有効でも 0
            ground.IsGroundDisplacementIgnored = true;
            Assert.AreEqual(0.0, ground.GetMassDisplacement(mass, 1, false));
        }

        private static string StripComments(string src) => Regex.Replace(src, "//.*", "");

        /// <summary><paramref name="signature"/> で始まるメソッドの本体 (波括弧の対応で切り出す)。</summary>
        private static string MethodBody(string src, string signature)
        {
            int at = src.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, $"{signature} が見つかりません (名前が変わった?)");
            int open = src.IndexOf('{', at);
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}' && --depth == 0) return src[open..(i + 1)];
            }
            Assert.Fail($"{signature} の本体の終わりが見つかりません");
            return "";
        }
    }
}

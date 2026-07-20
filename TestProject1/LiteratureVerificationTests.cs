using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 文献の手計算値に対する検証テスト（verification.html の内容の自動テスト化・第1弾）。
    ///
    /// 出典: 建築基礎構造設計指針 2019（基礎指針'19）
    ///  - 計算例2: 地盤の水平変位（例表2.2）— 地表面変位 文献値 127.4 mm（液状化なし）。
    ///    プログラム値 126.9 mm（差 約0.4%、verification.html に記載）。
    ///  - 計算例1: 補正N値から求める繰返しせん断ひずみ γcy（例表1.2）
    ///    文献値（上から）: 4.0 / 8.0 / 1.0 / 2.0 / 0.5 (%)
    ///    プログラム値     : 4.00 / 8.00 / 0.50 / 2.00 / 0.50 (%)
    ///    ※3層目は図3.2.6 のチャート読み取りで倍半分の誤差が許容される旨が文献に明記されている。
    ///
    /// verification.html の比較表の多くは値が画像（PNG）内にあり自動化できないため、
    /// テキストとして記録済みの上記 2 件を先行して固定する。以後、文献値を転記できた
    /// ケースはこのファイルに追記していく方針。
    /// </summary>
    [TestClass]
    public class LiteratureVerificationTests
    {
        private static GroundLayerViewModel LoadExample(string exampleCommand)
        {
            var mainVm = new MainWindowViewModel();
            var glvm = new GroundLayerViewModel(mainVm);
            switch (exampleCommand)
            {
                case "Example1": glvm.Example1Command.Execute(null); break;
                case "Example2": glvm.Example2Command.Execute(null); break;
                default: throw new ArgumentException(exampleCommand);
            }
            return glvm;
        }

        [TestMethod]
        public void Shishin19_Example2_SurfaceGroundDisplacement_MatchesLiterature()
        {
            var glvm = LoadExample("Example2");
            var masses = glvm.GroundInput!.GroundMassesData;
            Assert.IsTrue(masses is { Count: > 0 }, "計算例2 の地盤質点データが空");

            // 地表面 (先頭質点) の レベル2 地盤変位 [mm]（DmaxUStar[1] = Dmax×U*(z)×1000）
            double surfaceMm = masses[0].DmaxUStar[1];

            // 文献値 127.4 mm（例表2.2）。プログラムの既知値は 126.9 mm（差 0.4%）。
            // 許容 ±1.5%（チャート・丸めの範囲。これを超えたら地盤変位算定の回帰を疑う）。
            const double literature = 127.4;
            double relDiff = Math.Abs(surfaceMm - literature) / literature;
            Assert.IsTrue(relDiff <= 0.015,
                $"地表面地盤変位が文献値と乖離: program={surfaceMm:F1} mm, 文献={literature} mm (相対 {relDiff:P2}, 許容 1.5%)");
        }

        [TestMethod]
        public void Shishin19_Example1_CyclicShearStrain_MatchesDocumentedValues()
        {
            var glvm = LoadExample("Example1");
            var masses = glvm.GroundInput!.GroundMassesData;
            Assert.IsTrue(masses is { Count: > 0 }, "計算例1 の地盤質点データが空");

            // 例表1.2 の 5 点は、レベル1 (levelIndex=0) の GL-3.0〜-7.0 m の質点に対応する
            // （実測診断で 4.00/8.00/0.50/2.00/0.50 と一致することを確認済み）。
            var gammaCys = masses
                .Where(m => m.GLDepth <= -2.5 && m.GLDepth >= -7.5)
                .Select(m => (m.GammaCy != null && m.GammaCy.Count > 0) ? m.GammaCy[0] : null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value)
                .ToList();

            // verification.html に記録済みのプログラム値（回帰の固定）。
            double[] expectedProgram = [4.00, 8.00, 0.50, 2.00, 0.50];
            // 文献値（例表1.2）。3層目はチャート読み取りにより倍半分許容と文献に明記。
            double[] literature = [4.0, 8.0, 1.0, 2.0, 0.5];

            Assert.AreEqual(expectedProgram.Length, gammaCys.Count,
                $"γcy の層数が想定と不一致: got [{string.Join(", ", gammaCys.Select(v => v.ToString("F2")))}]");

            for (int i = 0; i < expectedProgram.Length; i++)
            {
                // 1) プログラム既知値との一致（回帰検出、±0.01%）
                Assert.AreEqual(expectedProgram[i], gammaCys[i], 0.01,
                    $"γcy[{i}] がプログラム既知値から変化: got={gammaCys[i]:F2}, expected={expectedProgram[i]:F2}");
                // 2) 文献値と倍半分（factor 2）以内（文献が明記する許容）
                Assert.IsTrue(gammaCys[i] >= literature[i] / 2.0 - 1e-9 && gammaCys[i] <= literature[i] * 2.0 + 1e-9,
                    $"γcy[{i}] が文献値の倍半分を超過: got={gammaCys[i]:F2}, 文献={literature[i]:F1}");
            }
        }
    }
}

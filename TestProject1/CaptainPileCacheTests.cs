using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// キャプテンパイル工法の N-M 曲線が、諸元を変えたら一緒に変わること。
    ///
    /// <c>CaptainPile</c> は <c>(ε₀, φ)</c> を鍵にした
    /// <c>ConcurrentDictionary</c> に N・M を溜めます。ところが算出は鍵だけの
    /// 関数ではなく、<c>CTPConcrete</c>・絞り率 <c>Nu</c>・引張定着筋にも依存します。
    /// そのうえキャッシュは<b>一度も捨てられません</b>。
    ///
    /// 諸元を変えると、鍵が同じところは<b>古い形状で計算した値</b>が返り、
    /// 鍵が変わったところだけ計算し直されます。曲線が新旧の混ざったものになり、
    /// どの断面にも対応しない形になります。
    ///
    /// これは <c>PileSection</c> で 2026-08-30 に直したものと同じ系統
    /// (遅延キャッシュ + 鍵だけの関数ではない算出) で、そちらだけ直っていました。
    /// </summary>
    [TestClass]
    public class CaptainPileCacheTests
    {
        private static CaptainPile Build(double nu, double fc = 24.0)
        {
            // 引数つきのコンストラクタが PC リングのカタログを読む
            var cp = new CaptainPile(fc, 22669.0);
            var ring = cp.PCRings.FirstOrDefault(r => (r.Name ?? "").EndsWith("N"))
                       ?? cp.PCRings.FirstOrDefault();
            Assert.IsNotNull(ring, "PC リングのカタログが読めていません");
            cp.PCRing = ring;
            cp.D = ring.D;
            cp.Nu = nu;
            cp.Update();
            return cp;
        }

        /// <summary>
        /// 安全限界 N-M 曲線の鍵 (ε₀, φ) は、終局ひずみ 0.003 と PC リング径・絞り率
        /// だけで決まり、<b>パイルキャップの Fc には依存しない</b>。一方で N・M は
        /// コンクリートの σ-ε を通じて Fc に依存する。
        ///
        /// つまり Fc を変えても鍵が 1 つも変わらないので、キャッシュが古い Fc で
        /// 計算した曲線をそのまま返す。
        /// </summary>
        [TestMethod]
        public void ChangingThePileCapStrength_ChangesTheUltimateCurve()
        {
            var changed = Build(nu: 1.00, fc: 24.0);
            Assert.IsTrue(changed.UltimateMs.Count > 2, "N-M 曲線が作れていません");

            changed.SetPileCapConcrete(42.0, 26000.0);   // Update() まで走る

            var fresh = Build(nu: 1.00, fc: 42.0);

            CollectionAssert.AreEqual(
                fresh.UltimateMs.Select(m => System.Math.Round(m, 6)).ToList(),
                changed.UltimateMs.Select(m => System.Math.Round(m, 6)).ToList(),
                "パイルキャップの Fc を変えたのに、安全限界 N-M 曲線が変わっていません。"
                + "鍵 (ε₀, φ) は終局ひずみ 0.003 と径・絞り率だけで決まり Fc を含まないので、"
                + "キャッシュが古い Fc の値をそのまま返しています");
        }

        [TestMethod]
        public void ChangingTheContractionRatio_ChangesTheCurve()
        {
            // 絞り率を途中で変えたもの
            var changed = Build(1.00);
            var before = changed.UltimateMs.ToList();
            Assert.IsTrue(before.Count > 2, "N-M 曲線が作れていません");

            changed.Nu = 0.70;
            changed.Update();

            // 最初から 0.70 で作ったもの
            var fresh = Build(0.70);

            CollectionAssert.AreEqual(
                fresh.UltimateMs.Select(m => System.Math.Round(m, 6)).ToList(),
                changed.UltimateMs.Select(m => System.Math.Round(m, 6)).ToList(),
                "絞り率を変えたあとの N-M 曲線が、最初からその絞り率で作ったものと違います。"
                + "鍵 (ε₀, φ) が同じところで、古い形状のまま溜めた値が返っています。"
                + "諸元が変わったらキャッシュを捨ててください");
        }
    }
}

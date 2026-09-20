using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 荷重-沈下曲線から沈下量を引けなかったとき、<b>黙って前回値が残らない</b>こと。
    ///
    /// <para><see cref="VerticalLoadTransferMethod.GetDisplacementForGivenLoad"/> は引けないと
    /// null を返し、呼び出し側は null なら何もしない。杭の沈下量プロパティには前回の解析の値が
    /// そのまま残る。そのまま黙っていると、更新できなかったことが誰にも伝わらない。</para>
    ///
    /// <para>引けない経路は 3 つ。曲線が 2 点に満たない / 軸力が曲線の範囲を超えている /
    /// どの区間にも入らない (軸力が NaN だと比較がすべて偽になるのでここへ来る)。
    /// いずれも <c>Warnings</c> に積み、沈下ウィンドウが解析後に出す。</para>
    /// </summary>
    [TestClass]
    public class SettlementCurveLookupTests
    {
        /// <summary>
        /// 曲線を 2 点だけ持つ土層-杭セットを作る。
        /// <see cref="VerticalLoadTransferMethod"/> はコンストラクタで解析まで走るため、
        /// ここでは <c>GetDisplacementForGivenLoad</c> の引き当てだけを見たい。
        /// 曲線を直接差し替えて確かめる。
        /// </summary>
        private static VerticalLoadTransferMethod? BuildWithCurve()
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            var soilPile = input?.ElementDivision?.SoilPiles?.FirstOrDefault();
            if (input == null || soilPile == null) return null;

            var vtm = new VerticalLoadTransferMethod(input, soilPile);
            vtm.LoadDisplacements.Clear();
            vtm.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement
            { PileTopLoad = 0, DD0s = 0, DDns = 0 });
            vtm.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement
            { PileTopLoad = 1000, DD0s = 5, DDns = 3 });
            vtm.Warnings.Clear();   // 解析そのものの警告は対象外
            return vtm;
        }

        /// <summary>範囲内なら引けて、何も知らせないこと (毎回警告を出さない)。</summary>
        [TestMethod]
        public void InsideTheCurveItInterpolatesQuietly()
        {
            var vtm = BuildWithCurve();
            if (vtm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            var d = vtm.GetDisplacementForGivenLoad(500);

            Assert.IsNotNull(d, "範囲内なのに引けていない");
            Assert.AreEqual(0.0025, d[0], 1e-9, "線形補間の値が違う (5mm の半分 = 2.5mm = 0.0025m)");
            Assert.AreEqual(0, vtm.Warnings.Count, "範囲内なのに知らせている: " + string.Join(" / ", vtm.Warnings));
        }

        /// <summary>
        /// 軸力が曲線の範囲を超えていたら、端の値を使ったことを知らせること。
        /// 曲線が極限で終わっていると沈下量を小さく見積もる。
        /// </summary>
        [TestMethod]
        public void BeyondTheCurveItSaysItUsedTheEnd()
        {
            var vtm = BuildWithCurve();
            if (vtm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            var d = vtm.GetDisplacementForGivenLoad(5000);

            Assert.IsNotNull(d, "端の値を返すこと");
            Assert.AreEqual(1, vtm.Warnings.Count, "範囲を超えたことを知らせていない");
            StringAssert.Contains(vtm.Warnings[0], "範囲");
        }

        /// <summary>
        /// <b>軸力が数値でないときは知らせること。</b> 比較がすべて偽になるので範囲外の判定も
        /// 素通りし、どの区間にも入らずに null で返る。呼び出し側は何もしないので、
        /// 沈下量は前回の値のまま残る。
        /// </summary>
        [TestMethod]
        public void ANotANumberLoadIsReported()
        {
            var vtm = BuildWithCurve();
            if (vtm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            var d = vtm.GetDisplacementForGivenLoad(double.NaN);

            Assert.IsNull(d, "NaN で値を返してはいけない");
            Assert.AreEqual(1, vtm.Warnings.Count, "沈下量を更新できなかったことを知らせていない");
            StringAssert.Contains(vtm.Warnings[0], "数値でない");
            StringAssert.Contains(vtm.Warnings[0], "更新できませんでした");
        }

        /// <summary>曲線が 2 点に満たないときも知らせること (解析が行き詰まった場合)。</summary>
        [TestMethod]
        public void AnEmptyCurveIsReported()
        {
            var vtm = BuildWithCurve();
            if (vtm == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            vtm.LoadDisplacements.Clear();
            vtm.Warnings.Clear();

            var d = vtm.GetDisplacementForGivenLoad(500);

            Assert.IsNull(d);
            Assert.AreEqual(1, vtm.Warnings.Count, "曲線が無いことを知らせていない");
            StringAssert.Contains(vtm.Warnings[0], "得られなかった");
        }

        /// <summary>同じことを何度も積まないこと (杭ごと・荷重ケースごとに呼ばれる)。</summary>
        [TestMethod]
        public void TheSameNoteIsNotRepeated()
        {
            var vtm = BuildWithCurve();
            if (vtm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            for (int i = 0; i < 5; i++) vtm.GetDisplacementForGivenLoad(double.NaN);

            Assert.AreEqual(1, vtm.Warnings.Count,
                "同じ警告が積み上がっている (杭 × 荷重ケースの数だけ出る): " + string.Join(" / ", vtm.Warnings));
        }
    }
}

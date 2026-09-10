using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 杭要素分割を行っていないときに注意が出ること。
    ///
    /// 分割しないと杭の節点は土層境界と杭区間の境にしか置かれない。厚い土層では要素が
    /// 長く残り、その区間の地盤反力が両端の節点へ集約されるため、<b>支持が実際より遠くに
    /// 置かれて水平変位が過大に出る</b>。
    ///
    /// 2026-09-10 に同梱の計算例で実測した (要素長を特性長 1/β と比べたもの)。
    ///
    /// <list type="bullet">
    /// <item>計算例9: 1/β = 3.62m に対し最長要素 9.00m (2.5 倍)。1 段細分化で変位 -10.2%</item>
    /// <item>設計例集3.1: 1/β = 2.64m に対し最長要素 5.00m (1.9 倍)。同 -27.4%</item>
    /// </list>
    ///
    /// 2 段目以降は 0.05〜0.86% に収まるので、離散化誤差であって要素長への誤った依存では
    /// ない (<see cref="AnalysisOutputInvariantTests"/> が収束を見張っている)。
    /// 要素分割の既定 (MaxPileSpacing = 1.0m) は特性長の 0.3 倍程度なので、分割すれば
    /// 収束した値になる。だから注意の条件は「分割したか」だけでよい。
    ///
    /// <b>解析は止めない。</b>分割するかは利用者の判断で、止めると既存の使い方を壊す。
    /// </summary>
    [TestClass]
    public class NotElementSplitWarningTests
    {
        private static object? InvokeCollect(PileDesign.Models.InputData.InputModel model, bool isElementSplit)
        {
            var t = typeof(PileDesign.Services.FileOperationService).Assembly
                .GetType("PileDesign.Services.CheckInputData");
            Assert.IsNotNull(t, "CheckInputData が見つかりません (名前が変わった?)");

            var m = t!.GetMethod("CollectInputWarnings",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(m, "CollectInputWarnings が見つかりません");

            return m!.Invoke(null, [model, isElementSplit]);
        }

        private static System.Collections.Generic.List<string> Warnings(
            PileDesign.Models.InputData.InputModel model, bool isElementSplit)
            => (System.Collections.Generic.List<string>)InvokeCollect(model, isElementSplit)!;

        /// <summary>分割していなければ注意が出ること。</summary>
        [TestMethod]
        public void WhenNotSplit_AWarningIsRaised()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }

            var warnings = Warnings(model, isElementSplit: false);

            var hit = warnings.Where(w => w.Contains("杭要素分割")).ToList();
            Assert.AreEqual(1, hit.Count,
                "杭要素分割をしていないのに注意が出ません。"
                + "水平変位が 10〜30% 大きく出ることに気づけません。出た注意: "
                + string.Join(" / ", warnings));

            // 何が起きるかと、どうすればよいかが書かれていること。
            // 「分割してください」だけでは、なぜ必要か分からず無視される
            StringAssert.Contains(hit[0], "水平変位",
                "何が起きるかが書かれていません");
            StringAssert.Contains(hit[0], "土層境界",
                "なぜ起きるか (節点が土層境界にしか置かれない) が書かれていません");

            // 実測した差の数字は文に出さない。2 例題の値なので一般の保証として
            // 読まれると誤解を招き、「その程度なら許容」とも読めてしまう
            foreach (var n in new[] { "10", "20", "30", "%" })
                Assert.IsFalse(hit[0].Contains(n),
                    $"注意の文に程度の数字 ({n}) が入っています。"
                    + "2 例題での実測値なので、一般の保証としては書かないこと: " + hit[0]);
        }

        /// <summary>分割していれば余計な注意を出さないこと。</summary>
        [TestMethod]
        public void WhenSplit_NoSuchWarning()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }

            var warnings = Warnings(model, isElementSplit: true);

            Assert.AreEqual(0, warnings.Count(w => w.Contains("杭要素分割")),
                "分割済みなのに注意が出ています。毎回出る注意は読まれなくなります: "
                + string.Join(" / ", warnings));
        }

        /// <summary>
        /// 杭が 1 本も無いときは出さないこと。
        /// 解析できない状態で細かい注意を並べても、原因にたどり着けない。
        /// </summary>
        [TestMethod]
        public void WithNoPiles_NoSuchWarning()
        {
            var model = new PileDesign.Models.InputData.InputModel();

            var warnings = Warnings(model, isElementSplit: false);

            Assert.AreEqual(0, warnings.Count(w => w.Contains("杭要素分割")),
                "杭が無いのに分割の注意が出ています");
        }

        /// <summary>
        /// 呼び出し側が分割の有無を渡していること。
        ///
        /// 既定引数が true なので、渡し忘れると<b>いつまでも注意が出ない</b>。
        /// 引数を足したのに配線を忘れる形は静かに通るので、ここで見張る。
        /// </summary>
        [TestMethod]
        public void TheCaller_PassesTheSplitState()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs");

            int at = src.IndexOf("CollectInputWarnings(", StringComparison.Ordinal);
            Assert.IsTrue(at > 0, "CollectInputWarnings の呼び出しが見つかりません");

            string call = src[at..Math.Min(src.Length, at + 200)];
            StringAssert.Contains(call, "isElementSplit:",
                "呼び出し側が分割の有無を渡していません。既定が true なので注意が出ません");
        }
    }
}

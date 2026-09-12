using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 杭配置に入力した<b>群杭係数 ξ・杭間隔比 R/B が水平解析に効くこと</b>。
    ///
    /// <para>2026-09-12 まで、地盤ばねを作る <c>SoilPile.SetHorizontalSoilReaction</c> は
    /// ξ = 1・R/B = 10,000 の決め打ちで、入力された群杭係数・杭間隔比は
    /// <b>画面と計算書に出るだけでどこにも効いていなかった</b>。R/B = 10,000 は
    /// 後方杭の κ を 3 で頭打ちにし µ・λ も R/B ≧ 3 の枝に入れるので、
    /// 実質「群杭の影響なし」だった。</para>
    ///
    /// <para>ξ・R/B は<b>杭ごとの入力</b>で、土層-杭セット (SoilPile) は
    /// (地盤, 杭体, 杭頭高さ) が同じ杭で共有される。そのため反力項目に焼き込まず、
    /// 前後方杭の判定 (<c>isFront</c>) と同じく <see cref="GroupPileEffect"/> として
    /// 評価時に渡す。この形を崩すと、どれか 1 本の値が他の杭にも効く。</para>
    /// </summary>
    [TestClass]
    public class GroupPileEffectTests
    {
        private const double Y = 0.02; // m。降伏の手前・奥のどちらでも成り立つ量で見る

        /// <summary>砂質土 φ = 30°、σz' = 100 kN/m² の要素。py = κ·Kp·σz'。</summary>
        private static HorizontalSoilReactionItem Sand()
        {
            var item = new HorizontalSoilReactionItem();
            item.SetParameters(
                name: "砂", soilType: "砂質土", gamma: 18, b: 1.0, e0: 2800,
                zTop: -5.0, zBtm: -6.0, xi: 1.0, rOnB: 0.0, nValue: 10, phi: 30, cu: 0,
                sigmaZPrimeTop: 100, sigmaZPrimeBtm: 100);
            return item;
        }

        private static GroupPileEffect Effect(double xi, double rOnB)
            => new() { Xi = xi, ROnB = rOnB, BetaL = 1.0 };   // βL は液状化の低減 (ここでは 1 = 低減なし)

        // ── 群杭係数 ξ ────────────────────────────────────────────

        /// <summary>ξ は kh0 に掛かる (基礎指針'19 (6.6.12))。</summary>
        [TestMethod]
        public void XiMultipliesTheReferenceCoefficient()
        {
            var item = Sand();
            double baseline = item.Kh0;

            Assert.AreEqual(baseline, item.GetKh0For(Effect(1.0, 0)), baseline * 1e-12,
                "ξ = 1 で kh0 が変わっています");
            Assert.AreEqual(0.8 * baseline, item.GetKh0For(Effect(0.8, 0)), baseline * 1e-12,
                "ξ が kh0 に掛かっていません。群杭の影響が解析に入りません");
        }

        /// <summary>
        /// ξ は<b>手入力で上書きした kh0 にも</b>掛かる。
        /// ξ は地盤の硬さではなく杭の並びで決まる係数なので、kh0 を手で与えても別に効く。
        /// </summary>
        [TestMethod]
        public void XiAlsoMultipliesAManuallyOverriddenCoefficient()
        {
            var item = Sand();
            item.Kh0 = 12_345.0; // 土層ごとの手入力オーバーライドと同じ状態
            item.IsKh0Manual = true;

            Assert.AreEqual(0.8 * 12_345.0, item.GetKh0For(Effect(0.8, 0)), 1e-6,
                "手入力した kh0 に ξ が掛かっていません");
        }

        /// <summary>ξ は割線・接線剛性の両方に同じだけ効く (剛性は kh0 に比例)。</summary>
        [TestMethod]
        public void XiScalesBothSecantAndTangentStiffness()
        {
            var item = Sand();
            var none = Effect(1.0, 0);
            var reduced = Effect(0.5, 0);

            double secFull = item.GetSoilSecantReactionCoefficient(Y, isTop: true, isFront: true, none);
            double secHalf = item.GetSoilSecantReactionCoefficient(Y, isTop: true, isFront: true, reduced);
            double tanFull = item.GetSoilTangentReactionCoefficient(Y, isTop: true, isFront: true, none);
            double tanHalf = item.GetSoilTangentReactionCoefficient(Y, isTop: true, isFront: true, reduced);

            Assert.AreEqual(0.5, secHalf / secFull, 1e-9, "ξ が割線剛性に比例して効いていません");
            Assert.AreEqual(0.5, tanHalf / tanFull, 1e-9, "ξ が接線剛性に比例して効いていません");
        }

        // ── 杭間隔比 R/B ──────────────────────────────────────────

        /// <summary>
        /// R/B は後方杭の py に入る。前方杭は κ = 3 固定なので R/B に依らない。
        /// </summary>
        [TestMethod]
        public void SpacingRatioReachesTheRearPileOnly()
        {
            var item = Sand();
            double kp = (1 + Math.Sin(30 * Math.PI / 180)) / (1 - Math.Sin(30 * Math.PI / 180));

            double frontWide = item.GetPyFor(isTop: true, isFront: true, Effect(1.0, 10.0));
            double frontTight = item.GetPyFor(isTop: true, isFront: true, Effect(1.0, 2.5));
            Assert.AreEqual(frontWide, frontTight, frontWide * 1e-12,
                "前方杭の py が R/B で変わっています (κ = 3 固定のはず)");
            Assert.AreEqual(3.0 * kp * 100.0, frontWide, frontWide * 1e-12);

            // 後方杭: κ = min((0.55 − 0.007φ)(R/B − 1) + 0.4, 3)
            double rearTight = item.GetPyFor(isTop: true, isFront: false, Effect(1.0, 2.5));
            double expectedKappa = (0.55 - 0.007 * 30) * (2.5 - 1.0) + 0.4;
            Assert.AreEqual(expectedKappa * kp * 100.0, rearTight, rearTight * 1e-9,
                "後方杭の py に R/B が入っていません。密な配置で群杭の影響が消えます");
            Assert.IsTrue(rearTight < frontWide * 0.5,
                $"R/B = 2.5 なら後方杭の py は前方杭の半分未満のはず (前 {frontWide:F0} / 後 {rearTight:F0})");
        }

        /// <summary>
        /// R/B 未入力 (0 以下) は「群杭の影響なし」= 直す前の決め打ち (R/B = 10,000) と同じ。
        /// 既定値 0 をそのまま式に入れると後方杭の κ が負になり py の符号が壊れる。
        /// </summary>
        [DataTestMethod]
        [DataRow(0.0)]
        [DataRow(-1.0)]
        public void AnUnsetSpacingRatioMeansNoGroupEffect(double unset)
        {
            var item = Sand();
            double asBefore = item.GetPyFor(isTop: true, isFront: false, Effect(1.0, 10_000.0));
            double front = item.GetPyFor(isTop: true, isFront: true, Effect(1.0, unset));
            double rear = item.GetPyFor(isTop: true, isFront: false, Effect(1.0, unset));

            Assert.AreEqual(asBefore, rear, asBefore * 1e-12,
                "R/B 未入力が、直す前 (R/B = 10,000) と同じ扱いになっていません");
            Assert.AreEqual(front, rear, front * 1e-12,
                "R/B 未入力なのに後方杭が前方杭と違う py になっています");
            Assert.IsTrue(rear > 0, $"R/B 未入力で py が {rear} になりました (κ が負になっている)");
        }

        /// <summary>粘性土でも R/B 未入力は前方杭と同じ µ・λ になる (z/B ≦ 2.5 とそれより深い両方)。</summary>
        [DataTestMethod]
        [DataRow(1.0)]
        [DataRow(5.0)]
        public void AnUnsetSpacingRatioMeansNoGroupEffectInClay(double zOverB)
        {
            var item = new HorizontalSoilReactionItem();
            item.SetParameters(
                name: "粘土", soilType: "粘性土", gamma: 17, b: 1.0, e0: 2800,
                zTop: -zOverB, zBtm: -zOverB - 0.001, xi: 1.0, rOnB: 0.0, nValue: 4, phi: 0, cu: 100,
                sigmaZPrimeTop: 0, sigmaZPrimeBtm: 0);

            double front = item.GetPyFor(isTop: true, isFront: true, Effect(1.0, 0.0));
            double rear = item.GetPyFor(isTop: true, isFront: false, Effect(1.0, 0.0));
            Assert.AreEqual(front, rear, front * 1e-12,
                $"粘性土 (z/B = {zOverB}) で R/B 未入力なのに後方杭の py が違います");
        }

        // ── 杭配置の入力から作ること ───────────────────────────────

        [TestMethod]
        public void TheEffectIsReadFromThePileLayoutInput()
        {
            var pile = new PileLayoutDataItem { GroupPileFactor = 0.87, PileSpacingFactor = 3.5 };
            var effect = GroupPileEffect.For(pile);

            Assert.AreEqual(0.87, effect.Xi, 0.0, "群杭係数が杭配置の入力から来ていません");
            Assert.AreEqual(3.5, effect.ROnB, 0.0, "杭間隔比が杭配置の入力から来ていません");

            var none = GroupPileEffect.For(null);
            Assert.AreEqual(1.0, none.Xi, 0.0);
            Assert.IsTrue(none.ROnB <= 0, "杭が無いときは群杭の影響なしとして扱うこと");
        }

        /// <summary>
        /// 土層-杭セットが作る反力は「群杭の影響なし」の基準値であること。
        /// ここに杭ごとの ξ・R/B を焼き込むと、同じ土層-杭セットを共有する他の杭にも効く。
        /// </summary>
        [TestMethod]
        public void TheSharedSoilPileKeepsTheSinglePileBaseline()
        {
            string source = TestSource.Read("Graphics_r1", "Models", "InputData", "SoilPile.cs");
            // 宣言に一意な断片で探す。単に "SetHorizontalSoilReaction" だと、
            // 宣言より前にある呼び出し (RebuildHorizontalSoilReactions の式本体) に当たる
            string body = StripComments(TestSource.MethodBody(source, "void SetHorizontalSoilReaction()"));

            Assert.IsTrue(Regex.IsMatch(body, @"double\s+xi\s*=\s*1\s*;"),
                "SoilPile が作る反力の ξ が 1 (群杭の影響なし) ではありません");
            Assert.IsTrue(Regex.IsMatch(body, @"double\s+rOnB\s*=\s*0\s*;"),
                "SoilPile が作る反力の R/B が 0 (未設定 = 群杭の影響なし) ではありません。"
                + "杭ごとの値を焼き込むと、同じ土層-杭セットを使う他の杭にも効きます");
        }

        /// <summary>
        /// 杭要素分割ウィンドウが、表示専用の ξ・R/B を解析へ書き戻さないこと。
        ///
        /// <para>このウィンドウは自分の Xi (既定 1) と ROnB (既定 10) で表示用の反力を組み、
        /// OK でモデルへ写していた。そのため<b>ウィンドウを開いて OK したかどうかで
        /// 解析結果が変わっていた</b> (R/B が 10,000 から 10 になる)。</para>
        /// </summary>
        [TestMethod]
        public void TheElementDivisionWindowDoesNotWriteItsDisplaySpringsBack()
        {
            string source = TestSource.Read("Graphics_r1", "ViewModels", "ElementDivisionViewModel.cs");
            // 呼び出し (引数を渡している箇所) ではなく宣言に当てる
            string body = StripComments(TestSource.MethodBody(
                source, "void SetZdataItemsAndHorizontalSoilReactionItems(int soilPileNo)"));

            Assert.IsFalse(body.Contains("HorizontalSoilReactions =", StringComparison.Ordinal),
                "杭要素分割ウィンドウが表示用の水平地盤反力を解析側へ書き戻しています。"
                + "ZDataItems の代入でモデル側が組み直すので、書き戻しは不要かつ有害です");
        }

        /// <summary>
        /// 群杭の影響は必ず名前付きの作り方で渡すこと。<c>default</c> / 引数なしの
        /// <c>new GroupPileEffect()</c> は ξ = 0 になり、<b>水平地盤ばねが静かに全部消える</b>。
        /// </summary>
        [TestMethod]
        public void NoCallSitePassesTheDefaultStruct()
        {
            int scanned = 0;
            foreach (var file in System.IO.Directory.EnumerateFiles(
                TestSource.Dir("Graphics_r1"), "*.cs", System.IO.SearchOption.AllDirectories))
            {
                if (file.Contains("\\obj\\", StringComparison.Ordinal)
                    || file.Contains("\\bin\\", StringComparison.Ordinal)) continue;

                string text = StripComments(System.IO.File.ReadAllText(file));
                if (!text.Contains("GroupPileEffect", StringComparison.Ordinal)) continue;
                scanned++;

                Assert.IsFalse(Regex.IsMatch(text, @"new\s+GroupPileEffect\s*\(\s*\)\s*[;,)]"),
                    $"{System.IO.Path.GetFileName(file)}: 引数なしの new GroupPileEffect() は ξ = 0 です。"
                    + "GroupPileEffect.None か .For(pile) を使ってください");
                Assert.IsFalse(Regex.IsMatch(text, @"GroupPileEffect\s+\w+\s*=\s*default"),
                    $"{System.IO.Path.GetFileName(file)}: default の GroupPileEffect は ξ = 0 です");
            }

            TestSource.AssertScanned(scanned, 5, "GroupPileEffect を使うファイル");
        }

        /// <summary>
        /// コメントを落とす。走査テストが<b>コメントに書いた例</b>を実装と誤認しないため
        /// (このリポジトリで実際に 3 回起きた)。
        /// </summary>
        private static string StripComments(string source)
        {
            string withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return Regex.Replace(withoutBlocks, @"//[^\r\n]*", "");
        }
    }
}

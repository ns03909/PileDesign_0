using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 回帰網の例題ビルダー (<see cref="IntegrationTests.BuildExampleInputModel"/>) が、
    /// 例題ファイルの<b>結果に効く入力を落としていないこと</b>。
    ///
    /// <para>ビルダーは実機の <see cref="PileExampleLoader.ApplyToInputModel"/> を手で写したもので、
    /// 写し忘れた項目は<b>既定値のまま解析が通る</b>。落ちないので気づけない。
    /// これまでに 2 度踏んでいる。</para>
    ///
    /// <list type="bullet">
    /// <item>軸力 — 全杭 0 のまま解析していた。M-φ が N = 0 でしか作られず、
    ///   軸力依存の誤りを一切検出できなかった</item>
    /// <item>群杭係数 ξ・杭間隔比 R/B — 2026-09-12 まで写しておらず、ξ = 1・R/B 未設定で
    ///   走っていた。設計例集3.1 の ξ = 0.981 は一度も検査されていなかった</item>
    /// </list>
    ///
    /// <para>同じ形の穴 (地盤変位が 0 のまま走っていた) を 2026-09-12 に直したのと同じ理由で、
    /// ここは「値が一致すること」を直接見る。写しがある限り、また取り残される。</para>
    /// </summary>
    [TestClass]
    public class ExampleBuilderFidelityTests
    {
        /// <summary>杭例題ごとに、ファイルの値とビルダーが作ったモデルの値を突き合わせる。</summary>
        [DataTestMethod]
        [DataRow("Example3_1", "PileExample3_1")]  // ξ = 0.981 (同梱例題で唯一 1 でないもの)
        [DataRow("Example10", "PileExample10")]    // R/B = 4.3 (同梱例題で最も密)
        [DataRow("Example9", "PileExample9")]
        [DataRow("ExampleK8", "PileExampleK8")]
        public void TheBuilderCarriesTheGroupPileInputs(string groundName, string pileName)
        {
            var dto = PileExampleLoader.LoadFromFile(pileName);
            if (dto?.PileLayoutItems == null || dto.PileLayoutItems.Count == 0)
            {
                Assert.Inconclusive($"{pileName} の杭配置が読めません");
                return;
            }

            var (model, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            if (model == null) { Assert.Inconclusive($"例題ロード失敗: {error}"); return; }

            Assert.AreEqual(dto.PileLayoutItems.Count, model.PileLayoutItems.Count,
                $"{pileName}: 杭の本数が例題ファイルと違います");

            for (int i = 0; i < dto.PileLayoutItems.Count; i++)
            {
                var expected = dto.PileLayoutItems[i];
                var actual = model.PileLayoutItems[i];

                Assert.AreEqual(expected.GroupPileFactor, actual.GroupPileFactor, 1e-12,
                    $"{pileName} 杭 {i + 1}: 群杭係数 ξ が例題ファイルの値になっていません。"
                    + "ξ は基準水平地盤反力係数 kh0 に掛かるので、既定値 1 のままだと"
                    + "群杭の影響が解析に入らず、回帰網がそれを検査できません");

                Assert.AreEqual(expected.PileSpacingFactor, actual.PileSpacingFactor, 1e-12,
                    $"{pileName} 杭 {i + 1}: 杭間隔比 R/B が例題ファイルの値になっていません。"
                    + "R/B は後方杭の塑性水平地盤反力 py に入ります");

                // 過去に踏んだ取り残し (軸力) も同じ場所で見張る
                Assert.AreEqual(expected.AxialForceVL0, actual.AxialForceVL0, 1e-9,
                    $"{pileName} 杭 {i + 1}: 常時軸力が例題ファイルの値になっていません");
                Assert.AreEqual(expected.PileBodyNo > 0 ? expected.PileBodyNo : 1, actual.PileBodyNo,
                    $"{pileName} 杭 {i + 1}: 杭体番号が違います");
                Assert.AreEqual(expected.GroundNo > 0 ? expected.GroundNo : 1, actual.GroundNo,
                    $"{pileName} 杭 {i + 1}: 地盤番号が違います");
            }
        }

        /// <summary>
        /// 同梱例題のうち、群杭の影響が実際に解析へ効く条件を満たすものがあること。
        ///
        /// <para>網が値を運んでいても、例題の値が全部「低減なし」(ξ = 1 かつ 前方杭のみ) だと
        /// 何も検査していないのと同じになる。ξ ≠ 1 の例題が少なくとも 1 つあることを確かめる。</para>
        /// </summary>
        [TestMethod]
        public void AtLeastOneExampleActuallyExercisesTheGroupPileFactor()
        {
            string[] pileExamples =
            [
                "PileExample3_1", "PileExample3_2", "PileExample3_3", "PileExample3_4",
                "PileExample3_5", "PileExample3_8", "PileExample5", "PileExample7",
                "PileExample9", "PileExample10", "PileExampleK8",
            ];

            int scanned = 0;
            int withReduction = 0;
            foreach (var name in pileExamples)
            {
                var dto = PileExampleLoader.LoadFromFile(name);
                if (dto?.PileLayoutItems == null || dto.PileLayoutItems.Count == 0) continue;
                scanned++;
                if (dto.PileLayoutItems.Any(p => p.GroupPileFactor < 1.0 - 1e-9)) withReduction++;
            }

            TestSource.AssertScanned(scanned, 8, "杭例題");
            Assert.IsTrue(withReduction >= 1,
                "群杭係数 ξ が 1 未満の例題が 1 つもありません。"
                + "ξ を解析へ通す経路が、例題では一度も使われないことになります");
        }
    }
}

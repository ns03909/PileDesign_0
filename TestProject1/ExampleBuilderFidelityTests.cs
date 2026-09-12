using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 回帰網の例題ビルダー (<see cref="IntegrationTests.BuildExampleInputModel"/>) が、
    /// 例題ファイルの<b>結果に効く入力を落としていないこと</b>。
    ///
    /// <para>ビルダーは実機の <see cref="PileExampleLoader.ApplyToInputModel"/> を手で写していて、
    /// 写し忘れた項目は<b>既定値のまま解析が通る</b>。落ちないので気づけない。
    /// 3 度踏んだので 2026-09-12 に写しを消し、実機の読込を呼ぶ形にした。</para>
    ///
    /// <list type="bullet">
    /// <item>軸力 — 全杭 0 のまま解析していた。M-φ が N = 0 でしか作られず、
    ///   軸力依存の誤りを一切検出できなかった</item>
    /// <item>群杭係数 ξ・杭間隔比 R/B — 2026-09-12 まで写しておらず、ξ = 1・R/B 未設定で
    ///   走っていた。設計例集3.1 の ξ = 0.981 は一度も検査されていなかった</item>
    /// <item>ΔZc・根入れ・基礎梁・杭頭工法 — 写しに無く、既定値のまま走っていた</item>
    /// </list>
    ///
    /// <para>いまの役目は「写しが正しいか」ではなく<b>実機の読込を通っているか</b>。
    /// 値の突合と、ビルダーが入力を自前で組み立てていないことの走査で見張る。</para>
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

                // ΔZc (杭頭と接合節点の高さの差)。写しには無く、既定 1 m のまま走っていた。
                // 杭 Z の意味は接合節点 Z なので、これが違うと杭下端がその差ぶんずれる。
                if (expected.DeltaZc.HasValue)
                    Assert.AreEqual(expected.DeltaZc.Value, actual.FoundationBeamDeltaZc, 1e-9,
                        $"{pileName} 杭 {i + 1}: ΔZc が例題ファイルの値になっていません");
            }
        }

        /// <summary>
        /// 根入れ・基礎梁も例題どおりに入ること (どちらも解析結果を動かす)。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example3_1", "PileExample3_1")]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example10", "PileExample10")]
        [DataRow("ExampleK8", "PileExampleK8")]
        public void TheBuilderCarriesEmbedmentAndFoundationBeams(string groundName, string pileName)
        {
            var dto = PileExampleLoader.LoadFromFile(pileName);
            if (dto == null) { Assert.Inconclusive($"{pileName} が読めません"); return; }

            var (model, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            if (model == null) { Assert.Inconclusive($"例題ロード失敗: {error}"); return; }

            if (dto.Embedment != null)
            {
                Assert.IsNotNull(model.ElementDivision?.SoilEmbedment,
                    $"{pileName}: 根入れが入っていません。土圧合力ばねが付かないまま解析します");
                Assert.AreEqual(dto.Embedment.EmbedmentTopAltitude,
                    model.ElementDivision.SoilEmbedment.EmbedmentTopAltitude, 1e-9,
                    $"{pileName}: 根入れ上端の標高が例題ファイルの値になっていません");
                Assert.AreEqual(dto.Embedment.EmbedmentBottomAltitude,
                    model.ElementDivision.SoilEmbedment.EmbedmentBottomAltitude, 1e-9,
                    $"{pileName}: 根入れ下端の標高が例題ファイルの値になっていません");
            }

            int expectedBeams = dto.FoundationBeamInput?.Beams?.Count ?? 0;
            int actualBeams = model.FoundationBeamInput?.Beams?.Count ?? 0;
            Assert.AreEqual(expectedBeams, actualBeams,
                $"{pileName}: 基礎梁の本数が例題ファイルと違います。"
                + "基礎梁は杭頭の拘束を変えるので、有無だけで応答が大きく動きます");
        }

        /// <summary>
        /// ビルダーが杭体・杭配置を<b>自前で組み立てていない</b>こと。
        ///
        /// <para>写しを消しても、次に何か足したいときに手で組む形へ戻りやすい。
        /// 実機の読込を呼ぶ形を走査で固定する。荷重ケースだけは回帰の契約として
        /// 意図的に上書きしているので、ここでは見ない。</para>
        /// </summary>
        [TestMethod]
        public void TheBuilderGoesThroughTheRealLoader()
        {
            string source = TestSource.Read("TestProject1", "IntegrationTests.cs");
            string body = TestSource.MethodBody(source,
                "(InputModel? model, string? error) BuildExampleInputModel(");
            // コメント行は落とす (説明文の中の型名を実装と読み違えないため)
            string code = string.Join("\n",
                body.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));

            Assert.IsTrue(code.Contains("PileExampleLoader.ApplyToInputModel(", System.StringComparison.Ordinal),
                "例題ビルダーが実機の読込 (PileExampleLoader.ApplyToInputModel) を通っていません");
            Assert.IsFalse(code.Contains("new PileLayoutDataItem", System.StringComparison.Ordinal),
                "例題ビルダーが杭配置を自前で組んでいます。実機の読込に寄せてください "
                + "(手で組むと ΔZc や軸力のような項目が取り残されます)");
            Assert.IsFalse(code.Contains("new PileBodyInput", System.StringComparison.Ordinal),
                "例題ビルダーが杭体を自前で組んでいます。実機の読込に寄せてください");
            Assert.IsFalse(code.Contains("new PileBodySegment", System.StringComparison.Ordinal),
                "例題ビルダーが杭体の段を自前で組んでいます。実機の読込に寄せてください");
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

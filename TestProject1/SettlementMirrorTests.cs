using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// 群杭沈下の結果が「ケースのレコード」と「平たい複製」の 2 か所にある問題。
    ///
    /// 正は <c>CaseRecords[ActiveCaseIndex]</c> で、
    /// <c>PileGroupSettlement.SettlementGridData</c> はそこから同期される複製
    /// (<c>ApplyActiveCaseToLegacyFields</c> という名前がそれを表している)。
    /// 同じ値が 2 か所にあると、同期を忘れた経路で画面と結果がずれる。
    ///
    /// 表示系はレコード側 (<c>ActiveSettlementGridData</c>) を読むように寄せた。
    /// <b>複製そのものはまだ消せない</b> — 理由は
    /// <see cref="RemovingTheMirrorWouldBreakExistingFiles"/> に実証してある。
    /// </summary>
    [TestClass]
    public class SettlementMirrorTests
    {
        private static ObservableCollection<SettlementGridDataItem> Grid(params double[] settlements) =>
            [.. settlements.Select((v, i) => new SettlementGridDataItem { No = i + 1, X = i, Y = 0, Settlement = v })];

        private static PileGroupSettlement WithCases(params ObservableCollection<SettlementGridDataItem>[] grids)
        {
            var pgs = new PileGroupSettlement
            {
                CaseRecords = [.. grids.Select((g, i) => new GroupSettlementCaseRecord
                {
                    LoadCaseName = $"case{i}",
                    SettlementGridData = g,
                })],
            };
            return pgs;
        }

        // ── 表示は「いま選んでいるケース」を返す ────────────

        [TestMethod]
        public void ActiveGrid_FollowsTheSelectedCase()
        {
            var pgs = WithCases(Grid(1.0), Grid(2.0));

            pgs.ActiveCaseIndex = 0;
            Assert.AreEqual(1.0, pgs.ActiveSettlementGridData[0].Settlement, 1e-12);

            pgs.ActiveCaseIndex = 1;
            Assert.AreEqual(2.0, pgs.ActiveSettlementGridData[0].Settlement, 1e-12);
        }

        /// <summary>
        /// 複製が古いままでも、表示は選んでいるケースを返すこと。
        /// これが「同期を忘れると画面がずれる」を断つ点。
        /// </summary>
        [TestMethod]
        public void ActiveGrid_IgnoresAStaleMirror()
        {
            var pgs = WithCases(Grid(1.0), Grid(2.0));
            pgs.SettlementGridData = Grid(999.0);   // 同期を忘れた複製
            pgs.ActiveCaseIndex = 1;

            Assert.AreEqual(2.0, pgs.ActiveSettlementGridData[0].Settlement, 1e-12,
                "複製の側を読んでいる");
        }

        /// <summary>ケースが選ばれていない・無いときは空を返し、落ちないこと。</summary>
        [TestMethod]
        public void ActiveGrid_IsEmptyWhenNothingIsSelected()
        {
            var pgs = WithCases(Grid(1.0));

            pgs.ActiveCaseIndex = -1;
            Assert.AreEqual(0, pgs.ActiveSettlementGridData.Count);
            Assert.IsNull(pgs.ActiveRecord);

            pgs.ActiveCaseIndex = 5;   // 範囲外
            Assert.AreEqual(0, pgs.ActiveSettlementGridData.Count);

            Assert.AreEqual(0, new PileGroupSettlement().ActiveSettlementGridData.Count);
        }

        // ── なぜ複製を消せないか ───────────────────────────

        /// <summary>
        /// <b>複製を外すと既存の保存ファイルが開けなくなる。</b>
        ///
        /// レコード側のグリッドは<b>同じ要素インスタンス</b>を指している
        /// (<c>new ObservableCollection&lt;&gt;(gridData)</c> はリストだけを複製する)。
        /// <c>ReferenceHandler.Preserve</c> では先に現れた複製の側に要素の <c>$id</c> が付き、
        /// レコード側は <c>$ref</c> になる。複製を <c>[JsonIgnore]</c> にすると
        /// <c>$id</c> が登録されず、<c>$ref</c> の解決に失敗する。
        ///
        /// 複製を消すには、先に<b>要素の共有をやめる</b>必要がある。
        /// </summary>
        [TestMethod]
        public void RemovingTheMirrorWouldBreakExistingFiles()
        {
            var shared = Grid(1.0, 2.0);
            var pgs = new PileGroupSettlement
            {
                SettlementGridData = shared,
                CaseRecords =
                [
                    new GroupSettlementCaseRecord
                    {
                        LoadCaseName = "VL",
                        // 実装と同じ: リストは新規、要素は同一インスタンス
                        SettlementGridData = new ObservableCollection<SettlementGridDataItem>(shared),
                    }
                ],
                ActiveCaseIndex = 0,
            };

            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true,
            };
            string json = JsonSerializer.Serialize(pgs, options);

            StringAssert.Contains(json, "$ref",
                "要素が共有されていない。共有が解けたなら複製を外せる (このテストごと見直すこと)");

            // 複製のプロパティを取り除いた JSON = 複製を [JsonIgnore] にした場合
            int mirror = json.IndexOf("\"SettlementGridData\"", StringComparison.Ordinal);
            int records = json.IndexOf("\"CaseRecords\"", StringComparison.Ordinal);
            Assert.IsTrue(mirror >= 0 && records > mirror, "前提が崩れている (プロパティの順序)");
            string withoutMirror = json.Remove(mirror, records - mirror);

            var ex = Assert.ThrowsException<JsonException>(
                () => JsonSerializer.Deserialize<PileGroupSettlement>(withoutMirror, options),
                "複製を外しても読めてしまう。共有が解けたなら複製を外せる");

            StringAssert.Contains(ex.Message, "Reference",
                $"想定と違う失敗: {ex.Message}");
        }
    }
}

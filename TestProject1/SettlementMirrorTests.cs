using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
    /// <see cref="SharedElements_MakeTheMirrorImpossibleToRemove"/> に実証してある。
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

        // ── 各杭の沈下量も同じ ─────────────────────────────

        /// <summary>
        /// 杭ごとの沈下量も、表示中のケースから引くこと。
        /// <c>PileLayoutDataItem.GroupPileSettlement</c> は同じ値の複製。
        /// </summary>
        [TestMethod]
        public void PileSettlement_ComesFromTheSelectedCase()
        {
            var pgs = new PileGroupSettlement
            {
                CaseRecords =
                [
                    new GroupSettlementCaseRecord
                    {
                        LoadCaseName = "VL",
                        PileSettlements_mm = new Dictionary<int, double> { [1] = 12.5, [2] = 7.5 },
                    },
                    new GroupSettlementCaseRecord
                    {
                        LoadCaseName = "U1",
                        PileSettlements_mm = new Dictionary<int, double> { [1] = 20.0, [2] = 10.0 },
                    },
                ],
                ActiveCaseIndex = 0,
            };

            Assert.AreEqual(12.5, pgs.SettlementOf(1), 1e-12);
            Assert.AreEqual(7.5, pgs.SettlementOf(2), 1e-12);

            pgs.ActiveCaseIndex = 1;
            Assert.AreEqual(20.0, pgs.SettlementOf(1), 1e-12);

            Assert.AreEqual(0.0, pgs.SettlementOf(99), 1e-12, "知らない杭は 0");
            pgs.ActiveCaseIndex = -1;
            Assert.AreEqual(0.0, pgs.SettlementOf(1), 1e-12, "未選択は 0");
        }

        /// <summary>
        /// 表示系が杭の複製 <c>GroupPileSettlement</c> を読んでいないこと。
        ///
        /// 読み手が残っていると、ケースを切り替えたのに古い沈下量が出る。
        /// 書き込み (解析・同期・クリア) と <c>DeepCopy</c> は複製がある限り必要なので対象外。
        /// </summary>
        [TestMethod]
        public void NothingReadsThePileSettlementMirror()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(SettlementMirrorTests).Assembly.Location)!);
            string? root = null;
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                {
                    root = dir.FullName;
                    break;
                }
            }
            Assert.IsNotNull(root, "ソリューションルートが見つかりません");

            var readers = new List<string>();
            foreach (string cs in Directory.EnumerateFiles(
                         Path.Combine(root!, "Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (Path.GetFileName(cs) == "PileLayoutDataItem.cs") continue;   // 定義と DeepCopy
                if (Path.GetFileName(cs) == "LegacySettlementMigration.cs") continue; // 旧ファイルの移行

                var lines = File.ReadAllLines(cs);
                for (int i = 0; i < lines.Length; i++)
                {
                    int comment = lines[i].IndexOf("//", StringComparison.Ordinal);
                    string code = comment >= 0 ? lines[i][..comment] : lines[i];
                    // 「GroupPileSettlementXOffset」等の別プロパティと区別するため語境界で見る
                    if (!Regex.IsMatch(code, @"\.GroupPileSettlement\b")) continue;

                    // 書き込みは可 (複製がある限り更新は要る)
                    if (Regex.IsMatch(code, @"\.GroupPileSettlement\s*=[^=]"))
                        continue;

                    readers.Add($"{Path.GetFileName(cs)}:{i + 1}  {code.Trim()}");
                }
            }

            Assert.AreEqual(0, readers.Count,
                "杭の複製を読んでいます。PileGroupSettlement.SettlementOf(pileNo) を使ってください:\n  "
                + string.Join("\n  ", readers));
        }

        /// <summary>
        /// 表示系がコンタの複製 <c>PileGroupSettlement.SettlementGridData</c> を読んでいないこと。
        ///
        /// 読み手が残っていると、ケースを切り替えたのにそこだけ古い図が出る。
        /// 実際、計算書のコンタ図だけが複製を読んでおり、画面と食い違う余地があった。
        ///
        /// 書き込み (解析・同期・クリア) と、読込時のコレクション変換・旧ファイルの移行は
        /// 複製がある限り必要なので対象外。
        /// </summary>
        [TestMethod]
        public void NothingReadsTheGridMirror()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(SettlementMirrorTests).Assembly.Location)!);
            string? root = null;
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                {
                    root = dir.FullName;
                    break;
                }
            }
            Assert.IsNotNull(root, "ソリューションルートが見つかりません");

            var readers = new List<string>();
            foreach (string cs in Directory.EnumerateFiles(
                         Path.Combine(root!, "Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (Path.GetFileName(cs) == "PileGroupSettlement.cs") continue;        // 定義と ActiveSettlementGridData
                if (Path.GetFileName(cs) == "FileOperationService.cs") continue;       // 読込時のコレクション変換
                if (Path.GetFileName(cs) == "LegacySettlementMigration.cs") continue;  // 旧ファイルの移行

                var lines = File.ReadAllLines(cs);
                for (int i = 0; i < lines.Length; i++)
                {
                    int comment = lines[i].IndexOf("//", StringComparison.Ordinal);
                    string code = comment >= 0 ? lines[i][..comment] : lines[i];

                    // record.SettlementGridData (記録側) が正しい読み手なので、複製側だけを見る
                    if (!Regex.IsMatch(code, MIRROR_READ)) continue;

                    // 書き込みは可
                    if (Regex.IsMatch(code, WRITE)) continue;

                    // nameof はプロパティ名の参照で、中身は読んでいない
                    if (code.Contains("nameof(", StringComparison.Ordinal)) continue;

                    readers.Add($"{Path.GetFileName(cs)}:{i + 1}  {code.Trim()}");
                }
            }

            Assert.AreEqual(0, readers.Count,
                "コンタの複製を読んでいます。PileGroupSettlement.ActiveSettlementGridData を使ってください:\n  "
                + string.Join("\n  ", readers));
        }

        private const string MIRROR_READ = @"(pgs|settlement|PileGroupSettlement)\??\.SettlementGridData\b";
        private const string WRITE = @"\.SettlementGridData\s*=[^=]";

        // ── 矩形荷重は入力。結果で上書きしない ─────────────

        /// <summary>
        /// ケースを表示に切り替えても、<b>利用者が入力した矩形荷重は書き換わらない</b>こと。
        ///
        /// 反復解析 (基礎梁考慮) は収束後の荷重をケースに持つ。以前はそれを
        /// <c>pgs.RectLoads</c> — 入力そのもの — へ書き戻していたため、
        /// 画面の入力表が収束反力に変わってしまい、元に戻すための退避フィールド
        /// (<c>NonBeamRectLoadsSnapshot</c>) を別に持つ羽目になっていた。
        /// </summary>
        [TestMethod]
        public void ShowingACaseDoesNotOverwriteTheInputLoads()
        {
            var input = new RectLoad { X1 = 0, X2 = 2, Y1 = 0, Y2 = 2, QA = 100 };
            var converged = new RectLoad { X1 = 0, X2 = 2, Y1 = 0, Y2 = 2, QA = 250 };

            var record = new GroupSettlementCaseRecord
            {
                LoadCaseName = "VL",
                LoadingType = "個別矩形（基礎梁考慮）",
                IsBeamAware = true,
                RectLoads = [converged],
                SettlementGridData = Grid(1.0),
            };
            var pgs = new PileGroupSettlement
            {
                RectLoads = [input],
                CaseRecords = [record],
                ActiveCaseIndex = 0,
            };

            PileDesign.ViewModels.GroupSettlementWithBeamCalculationViewModel
                .ApplyActiveCaseToLegacyFields(pgs, record);

            Assert.AreEqual(1, pgs.RectLoads.Count);
            Assert.AreEqual(100.0, pgs.RectLoads[0].QA, 1e-12,
                "入力の矩形荷重が収束後の値で上書きされている");
            Assert.AreSame(input, pgs.RectLoads[0], "入力のインスタンスが差し替えられている");
        }

        /// <summary>
        /// 結果として見せたい場面 (収束後の荷重) は、ケースから引けること。
        /// 未解析なら入力をそのまま返す。
        /// </summary>
        [TestMethod]
        public void ActiveLoads_AreTheCaseLoadsWhenAnalyzed()
        {
            var input = new RectLoad { X1 = 0, X2 = 2, Y1 = 0, Y2 = 2, QA = 100 };
            var pgs = new PileGroupSettlement { RectLoads = [input] };

            Assert.AreSame(pgs.RectLoads, pgs.ActiveRectLoads, "未解析なら入力を返すこと");

            pgs.CaseRecords =
            [
                new GroupSettlementCaseRecord
                {
                    LoadCaseName = "VL",
                    RectLoads = [new RectLoad { X1 = 0, X2 = 2, Y1 = 0, Y2 = 2, QA = 250 }],
                }
            ];
            pgs.ActiveCaseIndex = 0;

            Assert.AreEqual(250.0, pgs.ActiveRectLoads[0].QA, 1e-12,
                "解析済みならケースの荷重 (反復なら収束後) を返すこと");
        }

        private static string? FindSolutionRootOrNull()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(SettlementMirrorTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            return null;
        }

        // ── なぜ複製を消せないか ───────────────────────────

        /// <summary>
        /// <b>要素を共有している保存ファイルは、複製を外すと開けなくなる。</b>
        /// (2026-08-26 より前に保存されたファイルがこれに当たる)
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
        public void SharedElements_MakeTheMirrorImpossibleToRemove()
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

        // ── 新しく作るときは共有しない ─────────────────────

        /// <summary>
        /// <b>いま作られるモデルは、複製とケースで要素を共有しないこと。</b>
        ///
        /// これが成り立って初めて、複製 (<c>SettlementGridData</c>) を
        /// 「読み込めるが書き出さない」形にして撤去できる。
        /// 共有が残っていると、保存ファイルの中でケース側が複製側を <c>$ref</c> で参照し、
        /// 複製を外した瞬間にそのファイルが開けなくなる。
        /// </summary>
        [TestMethod]
        public void NewlyBuiltRecords_DoNotShareElementsWithTheMirror()
        {
            var pgs = BuildAsTheAppDoes();

            var mirrorGrid = pgs.SettlementGridData;
            var recordGrid = pgs.CaseRecords[0].SettlementGridData;

            Assert.AreEqual(mirrorGrid.Count, recordGrid.Count, "件数が違う");
            Assert.IsTrue(mirrorGrid.Count > 0, "前提が崩れている (空では検査にならない)");

            for (int i = 0; i < mirrorGrid.Count; i++)
            {
                Assert.AreNotSame(mirrorGrid[i], recordGrid[i],
                    $"グリッド {i} 番の要素を共有している");
                Assert.AreEqual(mirrorGrid[i].Settlement, recordGrid[i].Settlement, 1e-12,
                    "複製したのに値が違う");
            }

            for (int i = 0; i < pgs.RectLoads.Count; i++)
            {
                Assert.AreNotSame(pgs.RectLoads[i], pgs.CaseRecords[0].RectLoads[i],
                    $"矩形荷重 {i} 番を共有している (画面で編集すると保存済みの結果が変わる)");
            }
        }

        /// <summary>
        /// 共有をやめた結果、<b>複製を外しても読める</b>ようになったこと。
        /// 撤去 (第 2 段の最後) の前提がこれ。
        /// </summary>
        [TestMethod]
        public void WithoutSharing_TheMirrorCanBeRemoved()
        {
            var pgs = BuildAsTheAppDoes();

            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true,
            };
            string json = JsonSerializer.Serialize(pgs, options);

            int mirror = json.IndexOf("\"SettlementGridData\"", StringComparison.Ordinal);
            int records = json.IndexOf("\"CaseRecords\"", StringComparison.Ordinal);
            Assert.IsTrue(mirror >= 0 && records > mirror, "前提が崩れている (プロパティの順序)");

            string withoutMirror = json.Remove(mirror, records - mirror);
            var restored = JsonSerializer.Deserialize<PileGroupSettlement>(withoutMirror, options);

            Assert.IsNotNull(restored);
            Assert.AreEqual(1, restored!.CaseRecords.Count, "ケースが復元できていない");
            Assert.AreEqual(pgs.CaseRecords[0].SettlementGridData.Count,
                            restored.CaseRecords[0].SettlementGridData.Count,
                            "ケースの結果が復元できていない");
        }

        /// <summary>
        /// 矩形荷重を画面で編集しても、保存済みのケースの中身が変わらないこと。
        /// 共有していた頃は、入力を直すと過去の結果まで書き換わっていた。
        /// </summary>
        [TestMethod]
        public void EditingTheInputDoesNotAlterAStoredCase()
        {
            var pgs = BuildAsTheAppDoes();
            double before = pgs.CaseRecords[0].RectLoads[0].QA;

            pgs.RectLoads[0].QA = before + 100.0;

            Assert.AreEqual(before, pgs.CaseRecords[0].RectLoads[0].QA, 1e-12,
                "入力を編集したら保存済みのケースの荷重まで変わった");
        }

        /// <summary>
        /// アプリと同じ経路で「ケースの結果 → 表示用の複製」を作る。
        ///
        /// 同期は本番の <c>ApplyActiveCaseToLegacyFields</c> をそのまま呼ぶ。
        /// ここで組み立て方を書き写すと、本番が共有に戻っても検査が素通りしてしまう。
        /// </summary>
        private static PileGroupSettlement BuildAsTheAppDoes()
        {
            var record = new GroupSettlementCaseRecord
            {
                LoadCaseName = "VL",
                LoadingType = "任意矩形",
                RectLoads = [new RectLoad { X1 = 0, X2 = 2, Y1 = 0, Y2 = 2, QA = 100 }],
                SettlementGridData = Grid(1.0, 2.0, 3.0),
            };

            var pgs = new PileGroupSettlement
            {
                LoadingType = "任意矩形",
                // 利用者の入力。ケースの荷重とは別インスタンス
                RectLoads = [.. record.RectLoads.Select(r => r.Clone())],
                CaseRecords = [record],
                ActiveCaseIndex = 0,
            };

            // 本番の同期処理 (ケース → 表示用の複製)
            PileDesign.ViewModels.GroupSettlementWithBeamCalculationViewModel
                .ApplyActiveCaseToLegacyFields(pgs, record);

            return pgs;
        }

    }
}

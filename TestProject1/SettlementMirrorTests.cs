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

        // ── 旧ファイルはいまも開ける ───────────────────────

        /// <summary>
        /// <b>要素を共有している保存ファイル</b>が、いまも開けること。
        /// (2026-08-26 より前に保存されたファイルがこれに当たる)
        ///
        /// 当時はレコード側のグリッドが<b>同じ要素インスタンス</b>を指していた
        /// (<c>new ObservableCollection&lt;&gt;(gridData)</c> はリストだけを複製する)。
        /// <c>ReferenceHandler.Preserve</c> では先に現れる複製の側に要素の <c>$id</c> が付き、
        /// レコード側は <c>$ref</c> になる。複製の<b>セッター</b>を残しているのはこのためで、
        /// 消すと <c>$id</c> が登録されず「Reference が見つからない」で開けなくなる。
        /// </summary>
        [TestMethod]
        public void AnOldFileWithSharedElementsStillLoads()
        {
            // 当時の保存形。複製が $id を持ち、ケース側は $ref で参照する
            string oldJson = """
                {
                  "$id": "1",
                  "SettlementGridData": {
                    "$id": "2",
                    "$values": [
                      { "$id": "3", "X": 0, "Y": 0, "Settlement": 1.0 },
                      { "$id": "4", "X": 1, "Y": 0, "Settlement": 2.0 }
                    ]
                  },
                  "CaseRecords": {
                    "$id": "5",
                    "$values": [
                      {
                        "$id": "6",
                        "LoadCaseName": "VL",
                        "SettlementGridData": {
                          "$id": "7",
                          "$values": [ { "$ref": "3" }, { "$ref": "4" } ]
                        }
                      }
                    ]
                  },
                  "ActiveCaseIndex": 0
                }
                """;

            var options = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve };
            var pgs = JsonSerializer.Deserialize<PileGroupSettlement>(oldJson, options);

            Assert.IsNotNull(pgs);
            Assert.AreEqual(1, pgs!.CaseRecords.Count);
            Assert.AreEqual(2, pgs.ActiveSettlementGridData.Count, "$ref が解決できていない");
            Assert.AreEqual(2.0, pgs.ActiveSettlementGridData[1].Settlement, 1e-12);
        }

        // ── 新しく作るときは複製に入れない ─────────────────

        /// <summary>
        /// <b>いま作られるモデルは、コンタの複製に結果を入れないこと。</b>
        ///
        /// 入れると保存ファイルに複製が復活し、ケース側の要素が <c>$ref</c> になる。
        /// そうなると、あとで複製を撤去した瞬間にそのファイルが開けなくなる。
        /// 結果はケース記録が持ち、表示は <c>ActiveSettlementGridData</c> から読む。
        /// </summary>
        [TestMethod]
        public void NewlyBuiltRecords_LeaveTheMirrorEmpty()
        {
            var pgs = BuildAsTheAppDoes();

            Assert.IsTrue(pgs.CaseRecords[0].SettlementGridData.Count > 0,
                "前提が崩れている (ケースに結果が入っていない)");
            Assert.AreEqual(0, pgs.LegacySettlementGridData?.Count ?? 0,
                "コンタの複製に結果が入っています");

            // 表示はケースから引けること
            Assert.AreEqual(pgs.CaseRecords[0].SettlementGridData.Count,
                            pgs.ActiveSettlementGridData.Count);

            // 矩形荷重は入力なので、ケースと実体を分けたままであること
            for (int i = 0; i < pgs.RectLoads.Count; i++)
            {
                Assert.AreNotSame(pgs.RectLoads[i], pgs.CaseRecords[0].RectLoads[i],
                    $"矩形荷重 {i} 番を共有している (画面で編集すると保存済みの結果が変わる)");
            }
        }

        /// <summary>
        /// いま保存するファイルの複製が<b>空</b>であること。
        ///
        /// System.Text.Json では「読めるが書き出さない」プロパティは作れないので、
        /// プロパティは残したまま<b>結果を入れない</b>ことで中身を消している。
        /// 同期を戻すと複製に要素が入り、ケース側がそれを <c>$ref</c> で参照する形に戻る。
        /// そうなると、あとで複製を撤去した瞬間に、いま作ったファイルまで開けなくなる。
        /// </summary>
        [TestMethod]
        public void FilesSavedNowDoNotContainTheMirror()
        {
            var pgs = BuildAsTheAppDoes();

            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true,
            };
            string json = JsonSerializer.Serialize(pgs, options);

            Assert.AreEqual(0, pgs.LegacySettlementGridData?.Count ?? 0,
                "複製に結果が入っています (同期を戻していないか)");

            // 複製のプロパティ自体は残る (旧ファイルを開くために外せない)。
            // 中身が空なら $id を持つ要素が増えず、ケース側が $ref になることもない。
            StringAssert.DoesNotMatch(json,
                new System.Text.RegularExpressions.Regex(@"\$ref"),
                "保存ファイルに $ref が出ています (複製とケースで要素を共有している)");

            // 書き出していなくても、ケースは往復できること
            var restored = JsonSerializer.Deserialize<PileGroupSettlement>(json, options);
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
        // ── 保存に出さない / 旧ファイルは開ける ───────────

        /// <summary>
        /// 複製しか持たない旧ファイルは、これまでどおり開けること。
        ///
        /// System.Text.Json はゲッターの無いプロパティを直列化しないが、逆直列化はする。
        /// これが崩れると旧ファイルの沈下結果が丸ごと失われる。
        /// </summary>
        [TestMethod]
        public void AnOldFileWithOnlyTheMirrorStillLoads()
        {
            string oldJson = """
                {
                  "$id": "1",
                  "SettlementGridData": {
                    "$id": "2",
                    "$values": [
                      { "$id": "3", "X": 0, "Y": 0, "Settlement": 4.5 },
                      { "$id": "4", "X": 1, "Y": 0, "Settlement": 6.5 }
                    ]
                  }
                }
                """;

            var options = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve };
            var pgs = JsonSerializer.Deserialize<PileGroupSettlement>(oldJson, options);

            Assert.IsNotNull(pgs);
            Assert.AreEqual(2, pgs!.LegacySettlementGridData?.Count ?? 0,
                "旧ファイルの複製が読み込めていません (セッターを消していないか)");
            Assert.AreEqual(6.5, pgs.LegacySettlementGridData![1].Settlement, 1e-12);
        }

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

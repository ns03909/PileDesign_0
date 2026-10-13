using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 「単杭沈下 + 群杭沈下」を組む場所を <see cref="PileSettlementComposition"/> の 1 か所にしたことの検査 (2026-10-12)。
    ///
    /// <para>以前は 4 か所が自前で足しており、組み方がずれていた。
    /// <list type="bullet">
    /// <item>沈下量の検定・杭頭変形角の検定: 群杭沈下の<b>常時の記録</b>・「沈下検討の対象」を見る・無ければ検定不能。</item>
    /// <item>計算書の各杭位置の沈下量一覧: <b>画面で表示中のケース</b> (地震時のこともある) を足す。
    ///   同じ計算書の中で、沈下量の検定と一覧表の合計沈下量が食い違った。</item>
    /// <item>沈下グラフ・画面の表示: 表示中のケースを足し、値が無い杭は<b>黙って 0</b>。</item>
    /// </list></para>
    /// </summary>
    [TestClass]
    public class PileSettlementCompositionTests
    {
        // ===== 入力の組み立て =====

        /// <summary>
        /// 杭 3 本 (単杭沈下 10 / 20 / 30 mm、荷重-沈下曲線あり)。群杭沈下の記録は呼び出し側で足す。
        /// </summary>
        private static InputModel Build()
        {
            var input = new InputModel();
            input.AttachViewModel(new MainWindowViewModel { CurrentInputModel = input });
            input.FundamentalInput ??= new FundamentalInput();
            input.PileLayoutItems ??= [];
            for (int i = 1; i <= 3; i++)
                input.PileLayoutItems.Add(new PileLayoutDataItem
                { PileNo = i, No = i, PileBodyNo = 1, SinglePileSettlementVL = 0.010 * i });

            var soilPile = new SoilPile();
            soilPile.LoadDisplacements.Add(new PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 0, D0s = 0 });
            soilPile.LoadDisplacements.Add(new PileDesign.FEM.VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 1000, D0s = 5 });
            input.ElementDivision ??= new ElementDivision();
            input.ElementDivision.SoilPiles = [soilPile];
            foreach (var p in input.PileLayoutItems) p.SoilPileAltNo = 1;

            input.PileGroupSettlement ??= new PileGroupSettlement();
            input.PileGroupSettlement.CaseRecords ??= [];
            return input;
        }

        private static GroupSettlementCaseRecord Record(string name, bool beamAware, params (int PileNo, double Mm)[] values) => new()
        {
            LoadCaseName = name,
            LoadingType = beamAware ? "個別矩形（基礎梁考慮）" : "任意矩形",
            IsBeamAware = beamAware,
            IsConverged = true,
            PileSettlements_mm = values.ToDictionary(v => v.PileNo, v => v.Mm),
        };

        /// <summary>常時 (VL) 3 mm と、基礎梁ありの地震時 (L1) 50 mm の 2 記録。表示中は地震時。</summary>
        private static InputModel WithEarthquakeCaseDisplayed()
        {
            var input = Build();
            input.PileGroupSettlement.CaseRecords.Add(Record("VL", false, (1, 3.0), (2, 3.0), (3, 3.0)));
            input.PileGroupSettlement.CaseRecords.Add(Record("L1-1: 杭軸力", true, (1, 50.0), (2, 50.0), (3, 50.0)));
            input.PileGroupSettlement.ActiveCaseIndex = 1;   // 画面は地震時のケースを表示中
            return input;
        }

        // ===== 計算書の各杭位置の沈下量一覧 =====

        /// <summary>
        /// 計算書の一覧表を作り、表ごとに (杭No → 単杭・群杭・合計の文字) を返す。
        /// 以前の実装 (private) でも呼べるよう、名前で引く。
        /// </summary>
        private static (List<Dictionary<string, string[]>> Tables, string Text) ReportTable(InputModel input)
        {
            var doc = new PileDesign.Output.WordDocument(input, null!, null!);
            var method = typeof(PileDesign.Output.WordDocument).GetMethod("AddPileSettlementTable",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [typeof(Body)], null);
            Assert.IsNotNull(method, "計算書の各杭位置の沈下量一覧を作るメソッドが見つかりません");
            var body = new Body();
            method.Invoke(doc, [body]);

            var tables = new List<Dictionary<string, string[]>>();
            foreach (var table in body.Elements<Table>())
            {
                var rows = new Dictionary<string, string[]>();
                foreach (var row in table.Elements<TableRow>().Skip(1))   // 見出しを除く
                {
                    var cells = row.Elements<TableCell>().Select(c => c.InnerText.Trim()).ToArray();
                    rows[cells[0]] = [cells[3], cells[4], cells[5]];
                }
                tables.Add(rows);
            }
            return (tables, body.InnerText);
        }

        /// <summary>
        /// <b>本題。</b> 画面が地震時の群杭沈下を表示していても、一覧表の群杭沈下・合計沈下量は常時の記録の値で、
        /// 同じ計算書の沈下量の検定の応答値と一致すること。
        /// 以前は表示中のケース (地震時 50 mm) を足し、合計 60 mm を並べていた (検定は常時 3 mm で 13 mm)。
        /// </summary>
        [TestMethod]
        public void TheReportTableUsesTheLongTermRecord_EvenWhenAnEarthquakeCaseIsDisplayed()
        {
            var input = WithEarthquakeCaseDisplayed();
            input.FundamentalInput.EvaluateSettlement = true;
            input.FundamentalInput.AllowableSettlement_mm = 100;

            var (tables, text) = ReportTable(input);

            Assert.AreEqual(1, tables.Count, "常時の記録は 1 つなので表も 1 つのはずです");
            var row1 = tables[0]["1"];
            Assert.AreEqual("10.000", row1[0], "単杭沈下量");
            Assert.AreEqual("3.000", row1[1], "群杭沈下量が常時の記録の値ではありません (表示中の地震時のケースを使っています)");
            Assert.AreEqual("13.000", row1[2], "合計沈下量が常時の値ではありません");

            // 同じ計算書の沈下量の検定と同じ値
            var check = PileSettlementEvaluator.Evaluate(input).Single(i => i.PileNo == 1);
            Assert.AreEqual(check.Response.ToString("N3"), row1[2], "一覧表の合計沈下量が、沈下量の検定の応答値と食い違っています");

            // 表示中のケースを混ぜていないことを、黙らずに書く
            StringAssert.Contains(text, "表示中のケースの値は含めていない", "表示中のケースが常時でないことを注記していません");
        }

        /// <summary>
        /// 常時の記録に杭が欠けていれば、その杭の群杭沈下・合計は「—」(0 として足さない)。
        /// 群杭沈下の記録が無く「単杭＋群杭沈下」なら、検定と同じ理由を注記する。
        /// </summary>
        [TestMethod]
        public void TheReportTableShowsMissingGroupValuesAsMissing_NotZero()
        {
            var input = Build();
            input.PileGroupSettlement.CaseRecords.Add(Record("VL", false, (1, 3.0), (2, 3.0)));   // 杭3 が無い
            input.PileGroupSettlement.ActiveCaseIndex = 0;

            var (tables, _) = ReportTable(input);
            var row3 = tables.Single()["3"];
            Assert.AreEqual("30.000", row3[0]);
            Assert.AreEqual("—", row3[1], "記録に無い杭の群杭沈下量を 0 としています");
            Assert.AreEqual("—", row3[2], "群杭沈下が無い杭の合計を、単杭沈下だけで出しています");

            var none = Build();
            var (noneTables, noneText) = ReportTable(none);
            Assert.AreEqual("—", noneTables.Single()["1"][2], "群杭沈下を解いていないのに合計を出しています");
            StringAssert.Contains(noneText, SettlementDeformationAngleEvaluator.NoGroupResultReason,
                "「単杭＋群杭沈下」なのに群杭沈下の結果が無いことを、検定と同じ言葉で書いていません");
        }

        /// <summary>常時の記録が基礎梁なし・ありの 2 つあれば、検定と同じく両方の表を出す (どちらか一方を黙って選ばない)。</summary>
        [TestMethod]
        public void TwoLongTermRecordsGiveTwoTables()
        {
            var input = Build();
            input.PileGroupSettlement.CaseRecords.Add(Record("VL", false, (1, 3.0), (2, 3.0), (3, 3.0)));
            input.PileGroupSettlement.CaseRecords.Add(Record("杭軸力 VL", true, (1, 4.0), (2, 4.0), (3, 4.0)));
            input.PileGroupSettlement.ActiveCaseIndex = 1;

            var (tables, _) = ReportTable(input);
            Assert.AreEqual(2, tables.Count);
            CollectionAssert.AreEquivalent(new[] { "13.000", "14.000" }, tables.Select(t => t["1"][2]).ToArray());
        }

        // ===== 沈下グラフ =====

        private static GraphViewModel.SettlementLine Line(InputModel input, string option, string loadCase, GroupSettlementCaseRecord? rec)
            => GraphViewModel.BuildSettlementLine(input, input.PileLayoutItems, "Y", option, loadCase, rec, _ => 100.0, _ => 0.0);

        /// <summary>
        /// <b>本題。</b> 群杭沈下の値が無い杭は 0 を足して描かず、NaN (描かない) にして凡例に理由を書くこと。
        /// 以前は <c>SettlementOf</c> が 0 を返し、その杭だけ単杭沈下のみの値で描いていた。
        /// </summary>
        [TestMethod]
        public void TheGraphDoesNotDrawAMissingGroupValueAsZero()
        {
            var input = Build();
            var rec = Record("VL", false, (1, 3.0), (2, 3.0));   // 杭3 が無い
            input.PileGroupSettlement.CaseRecords.Add(rec);
            input.PileGroupSettlement.ActiveCaseIndex = 0;

            var line = Line(input, "沈下 単杭+群杭", "VL", rec);
            Assert.AreEqual(13.0, line.Ys[0], 1e-9);
            Assert.AreEqual(23.0, line.Ys[1], 1e-9);
            Assert.IsTrue(double.IsNaN(line.Ys[2]), $"群杭沈下の無い杭を {line.Ys[2]} mm として描いています (単杭沈下だけ・群杭ぶん 0)");
            Assert.IsTrue(line.Notes.Any(n => n.Contains("杭No.3") && n.Contains("群杭沈下の結果なし")),
                "値の無い杭を凡例で知らせていません: " + string.Join(" / ", line.Notes));

            var groupOnly = Line(input, "沈下 群杭", "VL", rec);
            Assert.IsTrue(double.IsNaN(groupOnly.Ys[2]), "群杭沈下のグラフで、記録に無い杭を 0 mm として描いています");

            var withBeam = Line(input, "沈下 基礎梁考慮単杭+群杭", "VL", rec);
            Assert.IsTrue(double.IsNaN(withBeam.Ys[2]), "基礎梁考慮単杭+群杭のグラフで、群杭沈下の無い杭に 0 を足しています");

            var noRecord = Line(input, "沈下 単杭+群杭", "VL", null);
            Assert.IsTrue(noRecord.Ys.All(double.IsNaN), "群杭沈下を解いていないのに、単杭+群杭を単杭沈下だけで描いています");
        }

        /// <summary>
        /// グラフはどの群杭沈下のケースを足したかを凡例に書くこと (表示中のケースが地震時なら、そう読める)。
        /// </summary>
        [TestMethod]
        public void TheGraphNamesTheGroupCaseItAdds()
        {
            var input = WithEarthquakeCaseDisplayed();
            var displayed = PileSettlementComposition.DisplayedGroupRecord(input);
            Assert.AreEqual("L1-1: 杭軸力", displayed?.LoadCaseName, "(前提) 表示中のケース");

            var line = Line(input, "沈下 単杭+群杭", "VL", displayed);
            StringAssert.Contains(line.Label, "L1-1: 杭軸力", "足した群杭沈下のケースを凡例に書いていません");
            StringAssert.Contains(line.Label, "単杭 (VL)");
            Assert.AreEqual(60.0, line.Ys[0], 1e-9, "表示中のケースを明示して選んだのに、その値を足していません");
        }

        /// <summary>単杭沈下の結果 (荷重-沈下曲線) が無い杭も 0 として描かない。</summary>
        [TestMethod]
        public void TheGraphDoesNotDrawAMissingSinglePileValueAsZero()
        {
            var input = Build();
            input.ElementDivision.SoilPiles[0].LoadDisplacements.Clear();
            var line = Line(input, "沈下 単杭", "VL", null);
            Assert.IsTrue(line.Ys.All(double.IsNaN), "単杭沈下を解いていない杭を描いています");
            Assert.IsTrue(line.Notes.Any(n => n.Contains("単杭沈下")), "単杭沈下の結果が無いことを凡例で知らせていません");
        }

        // ===== 画面の部材角 =====

        /// <summary>
        /// 部材角 (単杭+群杭) の沈下マップは、群杭沈下の値が無い杭を入れない (その杭に掛かる梁は描かない)。
        /// 以前は描画とツールチップの 2 か所の写しが、どちらも群杭ぶんを 0 として足していた。
        /// </summary>
        [TestMethod]
        public void TheMemberAngleMapLeavesOutPilesWithoutAGroupValue()
        {
            var input = Build();
            input.PileGroupSettlement.CaseRecords.Add(Record("VL", false, (1, 3.0), (2, 3.0)));
            input.PileGroupSettlement.ActiveCaseIndex = 0;
            var vm = new MainWindowViewModel { CurrentInputModel = input };

            var method = typeof(PileDesign.Views.MainWindow).GetMethod("BuildMemberAngleSettlementMap",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "部材角の沈下マップを組むメソッドが見つかりません");
            var map = (Dictionary<int, double>)method.Invoke(null, [vm, input, "単杭+群杭沈下部材角"])!;
            Assert.AreEqual(0.013, map[1], 1e-12);
            Assert.IsFalse(map.ContainsKey(3), "群杭沈下の無い杭を、単杭沈下だけの値で部材角に使っています");
        }

        // ===== 組む場所は 1 か所 (ソース走査) =====

        /// <summary>単杭沈下量 (杭の値) を読むところ。</summary>
        private static readonly Regex SingleToken = new(@"\.SinglePileSettlement(VL|Level1s|Level2s)\b");

        /// <summary>群杭沈下の記録 (の杭ごとの値) を読むところ。</summary>
        private static readonly Regex GroupToken = new(@"\bActiveRecord\b|\bPileSettlements_mm\b|(?<![A-Za-z])SettlementOf\s*\(|\bGroupSettlementOf\s*\(|\.GroupPileSettlement\b");

        /// <summary>
        /// <b>本題。</b> 単杭沈下量と群杭沈下の記録を同じメンバーの中で扱うのは <see cref="PileSettlementComposition"/> だけ。
        /// ほかの場所が自前で足し始めると、記録の選び方・欠けたときの扱いがまたずれる。
        /// 単杭＋群杭が要るなら <c>PileSettlementComposition.Compose</c> を使うこと。
        /// </summary>
        /// <summary>
        /// 画面の「基礎梁考慮+群杭沈下」の吹き出し: 基礎梁を考慮した沈下か群杭沈下のどちらかが無い杭は描かない (0 として足さない)。
        /// 以前は杭頭の沈下 (基礎梁考慮) が無い杭を 0、群杭沈下の値が無い杭も 0 (SettlementOf) として足していた。
        /// </summary>
        [TestMethod]
        public void VerticalBeamPlusGroup_DoesNotAddMissingValuesAsZero()
        {
            var rec = new GroupSettlementCaseRecord { LoadCaseName = "VL", PileSettlements_mm = new() { [1] = 5.0 } };
            Assert.AreEqual(12.0, PileSettlementComposition.VerticalBeamPlusGroup_mm(7.0, rec, 1));
            Assert.IsNull(PileSettlementComposition.VerticalBeamPlusGroup_mm(7.0, rec, 2), "群杭沈下の値が無い杭を 0 として足しています");
            Assert.IsNull(PileSettlementComposition.VerticalBeamPlusGroup_mm(null, rec, 1), "基礎梁考慮の沈下が無い杭を 0 として足しています");
            Assert.IsNull(PileSettlementComposition.VerticalBeamPlusGroup_mm(7.0, null, 1), "群杭沈下の記録が無いのに足しています");
            Assert.IsNull(PileSettlementComposition.VerticalBeamPlusGroup_mm(double.NaN, rec, 1));

            // 吹き出しはこの足し方を通し、群杭沈下を SettlementOf (無いと 0) で引かない
            string canvas = TestSource.Read("Graphics_r1", "Views", "MainWindow.CanvasResultsVertical.cs");
            StringAssert.Contains(canvas, "PileSettlementComposition.VerticalBeamPlusGroup_mm(", "吹き出しが共通の足し方を通していません");
            Assert.IsFalse(canvas.Contains(".SettlementOf(", StringComparison.Ordinal), "吹き出しが群杭沈下を SettlementOf (値が無いと 0) で引いています");
        }

        [TestMethod]
        public void OnlyTheCompositionServiceCombinesSingleAndGroupSettlements()
        {
            string root = TestSource.Dir("Graphics_r1");
            var offenders = new List<string>();
            int files = 0, singleMembers = 0, groupMembers = 0;
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (rel.StartsWith("obj/", StringComparison.Ordinal) || rel.StartsWith("bin/", StringComparison.Ordinal)) continue;
                files++;
                if (rel == "Services/PileSettlementComposition.cs") continue;

                foreach (var member in Members(StripCommentsAndStrings(File.ReadAllText(path))))
                {
                    bool single = SingleToken.IsMatch(member);
                    bool group = GroupToken.IsMatch(member);
                    if (single) singleMembers++;
                    if (group) groupMembers++;
                    if (single && group)
                        offenders.Add($"{rel}: {FirstLine(member)}");
                }
            }

            TestSource.AssertScanned(files, 300, "本体のソース");
            TestSource.AssertScanned(singleMembers, 5, "単杭沈下量を読むメンバー");
            TestSource.AssertScanned(groupMembers, 10, "群杭沈下の記録を読むメンバー");
            Assert.AreEqual(0, offenders.Count,
                "単杭沈下と群杭沈下を PileSettlementComposition の外で組んでいます:\n" + string.Join("\n", offenders));
        }

        /// <summary>ガードが以前の書き方を捕まえること (空振りしていないこと)。</summary>
        [TestMethod]
        public void TheGuardCatchesTheOldWayOfAdding()
        {
            const string old = @"
namespace X
{
    public partial class G
    {
        private void Draw()
        {
            foreach (var pile in piles)
                ys.Add(pile.SinglePileSettlementVL * 1000 + GroupSettlementOf(pile)); // m→mm + mm
        }
        private double GroupSettlementOf(PileLayoutDataItem pile) => InputModel?.PileGroupSettlement?.SettlementOf(pile.PileNo) ?? 0.0;
        private void Evaluate()
        {
            var singleByPileNo = piles.ToDictionary(p => p.PileNo, p => p.SinglePileSettlementVL);
            var heads = piles.Select(p => singleByPileNo[p.PileNo] + rec.PileSettlements_mm[p.PileNo] * 1e-3);
        }
        // pile.SinglePileSettlementVL + pgs.SettlementOf(1) はコメントなので数えない
        private string Text => ""pile.SinglePileSettlementVL + x.ActiveRecord"";
    }
}";
            var hits = Members(StripCommentsAndStrings(old)).Where(m => SingleToken.IsMatch(m) && GroupToken.IsMatch(m)).ToList();
            Assert.AreEqual(2, hits.Count, "以前の書き方 (グラフ・変形角の検定) を捕まえられません:\n" + string.Join("\n---\n", hits));
        }

        private static string FirstLine(string member)
            => member.Trim().Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

        /// <summary>コメントと文字列・文字リテラルの中身を落とす (説明文に名前が出ているだけで引っかからないように)。</summary>
        private static string StripCommentsAndStrings(string src)
        {
            var sb = new StringBuilder(src.Length);
            int i = 0;
            while (i < src.Length)
            {
                char c = src[i];
                char next = i + 1 < src.Length ? src[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                }
                else if (c == '/' && next == '*')
                {
                    int end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? src.Length : end + 2;
                    sb.Append(' ');
                }
                else if (c == '"' || ((c == '@' || c == '$') && (next == '"' || next == '@' || next == '$')))
                {
                    // 文字列の前置き (@ / $ / $@ / @$) を読み飛ばす
                    bool verbatim = false;
                    while (i < src.Length && src[i] != '"') { if (src[i] == '@') verbatim = true; i++; }
                    i++;   // 開きの "
                    while (i < src.Length)
                    {
                        if (verbatim)
                        {
                            if (src[i] == '"' && i + 1 < src.Length && src[i + 1] == '"') { i += 2; continue; }
                            if (src[i] == '"') { i++; break; }
                        }
                        else
                        {
                            if (src[i] == '\\') { i += 2; continue; }
                            if (src[i] == '"' || src[i] == '\n') { i++; break; }
                        }
                        i++;
                    }
                    sb.Append("\"\"");
                }
                else if (c == '\'')
                {
                    // 文字リテラル ('{' など) が括弧の数え違いを起こさないように
                    int j = i + 1;
                    if (j < src.Length && src[j] == '\\') j += 2; else j++;
                    while (j < src.Length && src[j] != '\'' && src[j] != '\n') j++;
                    i = j + 1;
                    sb.Append("' '");
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 型の本体の直下にあるメンバー (メソッド・プロパティ・フィールド) ごとに切り出す。
        /// 入れ子の型はその中のメンバーごとに切る。
        /// </summary>
        private static List<string> Members(string code)
        {
            var result = new List<string>();
            var stack = new Stack<bool>();   // 開いている括弧が型の本体か
            int boundary = 0;                // メンバーの外で、次の宣言が始まる位置
            int memberStart = -1;            // メンバーの中なら、そのメンバーの始まり
            var typeHeader = new Regex(@"\b(class|struct|record|interface)\s+[A-Za-z_]");

            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                bool inTypeBody = stack.Count > 0 && stack.Peek();
                if (c == '{')
                {
                    if (memberStart >= 0) { stack.Push(false); continue; }   // メンバーの中の括弧
                    bool isType = typeHeader.IsMatch(code[boundary..i]);
                    if (inTypeBody && !isType) memberStart = boundary;     // メンバーの本体 (メソッド・アクセサー・初期化子)
                    stack.Push(isType);
                    if (memberStart < 0) boundary = i + 1;                  // 名前空間・型の本体の始まり
                }
                else if (c == '}')
                {
                    if (stack.Count == 0) continue;
                    bool wasType = stack.Pop();
                    bool nowInTypeBody = stack.Count > 0 && stack.Peek();
                    if (memberStart >= 0)
                    {
                        if (!nowInTypeBody || wasType) continue;            // メンバーの中の括弧が閉じただけ
                        result.Add(code[memberStart..(i + 1)]);
                        memberStart = -1;
                    }
                    boundary = i + 1;
                }
                else if (c == ';' && memberStart < 0)
                {
                    if (inTypeBody) result.Add(code[boundary..(i + 1)]);   // 式形式のメンバー・フィールド
                    boundary = i + 1;
                }
            }
            return result;
        }
    }
}

using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.Results;
using PileDesign.Output;
using PileDesign.Services;

namespace TestProject1;

/// <summary>
/// 画面・計算書へ出す直前の共通の検査: 表の中の数値でない値 (NaN・無限大)。
///
/// 表ごとに行を組む処理がばらばらで、数値でない値はそのまま「NaN」「∞」と並んでいた。
/// 気付く手掛かりが各セルを見ることしかなかったので、画面の表は名前に件数を添え、
/// 計算書は出力の最後にどの表かを知らせる。
/// </summary>
[TestClass]
public class OutputNonFiniteCheckTests
{
    private static ResultTable Table(params ForceSummaryRow[] rows) => new()
    {
        Name = "外力・反力サマリー",
        Columns = ResultColumnReflectionCache.GetColumns(typeof(ForceSummaryRow)),
        Rows = rows,
        LoadCaseName = "L1",
    };

    [TestMethod]
    public void ScreenTable_CountsNonFiniteCells_AndShowsThemInTheName()
    {
        var clean = Table(new ForceSummaryRow { Item = "合計", Fx = 1, Fy = 2, Fz = 3, Fh = 4 });
        Assert.AreEqual(0, clean.NonFiniteCellCount);
        Assert.IsFalse(clean.DisplayName.Contains("数値でない"));

        var bad = Table(
            new ForceSummaryRow { Item = "合計", Fx = double.NaN, Fy = 2, Fz = double.PositiveInfinity, Fh = 4 },
            new ForceSummaryRow { Item = "別", Fx = 1, Fy = 2, Fz = 3, Fh = double.NegativeInfinity });
        Assert.AreEqual(3, bad.NonFiniteCellCount);
        StringAssert.Contains(bad.DisplayName, "数値でない値 3 か所");

        // 行を絞った複製でも数え直す (元の表の件数を持ち越さない)
        Assert.AreEqual(1, bad.WithRows([bad.Rows[1]]).NonFiniteCellCount);
    }

    [TestMethod]
    public void ReportText_RecognisesTheNonFiniteSpellings()
    {
        foreach (var t in new[] { "NaN", "∞", "-∞", " −∞ ", "Infinity", "-Infinity" })
            Assert.IsTrue(WordDocument.IsNonFiniteText(t), t);
        foreach (var t in new[] { "0.000", "—", "", "12,345", "N/A" })
            Assert.IsFalse(WordDocument.IsNonFiniteText(t), t);
    }

    /// <summary>計算書: 数値でない値を含む表を、直前の表題と件数で示す。無ければ null。</summary>
    [TestMethod]
    public void Report_NamesTheTablesWithNonFiniteCells()
    {
        static Table T(params string[] cells)
        {
            var row = new TableRow();
            foreach (var c in cells) row.Append(new TableCell(new Paragraph(new Run(new Text(c)))));
            return new Table(row);
        }
        var body = new Body(
            new Paragraph(new Run(new Text("表 1 杭頭反力"))), T("1.0", "2.0"),
            new Paragraph(new Run(new Text("表 2 杭頭変位"))), T("NaN", "∞", "3.0"));

        string? text = WordDocument.DescribeNonFiniteTableCells(body);
        Assert.IsNotNull(text);
        StringAssert.Contains(text, "1 個");
        StringAssert.Contains(text, "「表 2 杭頭変位」2 か所");
        Assert.IsFalse(text!.Contains("杭頭反力"), "数値でない値の無い表まで挙げている");

        Assert.IsNull(WordDocument.DescribeNonFiniteTableCells(new Body(T("1.0"))));
    }
}

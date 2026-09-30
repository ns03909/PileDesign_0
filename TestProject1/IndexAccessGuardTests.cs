using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1;

/// <summary>
/// 杭体・地盤・土層-杭セットの一覧を、番号 (「… - 1」) でじかに引かない。
///
/// <para>番号は保存データの食い違い・削除のあとの古い番号・画面の選択の食い違いで範囲の外になりうる。
/// 以前は範囲を手で確かめている所とそうでない所が混ざっていて、確かめ忘れた所だけが「インデックスが範囲外です」で落ちた。
/// 引くのは次のどれかにそろえる:</para>
/// <list type="bullet">
/// <item><c>InputModel.PileBodyAt / GroundAt</c> (無ければ null)・<c>RequirePileBody / RequireGround / RequireSoilPile</c> (杭番号つきの診断)</item>
/// <item>画面が自分で持つ一覧の選択中の要素 (<c>CurrentBody</c> など。選択番号を一覧の範囲に丸めて引く)</item>
/// </list>
/// 許すのは、範囲を確かめる式を同じ行に持つ取得処理の定義 (<c>&gt;= 1 &amp;&amp;</c>) と、丸めて引く定義 (<c>Math.Clamp</c>) だけ。
/// </summary>
[TestClass]
public class IndexAccessGuardTests
{
    [TestMethod]
    public void NoListIsIndexedByANumberDirectly()
    {
        var raw = new Regex(@"\b(PileBodies|GroundsInput|SoilPiles)!?\??\[[^\]]*-\s*1\s*\]");
        char sep = Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(TestSource.Dir("Graphics_r1"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .ToList();
        TestSource.AssertScanned(files.Count, 300, "アプリのソース");
        var hits = files.SelectMany(f => File.ReadAllLines(f).Select((l, i) => (File: Path.GetFileName(f), Line: i + 1, Text: l)))
            .Where(l => !l.Text.TrimStart().StartsWith("//") && raw.IsMatch(l.Text))
            .Where(l => !l.Text.Contains("Math.Clamp(") && !l.Text.Contains(">= 1 &&"))
            .Select(l => $"{l.File}:{l.Line}  {l.Text.Trim()}")
            .ToList();
        Assert.AreEqual(0, hits.Count, "番号で一覧をじかに引いています。PileBodyAt / RequirePileBody・CurrentBody などを使ってください:\n  "
            + string.Join("\n  ", hits));
    }
}

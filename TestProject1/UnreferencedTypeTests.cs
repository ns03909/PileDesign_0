using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 誰からも使われない型が溜まらないこと。
    ///
    /// 使われない型は検索の邪魔になる。名前で当たりを付けて読みに行った先が
    /// 死んだコードだと、そこを直しても何も変わらない。実際に 22 ファイル 1,734 行が
    /// 参照ゼロのまま残っていた (等高線の 3 クラス・未使用の変換器 5 個・
    /// 全行がコメントの地盤応答計算など)。
    ///
    /// <b>ここでは「増えていないこと」だけを見る。</b> ゼロを求めると、
    /// これから使う予定で先に置いた型まで消すことになる。
    ///
    /// <b>数え方</b>: その型の名前が<b>何回出てくるか</b>で判定する。宣言の 1 回だけなら
    /// 誰も使っていない。この作業では 2 度、数え方を誤って消しかけた。
    /// <list type="bullet">
    /// <item>ファイル数で数えると、同じファイルの中だけで使われている型を見落とす
    ///   (色の定義でこれを踏み、一度ビルドを壊した)</item>
    /// <item>本体だけを探すと、テストからしか使われていない型を見落とす
    ///   (BeamForceExtensions と GetDistance がこれ)</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class UnreferencedTypeTests
    {
        /// <summary>
        /// 現状の数。<b>減らすのはよい。増やすときは、その型が本当に要るか考えること。</b>
        /// </summary>
        private const int AtMost = 0;

        /// <summary>
        /// 対象外。
        /// ・XAML から名前で組み立てられるもの (変換器・ビヘイビア) は誤検出しやすい
        /// ・入口の型はコードから呼ばれない
        /// </summary>
        private static readonly string[] Ignored =
        [
            "App", "MainWindow", "Program",
        ];

        [TestMethod]
        public void UnreferencedTypes_DoNotAccumulate()
        {
            var root = TestSource.Dir("Graphics_r1");
            var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .ToArray();

            TestSource.AssertScanned(files.Length, 300, "本体のソース");

            // 型の宣言を集める
            var declPattern = new Regex(
                @"^\s*(?:public|internal|private|protected)?\s*(?:static\s+|sealed\s+|abstract\s+|partial\s+)*"
                + @"(?:class|struct|record|interface|enum)\s+(\w+)");
            var declared = new Dictionary<string, string>();   // 型名 → 宣言したファイル
            foreach (var file in files)
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    var m = declPattern.Match(line);
                    if (!m.Success) continue;
                    var name = m.Groups[1].Value;
                    if (Ignored.Contains(name)) continue;
                    // 同名 (partial・入れ子) は最初のものだけ覚える
                    declared.TryAdd(name, file);
                }
            }

            // 探す先には<b>テストも含める</b>。
            // 本体だけを見ると、テストからしか使われていない型を「使われていない」と言う。
            // 実際にこれで 2 つ (BeamForceExtensions・GetDistance) を消しかけた。
            var searchable = files
                .Concat(Directory.GetFiles(root, "*.xaml", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
                .Concat(Directory.GetFiles(TestSource.Dir("TestProject1"), "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
                .Select(File.ReadAllText)
                .ToArray();

            var unreferenced = new List<string>();
            foreach (var (name, file) in declared)
            {
                // ファイル数ではなく<b>出現回数</b>で数える。
                // ファイル数だと、同じファイルの中だけで使われている型を
                // 「使われていない」と誤って言う (宣言したファイルを 1 と数えるため)。
                var word = new Regex($@"\b{Regex.Escape(name)}\b");
                int hits = searchable.Sum(text => word.Matches(text).Count);
                if (hits <= 1)
                    unreferenced.Add($"{name}  ({Path.GetFileName(file)})");
            }

            Assert.IsTrue(unreferenced.Count <= AtMost,
                $"使われていない型が {unreferenced.Count} 個あります (基準 {AtMost} 個以下)。"
                + "消すか、使う予定があるなら基準値を上げてその理由を書いてください:"
                + Environment.NewLine + "  "
                + string.Join(Environment.NewLine + "  ", unreferenced.OrderBy(x => x, StringComparer.Ordinal)));
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 文字キーのショートカットが修飾キーを見ていること。
    ///
    /// <c>PreviewKeyDown</c> はトンネリングなので、ウィンドウの handler が
    /// TextBox より<b>先に</b>キーを受け取る。ここで修飾キーを見ずに
    /// <c>e.Key == Key.Y</c> だけで分岐し <c>e.Handled = true</c> を立てると、
    /// <b>文字入力中の y / Y が握り潰され、しかも Redo が走る</b>。
    /// 利用者から見ると「打った文字が入らず、編集内容が勝手に巻き戻る」。
    ///
    /// 実際に GroundWindow が Ctrl+Z / Ctrl+Y の実装を 2 箇所に持ち、
    /// 一方 (XAML 添付の handler) の Y 分岐だけ修飾キーの判定が抜けていた。
    /// ビルドもテストも通り、キーを打つまで分からない。
    /// </summary>
    [TestClass]
    public class KeyHandlerTests
    {
        /// <summary>
        /// 修飾キー無しで単独に使ってよいキー。
        /// ファンクションキー・Esc・Enter・Delete などは単独で意味を持つ。
        /// ここでは<b>英字 1 文字</b>のキーだけを検査対象にする。
        /// </summary>
        private static readonly Regex SingleLetterKey =
            new(@"e\.Key\s*==\s*(?:System\.Windows\.Input\.)?Key\.([A-Z])\b", RegexOptions.Compiled);

        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(KeyHandlerTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        [TestMethod]
        public void LetterKeyShortcuts_CheckTheModifierKeys()
        {
            string viewsDir = Path.Combine(FindSolutionRoot(), "Graphics_r1", "Views");
            var violations = new List<string>();

            foreach (string file in Directory.EnumerateFiles(viewsDir, "*.xaml.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                int[] depth = BraceDepths(lines);

                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripComment(lines[i]);
                    var m = SingleLetterKey.Match(code);
                    if (!m.Success) continue;

                    // 同じ条件式で修飾キーを見ていれば良い。
                    // 条件が複数行に折り返されることがある (MainWindow の Ctrl+Shift+P) ので、
                    // 括弧が閉じるまでを 1 つの条件式として見る。
                    if (JoinCondition(lines, i).Contains("Modifiers")) continue;

                    // 外側の if で見ていても良い (ChangWindow はこの形)
                    if (HasEnclosingModifierCheck(lines, depth, i)) continue;

                    violations.Add($"{Path.GetFileName(file)}:{i + 1}  {code.Trim()}");
                }
            }

            Assert.AreEqual(0, violations.Count,
                "修飾キーを見ずに英字キーだけで分岐している " +
                "(文字入力を握り潰す):\n  " + string.Join("\n  ", violations));
        }

        /// <summary>
        /// 折り返された条件式を 1 行に繋ぐ。丸括弧が閉じるまで後続行を足す。
        /// </summary>
        private static string JoinCondition(string[] lines, int index)
        {
            var sb = new System.Text.StringBuilder();
            int balance = 0;
            for (int i = index; i < lines.Length && i < index + 8; i++)
            {
                string code = StripComment(lines[i]);
                sb.Append(code).Append(' ');
                foreach (char c in code)
                {
                    if (c == '(') balance++;
                    else if (c == ')') balance--;
                }
                if (i > index || balance <= 0) break;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 行 <paramref name="index"/> より浅い括弧の深さに、修飾キーを見る条件があるか。
        /// メソッドの外まで遡らないよう、深さ 2 (クラス直下のメソッド本体) で打ち切る。
        /// </summary>
        private static bool HasEnclosingModifierCheck(string[] lines, int[] depth, int index)
        {
            int current = depth[index];
            for (int i = index - 1; i >= 0 && current > 2; i--)
            {
                if (depth[i] >= current) continue;

                current = depth[i];
                string code = StripComment(lines[i]);
                if (code.Contains("Modifiers") && code.Contains("if"))
                    return true;
            }
            return false;
        }

        /// <summary>各行を評価し終えた時点の括弧の深さ。</summary>
        private static int[] BraceDepths(string[] lines)
        {
            var depths = new int[lines.Length];
            int d = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string code = StripComment(lines[i]);
                foreach (char c in code)
                {
                    if (c == '{') d++;
                    else if (c == '}') d--;
                }
                depths[i] = d;
            }
            return depths;
        }

        private static string StripComment(string line)
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? line[..i] : line;
        }
    }
}

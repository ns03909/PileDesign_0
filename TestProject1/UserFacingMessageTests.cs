using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 利用者に出すメッセージの検査。
    ///
    /// ・同じ状況には同じ文が出ること (GuardMessages に集約)
    /// ・内部の識別子やソースツリーの名前が画面に出ないこと
    /// ・例外オブジェクトをそのままダイアログに出さないこと
    /// </summary>
    [TestClass]
    public class UserFacingMessageTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(UserFacingMessageTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        private static IEnumerable<(string File, int Line, string Text)> SourceLines()
        {
            string root = FindSolutionRoot();
            foreach (string cs in Directory.EnumerateFiles(
                         Path.Combine(root, "Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

                int n = 0;
                foreach (string line in File.ReadLines(cs))
                {
                    n++;
                    yield return (Path.GetFileName(cs), n, line);
                }
            }
        }

        /// <summary>
        /// 「杭が無い」の文言が 4 通りに分かれていた。GuardMessages に寄せたので、
        /// 生の文字列リテラルとして復活していないこと。
        /// </summary>
        [TestMethod]
        public void DuplicatedGuardTexts_AreCentralized()
        {
            string[] retired =
            {
                "\"杭配置が定義されていません。\"",
                "\"杭配置データがありません。\"",
                "\"杭が配置されていません。",
            };

            var hits = SourceLines()
                .Where(l => retired.Any(r => l.Text.Contains(r, StringComparison.Ordinal)))
                .Select(l => $"{l.File}:{l.Line}")
                .ToList();

            Assert.AreEqual(0, hits.Count,
                "「杭が無い」の案内が個別に書かれています。GuardMessages.NoPileLayout を使ってください:\n  "
                + string.Join("\n  ", hits));
        }

        /// <summary>
        /// 画面に出す文に内部の識別子・ソースツリー名が混ざっていないこと。
        /// 読んでも次の操作が決まらないうえ、不安にさせるだけになる。
        /// </summary>
        [TestMethod]
        public void MessagesDoNotLeakInternalNames()
        {
            // MessageService.Show(...) の引数に現れてはいけない語
            string[] forbidden =
            {
                "ForcesとDisplacements",
                "tangentStiffnesses",
                "ViewModel が設定されていません",
                "Graphics_r1 フォルダ",
                "選択されたアイテムの型が正しくありません",
            };

            var hits = SourceLines()
                .Where(l => l.Text.Contains("MessageService.Show", StringComparison.Ordinal))
                .Where(l => forbidden.Any(f => l.Text.Contains(f, StringComparison.Ordinal)))
                .Select(l => $"{l.File}:{l.Line}")
                .ToList();

            Assert.AreEqual(0, hits.Count,
                "内部の用語が画面のメッセージに出ています:\n  " + string.Join("\n  ", hits));
        }

        /// <summary>
        /// 例外オブジェクトそのもの ({ex}) をダイアログに出さないこと。
        /// スタックトレースが丸ごと出て、読める人がいない。
        /// {ex.Message} は状況によっては有用なので対象外。
        /// </summary>
        [TestMethod]
        public void ExceptionObjectIsNeverShownRaw()
        {
            var pattern = new Regex(@"MessageService\.Show\([^;]*\{ex\}", RegexOptions.Compiled);

            var hits = SourceLines()
                .Where(l => pattern.IsMatch(l.Text))
                .Select(l => $"{l.File}:{l.Line}")
                .ToList();

            Assert.AreEqual(0, hits.Count,
                "例外オブジェクトをそのままダイアログに出しています (ログに残して要約を出すこと):\n  "
                + string.Join("\n  ", hits));
        }

        /// <summary>
        /// 案内文が「現象 + 対処」になっていること。
        /// 対処のない Error/Warning が 75% を占めていたので、
        /// 少なくとも集約した分は形を保証する。
        /// </summary>
        [TestMethod]
        public void GuardMessages_TellTheUserWhatToDo()
        {
            var messages = new (string Name, string Text)[]
            {
                (nameof(GuardMessages.NoPileLayout),              GuardMessages.NoPileLayout),
                (nameof(GuardMessages.NotElementSplit),           GuardMessages.NotElementSplit),
                (nameof(GuardMessages.NoGroundLayer),             GuardMessages.NoGroundLayer),
                (nameof(GuardMessages.NoAnalysisTargetLoadCase),  GuardMessages.NoAnalysisTargetLoadCase),
                (nameof(GuardMessages.NoFoundationBeam),          GuardMessages.NoFoundationBeam),
                ("WindowOpenFailed",                              GuardMessages.WindowOpenFailed("テスト")),
            };

            foreach (var (name, text) in messages)
            {
                StringAssert.Contains(text, "ください", $"{name}: 対処が書かれていない");
                Assert.IsTrue(text.Contains('\n'), $"{name}: 現象と対処が 1 行にまとまっている");
            }
        }
    }
}

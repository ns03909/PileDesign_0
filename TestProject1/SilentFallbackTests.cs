using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 数値をつくる層 (<c>Models</c> / <c>FEM</c>) で、<b>何も記録せずに</b>
    /// 既定値へ落ちる <c>catch</c> を作らないこと。
    ///
    /// このリポジトリの壊れ方はいつも同じで、例外にならず、もっともらしい値だけが
    /// 間違います。<c>catch</c> の中で 0 や空を返して先へ進むと、解析はそのまま
    /// 完走し、検定も通り、記録も残りません。<b>後から追うこともできません。</b>
    ///
    /// 実際にありました。
    /// <list type="bullet">
    /// <item>M-φ が例外で<b>線形弾性</b>に落ちる (代替したことは記録していたが、理由は捨てていた)</item>
    /// <item>杭群の重心が原点 (0,0,0) に落ちる — 荷重の分布が変わる</item>
    /// <item>杭軸力の合計が 0 に落ちる — 転倒モーメントの配分が変わる</item>
    /// <item>設計軸力が常時軸力に落ちる — 以前この経路の取り違えで安全 M が 39% 過小になった</item>
    /// <item>製品カタログの行が黙って消える (「ログ出力に置き換える」と書かれたまま 4 か所)</item>
    /// </list>
    ///
    /// 記録の宛先は 2 つです。解析の途中なら <c>CalcFallbackTracker</c>
    /// (解析の完了時にまとめて出る)、それ以外は <c>Serilog</c>。
    /// </summary>
    [TestClass]
    public class SilentFallbackTests
    {
        /// <summary>
        /// 記録が無くてよいもの。増やすときは<b>なぜ黙ってよいか</b>を書くこと。
        /// </summary>
        private static readonly HashSet<string> AllowedToBeQuiet = new(StringComparer.Ordinal)
        {
            // 反射で全プロパティを舐める掃除・調査用。読み取り専用やインデクサに
            // 当たるのが普通で、記録すると雑音にしかならない。数値も作らない。
            "InputModel.cs:CleanFloatingPointSpecials",
            "InputModel.cs:FindSpecialNumberLocations",
        };

        [TestMethod]
        public void NoNumericFallback_HappensWithoutARecord()
        {
            var offenders = new List<string>();
            int scanned = 0;

            foreach (var dir in new[] { "Models", "FEM" })
            {
                var root = Path.Combine(TestSource.Root(), "Graphics_r1", dir);
                if (!Directory.Exists(root)) continue;

                foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                        continue;

                    string name = Path.GetFileName(file);
                    string text = File.ReadAllText(file);

                    // catch (...) { ... }  入れ子 1 段まで
                    foreach (Match m in Regex.Matches(
                                 text, @"catch\s*(?:\([^)]*\))?\s*\{((?:[^{}]|\{[^{}]*\})*)\}"))
                    {
                        scanned++;
                        string body = m.Groups[1].Value;

                        // 記録している / 呼び出し元へ知らせている なら良し
                        if (Regex.IsMatch(body,
                                @"Log\.|Logger|CalcFallbackTracker|MessageService|throw|Report\("))
                            continue;

                        // 何も返さず、何も続けない (単に握る) だけなら、値は作られない
                        string stripped = Regex.Replace(body, @"//[^\n]*", "").Trim();
                        bool substitutesAValue =
                            stripped.Contains("return") || stripped.Contains("continue");
                        if (!substitutesAValue) continue;

                        string method = EnclosingMethod(text, m.Index);
                        if (AllowedToBeQuiet.Contains($"{name}:{method}")) continue;

                        int line = text.Take(m.Index).Count(c => c == '\n') + 1;
                        offenders.Add($"{name}:{line} ({method}) → {Compact(stripped)}");
                    }
                }
            }

            TestSource.AssertScanned(scanned, 60, "Models / FEM の catch");

            Assert.AreEqual(0, offenders.Count,
                "既定値へ落ちるのに何も記録していません。解析は完走し、検定も通り、"
                + "後から追えません。CalcFallbackTracker.Report か Serilog を通してください "
                + "(黙ってよい理由があるなら AllowedToBeQuiet に理由つきで足す):"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
        }

        /// <summary>その位置を含むメソッドの名前をざっくり拾う。</summary>
        private static string EnclosingMethod(string text, int index)
        {
            var decl = Regex.Matches(text[..index],
                @"(?:public|private|internal|protected)[\w\s<>,\[\]?\.]*?\s(\w+)\s*\([^)]*\)\s*\{");
            return decl.Count > 0 ? decl[^1].Groups[1].Value : "(不明)";
        }

        private static string Compact(string s)
            => Regex.Replace(s, @"\s+", " ").Trim() is var t && t.Length > 60 ? t[..60] + "…" : t;
    }
}

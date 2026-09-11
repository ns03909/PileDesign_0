using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ViewModel が持つ「選択肢」が、画面に出ていること。
    ///
    /// <para>選択肢の一覧を作り、通知まで書いてあるのに<b>どの画面もそれを出していない</b>
    /// —— という組み合わせが実際に 4 件あった。出ていなければ利用者は選べないので、
    /// その裏にある機能はプログラムに入っているのに<b>一度も走らない</b>。</para>
    ///
    /// <para>たとえば単杭沈下の<b>変位制御法</b>（「極限状態でも安定」と書かれた解法）は
    /// 実装も引き渡しも済んでいるのに画面に選択肢が無く、実アプリで一度も走っていなかった。
    /// 走らせてみると引張側の符号と極限の判定に誤りを抱えていたので、2026-09-11 に削除した。</para>
    ///
    /// <para>残りは画面を作り替えたときの取り残しである。群杭沈下の 2 件は
    /// 独立ウィンドウへの移設（2026-09-06）で、有無の区別が荷重タイプの名前と
    /// タブへ移ったため、旧 UI 用の一覧が浮いた（通知だけは 4 か所から呼ばれ続けている）。</para>
    ///
    /// <para><b>この一覧は「増やさないための下限」であって、許可リストではない。</b>
    /// 消すか画面に出すかを決めたら、ここから外すこと。</para>
    /// </summary>
    [TestClass]
    public class UnreachableSettingsTests
    {
        /// <summary>画面に出ていないと分かっている選択肢と、その事情。</summary>
        private static readonly Dictionary<string, string> Known = new(StringComparer.Ordinal)
        {
            ["GroupSettlementBeamSelectorOptions"] =
                "群杭沈下の「基礎梁:有/無」。独立ウィンドウへの移設で、"
                + "区別が荷重タイプの名前とタブへ移ったため浮いた",
            ["GroupSettlementLoadTypeOptions"] =
                "同上。移設先は AvailableLoadingTypeOptionsNonBeam を使っている",
            ["ElementTypeOption"] =
                "中身が [\"ダミー\"] のまま。対になる ElementType も誰も読んでいない",
        };

        [TestMethod]
        public void EveryOptionList_IsShownSomewhere()
        {
            string vmDir = TestSource.Dir("Graphics_r1", "ViewModels");
            string viewsDir = TestSource.Dir("Graphics_r1", "Views");

            // 画面側 (XAML とコードビハインド) をひとまとめに読む
            var shown = new System.Text.StringBuilder();
            foreach (string f in Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories))
                shown.Append(File.ReadAllText(f));
            foreach (string f in Directory.GetFiles(viewsDir, "*.xaml.cs", SearchOption.AllDirectories))
                shown.Append(File.ReadAllText(f));
            string surface = shown.ToString();

            TestSource.AssertScanned(surface.Length, 100_000, "画面側のソース");

            // ViewModel の「選択肢っぽい」公開プロパティ
            var found = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(vmDir, "*.cs"))
            {
                string src = Regex.Replace(File.ReadAllText(path), "//.*", "");
                foreach (Match m in Regex.Matches(src,
                    @"public\s+[\w<>\[\],\.\? ]+\s+(\w*(?:Options?|Modes|Choices)|\w+List)\s*(?:\{|=>)"))
                {
                    found[m.Groups[1].Value] = Path.GetFileName(path);
                }
            }

            TestSource.AssertScanned(found.Count, 30, "選択肢っぽい公開プロパティ");

            var unreachable = found
                .Where(kv => !surface.Contains(kv.Key, StringComparison.Ordinal))
                .ToList();

            // 1. 知らない到達不能が増えていないこと
            var newly = unreachable.Where(kv => !Known.ContainsKey(kv.Key)).ToList();
            Assert.AreEqual(0, newly.Count,
                "画面に出ていない選択肢が増えました。"
                + "選べないなら、その裏の機能は一度も走りません:"
                + Environment.NewLine + "  "
                + string.Join(Environment.NewLine + "  ",
                    newly.Select(kv => $"{kv.Key} ({kv.Value})")));

            // 2. 解決したものが一覧に残っていないこと (残すと、次に増えたときの目印が濁る)
            var stale = Known.Keys
                .Where(k => !unreachable.Any(kv => kv.Key == k))
                .ToList();
            Assert.AreEqual(0, stale.Count,
                "画面に出るようになった (または消えた) のに、この一覧に残っています。"
                + "外してください: " + string.Join(", ", stale));
        }
    }
}

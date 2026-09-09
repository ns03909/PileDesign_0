using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TestProject1
{
    /// <summary>
    /// 下の層が画面を触らないこと。
    ///
    /// 断面の計算 (<c>Models/InputData/</c>) や解析 (<c>FEM/</c>) は、解析スレッドからも
    /// ケース並列のワーカーからも呼ばれる。そこから画面へ割り込んでダイアログを出すと、
    ///
    /// <list type="bullet">
    /// <item>並列実行では 8 本のワーカーがそれぞれ画面スレッドへ積む</item>
    /// <item>相手のスレッドが終了していると<b>永久に待つ</b>
    ///   (「全体実行でだけ時間切れになる」という形で実際に起きた)</item>
    /// <item>解析の途中で利用者の操作を待つことになる</item>
    /// </list>
    ///
    /// 計算が既定値で継続したことは <c>CalcFallbackTracker.Report</c> に記録し、
    /// 解析の完了時に画面側がまとめて出す。
    /// </summary>
    [TestClass]
    public class LayeringGuardTests
    {
        /// <summary>画面を触ってはいけない層。</summary>
        private static readonly string[] LowerLayers = ["Models", "FEM"];

        /// <summary>禁じる呼び出し。実害のあるものだけを見る。</summary>
        private static readonly string[] ForbiddenCalls =
        [
            "MessageService.Show",
            "MessageService.ShowError",
            "MessageService.ShowWarning",
            "MessageService.ShowInfo",
            "MessageBox.Show",
        ];

        [TestMethod]
        public void TheLowerLayers_DoNotOpenDialogs()
        {
            var root = FindSolutionRoot();
            var bad = new List<string>();
            int scanned = 0;

            foreach (var layer in LowerLayers)
            {
                var dir = Path.Combine(root, "Graphics_r1", layer);
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    scanned++;
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i].Trim();
                        if (line.StartsWith("//") || line.StartsWith("///")) continue;

                        foreach (var call in ForbiddenCalls)
                        {
                            if (!line.Contains(call)) continue;
                            bad.Add($"{Path.GetFileName(file)}:{i + 1}  {line}");
                            break;
                        }
                    }
                }
            }

            // 検査対象が実際にあったこと。層ごと消えたり移動したりしたら気づけるように。
            Assert.IsTrue(scanned >= 80,
                $"走査したファイルが {scanned} 件しかありません。層の場所が変わっていないか確認してください");

            Assert.AreEqual(0, bad.Count,
                "下の層から画面のダイアログを出しています。CalcFallbackTracker.Report に寄せてください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", bad));
        }

        /// <summary>
        /// 下の層が画面スレッドへ割り込まないこと。
        ///
        /// ダイアログを出さなくても、<c>Dispatcher.Invoke</c> 自体が「相手が終了していると
        /// 永久に待つ」性質を持つ。下の層に使う理由はない。
        /// </summary>
        [TestMethod]
        public void TheLowerLayers_DoNotBlockOnTheUiThread()
        {
            var root = FindSolutionRoot();
            var bad = new List<string>();

            foreach (var layer in LowerLayers)
            {
                var dir = Path.Combine(root, "Graphics_r1", layer);
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i].Trim();
                        if (line.StartsWith("//") || line.StartsWith("///")) continue;
                        if (line.Contains("Dispatcher.Invoke"))
                            bad.Add($"{Path.GetFileName(file)}:{i + 1}  {line}");
                    }
                }
            }

            Assert.AreEqual(0, bad.Count,
                "下の層が画面スレッドへ割り込んでいます (相手が終了していると永久に待ちます):"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", bad));
        }

        private static string FindSolutionRoot([CallerFilePath] string thisFile = "")
        {
            foreach (var start in new[] { Path.GetDirectoryName(typeof(LayeringGuardTests).Assembly.Location), Path.GetDirectoryName(thisFile) })
            {
                if (string.IsNullOrEmpty(start)) continue;
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                        return dir.FullName;
                }
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }
    }
}

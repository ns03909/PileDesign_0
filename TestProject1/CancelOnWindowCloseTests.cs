using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 入力ウィンドウを<b>× で閉じたとき</b>も「キャンセル」と同じ扱いになること。
    ///
    /// 入力の実体をそのまま編集するウィンドウでは、戻す仕事をしているのは
    /// キャンセルだけである。だから <c>Closing</c> を扱わないと、× で閉じた人だけ
    /// <b>編集が残る</b>。ボタンで閉じた人と × で閉じた人で結果が違ってしまう。
    ///
    /// 実際に単杭沈下・基礎梁・基本設定・プロジェクト情報の 4 つが素通りしていた
    /// (地盤・荷重ケース・杭体・杭断面・杭頭は塞がれていた)。
    /// 数が多いので、目で数える形にはしない。
    ///
    /// <para><b>見るのは「キャンセルに仕事があるか」だけ。</b>
    /// <c>OnCancel</c> が閉じるだけなら (編集対象が控えなら) × でも困らないので求めない。
    /// 何かを戻しているなら、× も同じ経路を通らなければならない。</para>
    /// </summary>
    [TestClass]
    public class CancelOnWindowCloseTests
    {
        /// <summary>
        /// 求めないウィンドウと、その理由。
        ///
        /// <para><b>水平解析</b>: このウィンドウのキャンセルは<b>閉じるのを断れる</b>
        /// （「解析結果を登録せずに閉じますか」に「いいえ」と答えると閉じない）。
        /// × から素朴に呼ぶと、その断りを握り潰して閉じてしまうので、
        /// <c>e.Cancel</c> へ返せる形にしないと繋げない。
        /// なお ViewModel は開くたびに作り直されるので、× で閉じても
        /// 状態は残らない（キャンセルがしている <c>AnaModels.RemoveAt(1)</c> は
        /// 捨てられる ViewModel の中の片付け）。
        /// <b>違うのは確認が出ないことだけ</b>で、そこは別途直す。</para>
        /// </summary>
        private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
        {
            ["HorizontalCalculationWindow"] = "キャンセルが閉じるのを断れるため",
        };

        [TestMethod]
        public void ClosingWithTheTitleBar_GoesThroughCancel()
        {
            string viewsDir = TestSource.Dir("Graphics_r1", "Views");
            string vmDir = TestSource.Dir("Graphics_r1", "ViewModels");

            var windows = Directory.GetFiles(viewsDir, "*Window.xaml").OrderBy(f => f).ToList();
            TestSource.AssertScanned(windows.Count, 20, "入力ウィンドウの XAML");

            var offenders = new List<string>();
            int needsCancel = 0;

            foreach (string xamlPath in windows)
            {
                string name = Path.GetFileNameWithoutExtension(xamlPath);   // XxxWindow
                string xaml = File.ReadAllText(xamlPath);

                string? vmPath = ResolveViewModel(vmDir, name, xaml);
                if (vmPath == null) continue;

                string vm = File.ReadAllText(vmPath);
                if (!vm.Contains("void OnCancel(", StringComparison.Ordinal)) continue;

                string body = StripComments(TestSource.MethodBody(vm, "void OnCancel("));

                if (!DoesRestoringWork(body)) continue;
                needsCancel++;

                if (Exempt.ContainsKey(name)) continue;

                string what = $"{name} ({Path.GetFileName(vmPath)})";

                var m = Regex.Match(xaml, @"Closing\s*=\s*""(\w+)""");
                if (!m.Success)
                {
                    offenders.Add($"{what}: Closing を扱っていません。"
                        + "× で閉じると編集が残ります");
                    continue;
                }

                // 扱っているだけでは足りない。キャンセルへ回していること。
                string codeBehind = xamlPath + ".cs";
                if (!File.Exists(codeBehind))
                {
                    offenders.Add($"{what}: コードビハインドがありません");
                    continue;
                }

                string behind = File.ReadAllText(codeBehind);
                if (!behind.Contains($"void {m.Groups[1].Value}(", StringComparison.Ordinal))
                {
                    offenders.Add($"{what}: {m.Groups[1].Value} がコードビハインドにありません");
                    continue;
                }

                string handler = StripComments(
                    TestSource.MethodBody(behind, $"void {m.Groups[1].Value}("));

                if (!handler.Contains("CancelCommand", StringComparison.Ordinal)
                    && !handler.Contains("OnCancel", StringComparison.Ordinal))
                {
                    offenders.Add($"{what}: {m.Groups[1].Value} がキャンセルへ回していません");
                }
            }

            TestSource.AssertScanned(needsCancel, 5, "キャンセルに戻す仕事があるウィンドウ");

            Assert.AreEqual(0, offenders.Count,
                "× で閉じるとキャンセルを通らないウィンドウがあります。"
                + "ボタンで閉じた人と × で閉じた人で結果が変わります:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
        }

        /// <summary>
        /// <c>XxxWindow.xaml</c> の ViewModel を探す。
        /// 名前が揃っていないもの (GroundWindow → GroundLayerViewModel) は
        /// XAML の <c>DesignInstance</c> から引く。
        /// </summary>
        private static string? ResolveViewModel(string vmDir, string windowName, string xaml)
        {
            var m = Regex.Match(xaml, @"DesignInstance\s+Type=\w+:(\w+ViewModel)");
            if (m.Success)
            {
                string byDesign = Path.Combine(vmDir, m.Groups[1].Value + ".cs");
                if (File.Exists(byDesign)) return byDesign;
            }

            string guess = Path.Combine(vmDir,
                windowName.Replace("Window", "ViewModel", StringComparison.Ordinal) + ".cs");
            return File.Exists(guess) ? guess : null;
        }

        /// <summary>
        /// キャンセルが<b>閉じる以外の仕事</b>をしているか。
        /// 閉じるだけなら (編集対象が控えなら) × でも困らない。
        /// </summary>
        private static bool DoesRestoringWork(string body)
        {
            // 閉じる合図と入れ物を取り除いて、何か残るかを見る
            string rest = Regex.Replace(body, @"RequestClose\s*\?\.\s*Invoke\s*\([^;]*\);", "");
            rest = Regex.Replace(rest, @"DialogResult\s*=[^;]*;", "");
            rest = Regex.Replace(rest, @"\bClose\s*\(\s*\)\s*;", "");
            rest = Regex.Replace(rest, @"[{}\s]", "");
            return rest.Length > 0;
        }

        /// <summary>
        /// コメントを落とす。落とさないと、説明文に名前が出ているだけで通ってしまう。
        /// </summary>
        private static string StripComments(string source)
        {
            string s = Regex.Replace(source, @"/\*[\s\S]*?\*/", "");
            return Regex.Replace(s, @"//[^\n]*", "");
        }
    }
}

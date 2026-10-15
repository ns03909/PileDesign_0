using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ウィンドウの下の OK・保存・キャンセル・閉じるのボタンは共通の見た目 (Styles.xaml の 3 つ) を使う (2026-10-16 のレビュー)。
    /// 以前は幅 60・80・100・120・150・180 と高さ 28・30 が窓ごとに混在し、共通のスタイルは 1 か所でしか使われていなかった。
    /// 幅や高さを個別に書くと、スタイルの大きさが効かなくなる。
    /// </summary>
    [TestClass]
    public class DialogButtonStyleTests
    {
        private static readonly Regex DialogButton = new(@"^(OK|保存|計算書作成|実行|適用\(_A\)|キャンセル|閉じる)");

        /// <summary>フォームの中のボタン (解析の中断・節点の選び直し・一括の適用) は対象外。</summary>
        private static readonly Regex InForm = new(@"CancelAnalysisCommand|CancelNodePairSelectionCommand");

        [TestMethod]
        public void DialogButtons_UseTheSharedStyles_WithoutTheirOwnSize()
        {
            var problems = new List<string>();
            int scanned = 0;
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1", "Views"), "*.xaml"))
            {
                string xaml = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(xaml, @"<Button\b(.*?)/?>", RegexOptions.Singleline))
                {
                    string attrs = m.Groups[1].Value;
                    var content = Regex.Match(attrs, @"Content=""([^""]*)""");
                    if (!content.Success || !DialogButton.IsMatch(content.Groups[1].Value) || InForm.IsMatch(attrs)) continue;
                    scanned++;
                    string where = $"{Path.GetFileName(file)} 「{content.Groups[1].Value}」";
                    if (!Regex.IsMatch(attrs, @"Style=""\{StaticResource (PrimaryDialogButtonStyle|DialogSecondaryButtonStyle|DialogCancelButtonStyle)\}"""))
                        problems.Add($"{where}: 共通のスタイルを使っていません");
                    foreach (var size in new[] { "Width", "Height", "MinWidth", "MinHeight", "Padding" })
                        if (Regex.IsMatch(attrs, $@"\s{size}="""))
                            problems.Add($"{where}: {size} を個別に書いています");
                }
            }
            TestSource.AssertScanned(scanned, 40, "ウィンドウの下のボタン");
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// <b>実装はあるのに画面から辿れない機能</b>が増えないようにする。
    ///
    /// <para><c>[RelayCommand]</c> は <c>Foo</c> から <c>FooCommand</c> を作る。XAML もコードも
    /// その名前を参照していなければ、その機能は<b>一度も走らない</b>。落ちないので気づけない。
    /// 2026-09-18 の横断で 23 件見つかり、うち実害があったのは次の 3 つだった。</para>
    ///
    /// <list type="bullet">
    /// <item>群杭沈下の例題 2 件 (設計例5・設計例集2.1) がリボンに無く、読み込めなかった
    ///   (JSON は同梱済みで、設計例5 はテストが使っている)</item>
    /// <item>基礎梁入力ウィンドウを開く経路が無かった (リボンのボタンが 2026-02-14 に外され、
    ///   窓・ViewModel・コマンドだけ残った)</item>
    /// <item>群杭係数・杭間隔比の「自動計算」コマンドが空実装のまま残っていた</item>
    /// </list>
    ///
    /// <para>この検査は<b>例題コマンド</b>に絞る。例題は「同梱したのに読み込めない」形で
    /// 取り残されやすく、対応表を手で書かずに機械で突き合わせられる
    /// (コマンド名 ⇔ リボンの Binding)。他のコマンドは用途が幅広く、
    /// コードから呼ぶ形も正しいので、ここでは対象にしない。</para>
    /// </summary>
    [TestClass]
    public class UnreachableFeatureTests
    {
        [TestMethod]
        public void EveryExampleCommandIsOnTheRibbon()
        {
            string examples = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.Examples.cs");
            string ribbon = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");

            // [RelayCommand] private async Task OnExample3_1() → Example3_1Command
            var commands = new List<string>();
            foreach (Match m in Regex.Matches(examples,
                @"\[RelayCommand[^\]]*\]\s*(?:private|public|internal)?\s*(?:async\s+)?"
                + @"(?:void|Task)\s+(\w*Example\w*)\s*\("))
            {
                string method = m.Groups[1].Value;
                string name = method.StartsWith("On") ? method[2..] : method;
                commands.Add(name + "Command");
            }

            TestSource.AssertScanned(commands.Count, 10, "例題コマンド");

            var missing = commands
                .Where(c => !ribbon.Contains(c, System.StringComparison.Ordinal))
                .Distinct()
                .ToList();

            Assert.AreEqual(0, missing.Count,
                "リボンに出ていない例題があります (同梱しているのに読み込めません): "
                + string.Join(" / ", missing));
        }

        /// <summary>
        /// 例題コマンドが指す JSON が同梱されていること。
        /// リボンに出ていても、ファイルが無ければ押した瞬間に失敗する。
        /// </summary>
        [TestMethod]
        public void EveryExampleCommandPointsAtAnExampleFile()
        {
            string examples = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.Examples.cs");
            string dir = TestSource.ExamplesDir();

            var referenced = Regex.Matches(examples,
                    @"Load(?:PileExample|GroupSettlementExample)Async\(\s*""([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            TestSource.AssertScanned(referenced.Count, 8, "例題コマンドが読む JSON");

            var missing = referenced
                .Where(n => !File.Exists(Path.Combine(dir, n + ".json")))
                .ToList();

            Assert.AreEqual(0, missing.Count,
                "例題コマンドが読む JSON が同梱されていません: " + string.Join(" / ", missing));
        }

        /// <summary>
        /// 画面を開くコマンドは、リボンかメニューから辿れること。
        ///
        /// <para>基礎梁入力ウィンドウがこの形で取り残された。窓を開くコマンドは
        /// 「その窓が使えるかどうか」に直結するので、例題と同じく機械で見張る。</para>
        /// </summary>
        [TestMethod]
        public void EveryWindowOpeningCommandIsReachable()
        {
            string dir = TestSource.Dir("Graphics_r1", "ViewModels");
            var xaml = Directory.EnumerateFiles(TestSource.Dir("Graphics_r1", "Views"), "*.xaml",
                    SearchOption.AllDirectories)
                .Select(File.ReadAllText).ToList();
            var code = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(TestSource.Dir("Graphics_r1", "Views"), "*.xaml.cs",
                    SearchOption.AllDirectories))
                .ToDictionary(f => f, File.ReadAllText);

            var unreachable = new List<string>();
            int scanned = 0;

            foreach (var (file, text) in code)
            {
                foreach (Match m in Regex.Matches(text,
                    @"\[RelayCommand[^\]]*\]\s*(?:private|public|internal)?\s*(?:async\s+)?"
                    + @"(?:void|Task)\s+(Open\w*Window\w*)\s*\("))
                {
                    string method = m.Groups[1].Value;
                    // 生成されるコマンド名は On と Async を落とした形
                    // (OpenElementDivisionWindowAsync → OpenElementDivisionWindowCommand)
                    string bare = method.StartsWith("On") ? method[2..] : method;
                    if (bare.EndsWith("Async")) bare = bare[..^5];
                    string name = bare + "Command";
                    scanned++;

                    bool inXaml = xaml.Any(x => x.Contains(name, System.StringComparison.Ordinal));
                    bool inCode = code.Any(kv =>
                        kv.Value.Contains(name, System.StringComparison.Ordinal)
                        || Regex.IsMatch(kv.Value, @"\b" + Regex.Escape(method) + @"\s*\(",
                                         RegexOptions.None) && kv.Key != file);
                    if (!inXaml && !inCode)
                        unreachable.Add($"{Path.GetFileName(file)}: {name}");
                }
            }

            TestSource.AssertScanned(scanned, 5, "画面を開くコマンド");

            Assert.AreEqual(0, unreachable.Count,
                "画面を開くコマンドがどこからも辿れません (その画面は使えません):\n  "
                + string.Join("\n  ", unreachable));
        }
    }
}

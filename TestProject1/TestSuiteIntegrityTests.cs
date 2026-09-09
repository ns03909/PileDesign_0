using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// テストそのものが減っていないことを見る。
    ///
    /// <b>失敗が見えることより、静かに実行されないことのほうが危険。</b>
    /// 実際に、中間生成物が食い違ったまま組み上がったアセンブリで
    /// 「1,768 件」が「1,308 件」になり、それでも <c>成功!</c> と表示されたことがある。
    /// 460 件が消えているのに、緑のまま commit できてしまう。
    ///
    /// ここでは<b>アセンブリに含まれるテストの数</b>を数える。ビルドの取りこぼしや、
    /// テストファイルの消し忘れ・消しすぎを捕まえる。
    /// 実行の途中でテストホストが落ちた場合は数えられないので、そちらは
    /// <c>tools/run-tests.ps1</c> が実行結果の合計を見る。両方いる。
    /// </summary>
    [TestClass]
    public class TestSuiteIntegrityTests
    {
        /// <summary>
        /// テスト数の下限。
        ///
        /// <b>減らすときは、なぜ減ったのかをコミットに書くこと。</b>
        /// 増やすのは自由だが、この値も上げておくと次の取りこぼしを捕まえられる。
        /// 実数との差はわずかしか空けていない。取りこぼしは 2 割 3 割まとめて起きるので、
        /// 緩くすると意味がなくなる。数本まとめて消すときは、ここも下げること。
        ///
        /// <b>実行件数とは一致しない。</b> ここが数えるのは <c>[TestMethod]</c> の宣言で、
        /// <c>[DataRow]</c> による展開は数えない。実行側は 100 件ほど多く出る。
        /// </summary>
        private const int MinimumTestCount = 1600;

        [TestMethod]
        public void TheSuite_StillHasAllItsTests()
        {
            int count = CountTestMethods();

            Assert.IsTrue(count >= MinimumTestCount,
                $"テストの宣言が {count} 件しか見つかりません（最低 {MinimumTestCount} 件のはず）。"
                + "［DataRow の展開は数えないので、実行件数より少なく出るのが正常です］"
                + Environment.NewLine
                + "ビルドの取りこぼしが疑われます。obj と bin を消して作り直してください。"
                + Environment.NewLine
                + "意図して減らしたのなら、MinimumTestCount を下げて理由をコミットに書いてください。");
        }

        /// <summary>
        /// 下限が実数からかけ離れていないこと。
        ///
        /// テストを足しても下限を上げないと、いつまでも古い数を見張ることになる。
        /// 実数の 9 割を切ったら知らせる（下限が形骸化している）。
        /// </summary>
        [TestMethod]
        public void TheMinimum_IsNotStale()
        {
            int count = CountTestMethods();

            Assert.IsTrue(MinimumTestCount >= count * 0.9,
                $"テストの宣言は {count} 件あるのに下限が {MinimumTestCount} 件です。"
                + $"下限を {(int)(count * 0.97)} 件あたりまで上げてください。"
                + "離れすぎていると、取りこぼしを捕まえられません。");
        }

        /// <summary>
        /// このアセンブリの <c>[TestMethod]</c> の数。
        /// <c>[DataRow]</c> による展開は数えないので、実行件数とは一致しない（下限の用途には十分）。
        /// </summary>
        private static int CountTestMethods() =>
            typeof(TestSuiteIntegrityTests).Assembly
                .GetTypes()
                .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Count(m => m.GetCustomAttribute<TestMethodAttribute>() != null);

        /// <summary>
        /// 例題の場所を、テストごとに別々の方法で探していないこと。
        ///
        /// 以前は 4 通りあり、3 つが出力先から <c>"..","..","..",".."</c> の決め打ちだった。
        /// 出力先を変えてビルドすると存在しないパスを返し、例題を使うテストが
        /// <b>静かに Inconclusive になる</b>。13 件が失敗し 203 件がスキップされたまま
        /// 「成功」と出たことがある。探し方は <see cref="TestSource.ExamplesDir"/> に 1 つだけ置く。
        /// </summary>
        [TestMethod]
        public void TheExamplesFolder_IsFoundOneWay()
        {
            var offenders = new System.Collections.Generic.List<string>();
            int scanned = 0;

            foreach (var file in Directory.GetFiles(TestSource.Dir("TestProject1"), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (Path.GetFileName(file) == "TestSource.cs") continue;
                scanned++;

                foreach (var line in File.ReadAllLines(file))
                {
                    var t = line.TrimStart();
                    if (t.StartsWith("//")) continue;
                    if (t.Contains("\"..\", \"..\", \"..\""))
                        offenders.Add($"{Path.GetFileName(file)}: {t.Trim()}");
                }
            }

            TestSource.AssertScanned(scanned, 60, "テストのソース");
            Assert.AreEqual(0, offenders.Count,
                "出力先から数えて遡っている箇所があります。出力先を変えると静かに見つからなくなります。"
                + $"TestSource.Root() / ExamplesDir() を使ってください:{Environment.NewLine}  "
                + string.Join(Environment.NewLine + "  ", offenders));
        }

        /// <summary>
        /// ソースを走査するテストが、件数の下限を置いていること。
        ///
        /// 「悪い書き方が 0 件であること」を見る作りは、<b>対象が 1 件も見つからなくても合格する</b>。
        /// 名前を変えた・ファイルを移した・層ごと移した、のどれでも起きる。
        /// 実際、以前は 16 の走査のうち下限を置いていたのは 5 つだけだった。
        ///
        /// 走査するのに下限を置かないテストが増えたら、ここが知らせる。
        /// 一時ファイルや自動保存のフォルダを見るものは対象外（生成された件数を
        /// そのまま検査するので、下限という考え方が合わない）。
        /// </summary>
        [TestMethod]
        public void TheScanningTests_AllHaveAFloor()
        {
            // 実行時に作られるフォルダを見るもの。件数を直接検査するので下限は要らない。
            string[] runtimeFolders =
            [
                "AutoSaveServiceNaNTests.cs",
                "SaveDurabilityTests.cs",
                "UnsavedWorkAndRestoreTests.cs",
                "IconRegenerator.cs",
            ];

            var missing = new System.Collections.Generic.List<string>();
            int scanned = 0;

            foreach (var file in Directory.GetFiles(TestSource.Dir("TestProject1"), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                scanned++;

                var name = Path.GetFileName(file);
                if (runtimeFolders.Contains(name)) continue;

                var text = File.ReadAllText(file);
                if (!text.Contains("Directory.GetFiles")) continue;
                if (text.Contains("AssertScanned") || text.Contains("TestSource.ExampleFiles")) continue;

                missing.Add(name);
            }

            TestSource.AssertScanned(scanned, 150, "テストのソース");
            Assert.AreEqual(0, missing.Count,
                "走査するのに件数の下限を置いていないテストがあります。"
                + "対象が見つからなくなっても合格してしまいます。"
                + $"TestSource.AssertScanned を足してください:{Environment.NewLine}  "
                + string.Join(Environment.NewLine + "  ", missing));
        }

        /// <summary>例題が実際に見つかること。ここが落ちたら、下の全テストの前提が崩れている。</summary>
        [TestMethod]
        public void TheExamplesFolder_Exists()
        {
            var dir = TestSource.ExamplesDir();
            Assert.IsTrue(Directory.Exists(dir), $"例題のフォルダが見つかりません: {dir}");
            Assert.IsTrue(Directory.GetFiles(dir, "*.json").Length > 0, $"例題の JSON がありません: {dir}");
        }
    }
}

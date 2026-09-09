using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace TestProject1
{
    /// <summary>
    /// ソースを読んで検査するテストの共通の道具。
    ///
    /// このリポジトリには「ビルドは通るが実行時に静かに壊れる」種類の不具合を捕まえるため、
    /// ソースを走査するガードテストが 50 本以上ある。それぞれが同じ補助関数を書き写して
    /// いたため、<c>FindSolutionRoot</c> が 43 通り、<c>ExtractMethodBody</c> が 8 通り、
    /// <c>ReadSource</c> が 8 通りに増えていた。しかも
    ///
    /// <list type="bullet">
    /// <item>ルートの探し方が 4 系統あり、うち 1 つは <c>"..","..","..",".."</c> の
    ///   決め打ちで、対象フレームワークが変わると黙って存在しないパスを返す</item>
    /// <item><c>ExtractMethodBody</c> の 1 つは括弧を数えず<b>先頭から 800 文字</b>を返す。
    ///   対象が長ければ後半を見落とし、短ければ次のメソッドが混ざる</item>
    /// </list>
    ///
    /// という食い違いがあった。ここに集約する。
    /// </summary>
    internal static class TestSource
    {
        /// <summary>
        /// ソリューションのルート。
        ///
        /// テストの出力先から遡るのが基本だが、出力先を変えてビルドしたとき
        /// (アプリ起動中で通常の出力先が使えない場合など) に見つからなくなる。
        /// このファイル自身の位置からも遡り、どちらかで見つける。
        /// </summary>
        internal static string Root([CallerFilePath] string thisFile = "")
        {
            foreach (var start in new[]
            {
                Path.GetDirectoryName(typeof(TestSource).Assembly.Location),
                Path.GetDirectoryName(thisFile),
            })
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

        /// <summary>ルートからの相対パスでソースを読む。</summary>
        internal static string Read(params string[] relativeParts)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = Root();
            Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
            return File.ReadAllText(Path.Combine(parts));
        }

        /// <summary>ルートからの相対パスでディレクトリを指す。</summary>
        internal static string Dir(params string[] relativeParts)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = Root();
            Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
            return Path.Combine(parts);
        }

        /// <summary>
        /// 例題 (<c>Graphics_r1/Examples</c>) の場所。
        ///
        /// <b>出力先から数えて遡らないこと。</b> 以前は 4 通りの探し方があり、
        /// うち 3 つが <c>"..","..","..",".."</c> の決め打ちだった。出力先を変えて
        /// ビルドすると（アプリ起動中で通常の出力先が使えないときなど）存在しない
        /// パスを返し、例題を使うテストが<b>静かに Inconclusive になる</b>。
        /// 実際に 13 件が失敗、203 件がスキップされたまま「成功」と出たことがある。
        /// <see cref="Root"/> は自分のソース位置からも遡るので、出力先に依らない。
        /// </summary>
        internal static string ExamplesDir() => Dir("Graphics_r1", "Examples");

        /// <summary>
        /// 例題 1 つのパス。無ければ null（呼び出し側で Inconclusive にする用）。
        /// </summary>
        internal static string? ExamplePath(string fileName)
        {
            var path = Path.Combine(ExamplesDir(), fileName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// 例題のファイル一覧。<b>0 件なら落とす。</b>
        ///
        /// 「全例題について〜」という形のテストは、1 件も見つからなくても
        /// ループが 0 回まわるだけで合格する。例題の名前が変わった・場所が変わった、
        /// のどちらでも起きる。件数の下限をここで一度に置いておく。
        /// </summary>
        internal static string[] ExampleFiles(string pattern, int atLeast)
        {
            var files = Directory.GetFiles(ExamplesDir(), pattern);
            AssertScanned(files.Length, atLeast, $"例題 ({pattern})");
            return files;
        }

        /// <summary>
        /// メソッドの本体を、最初の '{' から対応する '}' まで切り出す。
        ///
        /// 文字数で切ると、対象が長ければ後半を見落とし、短ければ次のメソッドが混ざる。
        /// 括弧を数えること。
        /// </summary>
        internal static string MethodBody(string source, string signatureFragment)
        {
            int at = source.IndexOf(signatureFragment, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, $"シグネチャが見つかりません: {signatureFragment}");

            int open = source.IndexOf('{', at);
            Assert.IsTrue(open >= 0, $"本体の開き括弧が見つかりません: {signatureFragment}");

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) return source[open..(i + 1)];
                }
            }

            Assert.Fail($"本体の閉じ括弧が見つかりません: {signatureFragment}");
            return "";
        }

        /// <summary>
        /// 目印から指定した文字数ぶんを切り出す。
        ///
        /// メソッドではない箇所 (文の途中・初期化子の中) を見たいときに使う。
        /// <see cref="MethodBody"/> は括弧を数えるので、メソッドでない目印には使えない
        /// (別のブロックの終わりまで飲み込む)。
        /// 「メソッド本体を文字数で切る」のは誤りだが、「この目印の周りを見る」のは正しい。
        /// どちらなのかが名前で分かるように分けてある。
        /// </summary>
        internal static string Region(string source, string marker, int length)
        {
            int at = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, $"目印が見つかりません: {marker}");
            return source[at..Math.Min(source.Length, at + length)];
        }

        /// <summary>
        /// ガードが空振りしていないことを確かめる。
        ///
        /// ソース走査のガードは「悪い書き方が 0 件であること」を見る作りなので、
        /// <b>検査対象そのものが見つからなくなっても合格する</b>。
        /// 名前を変えた・ファイルを分けた・層ごと移した、のどれでも起きる。
        /// 対象を数えて下限を置くこと。
        /// </summary>
        internal static void AssertScanned(int actual, int atLeast, string what)
        {
            Assert.IsTrue(actual >= atLeast,
                $"{what}: 検査対象が {actual} 件しか見つかりません (最低 {atLeast} 件のはず)。"
                + "名前かファイルの場所が変わって、検査が空振りしている可能性があります");
        }
    }
}

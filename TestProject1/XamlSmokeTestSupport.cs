using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;

namespace TestProject1
{
    /// <summary>
    /// XAML スモークテストの共通処理。
    ///
    /// StaticResource のキー誤り・x:Static の解決失敗・DataTemplate の型不整合は
    /// ビルドでは検出されず、ウィンドウを開いた瞬間に例外になる。
    /// Window の生成には STA スレッドとアプリケーションレベルのリソース辞書が要るため、
    /// その用意をここに集約する。
    /// </summary>
    internal static class XamlSmokeTestSupport
    {
        // スモークテスト用の STA スレッドは<b>プロセスの間ずっと生かしておく</b>。
        //
        // 以前はテストごとに STA スレッドを起こして終了させていた。すると
        // Application.Current（AppDomain に 1 つ）のディスパッチャと MainWindow が
        // 終了済みスレッドを指したまま残り、あとから WPF の描画を行うテスト
        // （計算書の図など）がスレッド親和性の違反でテストホストごと落ちていた。
        //
        // 症状は「単体では 2 秒で通るのに、クラス名の並び順でウィンドウを開くテストが
        // 先に走ったときだけ全体実行が壊れる」という形で出るため、原因に辿り着きにくい。
        // 実アプリと同じく UI スレッドを 1 本だけ生かし続けることで、この問題自体を無くす。
        private static readonly object _staLock = new();
        private static Dispatcher? _staDispatcher;

        private static Dispatcher GetStaDispatcher()
        {
            lock (_staLock)
            {
                if (_staDispatcher != null) return _staDispatcher;

                var ready = new ManualResetEventSlim();
                var thread = new Thread(() =>
                {
                    EnsureApplicationResources();
                    _staDispatcher = Dispatcher.CurrentDispatcher;

                    // このスレッドに後から届く処理の例外を拾う。
                    //
                    // ここで作った Application が Application.Current になるので、
                    // 本体が Application.Current?.Dispatcher.BeginInvoke(...) で投げた処理は
                    // すべてこのスレッドで走る。投げ放しなので、例外は誰も受け取らない。
                    // 何もしないと <b>テストホストごと落ちる</b>。
                    //
                    // 実際に 1 日で 3 回、全体実行が途中で止まった (1,342 / 1,336 件など)。
                    // dotnet test はそれでも「成功!」と表示するので、
                    // tools/run-tests.ps1 が件数で気づくまで分からなかった。
                    // 一度は Unhandled exception. System.ArgumentOutOfRangeException が見えた。
                    //
                    // 落とさずに記録する。実行は最後まで進み、何が起きたかは残る。
                    _staDispatcher.UnhandledException += (_, e) =>
                    {
                        RecordStrayException(e.Exception);
                        e.Handled = true;
                    };

                    ready.Set();
                    Dispatcher.Run();   // テスト実行の間ずっとメッセージを処理し続ける
                })
                {
                    // プロセス終了を妨げないようバックグラウンドスレッドにする
                    IsBackground = true,
                    Name = "XamlSmokeTest STA",
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                ready.Wait();
                return _staDispatcher!;
            }
        }

        /// <summary>
        /// 共有 STA スレッドに後から届いた処理が投げた例外。<b>テストが直接呼んだものではない。</b>
        ///
        /// 本体が投げ放し (<c>BeginInvoke</c>) にした処理が、テストが次へ進んだあとに
        /// 走って落ちるとここに入る。解析の進捗更新のように、走り終えた頃に
        /// 画面へ書き戻すものが疑わしい。
        /// </summary>
        public static IReadOnlyList<string> StrayExceptions
        {
            get { lock (_strayLock) return _strayExceptions.ToArray(); }
        }
        private static readonly object _strayLock = new();
        private static readonly List<string> _strayExceptions = [];

        /// <summary>
        /// 拾った例外を控える。<b>ファイルにも書く。</b>
        /// あとで走るテストが見るだけだと、それより後に起きたものを取りこぼす。
        /// 実行が終わったあとに tools/run-tests.ps1 がファイルの有無で気づく。
        /// </summary>
        private static void RecordStrayException(Exception ex)
        {
            string text = $"{DateTime.Now:HH:mm:ss}  {ex.GetType().Name}: {ex.Message}"
                        + Environment.NewLine + ex.StackTrace + Environment.NewLine;
            lock (_strayLock)
            {
                _strayExceptions.Add(text);
                try
                {
                    var dir = Path.Combine(TestSource.Root(), "TestProject1", "TestResults");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "dispatcher-exceptions.log"), text);
                }
                catch { /* 記録に失敗しても実行は続ける */ }
            }
        }

        /// <summary>
        /// 共有の STA スレッド上で <paramref name="action"/> を実行し、送出された例外を返す。
        /// 時間内に終わらなければ <c>timedOut</c> を true にする。
        /// </summary>
        public static Exception? RunOnStaThread(Action action, out bool timedOut, int timeoutSeconds = 60)
        {
            Exception? captured = null;
            var op = GetStaDispatcher().InvokeAsync(() =>
            {
                try { action(); }
                catch (Exception ex) { captured = ex; }
            });

            timedOut = !op.Task.Wait(TimeSpan.FromSeconds(timeoutSeconds));
            return captured;
        }

        /// <summary>
        /// App.xaml と同じアプリケーションレベルのリソース辞書を用意する。
        /// これがないと CustomDataGridCellStyle 等の StaticResource が解決できず、
        /// 変更と無関係な理由でパースに失敗する。
        /// </summary>
        public static void EnsureApplicationResources()
        {
            var app = System.Windows.Application.Current ?? new System.Windows.Application();

            // Application は AppDomain に 1 つで、既定の ShutdownMode は OnLastWindowClose。
            // スモークテストが最後のウィンドウを閉じるとシャットダウンが要求され、
            // ディスパッチャが動いていると実際に実行されてしまう。以後は
            // 「アプリケーション オブジェクトはシャットダウンされています」で
            // XAML の読込すらできなくなり、WPF の描画を行うテストはホストごと落ちる。
            app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;



            string[] dictionaries =
            [
                "pack://application:,,,/Fluent;component/Themes/Themes/Light.Blue.xaml",
                "pack://application:,,,/Fluent;component/Themes/Styles.xaml",
                "pack://application:,,,/PileDesign;component/Styles.xaml",
                "pack://application:,,,/PileDesign;component/Themes/AvalonDockTabStyles.xaml",
            ];

            foreach (string source in dictionaries)
            {
                var uri = new Uri(source, UriKind.Absolute);
                bool alreadyLoaded = false;
                foreach (var d in app.Resources.MergedDictionaries)
                {
                    if (d.Source == uri) { alreadyLoaded = true; break; }
                }
                if (!alreadyLoaded)
                {
                    app.Resources.MergedDictionaries.Add(
                        new System.Windows.ResourceDictionary { Source = uri });
                }
            }
        }
    }
}

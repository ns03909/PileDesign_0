using System;
using System.Threading;

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
        /// <summary>
        /// STA スレッド上で <paramref name="action"/> を実行し、送出された例外を返す。
        /// 時間内に終わらなければ <c>timedOut</c> を true にする。
        /// </summary>
        public static Exception? RunOnStaThread(Action action, out bool timedOut, int timeoutSeconds = 60)
        {
            Exception? captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    EnsureApplicationResources();
                    action();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            timedOut = !thread.Join(TimeSpan.FromSeconds(timeoutSeconds));
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

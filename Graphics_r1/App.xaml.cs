//using PileDesign.Models.InputData;
//using System.Windows;

//namespace PileDesign
//{
//    /// <summary>
//    /// App.xaml の相互作用ロジック
//    /// </summary>
//    public partial class App : Application
//    {
//        public static InputModel InputModel { get; set; }

//        public App()
//        {
//            InputModel = new InputModel();
//        }

//    }
//}

using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PileDesign
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        public static InputModel InputModel { get; set; }

        //public App()
        //{
        //    InputModel = new InputModel();

        //    // グローバルな未処理例外ハンドラを登録
        //    this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        //}

        public App()
        {
            try
            {
                // アプリ起動確認ログ
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup.log");
                File.AppendAllText(path, $"[{DateTime.Now}] App constructor start\n");
                // MainWindowViewModelを先に生成
                var mainWindowViewModel = new MainWindowViewModel();
                InputModel = new InputModel();
                InputModel.SetMainWindowViewModel(mainWindowViewModel);

                this.DispatcherUnhandledException += App_DispatcherUnhandledException;

                File.AppendAllText(path, $"[{DateTime.Now}] App constructor end\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText("startup_error.log", $"[{DateTime.Now}] 例外: {ex}\n");
                MessageBox.Show($"アプリ起動時に致命的なエラーが発生しました。\n{ex.Message}", "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                ShowAndLogException(ex);
                Environment.Exit(1);
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // ★ IME/TextStore 関連の COMException は無視して続行
            // HResult 0x80040206 = "現在、レイアウトは使用できません"
            if (e.Exception is System.Runtime.InteropServices.COMException comEx 
                && comEx.HResult == unchecked((int)0x80040206))
            {
                System.Diagnostics.Debug.WriteLine($"[App] IME/TextStore COMException ignored: {comEx.Message}");
                e.Handled = true;
                return; // アプリを終了せずに続行
            }

            ShowAndLogException(e.Exception);
            e.Handled = true;
            Environment.Exit(1);
        }

        private void ShowAndLogException(Exception ex)
        {
            // ログファイルに出力
            try
            {
                File.WriteAllText("error.log", ex.ToString());
            }
            catch { /* ログ出力失敗時は無視 */ }

            // ユーザーに通知
            MessageBox.Show(
                ex.ToString(),
                "エラーが発生しました",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }
}

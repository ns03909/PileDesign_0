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
using PileDesign.Views;
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
                // アプリ起動確認ログ（AppData/Local/PileDesign/Logs/に書き込み）
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PileDesign", "Logs");
                Directory.CreateDirectory(logDir);
                string path = Path.Combine(logDir, "startup.log");
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
                var errorLogDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PileDesign", "Logs");
                try { Directory.CreateDirectory(errorLogDir); } catch { }
                File.AppendAllText(Path.Combine(errorLogDir, "startup_error.log"), $"[{DateTime.Now}] 例外: {ex}\n");
                MessageBox.Show($"アプリ起動時に致命的なエラーが発生しました。\n{ex.Message}", "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);

                // WelcomeDialog閉時にアプリが終了しないようにする
                this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // ウェルカムダイアログ表示
                WelcomeDialogResult welcomeResult = WelcomeDialogResult.None;

                // TODO: サンプルプロジェクト実装後に有効化する
                //if (PileDesign.Properties.Settings.Default.ShowWelcomeDialog)
                //{
                //    var welcomeVm = new WelcomeDialogViewModel();
                //    var welcomeDialog = new WelcomeDialog { DataContext = welcomeVm };
                //    welcomeDialog.ShowDialog();
                //    welcomeResult = welcomeVm.Result;
                //}

                // MainWindowを手動で生成・表示（StartupUri削除のため）
                var mainWindow = new MainWindow();
                this.MainWindow = mainWindow;
                mainWindow.Show();

                // MainWindow表示後、通常のシャットダウンモードに戻す
                this.ShutdownMode = ShutdownMode.OnMainWindowClose;

                // ウェルカムダイアログの結果に応じてアクションを実行
                if (mainWindow.DataContext is MainWindowViewModel vm)
                {
                    switch (welcomeResult)
                    {
                        case WelcomeDialogResult.OpenExisting:
                            vm.OpenInputModelFileSimple();
                            break;
                        case WelcomeDialogResult.OpenSample:
                            string examplePath = Path.Combine(
                                AppDomain.CurrentDomain.BaseDirectory, "Examples", "Example3_2.json");
                            if (File.Exists(examplePath))
                                vm.TryLoadInputModelFileUsingInputModelLoader(examplePath);
                            break;
                        case WelcomeDialogResult.NewProject:
                        case WelcomeDialogResult.None:
                        default:
                            break;
                    }
                }
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
                e.Handled = true;
                return; // アプリを終了せずに続行
            }

            ShowAndLogException(e.Exception);
            e.Handled = true;
            Environment.Exit(1);
        }

        private void ShowAndLogException(Exception ex)
        {
            // ログファイルに出力（AppData/Local/PileDesign/Logs/）
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PileDesign", "Logs");
                Directory.CreateDirectory(logDir);
                File.WriteAllText(Path.Combine(logDir, "error.log"), ex.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                // ファイル書き込み権限がない場合は無視（デバッグ出力のみ）
            }
            catch (IOException)
            {
                // ファイルI/O例外は無視（デバッグ出力のみ）
            }

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

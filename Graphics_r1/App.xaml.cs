using PileDesign.Common.Logging;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using PileDesign.Views;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PileDesign
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        public static InputModel InputModel { get; set; } = null!;

        /// <summary>
        /// 致命的例外時に緊急 AutoSave を呼び出すための MainWindowViewModel 参照。
        /// </summary>
        public static MainWindowViewModel? CurrentMainViewModel { get; private set; }

        public App()
        {
            // ロギングを最優先で初期化 (それ自身が失敗しても続行)
            AppLog.Initialize();

            // 全 3 ソースの未捕捉例外をフック
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += AppDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            try
            {
                Log.Information("App constructor start");

                var mainWindowViewModel = new MainWindowViewModel();
                CurrentMainViewModel = mainWindowViewModel;
                InputModel = new InputModel();
                InputModel.SetMainWindowViewModel(mainWindowViewModel);

                Log.Information("App constructor complete");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "App constructor failed");
                MessageBox.Show(
                    $"アプリ起動時に致命的なエラーが発生しました。\n{ex.Message}\n\nログ: {AppLog.LogDirectory}",
                    "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                AppLog.Close();
                Environment.Exit(1);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);

                var mainWindow = new MainWindow();
                this.MainWindow = mainWindow;
                mainWindow.Show();

                Log.Information("MainWindow shown");
            }
            catch (Exception ex)
            {
                HandleFatalException(ex, source: "OnStartup");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("App exiting (code={Code})", e.ApplicationExitCode);
            AppLog.Close();
            base.OnExit(e);
        }

        // --- 未捕捉例外ハンドラ -----------------------------------------------

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // IME/TextStore 関連の COMException は無視して続行 (HResult 0x80040206)
            if (e.Exception is System.Runtime.InteropServices.COMException comEx
                && comEx.HResult == unchecked((int)0x80040206))
            {
                Log.Debug("Suppressed benign IME COMException (0x80040206)");
                e.Handled = true;
                return;
            }

            HandleFatalException(e.Exception, source: "DispatcherUnhandledException");
            e.Handled = true;
        }

        private void AppDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception ?? new Exception($"Non-Exception throw: {e.ExceptionObject}");
            HandleFatalException(ex, source: "AppDomain.UnhandledException");
            // IsTerminating の場合は CLR が直後にプロセスを落とすので Environment.Exit は不要
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            HandleFatalException(e.Exception, source: "TaskScheduler.UnobservedTaskException");
            e.SetObserved(); // CLR にプロセスを落とさせない
        }

        // --- 致命的例外の共通処理 ---------------------------------------------

        private bool _handlingFatal = false;

        /// <summary>
        /// 全ハンドラから集約される致命的例外処理:
        ///   1) ログに Fatal で記録
        ///   2) 緊急 AutoSave を試行 (作業中データの退避)
        ///   3) ユーザーに退避先とログ場所を含むダイアログを表示
        ///   4) ログをフラッシュしてプロセス終了
        /// 多重呼び出し防止のためフラグでガードする。
        /// </summary>
        private void HandleFatalException(Exception ex, string source)
        {
            if (_handlingFatal)
            {
                // 入れ子の致命的例外。ログだけ残して握りつぶす。
                try { Log.Fatal(ex, "Recursive fatal exception from {Source}", source); } catch { }
                return;
            }
            _handlingFatal = true;

            string emergencySavePath = "(緊急保存に失敗)";
            string? emergencyPathOrNull = null;
            try
            {
                Log.Fatal(ex, "Fatal exception from {Source}", source);

                emergencyPathOrNull = CurrentMainViewModel?.TryEmergencyAutoSave();
                if (!string.IsNullOrEmpty(emergencyPathOrNull))
                {
                    emergencySavePath = emergencyPathOrNull;
                    Log.Information("Emergency AutoSave saved to {Path}", emergencyPathOrNull);
                }
            }
            catch (Exception inner)
            {
                try { Log.Error(inner, "Emergency AutoSave path retrieval failed"); } catch { }
            }

            // クラッシュレポート zip を作成 (失敗しても続行)
            string? crashReportPath = null;
            try
            {
                crashReportPath = CrashReporter.TryCreateReport(ex, source, emergencyPathOrNull);
            }
            catch (Exception inner)
            {
                try { Log.Error(inner, "CrashReporter invocation failed"); } catch { }
            }

            try
            {
                var reportLine = string.IsNullOrEmpty(crashReportPath)
                    ? "クラッシュレポート: (作成に失敗)"
                    : $"クラッシュレポート (開発元への共有用):\n  {crashReportPath}";

                var msg =
                    "予期しないエラーが発生しました。アプリケーションを終了します。\n" +
                    "━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"エラー: {ex.GetType().Name}\n" +
                    $"内容: {ex.Message}\n\n" +
                    $"作業中のデータを退避しました:\n  {emergencySavePath}\n\n" +
                    $"{reportLine}\n\n" +
                    $"ログ: {AppLog.LogDirectory}\n\n" +
                    "「はい」を押すとクラッシュレポートのフォルダを開きます。\n" +
                    "次回起動時に「最近開いたファイル」または上記の自動保存ファイルから復元できます。";

                var result = MessageBox.Show(msg, "致命的エラー",
                    MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No);

                if (result == MessageBoxResult.Yes && !string.IsNullOrEmpty(crashReportPath))
                {
                    CrashReporter.TryOpenInExplorer(crashReportPath);
                }
            }
            catch
            {
                // ダイアログ表示すら失敗するなら諦めて落ちる
            }

            AppLog.Close();
            Environment.Exit(1);
        }
    }
}

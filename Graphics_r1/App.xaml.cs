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
using PileDesign.Services;

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

        /// <summary>
        /// コマンドライン引数 `--open &lt;path&gt;` または `&lt;path&gt;` (位置引数) で指定された
        /// プロジェクトファイルパス。OnStartup でセットされ、MainWindow.Loaded で自動ロードされる。
        /// </summary>
        public static string? StartupFilePath { get; private set; }

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
                MessageService.Show(
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

                // コマンドライン引数の解析:
                //   PileDesign.exe --open project.pdj
                //   PileDesign.exe project.pdj            (位置引数、ファイル関連付け用)
                //   .json (旧形式) も同様に受け付ける
                StartupFilePath = ParseStartupFilePath(e.Args);
                if (!string.IsNullOrEmpty(StartupFilePath))
                {
                    Log.Information("Startup file requested: {Path}", StartupFilePath);
                }

                // .pdj 関連付けの自動パス追従:
                // 既に登録済みで、かつ登録パスが現在の exe と違う場合 (= exe を移動 or 別フォルダに更新)
                // のみサイレントに再登録する。未登録なら何もしない (勝手な登録は避ける)。
                try
                {
                    if (FileAssociationService.IsRegistered() &&
                        !FileAssociationService.IsRegisteredPathCurrent())
                    {
                        if (FileAssociationService.Register())
                        {
                            Log.Information("[FileAssociation] Path auto-refreshed to current exe");
                        }
                    }
                }
                catch (Exception assocEx)
                {
                    // 関連付けの失敗で起動を止めない
                    Log.Warning(assocEx, "[FileAssociation] Auto-refresh failed (non-fatal)");
                }

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

        /// <summary>
        /// 起動時引数からファイルパスを抽出する。`--open path` または最初の位置引数を採用。
        /// 存在しないファイルは null を返す (エラーダイアログは MainWindow 側に任せる)。
        /// </summary>
        private static string? ParseStartupFilePath(string[] args)
        {
            if (args == null || args.Length == 0) return null;

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (string.Equals(a, "--open", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(a, "-o", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        var p = args[i + 1];
                        if (File.Exists(p)) return p;
                    }
                    continue;
                }
                // 位置引数: スイッチ系 (--, -) でない最初の引数がパス
                if (!a.StartsWith("-") && File.Exists(a)) return a;
            }
            return null;
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
            if (IsBenignSuppressible(e.Exception, out string? reason))
            {
                Log.Debug("Suppressed benign exception ({Reason}): {Type} HResult=0x{HR:X8}",
                    reason, e.Exception.GetType().Name,
                    e.Exception is System.Runtime.InteropServices.COMException c ? c.HResult : 0);
                e.Handled = true;
                return;
            }

            // UI スレッドの未捕捉例外: アプリ状態は破損していない可能性があるため、続行可で扱う。
            HandleFatalException(e.Exception, source: "DispatcherUnhandledException", canContinue: true);
            e.Handled = true;
        }

        private void AppDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception ?? new Exception($"Non-Exception throw: {e.ExceptionObject}");
            if (IsBenignSuppressible(ex, out string? reason))
            {
                Log.Debug("Suppressed benign AppDomain exception ({Reason}): {Type}", reason, ex.GetType().Name);
                return;
            }
            // AppDomain は CLR がプロセスを落とす経路なので続行不可。
            HandleFatalException(ex, source: "AppDomain.UnhandledException", canContinue: false);
            // IsTerminating の場合は CLR が直後にプロセスを落とすので Environment.Exit は不要
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            // AggregateException の中身が全て良性なら握りつぶす
            bool allBenign = true;
            string? firstReason = null;
            foreach (var inner in e.Exception.Flatten().InnerExceptions)
            {
                if (!IsBenignSuppressible(inner, out string? r))
                {
                    allBenign = false;
                    break;
                }
                firstReason ??= r;
            }
            if (allBenign)
            {
                Log.Debug("Suppressed benign unobserved task exception ({Reason})", firstReason ?? "unknown");
                e.SetObserved();
                return;
            }

            HandleFatalException(e.Exception, source: "TaskScheduler.UnobservedTaskException", canContinue: false);
            e.SetObserved(); // CLR にプロセスを落とさせない
        }

        /// <summary>
        /// アプリの動作に支障のない既知の良性例外なら true。
        /// 該当しない例外はログ Fatal + 致命的処理へ進める。
        /// </summary>
        private static bool IsBenignSuppressible(Exception ex, out string? reason)
        {
            reason = null;
            if (ex == null) return false;

            // COMException: IME/TextStore / WebView2 シャットダウン時の中断系
            if (ex is System.Runtime.InteropServices.COMException com)
            {
                int hr = com.HResult;
                switch (unchecked((uint)hr))
                {
                    case 0x80040206: reason = "TS_E_NOLOCK (IME)";       return true;
                    case 0x80040207: reason = "TS_E_NOSELECTION (IME)";  return true;
                    case 0x80004004: reason = "E_ABORT (WebView2 等)";   return true;
                }
            }

            // ユーザー/シャットダウン要因のキャンセル例外は無害。
            // 通常はキャンセル元で観測されるべきだが、observation 漏れで globalに来てもアプリは落とさない。
            if (ex is OperationCanceledException)
            {
                reason = "OperationCanceled (タスクキャンセル)";
                return true;
            }

            return false;
        }

        // --- 致命的例外の共通処理 ---------------------------------------------

        private bool _handlingFatal = false;

        /// <summary>
        /// 全ハンドラから集約される致命的例外処理:
        ///   1) ログに Fatal で記録
        ///   2) 緊急 AutoSave を試行 (作業中データの退避)
        ///   3) クラッシュレポート zip を作成
        ///   4) ユーザーに退避先/レポート/ログ場所を含むダイアログを表示
        ///   5) canContinue=true かつユーザーが続行を選んだ場合のみ続行、それ以外は終了
        /// 多重呼び出し防止のためフラグでガードする。
        /// </summary>
        /// <param name="canContinue">UI スレッド未捕捉のように、アプリ状態が破損していない見込みがあり、
        /// ユーザーに続行を提示してよい場合に true。AppDomain / 未観測 Task のように
        /// CLR がプロセスを落とす経路、または状態不明な経路では false。</param>
        private void HandleFatalException(Exception ex, string source, bool canContinue = false)
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

                string title;
                string actionPrompt;
                if (canContinue)
                {
                    title = "予期しないエラー (続行可能)";
                    actionPrompt =
                        "「はい」を押すと自己責任で続行します（アプリ状態が破損している可能性があります。\n" +
                        "速やかに作業内容を別名保存して再起動することを推奨します）。\n" +
                        "「いいえ」を押すとアプリを終了します。\n" +
                        "ログ・クラッシュレポートは上記の場所に保存済みです。";
                }
                else
                {
                    title = "致命的エラー";
                    actionPrompt =
                        "「はい」を押すとクラッシュレポートのフォルダを開きます。\n" +
                        "次回起動時に「最近開いたファイル」または上記の自動保存ファイルから復元できます。";
                }

                var msg =
                    (canContinue
                        ? "予期しないエラーが発生しました。続行を試みるかアプリを終了するか選択してください。\n"
                        : "予期しないエラーが発生しました。アプリケーションを終了します。\n") +
                    "━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"エラー: {ex.GetType().Name}\n" +
                    $"内容: {ex.Message}\n\n" +
                    $"作業中のデータを退避しました:\n  {emergencySavePath}\n\n" +
                    $"{reportLine}\n\n" +
                    $"ログ: {AppLog.LogDirectory}\n\n" +
                    actionPrompt;

                var result = MessageService.Show(msg, title,
                    MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No);

                if (canContinue)
                {
                    if (result == MessageBoxResult.Yes)
                    {
                        Log.Warning("User chose to CONTINUE after fatal exception from {Source}", source);
                        _handlingFatal = false; // 次の例外を再度処理できるよう解除
                        return;                  // 終了せず続行
                    }
                    // else: そのまま終了処理へ
                }
                else
                {
                    if (result == MessageBoxResult.Yes && !string.IsNullOrEmpty(crashReportPath))
                    {
                        CrashReporter.TryOpenInExplorer(crashReportPath);
                    }
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

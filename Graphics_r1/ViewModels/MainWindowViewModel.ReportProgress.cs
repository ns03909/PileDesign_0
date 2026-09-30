using PileDesign.Models;
using PileDesign.Services;
using PileDesign.Views;
using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PileDesign.ViewModels;

public partial class MainWindowViewModel
{
    /// <summary>計算書の作成を中止するかの確認 (進み具合の窓の「中止」)。</summary>
    internal const string ReportCancelConfirmation =
        "計算書の作成を中止しますか？\n\n作りかけの計算書は保存しません（同じ名前で前に出力した計算書はそのまま残ります）。";

    /// <summary>
    /// 計算書を進み具合の窓を出して作る。作成は画面のスレッドのまま行い、区切りごとに画面のメッセージを回して
    /// 「中止」を受け付ける (<see cref="Output.ReportRunControl"/>)。
    ///
    /// <para>以前は砂時計を出して画面のスレッドで一気に作っていたので、大規模なモデルでは数十秒〜数分のあいだ
    /// 画面が止まり、止める手段も無かった。作成を別のスレッドへ移さないのは、解析モデルの断面の遅延計算を
    /// 画面の描画と同時に読ませないため (並列に読むと耐力が壊れたことがある) と、モデル図の写し・図の描画が
    /// 画面のスレッドのものだから。窓はモーダルなので、作成中にメイン画面の入力は受け付けない。</para>
    /// </summary>
    /// <returns>作り終えたら true。中止したら false (出力先のファイルは作らず、変えない)。</returns>
    internal bool RunCancellableReport(Action<Output.ReportRunControl> build)
    {
        var app = Application.Current;
        if (MessageService.IsUnattended || app == null || !app.Dispatcher.CheckAccess())
        {
            build(new Output.ReportRunControl());
            return true;
        }

        using var cancellation = new CancellationTokenSource();
        var owner = app.MainWindow?.IsVisible == true ? app.MainWindow : null;
        var window = new ProgressWindow(cancellation, ReportCancelConfirmation) { Title = "計算書の作成", Owner = owner, ShowsRemainingTime = false };
        var startedAt = DateTime.Now;
        window.UpdateProgress(new AnalysisProgress { CurrentStep = "準備しています...", TotalSteps = Output.WordDocument.ReportStepCount, StartTime = startedAt });

        Exception? failure = null;
        bool completed = false;
        bool cancelled = false;
        // × で閉じようとしたら中止の求めとして扱う (作成を抜けてから閉じる)
        window.Closing += (_, e) =>
        {
            if (!completed)
            {
                cancellation.Cancel();
                e.Cancel = true;
            }
        };
        window.Loaded += async (_, _) =>
        {
            // 窓を描き終えてから作り始める
            await Dispatcher.Yield(DispatcherPriority.Background);
            try
            {
                build(new Output.ReportRunControl
                {
                    Token = cancellation.Token,
                    PumpMessages = PumpUiMessages,
                    Report = p => window.UpdateProgress(new AnalysisProgress
                    {
                        CurrentStep = p.Items > 0 ? $"{p.Step}（見出し・図表 {p.Items} 個）" : p.Step,
                        CurrentStepNumber = p.StepNumber,
                        TotalSteps = p.TotalSteps,
                        Percentage = 100.0 * p.StepNumber / Math.Max(1, p.TotalSteps),
                        StartTime = startedAt,
                    }),
                });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
            catch (Exception ex) { failure = ex; }
            finally
            {
                // 作り終えた・止めた・失敗した、どの場合も窓は閉じてよい (ProgressWindow は中止の求めがあれば閉じられる)
                completed = true;
                cancellation.Cancel();
                window.Close();
            }
        };
        window.ShowDialog();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return !cancelled;
    }

    /// <summary>
    /// 画面のメッセージを回す (入力と描画を処理してから戻る)。待ち行列の優先度 Background の処理が走るところで
    /// 抜けるので、それより優先度の高い入力・描画は済んでいる。
    /// </summary>
    private static void PumpUiMessages()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}

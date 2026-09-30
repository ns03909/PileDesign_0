using System;
using System.Threading;

namespace PileDesign.Output
{
    /// <summary>計算書の作成の進み具合 (いまの段階と、ここまでに作った図表・見出しの数)。</summary>
    public sealed record ReportProgress(string Step, int StepNumber, int TotalSteps, int Items);

    /// <summary>
    /// 計算書の作成を途中で止めるための受け口。作成は区切り (段階・見出し・図表) ごとに
    /// <see cref="Token"/> を確かめ、止められていれば <see cref="OperationCanceledException"/> で抜ける。
    ///
    /// <para>計算書は画面のスレッドで組み立てる (モデル図の写しや図の描画が画面のスレッドのもので、
    /// 解析モデルの断面の遅延計算を画面の描画と同時に読ませないため)。そのままでは作成中に画面が止まり、
    /// 取り消しのボタンも押せないので、区切りで <see cref="PumpMessages"/> を呼んで画面のメッセージを回す。</para>
    /// </summary>
    public sealed class ReportRunControl
    {
        public CancellationToken Token { get; init; }

        /// <summary>画面のメッセージを回す (取り消しの押下・進み具合の表示を処理する)。null なら回さない。</summary>
        public Action? PumpMessages { get; init; }

        /// <summary>進み具合を知らせる。null なら知らせない。</summary>
        public Action<ReportProgress>? Report { get; init; }

        /// <summary>メッセージを回す間隔の下限 (区切りのたびに回すと、回す手間で作成が遅くなる)。</summary>
        public TimeSpan PumpInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    }
}

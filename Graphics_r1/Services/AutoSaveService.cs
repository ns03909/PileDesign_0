using PileDesign.Common.Logging;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace PileDesign.Services
{
    /// <summary>
    /// 自動保存サービス
    ///
    /// 機能:
    /// - 定期的にプロジェクトデータをバックアップフォルダに保存
    /// - 古い自動保存ファイルの自動削除
    /// - クラッシュ時の復元用データ管理
    /// </summary>
    public class AutoSaveService
    {
        private readonly FileOperationService _fileOperationService;
        private readonly DispatcherTimer _autoSaveTimer;
        private string? _currentFilePath;
        private InputModel? _currentInputModel;
        private AnaModel? _currentModel;
        private IList<FEM.VerticalBeamCaseResult>? _verticalBeamCaseResults;

        /// <summary>
        /// 保存時に「現在のライブ状態」(InputModel / AnaModel / VerticalBeamCaseResults) を取得するプロバイダ。
        /// 設定されていれば Start 時にキャプチャした固定参照より優先される。
        /// これにより (a) 解析完了後の最新結果、(b) Undo/Redo 後の最新 InputModel、
        /// (c)「解析結果も保存する」チェックボックスの状態、を保存時点で正しく反映できる。
        /// (Start 引数の参照は呼び出し時点で固定されるため、解析後に古くなる問題を回避する)
        ///
        /// <b>元ファイルのパスも入力と一組で返す。</b>以前はパスだけ Start 時の控え
        /// (<c>_currentFilePath</c>) を、しかもバックグラウンドの書き出しの時点で読んでいた。
        /// ファイルを開く処理は途中で待つので、入力は新しいのにパスは古い、という瞬間がある。
        /// そこで保存すると、自動保存ファイルの名前と中に記録する元パスが別のプロジェクトを指し、
        /// 復元したあとの上書き保存が別のファイルに向かう。
        /// </summary>
        public Func<(InputModel? input, string? filePath, AnaModel? ana, IList<FEM.VerticalBeamCaseResult>? vbcr)>? LiveStateProvider { get; set; }

        /// <summary>
        /// このアプリの起動ごとの印。自動保存・緊急保存のファイルの中に記録する
        /// (<c>ProjectData.AutoSaveSessionId</c>)。起動時の復元の確認に答えたとき、
        /// 同じ作業の古い自動保存だけを見送るために使う (名前の無いプロジェクトは元ファイルで結べないため)。
        /// </summary>
        internal string SessionId { get; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 自動保存フォルダのパス（AppData/Local/PileDesign/AutoSave/）
        /// </summary>
        public string AutoSaveFolder { get; }

        /// <summary>
        /// 自動保存の間隔（分）
        /// </summary>
        public int AutoSaveIntervalMinutes { get; set; } = 3;

        /// <summary>
        /// 自動保存ファイルの保持期間（日）
        /// </summary>
        public int RetentionDays { get; set; } = 7;

        /// <summary>
        /// 自動保存が有効かどうか
        /// </summary>
        public bool IsEnabled => _autoSaveTimer.IsEnabled;

        /// <summary>
        /// 最後の自動保存時刻
        /// </summary>
        public DateTime? LastAutoSaveTime { get; private set; }

        /// <summary>
        /// 連続失敗回数。成功で 0 にリセット。
        /// UI 側はこの値を使って閾値超過時に通知エスカレーションできる。
        /// </summary>
        public int ConsecutiveFailures
        {
            get => System.Threading.Volatile.Read(ref _consecutiveFailures);
            private set => System.Threading.Volatile.Write(ref _consecutiveFailures, value);
        }
        private int _consecutiveFailures;

        /// <summary>
        /// 自動保存時のイベント
        /// </summary>
        public event EventHandler<AutoSaveEventArgs>? AutoSaveCompleted;

        public AutoSaveService(FileOperationService fileOperationService)
        {
            _fileOperationService = fileOperationService;

            // 自動保存フォルダのパスを設定
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            AutoSaveFolder = Path.Combine(appDataPath, "PileDesign", "AutoSave");

            // フォルダが存在しなければ作成
            Directory.CreateDirectory(AutoSaveFolder);

            // タイマー設定
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(AutoSaveIntervalMinutes)
            };
            _autoSaveTimer.Tick += OnAutoSaveTimer;
        }

        /// <summary>
        /// 自動保存を開始
        /// </summary>
        /// <param name="currentFilePath">現在のファイルパス</param>
        /// <param name="inputModel">InputModel</param>
        /// <param name="anaModel">AnaModel</param>
        public void Start(string? currentFilePath, InputModel inputModel, AnaModel? anaModel,
            IList<FEM.VerticalBeamCaseResult>? verticalBeamCaseResults = null)
        {
            _currentFilePath = currentFilePath;
            _currentInputModel = inputModel;
            _currentModel = anaModel;
            _verticalBeamCaseResults = verticalBeamCaseResults;

            // 既存の古いファイルをクリーンアップ
            CleanupOldAutoSaveFiles();

            // タイマー間隔を更新
            _autoSaveTimer.Interval = TimeSpan.FromMinutes(AutoSaveIntervalMinutes);

            if (!_autoSaveTimer.IsEnabled)
            {
                _autoSaveTimer.Start();
            }
        }

        /// <summary>
        /// 自動保存を停止
        /// </summary>
        public void Stop()
        {
            _autoSaveTimer.Stop();
            _currentFilePath = null;
            _currentInputModel = null;
            _currentModel = null;
            _verticalBeamCaseResults = null;
            ConsecutiveFailures = 0;
        }

        /// <summary>
        /// タイマーイベント
        /// </summary>
        private async void OnAutoSaveTimer(object? sender, EventArgs e)
        {
            // モーダルの入力ウィンドウが開いているあいだは見送る。
            //
            // DispatcherTimer.Tick は ShowDialog の入れ子ディスパッチャでも発火するので、
            // 「入力ウィンドウはモーダルだから保存とぶつからない」という前提は
            // 自動保存には効かない。以前は写した器をバックグラウンドで直列化していたので、
            // 土層表や区間表のような<b>入れ子の表</b> (写しでは守れない。要素を複製すると
            // 保存ファイルの $ref の畳まれ方が変わる) を辿っている最中の編集で列挙が壊れた。
            // いまは直列化も画面のスレッドで済ませる (PrepareState) のでその危険は無いが、
            // 入力ウィンドウは実体を直接書き換え、キャンセルで元に戻す。確定していない
            // 途中の値を復元の元にしないため、見送りは残す。次の Tick で保存される。
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal)
            {
                Log.Debug("AutoSave skipped: a modal window is open");
                return;
            }

            // 保存する中身は<b>画面のスレッドで</b>確定させる (写しから直列化まで)。
            // Task.Run の中で取ると、写す処理そのものが元のコレクションを列挙するので、
            // そのあいだの編集で列挙が壊れる。直列化をバックグラウンドで行うと、保存の途中で
            // 書き換えた値が、ファイルの一部にだけ入る (どの時点にも無かった入力が残る)。
            PreparedState prepared;
            try
            {
                var p = PrepareState();
                if (p == null) return;   // 保存対象が無い
                prepared = p.Value;
            }
            catch (Exception ex)
            {
                // 写しで失敗したときも、書き出しの失敗と同じく数えて知らせる。
                // 以前はログだけで終わり、毎回ここで落ちても連続失敗の回数も画面の表示も動かなかった
                // (ステータスバーには最後に成功した時刻が出たまま)。
                ReportFailure(ex, "snapshot");
                return;
            }

            // 書き出しはバックグラウンドで実行して UI スレッドをブロックしない
            // (DispatcherTimer.Tick は UI スレッドで発火するため明示的に Task.Run へ逃がす)
            try
            {
                await Task.Run(() => PerformAutoSave(prepared));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AutoSave timer task failed");
            }
        }

        /// <summary>
        /// 保存にかける状態。<see cref="Save"/> は画面のスレッドで直列化まで済ませた中身。
        /// <see cref="SourceFilePath"/> は中身と同じ瞬間に取った元ファイルのパス
        /// (名前の無いプロジェクトでは null)。書き出しの時点で改めて読まないこと。
        /// </summary>
        private readonly record struct PreparedState(
            FileOperationService.PreparedSave Save, string? SourceFilePath);

        /// <summary>
        /// 保存する状態を解決し、中身を確定させる (写しから直列化まで)。<b>画面のスレッドから呼ぶこと。</b>
        ///
        /// 写しと直列化は手動保存と同じ <see cref="FileOperationService.PrepareSave"/> に任せる。
        /// 自前で <c>InputModel.SnapshotForSaving()</c> を呼ぶと、解析結果と同じ実体を
        /// 指している場面でも写してしまい、保存ファイルの $ref の畳まれ方が変わる。
        /// 元ファイルのパスは復元後の保存先としてファイルの中に残すので、中身と同じ瞬間に取る。
        /// </summary>
        private PreparedState? PrepareState()
        {
            var (input, filePath, ana, vbcr) = ResolveState();
            if (input == null) return null;

            // NaN 検査も直列化と同じ時点でここで行う (書く JSON と同じ値を検査する)。
            // 自動保存は<b>あとで復元する元</b>なので、NaN を含んだまま書くと、壊れたファイルしか
            // 残っていない状態で復元することになる
            var save = _fileOperationService.PrepareSave(input, ana, vbcr, sourceFilePath: filePath,
                autoSaveSessionId: SessionId, validateFinite: true);
            return new PreparedState(save, filePath);
        }

        /// <summary>
        /// 自動保存を実行
        /// </summary>
        // 保存対象の状態を解決する。LiveStateProvider があればそれを優先 (最新状態 + チェックボックス反映)。
        // 無ければ Start 時にキャプチャした参照にフォールバック。
        private (InputModel? input, string? filePath, AnaModel? ana, IList<FEM.VerticalBeamCaseResult>? vbcr) ResolveState()
        {
            if (LiveStateProvider != null)
            {
                try { return LiveStateProvider(); }
                catch (Exception ex) { Log.Warning(ex, "AutoSave LiveStateProvider failed; fallback to captured refs"); }
            }
            return (_currentInputModel, _currentFilePath, _currentModel, _verticalBeamCaseResults);
        }

        private void PerformAutoSave(PreparedState prepared)
        {
            try
            {
                var path = SaveSnapshot(tag: "autosave", prepared);
                // 保存対象が無い (ライブ状態が空) 場合は何もしない
                if (path == null)
                    return;

                LastAutoSaveTime = DateTime.Now;
                ConsecutiveFailures = 0;

                RaiseCompleted(new AutoSaveEventArgs
                {
                    FilePath = path,
                    Success = true,
                    Timestamp = LastAutoSaveTime.Value,
                    ConsecutiveFailures = 0
                });
            }
            catch (Exception ex)
            {
                ReportFailure(ex, "write");
            }
        }

        /// <summary>
        /// 完了の知らせ (<see cref="AutoSaveCompleted"/>) を<b>画面のスレッドで</b>発火する。
        ///
        /// 書き出しはバックグラウンド (Task.Run) で行うので、以前はそのスレッドから直接発火していた。
        /// 受け手はステータスバーの表示を書き換え、3 回続けて失敗したらトーストを出す。トーストは
        /// 画面の部品を作るので、画面のスレッド以外からは例外になり、その例外は書き出しの catch に
        /// 吸い込まれた。<b>3 回続けて失敗したときこそ出るべき通知が出ていなかった。</b>
        /// あわせて、受け手の例外が「書き出しの失敗」として数えられることもあった。
        ///
        /// 画面のスレッドはタイマーを作ったスレッド (このサービスを作った画面のスレッド)。
        /// 戻すときは待たない (BeginInvoke)。終了済みの Dispatcher に Invoke すると永久に待つ。
        /// 同じスレッドから呼ばれたとき (写しの段階の失敗・試験) はその場で発火する。
        /// </summary>
        private void RaiseCompleted(AutoSaveEventArgs args)
        {
            var handler = AutoSaveCompleted;
            if (handler == null) return;

            void Raise()
            {
                try { handler(this, args); }
                catch (Exception ex) { Log.Warning(ex, "AutoSave completion handler failed"); }
            }

            var dispatcher = _autoSaveTimer.Dispatcher;
            if (dispatcher.CheckAccess())
                Raise();
            else if (!dispatcher.HasShutdownStarted)
                dispatcher.BeginInvoke(Raise);
        }

        /// <summary>
        /// 自動保存の失敗を数えて知らせる。写し (画面のスレッド) と書き出し (バックグラウンド) の
        /// どちらで失敗しても同じ扱いにする。片方だけ数えると、そちらで失敗し続けたときに気づけない。
        /// </summary>
        private void ReportFailure(Exception ex, string stage)
        {
            int count = System.Threading.Interlocked.Increment(ref _consecutiveFailures);
            Log.Warning(ex, "AutoSave failed at {Stage} (consecutive: {Count})", stage, count);

            RaiseCompleted(new AutoSaveEventArgs
            {
                FilePath = null,
                Success = false,
                ErrorMessage = ex.Message,
                Timestamp = DateTime.Now,
                ConsecutiveFailures = count
            });
        }

        /// <summary>
        /// 致命的例外発生時に呼び出される緊急保存。
        /// 通常の AutoSave と異なり、イベントを発火せず、成功時はファイルパスを返す。
        /// 失敗時 (または保存する状態が無いとき) は null を返す。
        /// 例外を投げない (呼び出し側がさらに例外処理する手間を避けるため)。
        ///
        /// <b>タイマーが止まっていても保存する。</b>「新規作成」直後は Stop() されているが、
        /// そこで落ちたときこそ作業を残す必要があるため、_currentInputModel ではなく
        /// ライブ状態 (<see cref="LiveStateProvider"/>) の有無で判断する。
        /// </summary>
        public string? TryEmergencyAutoSave()
        {
            // 成否を問わず印を付ける。正常終了の後始末 (EndSessionNormally) が、
            // 作業を取り戻す手段である定期の自動保存を確認済みにしないように
            _emergencySaveAttempted = true;
            try
            {
                // 緊急保存はその場で写す。落ちる直前なので、画面のスレッドかどうかを
                // 選べない。列挙が壊れる危険は残るが、何も残さないより残すほうがよい。
                var prepared = PrepareState();
                if (prepared == null) return null;

                var path = SaveSnapshot(tag: "emergency", prepared.Value);
                if (path == null) return null;
                Log.Information("Emergency AutoSave succeeded: {Path}", path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Emergency AutoSave failed");
                return null;
            }
        }

        /// <summary>
        /// 共通スナップショット保存ロジック。例外はそのまま伝播する。
        /// tag は "autosave" または "emergency" を渡す (ファイル名に埋め込む)。
        /// 保存する状態が無ければ null を返す。
        /// </summary>
        /// <summary>
        /// 書き出しの重なりを防ぐ錠。
        ///
        /// ファイル名は秒までなので、同じ秒に 2 回書くと同じ一時ファイルを取り合って
        /// 「別のプロセスが使用中」で落ちる。自動保存と緊急保存が重なる場合がこれにあたる。
        /// </summary>
        private readonly object _saveLock = new();

        private string? SaveSnapshot(string tag, PreparedState prepared)
        {
            lock (_saveLock)
            {
                // 正常に終了したあとは、定期の自動保存を書かない (EndSessionNormally 参照)。
                // タイマーを止める前に写しを取った Tick が、終了の処理のあとで書き出しに来ることがある。
                if (_sessionEndedNormally && tag == "autosave")
                    return null;

                var path = SaveSnapshotCore(tag, prepared);
                if (path != null && tag == "autosave")
                    _writtenThisSession.Add(path);
                return path;
            }
        }

        /// <summary>このプロセスが書いた定期の自動保存ファイル (<see cref="_saveLock"/> の中で触る)。</summary>
        private readonly List<string> _writtenThisSession = [];

        /// <summary>正常に終了した (<see cref="EndSessionNormally"/> を通った)。<see cref="_saveLock"/> の中で触る。</summary>
        private bool _sessionEndedNormally;

        /// <summary>このセッションで緊急保存を試みた (成否を問わない)。</summary>
        private volatile bool _emergencySaveAttempted;

        /// <summary>
        /// 正常に終了するときに呼ぶ。自動保存を止め、<b>このプロセスが書いた定期の自動保存</b>を確認済みにする
        /// (<c>_dismissed_</c> を付ける。消さないので手で復元できる)。
        ///
        /// 自動保存は落ちたときに作業を取り戻すためのもので、正常に終了できたなら取り戻すものは無い。
        /// 以前は変更が無くても 3 分ごとに書いたまま残していたので、上書き保存して普通に終了しても、
        /// 最後の自動保存が元のファイルより新しいために次の起動で復元を勧めた。「保存しない」を選んで
        /// 終了した場合や名前の無いプロジェクトを捨てた場合も、捨てると決めた作業を勧めていた。
        ///
        /// <list type="bullet">
        /// <item>フォルダの中をまとめて扱わない。アプリを 2 つ開いていると、まだ動いている (あとで落ちる
        ///   かもしれない) もう一方の自動保存まで確認済みにしてしまう。</item>
        /// <item>緊急保存は対象にしない。また、このセッションで緊急保存を試みていたら何もしない。
        ///   致命的なエラーの終了処理でも画面は閉じるので、ここへ来ることがある。緊急保存に
        ///   失敗していたら、残っている定期の自動保存が作業を取り戻す唯一の手段になる。</item>
        /// </list>
        /// </summary>
        public void EndSessionNormally()
        {
            _autoSaveTimer.Stop();
            if (_emergencySaveAttempted)
            {
                Log.Information("[AutoSave] このセッションで緊急保存を試みたので、自動保存は確認済みにせず残します");
                return;
            }

            lock (_saveLock)
            {
                _sessionEndedNormally = true;
                foreach (var path in _writtenThisSession)
                    MarkDismissed(path);   // 既に印が付いたもの (起動時の確認で見送った等) は飛ばす
                _writtenThisSession.Clear();
            }
        }

        /// <summary>
        /// 自動保存ファイルの置き場所。同じ名前が既にあれば「_2」「_3」… を付けて重ならない名前にする。
        ///
        /// 名前は秒までの時刻で決まるので、同じ元ファイル・同じ保存種別で同じ秒に 2 回保存すると
        /// 同じファイルに上書きしていた (先の保存が黙って消える)。書き出しの錠
        /// (<see cref="_saveLock"/>) の中で呼ぶので、確かめてから書くまでの間に同じプロセスの保存は割り込まない。
        /// 番号は「_autosave_」「_emergency_」より後ろに付くので、一覧・片付けの名前の型にはそのまま当たる。
        /// </summary>
        private string UniqueSnapshotPath(string baseName)
        {
            string path = Path.Combine(AutoSaveFolder, baseName + ".pdj");
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(AutoSaveFolder, $"{baseName}_{n}.pdj");
            return path;
        }

        private string? SaveSnapshotCore(string tag, PreparedState prepared)
        {
            var sourceFilePath = prepared.SourceFilePath;

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var originalFileName = !string.IsNullOrEmpty(sourceFilePath)
                ? Path.GetFileNameWithoutExtension(sourceFilePath)
                : "Untitled";

            var filePath = UniqueSnapshotPath($"{originalFileName}_{tag}_{timestamp}");

            // 元ファイルのフルパスは、中身を確定させたとき (PrepareState) にファイルの中へ入れてある。
            // ファイル名には「拡張子を除いた名前」しか入らないので、復元したあとの保存先を
            // 名前から推測すると、カレントディレクトリ相対の別ファイルに書いてしまう。
            //
            // NaN 検査は中身を確定させたとき (PrepareState) に済んでいる。NaN があれば、ここで
            // その例外が投げ直され、書き出しの失敗として知らせる。
            _fileOperationService.WritePrepared(filePath, prepared.Save);
            return filePath;
        }

        /// <summary>
        /// 古い自動保存ファイルを削除
        /// </summary>
        private void CleanupOldAutoSaveFiles() => CleanupOldAutoSaveFiles(AutoSaveFolder, DateTime.Now, RetentionDays);

        /// <summary>
        /// <paramref name="folder"/> の自動保存ファイルのうち、<b>最後に書かれてから</b> <paramref name="retentionDays"/> 日を過ぎたものを消す。
        /// 消した数を返す。
        ///
        /// 以前は作成日時で判定していた。一方、復元候補の選択 (<see cref="FindRestoreCandidate"/>) は最終更新日時で決める。
        /// 作られたのは古いがあとで書き直されたファイル (同じ名前で作り直すと、Windows は元の作成日時を引き継ぐことがある) は、
        /// 復元の候補になる新しい内容なのに消えた。どちらも最終更新日時で揃える。
        /// </summary>
        internal static int CleanupOldAutoSaveFiles(string folder, DateTime now, int retentionDays)
        {
            int deleted = 0;
            try
            {
                var cutoffUtc = now.ToUniversalTime().AddDays(-retentionDays);
                // 通常の autosave と緊急保存 (emergency) の両方をクリーンアップ対象にする
                // 旧形式 (.json) と新形式 (.pdj) の両方を拾う
                var autoSaveFiles = Directory.GetFiles(folder, "*_autosave_*.pdj")
                    .Concat(Directory.GetFiles(folder, "*_emergency_*.pdj"))
                    .Concat(Directory.GetFiles(folder, "*_autosave_*.json"))
                    .Concat(Directory.GetFiles(folder, "*_emergency_*.json"));

                foreach (var file in autoSaveFiles)
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                    {
                        File.Delete(file);
                        deleted++;
                    }
                }
            }
            catch (Exception ex)
            {
                // クリーンアップ失敗は次回に再試行
                Log.Warning(ex, "[AutoSave] クリーンアップ失敗");
            }
            return deleted;
        }

        /// <summary>復元を勧める自動保存・緊急保存。<see cref="FindRestoreCandidate()"/> が選ぶ。</summary>
        /// <param name="FilePath">自動保存ファイル。</param>
        /// <param name="SavedAt">書いた時刻 (ファイルの更新時刻、ローカル時刻)。</param>
        /// <param name="SourceFilePath">中に記録した元ファイル (名前の無いプロジェクト・旧い自動保存ファイルでは null)。</param>
        /// <param name="SessionId">中に記録した、書いたアプリの起動ごとの印 (旧い自動保存ファイルでは null)。</param>
        public sealed record RestoreCandidate(string FilePath, DateTime SavedAt, string? SourceFilePath, string? SessionId = null);

        /// <summary>復元を勧めるのは、書いてからこの時間以内のものだけ。</summary>
        public static readonly TimeSpan RestoreWindow = TimeSpan.FromHours(24);

        /// <summary>
        /// 起動時に復元を勧める自動保存ファイルを選ぶ。無ければ null。
        /// </summary>
        /// <param name="excluded">この起動の中で既に扱った候補 (見送りの印を付けられなかったものを含む)。選ばない。</param>
        public RestoreCandidate? FindRestoreCandidate(IReadOnlyCollection<string>? excluded = null)
            => FindRestoreCandidate(AutoSaveFolder, DateTime.Now, excluded);

        /// <summary>
        /// 復元を勧める自動保存ファイルを選ぶ (フォルダと現在時刻を渡せる形。テストはこちらを使う)。
        ///
        /// 新しい順に見て、次のものを飛ばす。
        /// <list type="bullet">
        /// <item>見送り済み (<c>_dismissed_</c>)、および <paramref name="excluded"/> (この起動の中で既に扱ったもの)。
        ///   見送りの印は名前を変えて付けるので、読み取り専用や他のソフトが掴んでいると付けられない。
        ///   そのときも同じ候補を選び直さないよう、呼び出し側が扱った候補を渡す。</item>
        /// <item>書いてから <see cref="RestoreWindow"/> を過ぎたもの。</item>
        /// <item><b>元ファイルのほうが新しいもの</b>。以前は自動保存ファイルの時刻だけを見ていたので、
        ///   そのあと元ファイルを普通に保存していても復元を勧め、復元すると上書き保存がその元ファイルへ
        ///   向かった (保存した新しい内容を、古い自動保存の内容で上書きする)。元ファイルは中に記録した
        ///   フルパスで引く。記録が無い・元ファイルが無いものは比べられないので、従来どおり勧める。</item>
        /// </list>
        /// 時刻は作成時刻ではなく更新時刻で比べる。元ファイルは上書き保存で作成時刻が動かないため
        /// (一時ファイルからの差し替えでも、同じ名前の作り直しは作成時刻を引き継ぐことがある)。
        /// </summary>
        internal static RestoreCandidate? FindRestoreCandidate(string folder, DateTime now,
            IReadOnlyCollection<string>? excluded = null)
        {
            try
            {
                foreach (var file in EnumerateRestoreCandidates(folder)
                             .OrderByDescending(f => f.LastWriteTimeUtc))
                {
                    if (excluded != null && excluded.Contains(file.FullName, StringComparer.OrdinalIgnoreCase))
                        continue;

                    if (now - file.LastWriteTime > RestoreWindow)
                        return null;   // 以降はもっと古い

                    var (source, sessionId) = ReadRecordedOrigin(file.FullName);
                    if (!string.IsNullOrEmpty(source) && File.Exists(source)
                        && File.GetLastWriteTimeUtc(source) >= file.LastWriteTimeUtc)
                    {
                        Log.Information("[AutoSave] 元ファイルのほうが新しいので復元を勧めません: {AutoSave} (元 {Source})",
                            file.Name, source);
                        continue;
                    }

                    return new RestoreCandidate(file.FullName, file.LastWriteTime, source, sessionId);
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[AutoSave] 復元候補を探せません");
                return null;
            }
        }

        /// <summary>見送っていない自動保存・緊急保存のファイル (旧形式 .json と新形式 .pdj)。</summary>
        private static IEnumerable<FileInfo> EnumerateRestoreCandidates(string folder)
        {
            if (!Directory.Exists(folder)) return [];
            return Directory.GetFiles(folder, "*_autosave_*.pdj")
                .Concat(Directory.GetFiles(folder, "*_emergency_*.pdj"))
                .Concat(Directory.GetFiles(folder, "*_autosave_*.json"))
                .Concat(Directory.GetFiles(folder, "*_emergency_*.json"))
                .Where(f => !Path.GetFileName(f).Contains("_dismissed_"))
                .Select(f => new FileInfo(f));
        }

        /// <summary>
        /// 自動保存ファイルに記録した元ファイルのパス (<c>ProjectData.SourceFilePath</c>) と
        /// 書いたアプリの起動ごとの印 (<c>ProjectData.AutoSaveSessionId</c>) だけを読む。
        /// 記録が無い・読めなければ null。
        ///
        /// <b>ファイルの頭 (<see cref="OriginHeadBytes"/>) だけを読む。</b>2 つの項目はファイルの先頭に書く
        /// (<c>JsonPropertyOrder</c>) ので、それで足りる。以前はファイル全体をメモリへ読んでから走査していたので、
        /// 解析結果込みの自動保存 (数十 MB) が候補に並ぶと、起動時の候補検索が長く止まり、メモリも増えた。
        ///
        /// 先頭に書くようにする前の自動保存ファイル (旧形式) は、2 つの項目が<b>末尾</b>にある。
        /// 頭に収まる小さなファイルは全体を走査して拾い、収まらないファイルは<b>末尾 (<see cref="OriginHeadBytes"/>) を読んで</b>拾う
        /// (<see cref="FindOriginInTail"/>)。入力だけの自動保存でも 100 KB を超えるので、頭だけで諦めると
        /// 旧形式のほとんどが「記録なし」になり、元ファイルのほうが新しいのに復元を勧めてしまう。
        /// 全体をストリームで走査しないのは、数十 MB のファイルでは結局全部読むことになるため。
        /// </summary>
        internal static (string? SourceFilePath, string? SessionId) ReadRecordedOrigin(string autoSaveFile)
        {
            string? source = null, session = null;
            try
            {
                byte[] head;
                bool whole;
                using (var stream = new FileStream(autoSaveFile, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.SequentialScan))
                {
                    int length = (int)Math.Min(OriginHeadBytes, stream.Length);
                    head = new byte[length];
                    stream.ReadExactly(head, 0, length);
                    whole = length == stream.Length;
                }

                var reader = new System.Text.Json.Utf8JsonReader(head, isFinalBlock: whole, state: default);
                if (!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
                    return (null, null);

                while (reader.Read() && reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
                {
                    bool isSource = reader.ValueTextEquals("SourceFilePath");
                    bool isSession = reader.ValueTextEquals("AutoSaveSessionId");
                    bool isId = reader.ValueTextEquals("$id");
                    if (!reader.Read()) break;   // 値が頭に収まっていない

                    if (isSource || isSession)
                    {
                        string? value = reader.TokenType == System.Text.Json.JsonTokenType.String ? reader.GetString() : null;
                        if (isSource) source = value; else session = value;
                        if (source != null && session != null) break;   // 両方そろった
                        continue;
                    }
                    if (isId) continue;

                    // 2 つの項目より後ろの項目に来た。新しい形のファイルならここで終わり。
                    // 旧い形 (末尾に書いていた) でも、全体が頭に収まっていれば残りを走査して拾う
                    if (!whole) break;
                    reader.Skip();
                }

                // 頭に無く、頭に収まらないファイル = 旧形式。末尾を読む
                if (!whole && source == null && session == null)
                    (source, session) = FindOriginInTail(autoSaveFile);
                return (source, session);
            }
            catch (Exception ex)
            {
                // 途中まで読めた分は使う (壊れたファイルでも、先頭側の記録で同じ作業かは分かる)
                Log.Debug(ex, "[AutoSave] 元ファイルの記録を読めません: {File}", autoSaveFile);
                return (source, session);
            }
        }

        /// <summary>復元候補の元ファイルの記録を探すときに読む、ファイルの頭 (旧形式では末尾) の大きさ。</summary>
        internal const int OriginHeadBytes = 64 * 1024;

        /// <summary>
        /// 旧形式の自動保存ファイルの<b>末尾</b>から、元ファイルのパスと起動ごとの印を探す。
        ///
        /// 旧形式では 2 つの項目が <c>ProjectData</c> の最後に並ぶ。項目名 <c>"SourceFilePath"</c> は
        /// 保存データの中で <c>ProjectData</c> にしか無く、JSON の文字列の中では <c>"</c> が必ず <c>\"</c> になるので、
        /// 同じ並びが値の中に紛れることは無い。見つけた項目名の直後の値だけを JSON として読む。
        /// </summary>
        private static (string? SourceFilePath, string? SessionId) FindOriginInTail(string autoSaveFile)
        {
            byte[] tail;
            using (var stream = new FileStream(autoSaveFile, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete, bufferSize: 1))
            {
                int length = (int)Math.Min(OriginHeadBytes, stream.Length);
                stream.Seek(-length, SeekOrigin.End);
                tail = new byte[length];
                stream.ReadExactly(tail, 0, length);
            }
            return (ReadStringPropertyInTail(tail, "SourceFilePath"u8), ReadStringPropertyInTail(tail, "AutoSaveSessionId"u8));
        }

        /// <summary>末尾の断片から <c>"name": "値"</c> の値を読む (最後に現れたもの)。無い・文字列でなければ null。</summary>
        private static string? ReadStringPropertyInTail(ReadOnlySpan<byte> tail, ReadOnlySpan<byte> name)
        {
            // "name" の形で探す (前後の引用符も含めて探すので、別の項目名の一部には当たらない)
            Span<byte> quoted = stackalloc byte[name.Length + 2];
            quoted[0] = (byte)'"';
            name.CopyTo(quoted[1..]);
            quoted[^1] = (byte)'"';

            int at = tail.LastIndexOf(quoted);
            if (at < 0) return null;

            var rest = tail[(at + quoted.Length)..];
            int i = 0;
            while (i < rest.Length && rest[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') i++;
            if (i >= rest.Length || rest[i] != (byte)':') return null;

            var reader = new System.Text.Json.Utf8JsonReader(rest[(i + 1)..], isFinalBlock: false, state: default);
            if (!reader.Read()) return null;
            return reader.TokenType == System.Text.Json.JsonTokenType.String ? reader.GetString() : null;
        }

        /// <summary>
        /// 復元の確認に答えたら (いいえ、または復元できたとき)、<b>その候補と、同じ作業のそれより古い候補を
        /// まとめて</b>見送り済みにする。ファイルは消さず名前に <c>_dismissed_</c> を付ける (手で復元できるように残す)。
        ///
        /// <list type="bullet">
        /// <item>以前は提示した 1 件だけに印を付けていた。候補を探す側は残りから次に新しいものを選ぶので、
        ///   自動保存が何世代もあると、起動のたびに 1 つずつ古い自動保存の復元を勧め続けた。</item>
        /// <item>次に、フォルダ全体からそれより古いものをまとめて印を付けるようにしたが、それでは
        ///   プロジェクト A への答えで、プロジェクト B のまだ復元していない作業まで案内されなくなった。
        ///   いまは同じ作業のものだけに印を付ける (<see cref="IsSameWork"/>)。</item>
        /// </list>
        /// 自動保存と緊急保存の両方に印を付けること (以前は "_autosave_" だけを置換していたので、
        /// 緊急保存は名前が変わらず、同じ確認が出続けた)。
        /// </summary>
        public void DismissRestoreCandidatesUpTo(RestoreCandidate answered)
            => DismissRestoreCandidatesUpTo(AutoSaveFolder, answered);

        internal static void DismissRestoreCandidatesUpTo(string folder, RestoreCandidate answered)
        {
            DateTime cutoffUtc;
            try { cutoffUtc = File.GetLastWriteTimeUtc(answered.FilePath); }
            catch (Exception ex) { Log.Warning(ex, "[AutoSave] 見送りの基準時刻を読めません"); return; }

            IEnumerable<FileInfo> files;
            try { files = EnumerateRestoreCandidates(folder).ToList(); }
            catch (Exception ex) { Log.Warning(ex, "[AutoSave] 見送りの対象を探せません"); return; }

            foreach (var file in files)
            {
                if (file.LastWriteTimeUtc > cutoffUtc) continue;   // 答えたあとに書かれたもの (他で動いている分) は残す
                if (!string.Equals(file.FullName, answered.FilePath, StringComparison.OrdinalIgnoreCase)
                    && !IsSameWork(answered, file.FullName))
                    continue;                                       // 別の作業 (別のプロジェクト) の候補は残す
                MarkDismissed(file.FullName);
            }
        }

        /// <summary>
        /// 復元に失敗した候補 1 件だけを見送り済みにする。同じ作業のより古い候補は残す。
        ///
        /// 以前は失敗しても、答えたときと同じくそれより古い候補をまとめて見送っていた。
        /// 最新の自動保存が壊れていると、読める古い自動保存まで二度と案内されなくなる。
        /// 壊れた 1 件に印を付けないと、起動のたびに同じ失敗を繰り返す。
        /// </summary>
        public void DismissRestoreCandidate(RestoreCandidate failed) => MarkDismissed(failed.FilePath);

        /// <summary>
        /// 自動保存ファイル <paramref name="file"/> が、答えた候補 <paramref name="answered"/> と同じ作業のものか。
        ///
        /// <list type="bullet">
        /// <item>同じ元ファイルを記録している (上書き保存しているプロジェクト)。</item>
        /// <item>同じアプリの起動が書いた (<see cref="SessionId"/>)。名前の無いプロジェクトはこちらでしか結べない。
        ///   同じ起動の中でプロジェクトを取り替えた場合も同じ作業に数えるが、取り替えるときに
        ///   「保存しますか？」で利用者が保存か破棄を決めているので、前のプロジェクトの自動保存を案内し直す必要は無い。</item>
        /// <item>どちらも記録していない旧い自動保存ファイルは、名前の頭 (元ファイル名) が同じなら同じ作業とみなす。</item>
        /// </list>
        /// </summary>
        private static bool IsSameWork(RestoreCandidate answered, string file)
        {
            var (source, session) = ReadRecordedOrigin(file);

            if (!string.IsNullOrEmpty(answered.SourceFilePath) && !string.IsNullOrEmpty(source)
                && PathsEqual(answered.SourceFilePath, source))
                return true;
            if (!string.IsNullOrEmpty(answered.SessionId) && answered.SessionId == session)
                return true;

            bool answeredHasNoRecord = string.IsNullOrEmpty(answered.SourceFilePath) && string.IsNullOrEmpty(answered.SessionId);
            bool fileHasNoRecord = string.IsNullOrEmpty(source) && string.IsNullOrEmpty(session);
            return answeredHasNoRecord && fileHasNoRecord
                && string.Equals(NamePrefix(answered.FilePath), NamePrefix(file), StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>自動保存ファイル名の頭 (<c>_autosave_</c> / <c>_emergency_</c> より前 = 元ファイル名)。</summary>
        private static string NamePrefix(string file)
        {
            string name = Path.GetFileName(file);
            var m = System.Text.RegularExpressions.Regex.Match(name, "^(.*)_(autosave|emergency)_");
            return m.Success ? m.Groups[1].Value : name;
        }

        /// <summary>自動保存ファイルに見送り済みの印 (<c>_dismissed_</c>) を付ける。消さない。</summary>
        private static void MarkDismissed(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var dismissed = System.Text.RegularExpressions.Regex.Replace(
                    path, "_(autosave|emergency)_", "_$1_dismissed_");
                if (dismissed != path)
                    File.Move(path, dismissed);
            }
            catch (Exception ex) { Log.Warning(ex, "[AutoSave] リネーム失敗: {File}", Path.GetFileName(path)); }
        }

        /// <summary>
        /// 指定されたファイルの自動保存ファイル (新しい順)。見送り済み (<c>_dismissed_</c>) も含む。
        ///
        /// <b>中に記録した元ファイルのフルパスで照合する。</b>以前はファイル名 (拡張子を除いた元ファイル名) だけで
        /// 照合していたので、別のフォルダにある同じ名前のプロジェクト (例: 案A\基礎.pdj と 案B\基礎.pdj) の
        /// 自動保存が互いに混ざった。ファイル名は候補を絞るのにだけ使う。
        /// 元ファイルを記録していない旧い自動保存ファイルは、どのプロジェクトのものか確かめられないので含めない。
        /// </summary>
        /// <param name="filePath">元のファイルパス</param>
        /// <returns>関連する自動保存ファイルのリスト</returns>
        public string[] GetAutoSaveFilesForProject(string filePath) => GetAutoSaveFilesForProject(AutoSaveFolder, filePath);

        internal static string[] GetAutoSaveFilesForProject(string folder, string filePath)
        {
            try
            {
                var originalFileName = Path.GetFileNameWithoutExtension(filePath);
                // 旧形式 (.json) と新形式 (.pdj) の両方を拾う
                var autoSaveFiles = Directory
                    .GetFiles(folder, $"{originalFileName}_autosave_*.pdj")
                    .Concat(Directory.GetFiles(folder, $"{originalFileName}_emergency_*.pdj"))
                    .Concat(Directory.GetFiles(folder, $"{originalFileName}_autosave_*.json"))
                    .Concat(Directory.GetFiles(folder, $"{originalFileName}_emergency_*.json"));

                return [.. autoSaveFiles
                    .Where(f => ReadRecordedOrigin(f).SourceFilePath is { Length: > 0 } source && PathsEqual(source, filePath))
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.FullName)];
            }
            catch
            {
                return [];
            }
        }
    }

    /// <summary>
    /// 自動保存イベント引数
    /// </summary>
    public class AutoSaveEventArgs : EventArgs
    {
        public string? FilePath { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 連続失敗回数 (成功時は 0)。閾値超過で UI 側がエスカレーション通知に使う。
        /// </summary>
        public int ConsecutiveFailures { get; set; }
    }
}

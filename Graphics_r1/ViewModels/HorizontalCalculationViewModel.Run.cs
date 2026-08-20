using PileDesign.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MathNet.Numerics;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Serilog;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    // HorizontalCalculationViewModel partial: 解析実行本体 RunAsync（ステップ/ケースループ・NR 反復・収束判定・cut-back retry）
    public partial class HorizontalCalculationViewModel
    {
        // v21 Phase 3 prep: ばね剛性 min/max はインスタンスフィールドを廃し、
        // FindK / PrepareKmat の戻り値（out パラメータ）で局所管理する。

        public async Task RunAsync(CancellationToken token, IProgress<Models.AnalysisProgress>? progress = null, bool additive = false)
        {
            // additive=true: 既存結果を保持し、未計算ケースのみを実行 (段階追加再解析)
            // additive=false: 通常実行 (既存結果を明示クリアして全ケース計算)
            await Task.Yield();

            // === 追加実行モード用: 既存ケースキー集合 ===
            // RunAsync 開始時に 1 回スナップショットを取り、ループ内で skip 判定に使用。
            // 並列ループ実行中は targetModel.AnalysisStepResults へ append が走るため
            // 並列実行内で再評価しない (ロック保護下のスナップショット)。
            HashSet<FEM.AnalysisRunSnapshot.CaseKey> existingKeys = new();

            var preTargetModel = TryGetTargetAnaModel();
            if (preTargetModel != null)
            {
                lock (_caseMergeLock)
                {
                    if (additive && preTargetModel.LastRunConfig != null)
                    {
                        existingKeys = preTargetModel.LastRunConfig.ExecutedCaseKeys.ToHashSet();
                    }
                    else if (additive && preTargetModel.AnalysisStepResults?.Count > 0)
                    {
                        // 防御: LastRunConfig が null だが結果がある旧データの場合、結果から復元
                        existingKeys = preTargetModel.AnalysisStepResults
                            .Select(r => new FEM.AnalysisRunSnapshot.CaseKey(
                                r.LoadCase.LoadName, r.LoadCombination.Name, r.IsLiquefaction))
                            .ToHashSet();
                    }
                }
            }

            if (additive)
            {
                await AddLogAsync($"=== 追加実行: 既存 {existingKeys.Count} ケースを保持し、未計算分のみ計算します ===");
                // _stepSummaries.Clear() は呼ばない (既存ログを保持)
                // 既存結果のステップ数を baseline として記録 (プログレスバー分母から除外)
                _additiveBaselineSteps = preTargetModel?.AnalysisStepResults?.Count ?? 0;
            }
            else
            {
                // 既に「計算モデル作成開始」が出ているので、ここでは「計算開始」を追記する
                await AddLogAsync("解析計算処理開始");
                // v29: ステップサマリーをリセット (前回解析の結果をクリア)
                _stepSummaries.Clear();
                // 結果と前回設定を明示クリア (新規実行時)
                if (preTargetModel != null)
                {
                    lock (_caseMergeLock)
                    {
                        preTargetModel.ClearAllAnalysisResults();
                    }
                }
                // baseline をリセット (通常実行)
                _additiveBaselineSteps = 0;
            }
            OnPropertyChanged(nameof(EffectiveProgressTotal));
            OnPropertyChanged(nameof(EffectiveProgress));
            OnPropertyChanged(nameof(ProgressText));

            // v29: 経過/残り時間表示用に開始時刻記録 + 1 秒タイマーで定期更新
            _analysisStartUtc = DateTime.UtcNow;
            StartElapsedTimer();

            // 並列モニタ (案 B): 新解析のたびに Active/Completed をリセット
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                ActiveCases.Clear();
                ActiveCasesCount = 0;
                CompletedCaseCount = 0;
                // load case 単位 (= CompletedCaseCount と同じ単位) で完了率を表示
                TotalPlannedCaseCount = TotalLoadCaseCount;
                // setter 内で PendingCaseCount PropertyChanged も発火するが念のため再度発火
                OnPropertyChanged(nameof(PendingCaseCount));
            });

            // MDOP>=2 ならモニタを表示
            bool showMonitor = MaxCaseDegreeOfParallelism > 1;
            if (showMonitor)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => RequestShowParallelMonitor?.Invoke());
            }

            // 進捗報告用の開始時刻を記録
            var startTime = DateTime.Now;

            // 計算対象モデルを決定（編集用があればそれ、なければ本体）
            var targetModel = AnaModels.Count > 1 ? AnaModels[1] : AnaModels[0];
            if (targetModel == null)
            {
                await AddLogAsync("計算モデルが存在しません。");
                return;
            }

            targetModel.SetSlaveNodes(); // 剛体連結のスレーブ節点のセット

            // E3c-3 (2026-04-23): ケース並列化対応。calcNo を StrongBox に包み Interlocked で
            // atomic にインクリメントする。MDOP=1 (逐次) でも同じ経路 (overhead は無視できる)。
            var calcNoBox = new System.Runtime.CompilerServices.StrongBox<int>(0);

            // v19: 解析開始時に再試行による追加ステップ数をリセット
            _bisectionExtraSteps = 0;
            NotifyProgressPropertiesChanged();
            OnPropertyChanged(nameof(TotalLoadCaseCount));

            // 初期進捗を報告
            progress?.Report(new Models.AnalysisProgress
            {
                Percentage = 0,
                CurrentStep = "解析計算を開始しています...",
                CurrentStepNumber = 0,
                TotalSteps = TotalCalculationCount,
                StartTime = startTime
            });

            const double alpha = 1e-6;

            // v21 Phase 3 prep: 将来のケース並列化に向けた設計メモ（このループを並列実行する場合の必要要件）
            //
            // ■ 現状はスレッド安全ではない以下の共有状態に注意:
            //   - InputModel.PileLayoutItems[].AxialForce   → InitializeAxialForces / UpdateAxialForceFromAnalysis が書換
            //   - InputModel.PileLayoutItems[].SoilNodes[].CumulativeForcedDisp  → UpdateSoilDisp が書換
            //   - InputModel.ElementDivision.DoatsuGoryokuBane.Items[].*SoilNode.CumulativeForcedDisp  → 同上
            //   - InputModel.ElementDivision.SoilPiles[].HorizontalSoilReactions  → PrepareKmat が評価（読み取り中に他 worker が AxialForce 経由で値を変える可能性）
            //   - targetModel.AnalysisStepResults / Nodes[].NodeResults / Beams[].BeamResults / HorizontalSoilSprings[].HorizontalSpringResults / RotationalSprings[].RotationalSpringResults  → 結果 append
            //
            // ■ 並列化手順（Phase 3.1 本実装時）:
            //   (a) 事前に cases = [(lc, combo, isLiq), ...] を全列挙
            //   (b) 各 case ごとに InputModel+AnaModel の「スレッド固有コピー」を構築
            //       （PileLayoutItem / DoatsuGoryokuBane / SoilPiles の書換対象を clone し、
            //        AnaModel.DeepCopy 済みのノードへ参照 fixup）
            //   (c) Parallel.ForEachAsync(cases, new ParallelOptions { MaxDegreeOfParallelism = MaxCaseDegreeOfParallelism }, ...)
            //   (d) 完了後、key=(lc.No, combo.No, isLiq, step) で AnalysisStepResults / 各要素 Results を
            //       決定的順序でマージ（乱序混入を防ぐ）
            //   (e) MathNet Control.MaxDegreeOfParallelism を並列実行中は 1 にクランプ
            //   (f) AddLogAsync / CurrentProgress / _bisectionExtraSteps を Interlocked or lock で保護

            // E3c-3 (2026-04-23): ケース並列化中は MathNet 内部並列度を 1 に clamp。
            // 並列ケース × 並列 MathNet の掛け算でスレッド過剰を防ぐ。MDOP=1 (逐次) では clamp 不要。
            int _caseMDOP = Math.Max(1, MaxCaseDegreeOfParallelism);
            int _origMathNetMDOP = MathNet.Numerics.Control.MaxDegreeOfParallelism;
            // 元の MKL/OMP 環境変数を退避 (try/finally で復元)
            string? _origMklNumThreads = Environment.GetEnvironmentVariable("MKL_NUM_THREADS");
            string? _origOmpNumThreads = Environment.GetEnvironmentVariable("OMP_NUM_THREADS");
            if (_caseMDOP > 1)
            {
                MathNet.Numerics.Control.MaxDegreeOfParallelism = 1;
                // hang 対策 (2026-04-26): MKL/OMP ネイティブ層の並列度を強制 1 に。
                // MathNet.Numerics.Control の clamp は managed 層のみで、MKL ネイティブが
                // 内部スレッドプールを別途持つ場合 oversubscription が起き ThreadPool starvation
                // → hang の一因。環境変数で強制クランプしておく。
                Environment.SetEnvironmentVariable("MKL_NUM_THREADS", "1");
                Environment.SetEnvironmentVariable("OMP_NUM_THREADS", "1");
            }

            // E3c-3-enable: MDOP > 1 のとき、ケースを Task.Run で並行実行し SemaphoreSlim で
            // 同時実行数を _caseMDOP に制限。MDOP=1 では null のまま、従来通り逐次実行。
            var _caseTasks = new List<System.Threading.Tasks.Task>();
            System.Threading.SemaphoreSlim? _caseSemaphore = _caseMDOP > 1 ? new System.Threading.SemaphoreSlim(_caseMDOP) : null;

            try
            {
            // P-S 非線形ばね + VL 単独解析が有効な場合: VL 仮想ケースを先頭に挿入
            // VL ケース: 水平荷重 0、各杭頭に AxialForceVL を外力として与える
            // No=1 とするのは iLC=0 で配列アクセスが安全になるため (VL 判定は LoadName で行う)
            // IsPileNonLinear=true / IsSoilNonLinear=true は、P-S ばねの非線形性に対応するため
            // Level1CalculationStepsCount に基づく段階適用 (nStep > 1) を有効化する目的。
            // (configuredNStep の決定で両者が false だと nStep=1 となり 1 ステップで全 N0 適用 → 発散)
            var casesToRun = new List<LoadCase>();
            if (InputModel.UsePsSpringAtPileTip && InputModel.IsVLAnalysisEnabled)
            {
                casesToRun.Add(new LoadCase
                {
                    LoadName = "VL",
                    No = 1,
                    Level = 1,
                    LoadAngle = 0,
                    UpperMassForce = 0,
                    FoundationMassForce = 0,
                    IsPileNonLinear = true,
                    SoilNonlinearityMode = SoilNonlinearityMode.KhReductionWithPy,
                });
            }
            foreach (var lc in InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases)
                casesToRun.Add(lc);

            foreach (var loadCaseItem in casesToRun)
            {
                LoadCase loadCase = loadCaseItem;
                int iLC = loadCaseItem.No - 1;
                int level = loadCaseItem.Level;
                bool isVLCase = loadCase.LoadName == "VL";

                // 荷重がゼロの場合はスキップ (VL ケースは水平荷重 0 でも実行)
                if (!isVLCase && loadCase.UpperMassForce == 0 && loadCase.FoundationMassForce == 0)
                {
                    await AddLogAsync($"レベル{level}-{iLC + 1}: 荷重がゼロのためスキップ");
                    continue;
                }

                // VL ケースは水平荷重 0 のため、組合せ係数 (β1/β2/α1) は結果に影響しない。
                // 結果重複を避けるため、VL は組合せ・液状化ループを 1 回のみ実行する。
                IEnumerable<LoadCombination> combosToRun = isVLCase
                    ? new[] { InputModel.LoadCasesInput.AllLoadCombinations.FirstOrDefault() }
                        .Where(c => c != null)
                    : InputModel.LoadCasesInput.AllLoadCombinations;

                foreach (var loadCombination in combosToRun)
                {
                    int iLCOM = loadCombination.No - 1;

                    IEnumerable<bool> liquefactionCases = isVLCase
                        ? new[] { false }
                        : LiquefactionOption switch
                    {
                        LiquefactionOptionType.Both => [true, false],
                        LiquefactionOptionType.Yes => [true],
                        LiquefactionOptionType.None => [false],
                        _ => new[] { false }
                    };

                    foreach (bool isLiquefaction in liquefactionCases)
                    {
                        // 追加実行モード: 既存結果に同じキーがあるケースはスキップ
                        if (additive)
                        {
                            var caseKey = new FEM.AnalysisRunSnapshot.CaseKey(
                                loadCase.LoadName, loadCombination.Name, isLiquefaction);
                            if (existingKeys.Contains(caseKey))
                            {
                                await AddLogAsync($"[skip] {BuildCaseTag(loadCase, level, iLC, iLCOM, isLiquefaction)} は既存結果あり (追加実行モード)");
                                continue;
                            }
                        }

                        // E3c-3-enable: ケース 1 件分の body を local function に wrap。
                        // MDOP=1 ではこのまま直接 await で呼出、MDOP>1 では Task.Run に投げて並行実行。
                        // foreach 変数 (loadCase, loadCombination, isLiquefaction, iLC, iLCOM, level) は
                        // C# 5.0 以降の仕様により各反復で fresh な変数として cap される。
                        async System.Threading.Tasks.Task RunThisCaseAsync()
                        {
                        // E3c-3 tune (2026-04-23): inner Task.Run ネスト回避用ヘルパー。
                        // MDOP=1 (逐次): 従来通り Task.Run に投げて UI をブロックしない。
                        // MDOP>1 (並列): 既に外側 Task.Run 内 (ThreadPool worker) で実行中なので
                        //   同期実行で十分。inner Task.Run を挟むと ThreadPool 消費量が
                        //   MDOP × (NR 反復数) 倍に膨らみ starvation を招く。
                        async System.Threading.Tasks.Task RunWork(System.Action work)
                        {
                            if (_caseMDOP > 1)
                            {
                                work();
                            }
                            else
                            {
                                await System.Threading.Tasks.Task.Run(work, token);
                            }
                        }

                        // E2 (2026-04-23): 並列化後のログ混在対策。反復/収束/プロファイルログの先頭に付与
                        string caseTag = BuildCaseTag(loadCase, level, iLC, iLCOM, isLiquefaction);

                        // 並列モニタ (案 B, 2026-04-24): ケース開始時に Active リストへ追加。
                        // TotalSteps は nStep 確定後 (後続の baseNStep 計算後) に更新される。
                        // BeginInvoke: ワーカースレッドをブロックしない (Invoke は UI 処理完了まで待機)
                        var monitorItem = new CaseMonitorItem(caseTag, 0);
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            ActiveCases.Add(monitorItem);
                            ActiveCasesCount = ActiveCases.Count;
                            OnPropertyChanged(nameof(PendingCaseCount));
                        });
                        // 診断ログ (hang 調査用、2026-04-26): ケース開始時刻と thread id を記録。
                        // MDOP > 1 時のみ。完了せず固まった場合、最後に開始したケースが特定可能。
                        var _diagCaseStartUtc = DateTime.UtcNow;
                        if (_caseMDOP > 1)
                        {
                            int tid = System.Threading.Thread.CurrentThread.ManagedThreadId;
                            await AddLogAsync($"{caseTag} 🟢 開始 (Tid={tid}, ActiveCases={ActiveCasesCount})");
                        }
                        try
                        {

                        // E3c-2 (2026-04-23): ケース固有モデル = targetModel の DeepCopy。
                        // E3c-3 (2026-04-23): snapshot + DeepCopy は targetModel の state を同時に
                        // 参照するため atomic である必要あり。逐次モードでは lock は no-op。
                        int caseSnapAnaStepResults;
                        int[] caseSnapNodeResultCounts, caseSnapBeamResultCounts, caseSnapHSpringResultCounts;
                        int[]? caseSnapRotSpringResultCounts = null;
                        AnaModel caseModel;
                        lock (_caseMergeLock)
                        {
                            caseSnapAnaStepResults = targetModel.AnalysisStepResults?.Count ?? 0;
                            caseSnapNodeResultCounts = targetModel.Nodes.Select(n => n.NodeResults.Count).ToArray();
                            caseSnapBeamResultCounts = targetModel.Beams.Select(b => b.BeamResults.Count).ToArray();
                            caseSnapHSpringResultCounts = targetModel.HorizontalSoilSprings.Select(s => s.HorizontalSpringResults.Count).ToArray();
                            if (targetModel.RotationalSprings != null)
                                caseSnapRotSpringResultCounts = targetModel.RotationalSprings.Select(rs => rs.RotationalSpringResults.Count).ToArray();

                            caseModel = targetModel.DeepCopy();
                        }

                        // 地盤 (p-y) 非線形モードはケース単位の設定。PrepareKmat が caseModel から読む。
                        caseModel.SoilNonlinearityMode = loadCase.SoilNonlinearityMode;

                        // ── 軸剛性 0 (Uz 解放): 引張定着筋なし半剛接合 (キャプテン/F.T.Pile/キャプリング) で
                        //    入力軸力が引張となる杭について、case-local モデルの杭頭 Uz master-slave を
                        //    解放する。「軸剛性 0、曲げ剛性 0」(Mu=0 と併用) でピン接合的挙動を実現する。
                        //    判定は入力軸力ベース (UseAnalysisAxialForce=true でも初期推定値で固定)。
                        //    詳細は help.html「引張軸力時の杭頭軸剛性解放」を参照。
                        var axialReleasePileNos = GetPileNosForAxialReleaseInCase(loadCase);
                        if (axialReleasePileNos.Count > 0)
                        {
                            try
                            {
                                caseModel.ApplyAxialReleaseAtPileHeads(axialReleasePileNos);
                                await AddLogAsync(
                                    $"  {caseTag} 軸剛性 0 適用: 杭No.[{string.Join(",", axialReleasePileNos)}] " +
                                    "(引張定着筋なし半剛接合 × 引張軸力 → 杭頭 Uz 解放)");
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "[ApplyAxialReleaseAtPileHeads] 失敗: {CaseTag}", caseTag);
                                await AddLogAsync($"  {caseTag} ⚠ 軸剛性 0 適用に失敗: {ex.Message}");
                            }
                        }

                        // v20: 荷重方向の事前検出 — counter-loading (逆方向組合せ) を検出し、
                        // 最初から小さな荷重ステップで実行することで失敗試行のムダを回避
                        //
                        // v28 (2026-04-23): Approach I で杭頭 Ry リミットサイクルが根本解決したため、
                        // 分類を βU × βL の符号のみに簡素化、CounterLoading 時の nStep を 16 → 12 に緩和。
                        // 物理的根拠: 逆方向組合せは S 字曲げ (杭頭と杭体下部で逆符号の塑性ヒンジが
                        // 同時形成) が発生し、Newton 方向が接線不連続で振動しやすい。その他の難しさは
                        // Approach I で除去済み、または早期適応検出 (v26 案 B) が実測ベースで救済。
                        //
                        // 仕組み:
                        //   - Forward (βU × βL ≥ 0): configured nStep で開始、retry 最大 3
                        //   - CounterLoading (βU × βL < 0): 基本 ×2 (min 12) で開始、retry 最大 2
                        bool isSoilNonLinear = loadCase.SoilNonlinearityMode.IsNonLinear();
                        int configuredNStep = (!isSoilNonLinear && !loadCase.IsPileNonLinear) ? 1 :
                            loadCase.Level == 1 ? Level1CalculationStepsCount :
                            loadCase.Level == 2 ? Level2CalculationStepsCount :
                            1;
                        var loadDirection = ClassifyLoadCombinationDirection(loadCase, loadCombination, isLiquefaction);
                        int baseNStep = configuredNStep;
                        int MAX_STEP_BISECTIONS = 3;
                        if (isSoilNonLinear || loadCase.IsPileNonLinear)
                        {
                            // 非線形ケースのみ事前検出を適用
                            switch (loadDirection)
                            {
                                case LoadCombinationDirection.CounterLoading:
                                    baseNStep = Math.Max(configuredNStep * 2, 12);
                                    MAX_STEP_BISECTIONS = 2;
                                    break;
                                case LoadCombinationDirection.Forward:
                                default:
                                    // 基本 nStep のまま (3 回 retry)
                                    break;
                            }
                            if (baseNStep != configuredNStep)
                            {
                                // v22: Phase 1 事前検出による初期 nStep 増加分も TotalCalculationCount に
                                // 反映する。旧実装は再試行時のみ _bisectionExtraSteps を加算していたため、
                                // 事前検出で初期から大きな nStep を選んだ場合、進捗カウンタが超過表示
                                // （例: 207/180）になっていた。
                                System.Threading.Interlocked.Add(ref _bisectionExtraSteps, baseNStep - configuredNStep);
                                NotifyProgressPropertiesChanged();
                                string directionLabel = loadDirection == LoadCombinationDirection.CounterLoading ? "逆方向組合せ" : "順方向組合せ";
                                await AddLogAsync($"  🔎 荷重方向事前検出: {directionLabel} (αL={loadCombination.Alpha1:N2}, βU={loadCombination.Beta1:N2}, βL={loadCombination.Beta2:N2}) → 初期 nStep={baseNStep} (設定値 {configuredNStep} の代わり, 総ステップ数: {TotalCalculationCount})");
                            }
                        }
                        int nStep = baseNStep;
                        int bisectionAttempt = 0;
                        bool caseConverged = false;

                        // 並列モニタ: 初期 nStep 確定後に TotalSteps を更新 (BeginInvoke で fire-and-forget)
                        {
                            int initialNStep = nStep;
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => monitorItem.TotalSteps = initialNStep);
                        }

                        // v28 (2026-04-23) 改善ゲート: 前 attempt の平均反復数を保持し、
                        // retry 後にほとんど改善しない場合 (細分化が無効な構造的ラインサーチ制約)
                        // は以降の retry を抑制して無駄な計算を避ける。
                        double prevAttemptAvgIter = double.PositiveInfinity;
                        bool retryGateDisabled = false;

                        while (true)
                        {
                            // 再試行時の巻き戻し用に、結果リストのサイズをスナップショット
                            int snapAnaStepResults = caseModel.AnalysisStepResults?.Count ?? 0;
                            var snapNodeResults = new int[caseModel.Nodes.Count];
                            for (int i_ = 0; i_ < caseModel.Nodes.Count; i_++)
                                snapNodeResults[i_] = caseModel.Nodes[i_].NodeResults.Count;
                            var snapBeamResults = new int[caseModel.Beams.Count];
                            for (int i_ = 0; i_ < caseModel.Beams.Count; i_++)
                                snapBeamResults[i_] = caseModel.Beams[i_].BeamResults.Count;
                            var snapHSpringResults = new int[caseModel.HorizontalSoilSprings.Count];
                            for (int i_ = 0; i_ < caseModel.HorizontalSoilSprings.Count; i_++)
                                snapHSpringResults[i_] = caseModel.HorizontalSoilSprings[i_].HorizontalSpringResults.Count;
                            int[]? snapRotSpringResults = null;
                            if (caseModel.RotationalSprings != null)
                            {
                                snapRotSpringResults = new int[caseModel.RotationalSprings.Count];
                                for (int i_ = 0; i_ < caseModel.RotationalSprings.Count; i_++)
                                    snapRotSpringResults[i_] = caseModel.RotationalSprings[i_].RotationalSpringResults.Count;
                            }

                            caseModel.InitializeStates();

                            // 荷重ケース固有の剛体スレーブ割当を適用（回転ばねの有効/無効を切替）
                            ApplyPileHeadRigidBindingForLoadCase(caseModel, loadCase);

                            // 杭非線形ONのときだけ M–φ/M–θ をセット
                            if (loadCase.IsPileNonLinear)
                            {
                                SetupMPhiFromPileSectionForLoadCase(caseModel, loadCase);
                            }
                            // M–θ は常にセット（非線形OFFは剛 KThetaXY=KRigid）
                            SetupNonlinearMThetaForLoadCase(caseModel, loadCase);
                            // Y 案: caseModel のばね構成を targetModel のばねへスナップショット書き戻し。
                            // GraphViewModel / AnalysisResultTableService がケース別構成を可視化できるようにする。
                            SnapshotMThetaToOriginalSprings(caseModel, targetModel, loadCase, loadCombination, isLiquefaction);

                            // Phase 3: ロード増分の denominator を別変数で管理。
                            // ・nStep: for-loop 連番上限 (cut-back で「残りステップ分」だけ増える)
                            // ・effectiveDenom: SetVectorDF / InitializeSoilDisp に渡す増分分母 (cut-back で ×M)
                            // フラグ off ではどちらも nStep のまま動作 (= 従来挙動)。
                            int effectiveDenom = nStep;
                            SetVectorDF(caseModel, loadCase, loadCombination, level, iLC, effectiveDenom);
                            caseModel.MapOnVectorDF();
                            InitializeSoilDisplacementIncrement(caseModel, loadCase, loadCombination, level, isLiquefaction, effectiveDenom);

                            if (bisectionAttempt > 0)
                                await AddLogAsync($"  ♻ ケース再試行 ({bisectionAttempt}/{MAX_STEP_BISECTIONS}): ステップ数を {baseNStep} → {nStep} に増やして再計算 (以降の総ステップ数: {TotalCalculationCount})");

                            // このアテンプト内で未収束ステップが発生したか
                            bool caseFailedThisAttempt = false;

                            // v20 Phase 2: 物理的未収束と判定された場合、再試行ループから直接抜ける
                            bool physicallyUnconvergeable = false;

                            // v19: このアテンプト内で実際に実行されたステップ数（早期脱出時に使用）
                            int stepsExecutedInAttempt = 0;

                            // v15: 変位予測器用の変数（前ステップの変位増分を記録）
                            // v23 (C): 2 ステップ外挿のため前々ステップの増分も保持。
                            // 3 ステップ目以降は du_predict = du_prev + 0.5×(du_prev − du_prev_prev) で
                            // 加速度項（変位の曲率）を考慮した 2 次予測に切替。1 反復目の残差を 1 桁下げる効果。
                            MathNet.Numerics.LinearAlgebra.Vector<double>? prevStepDispIncrement = null;
                            MathNet.Numerics.LinearAlgebra.Vector<double>? prevPrevStepDispIncrement = null;

                            // v29 (2026-05-05): 退化トレンド検出 — 各ステップの反復数を履歴に蓄積し、
                            // 「直近 3 ステップで反復数が単調増加」かつ「最新 ≥ 60 反復」の複合条件で発火。
                            // v26 (案 B) の「最初 2 ステップの平均反復数 ≥ 18」は「収束しているが反復多い」
                            // を退化と誤判定する事案 (例題 9 + 基礎梁) があったため、複合条件で誤検知を避ける。
                            var stepIterHistory = new List<int>();

                            // Phase 3 (2026-05-06): step-level cut-back retry のための状態変数。
                            // ・lastSuccessCheckpoint: 直近成功ステップ終了時点の状態。null = 未捕捉。
                            // ・cutbackCount: この attempt 内で発動した cut-back 回数 (上限 MAX_CUTBACKS_PER_ATTEMPT)。
                            // フラグ off (`_useStepLevelCutback==false`) では capture せず、cut-back 経路も使用しない。
                            FEM.StepCheckpoint? lastSuccessCheckpoint = null;
                            int cutbackCount = 0;

                            // 引張定着筋なし半剛接合杭で引張軸力 NG をログ済みの杭番号 (1 ケース内重複抑制)。
                            // ローカル変数のため MDOP>1 並列でも安全。
                            var loggedNgTensionPileNos = new HashSet<int>();

                            for (int step = 0; step < nStep; step++)
                        {
                            await Task.Yield(); // ここでUIスレッドを解放
                            token.ThrowIfCancellationRequested();
                            _pauseEvent.Wait(token); // ここで一時停止を考慮

                            // 並列モニタ: 現ステップ / 総ステップ を更新 (BeginInvoke で UI をブロックしない)
                            {
                                int stepDisplay = step + 1;
                                int nStepDisplay = nStep;
                                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                                {
                                    monitorItem.CurrentStep = stepDisplay;
                                    monitorItem.TotalSteps = nStepDisplay;
                                });
                            }

                            // v15: ステップ開始時の変位を記録（予測器用）
                            var vectorDAtStepStart = caseModel.VectorD?.Clone();

                            // E3c-3: Interlocked で atomic にインクリメント (MDOP>1 対応)。
                            // curCalcNo は以降このステップ内で使用する (他スレッドが更新しても読み取りは安定)。
                            int curCalcNo = System.Threading.Interlocked.Increment(ref calcNoBox.Value);
                            // CurrentProgress 更新は WPF UI binding を触るため Dispatcher 経由で安全に
                            // (UI スレッド上なら即時実行、pool スレッドなら UI キューへ投げる)。
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => CurrentProgress = curCalcNo);

                            // 進捗を報告
                            progress?.Report(new Models.AnalysisProgress
                            {
                                Percentage = TotalCalculationCount > 0 ? (curCalcNo * 100.0 / TotalCalculationCount) : 0,
                                CurrentStep = $"レベル{level}-{iLC + 1}, {(isLiquefaction ? "液状化考慮" : "液状化非考慮")}, " +
                                             $"組合せ[{iLCOM + 1}], ステップ{step + 1}/{nStep}",
                                CurrentStepNumber = curCalcNo,
                                TotalSteps = TotalCalculationCount,
                                StartTime = startTime
                            });

                            string retryTag = bisectionAttempt > 0 ? $" 再試行{bisectionAttempt}/{MAX_STEP_BISECTIONS}" : "";
                            await AddLogAsync($"[{curCalcNo}/{TotalCalculationCount}{retryTag}]" + "荷重ケース：" + level + "-" + $"{iLC + 1}" + ", " + "液状化" + (isLiquefaction ? "考慮, " : "非考慮, ") +
                                $"[{iLCOM + 1}]" +
                                "αL:" + $"{loadCombination.Alpha1:N2}" +
                                ", βU:" + $"{loadCombination.Beta1:N2}" +
                                ", βL:" + $"{loadCombination.Beta2:N2}" +
                                ",　荷重ステップ" + (step + 1) + "/" + nStep +
                                (RelaxationFactor < 1.0 ? $", 緩和係数={RelaxationFactor:N2}" : ""));

                            // v15/v23: 予測ステップ（前ステップの変位増分があれば適用）
                            if (step > 0 && prevStepDispIncrement != null && caseModel.VectorD != null)
                            {
                                MathNet.Numerics.LinearAlgebra.Vector<double> predictorIncrement;
                                if (step > 1 && prevPrevStepDispIncrement != null)
                                {
                                    // v23 (C): 2 次外挿 u_{n+1} − u_n ≈ Δu_prev + 0.5×(Δu_prev − Δu_prev_prev)
                                    // 変位増分の変化率（加速度項）を取り込み、非線形が強いステップでも良い初期点になる。
                                    // 係数 0.5 は過剰外挿を避けるためのダンピング（全振幅 1.0 で共振する可能性あり）。
                                    var accel = prevStepDispIncrement - prevPrevStepDispIncrement;
                                    predictorIncrement = prevStepDispIncrement + 0.5 * accel;
                                }
                                else
                                {
                                    // 初回はデータ不足で 1 次予測（前ステップ増分の 80%）
                                    const double predictorFactor = 0.8;
                                    predictorIncrement = predictorFactor * prevStepDispIncrement;
                                }
                                caseModel.ApplyDispIncrement(predictorIncrement);

                                // 節点変位も更新（既存のラインサーチ用メソッドを流用）
                                UpdateNodeDisplacementsForLineSearch(caseModel, predictorIncrement);
                            }

                            caseModel.InitializeNormsqR_onNormsqFint();

                            // NaN診断: ステップ開始
                            // if (step == 0)
                            //     FEM.NaNDiagnostics.Begin();
                            // FEM.NaNDiagnostics.Log($"=== Step {step + 1}/{nStep}, LC={loadCase.LoadName}, Liq={isLiquefaction} ===");

                            int n_iteration = 1;

                            // v28 D: プロファイリング用 Stopwatch (ステップ局所、並列化時もワーカー内で安全)
                            var profStepTimer = System.Diagnostics.Stopwatch.StartNew();
                            long profFindKTicks = 0;
                            long profSolveTicks = 0;
                            long profLineSearchTicks = 0;
                            long profFindTTicks = 0;
                            int profFindKCalls = 0;
                            int profLineSearchCalls = 0;
                            int profLineSearchTrialsTotal = 0;
                            int profLineSearchTrialsMax = 0;          // 2026-05-13: 1 反復あたり LS trial の最大値
                            int profPlateauRefreshCount = 0;          // 2026-05-13: プラトー検知発動回数

                            // v28 F-new: CsparseLinearSolver の内部タイマー (CSC 変換 / 分解 / 代入) をリセット
                            FEM.CsparseLinearSolver.ResetInternalTimers();

                            UpdateSoilDisp(caseModel);
                            UpdateF(caseModel);

                            // 入力値＋応力解析結果モード: 前ステップのFxiを入力軸力に加算
                            if (UseAnalysisAxialForce && step > 0)
                            {
                                UpdateAxialForceFromAnalysis(caseModel);
                            }

                            // 引張定着筋なし半剛接合 (キャプテン/F.T.Pile/キャプリング) で軸力モードに応じた
                            // 軸力が引張になった杭を NG としてログに残す (杭ごとに 1 ケース内 1 回のみ)
                            await LogTensionForSemiRigidPilesAsync(caseModel, caseTag, step + 1, nStep, loggedNgTensionPileNos);

                            // 現ステップ軸力での M–φ 再解決は、杭非線形ONのときのみ
                            if (loadCase.IsPileNonLinear)
                            {
                                SetupMPhiByCurrentAxialForMiddleBeam(caseModel, loadCase);
                            }

                            caseModel.SetR();

                            // 反復なし簡易法の場合は1回で終了
                            int maxIterations = SkipIteration ? 1 : 100;
                            // 適応的緩和係数の初期化
                            double currentRelaxFactor = SkipIteration ? 1.0 : RelaxationFactor; // 簡易法は緩和なし
                            double prevResidual = caseModel.NormsROnNormsFint;
                            int consecutiveDecrease = 0; // 連続減少カウント

                            // v11: 停滞検出用の変数
                            int stagnationCount = 0;           // 停滞カウント（残差がほぼ変化しない回数）
                            const int STAGNATION_LIMIT = 15;   // 停滞判定の閾値回数
                            const double STAGNATION_RATIO = 0.98; // 残差比がこれ以上なら停滞とみなす
                            const double RELAXED_ALPHA = 1e-5; // 停滞時の緩和収束基準
                            double effectiveAlpha = alpha;     // 実効収束基準（停滞時に緩和）

                            // v12: 発散検出用の変数
                            double initialResidual = prevResidual;  // 初期残差（発散判定の基準）
                            double minResidualSeen = prevResidual;  // これまでの最小残差
                            int divergenceCount = 0;                // 発散検出回数
                            const double DIVERGENCE_RATIO = 100.0;  // 残差がこの倍率を超えたら発散とみなす
                            bool autoSwitchedToLineSearch = false;  // 自動でライン探索に切り替えたか
                            // v21 Phase 3 prep: インスタンスフィールド _useLineSearch の書き換えを廃止。
                            // 効果的ライン探索フラグは「ユーザー設定 UseLineSearch OR 自動切替 autoSwitchedToLineSearch」の union として
                            // 反復の各タイミングで再評価する（旧 _useLineSearch フィールド書き換え時と同じ意味論）。

                            // v13: 緩やかな発散検出用の変数
                            int slowDivergenceCount = 0;            // 連続増加回数
                            const int SLOW_DIVERGENCE_LIMIT = 10;   // この回数連続増加で緩やかな発散と判定

                            // v18: 長期未改善検出（counter-loading で residual が振動するケース対策）
                            // minResidualSeen が一定回数更新されない場合、収束基準を minSeen * 1.2 に緩和
                            int iterationsSinceMinUpdated = 0;
                            const int NO_IMPROVEMENT_LIMIT = 30;

                            // 2026-05-06: NR モード追跡 (Full NR / Modified NR)
                            //   K 行列を組み直した反復 = Full NR 反復 (kRebuildCount)
                            //   K を再利用した反復     = Modified NR 反復 (kReuseCount)
                            //   サマリーレポート / iter ログ / per-step プロファイル行に表示。
                            int kRebuildCount = 0;
                            int kReuseCount = 0;

                            // 2026-05-06 (D): 改善率ベース緩和判定。
                            // 残差が「微減し続ける」ケースでは minResidualSeen が更新され続け、
                            // iterationsSinceMinUpdated が NO_IMPROVEMENT_LIMIT に到達せず緩和が発火しない問題対策。
                            // 過去 SLOW_IMPROVEMENT_WINDOW 反復前の残差と現在残差を比較し、改善率が
                            // SLOW_IMPROVEMENT_THRESHOLD 未満なら「実質停滞」とみなして緩和を発火する。
                            const int SLOW_IMPROVEMENT_WINDOW = 30;       // 比較ウィンドウ
                            const double SLOW_IMPROVEMENT_THRESHOLD = 0.10; // 30 反復で 10% 未満の減少なら停滞
                            const double SLOW_IMPROVEMENT_RELAX_CAP = 1e-2; // この残差を超えていたら緩和しない (発散領域なので)
                            var residualHistory = new Queue<double>();

                            // v17: 長時間反復時の収束基準緩和（40反復以上 + 残差≦RELAXED_ALPHA で緩和）

                            // v16: 診断値をループ外に宣言（Modified NRフェーズでスキップしても前回値を保持）
                            double diagKMin = double.NaN, diagKMax = double.NaN;
                            double dispMaxAbs = double.NaN;
                            // v21 Phase 3 prep: 旧 _lastSpringKMin/_lastSpringKMax（インスタンスフィールド）は
                            // 反復間／ステップ間で値を保持する副作用があり、診断ログに前回値を表示していた。
                            // 同じ挙動を保つため、ここでステップ局所として宣言し、FindK を呼ばない反復では
                            // 前回反復の値をそのまま引き継ぐ。
                            double springKMin = double.NaN, springKMax = double.NaN;

                            // v27: 振動診断 — 支配 DOF (最大 |δu| の自由度) の符号反転 (flip-flop) 検出
                            // リミットサイクル型の発散（残差が単調減少せず周期的に跳ね返る）の原因 DOF を特定する。
                            // 反復間で同じ key が連続して現れ、かつ符号が逆転したら flip としてカウント。
                            List<(string Key, string NodeName, string DofName, double Value)> dominantDofs = null;
                            string prevDominantDofKey = null;
                            int prevDominantSign = 0;   // +1 / -1 / 0
                            int flipFlopCount = 0;

                            // v27: 案 A — リミットサイクル Aitken 平均化
                            // flip# が AITKEN_FLIP_TRIGGER に達したら、直近 AITKEN_HISTORY 反復の CumulativeDisp を
                            // 単純平均で置換し、周期振動のアトラクタ平均点に飛ばす。暴走防止のため MAX_FIRE 回まで。
                            // 検証結果 (2026-04-23): TRIGGER=2 (A-plus) や MAX_FIRE=4+リセット (A-rev) を試したが
                            // いずれも案 A より悪化。TRIGGER=3 / MAX_FIRE=2 / リセットなしが最適だった。
                            // 理由: 長期未改善検出が Aitken 後の最小残差 (≈10.9) をそのまま採用することで
                            // 緩和基準判定が有利になる。minResidualSeen をリセットすると逆効果。
                            const int AITKEN_HISTORY = 3;
                            const int AITKEN_FLIP_TRIGGER = 3;
                            const int AITKEN_MAX_FIRE = 2;
                            var recentCumulativeDisp = new Queue<Dictionary<string, NodeDisp>>();
                            int aitkenFiredCount = 0;

                            // v28 問題 A 診断: 反復間の状態変化 (M-φ セグメント, 土ばね降伏) を検出するスナップショット
                            // 各反復の末尾で現在状態を記録、次反復の Task.Run 外で差分ログを出力
                            // Beam.Name が全 beam 共通のため List インデックスで一意識別
                            int[] prevBeamSegments = null;  // index = beam idx, value = segment index
                            var prevYieldedSoilSprings = new HashSet<string>();    // "pileNo-nodeIdx-side" で識別
                            bool isFirstIterSnapshot = true;

                            // v29 (2026-04-27): Modified NR で K を再利用していると、M-φ セグメント変化や
                            // p-y 降伏状態変化が起きた直後に K が物理的に無効化され、Newton 方向が暴れて
                            // ラインサーチが α=0.05 級の小ステップに張り付き収束停滞する。
                            // 状態変化を検知したら次反復で K を強制再構築 (Full NR 化) して立て直す。
                            bool forceFullNRNextIter = false;

                            // 2026-05-12: 周期的 K 再構築 (Periodic K Refresh for MNR plateau)
                            //   状態変化が検知されないまま MNR が長く続いても、Modified NR の K は時間と共に
                            //   実状態と乖離して残差が緩慢にしか減らないことがある (実測: 5/49 のような NR/MNR 配分)。
                            //   N 反復ごとに残差低下率を測り、p > MNR_PLATEAU_RATIO ならプラトーとみなして K を強制再構築。
                            //   Jacobian の連続性は維持 (反復前後で同一 K か、明示的な新 K への置換のみ)。
                            //
                            //   2026-05-12 dynamic: 残差レベル別に interval を動的調整。
                            //   公差近接 (< 5×tol) では interval=10 で速く K refresh、遠方 (> 100×tol) では interval=20。
                            //   ハードケースの「あと一歩で収束」域で特に効果が高い。
                            const double MNR_PLATEAU_RATIO = 0.5;       // 残差が当該 interval で半分未満に減らないとプラトー
                            double residualAtMnrPhaseStart = double.PositiveInfinity;
                            int mnrIterSincePhaseStart = 0;

                            // 公差残差比に応じた動的 interval (近接: 短く / 遠方: 長く)
                            static int CalcMnrPlateauInterval(double currentRes, double tolAlpha)
                            {
                                if (tolAlpha <= 0) return 20;
                                double ratio = currentRes / tolAlpha;
                                if (ratio < 5.0) return 10;       // 公差近接: 速く K refresh で finishing
                                if (ratio < 100.0) return 15;     // 中間: 標準やや短め
                                return 20;                         // 遠方: 長く (MNR の早期収束を活かす)
                            }

                            while (caseModel.NormsROnNormsFint >= effectiveAlpha && n_iteration <= maxIterations)
                            {
                                // v21 Phase 3 prep: 効果的ライン探索フラグを「ユーザー設定 ∪ 自動切替」の union として毎反復評価
                                // （旧コード: auto-switch 時に _useLineSearch フィールドを true に書換 → UseLineSearch プロパティが true になる
                                //   を field 書換なしで再現。インスタンスフィールドを汚さないため並列化への準備になる）
                                bool effectiveUseLineSearch = UseLineSearch || autoSwitchedToLineSearch;
                                double usedRelaxFactor = currentRelaxFactor; // このステップで使う値を保存

                                // 2026-05-06: NR モード追跡用フラグ。RunWork 内で更新、ログ出力 (RunWork 外) で参照。
                                bool kRebuiltThisIter = false;

                                // v28: Mcr 同期 Mode 切替で新規クラック検出したばね名を収集 (Task.Run 外でログ出力するため)
                                var newlyCrackedSprings = new List<(string Name, double M, double Mcr)>();

                                // v28 問題 A 診断: 反復内で状態変化した要素を収集 (Beam は List idx で一意識別)
                                var mphiChanges = new List<(int BeamIdx, int Prev, int Curr)>();
                                var newlyYieldedSoilSprings = new List<string>();  // 新規降伏した p-y ばね
                                var newlyUnyieldedSoilSprings = new List<string>(); // 降伏解除された p-y ばね
                                int[] currentBeamSegments = null;  // index = beam idx
                                var currentYieldedSoilSprings = new HashSet<string>();

                                // 重い計算をバックグラウンドで実行（診断値もここで算出）
                                // E3c-3 tune: MDOP>1 時は RunWork が同期実行に切替えて Task.Run nesting を回避
                                await RunWork(() =>
                                {
                                    // トークンを投げて途中キャンセルを可能にする
                                    token.ThrowIfCancellationRequested();

                                    // N は荷重ケース一定だが、簡便に毎回解決しても可（コストは小）
                                    //SetupNonlinearMThetaForLoadCase(caseModel, loadCase);

                                    // Newton-Raphsonモード:
                                    // - Full NR: 常に毎反復で接線剛性+Kマトリクス更新
                                    // - Modified NR: 最初の FullNRIterations 回は Full NR、以降は K 再利用
                                    // v29: 直前反復で M-φ セグメント変化 / p-y 降伏変化を検知した場合は強制 Full NR
                                    bool useFullNR = !UseModifiedNewtonRaphson || n_iteration <= FullNRIterations || forceFullNRNextIter;
                                    forceFullNRNextIter = false; // フラグ消費

                                    if (loadCase.IsPileNonLinear && useFullNR)
                                    {
                                        // Full NR: ダンピングなし（正確なヤコビアンで2次収束）
                                        // Modified NR の初期反復: ダンピングあり（安定化）
                                        bool relaxTangent = UseModifiedNewtonRaphson;
                                        UpdateBeamMPhiTangent(caseModel, useRelaxation: relaxTangent);
                                    }

                                    // KTan 組立（戻り値で springK の min/max を受け取る）
                                    // v17: Modified NRモードの適応フェーズではKマトリクス組立をスキップ（高速化）
                                    if (useFullNR || !loadCase.IsPileNonLinear || n_iteration == 1)
                                    {
                                        long _tsFindK = System.Diagnostics.Stopwatch.GetTimestamp();
                                        (springKMin, springKMax) = FindK(iLC, caseModel);
                                        profFindKTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFindK;
                                        profFindKCalls++;
                                        kRebuiltThisIter = true;
                                        kRebuildCount++;

                                        // 初回反復時のみ剛性マトリクスの安定性チェック
                                        if (n_iteration == 1)
                                        {
                                            caseModel.ValidateStability(useEigenvalueCheck: false);
                                        }
                                    }
                                    else
                                    {
                                        kRebuiltThisIter = false;
                                        kReuseCount++;
                                    }

                                    // ラインサーチ or 通常の更新
                                    if (effectiveUseLineSearch)
                                    {
                                        // ラインサーチ: Newton方向を計算し、最適なステップ長を探索
                                        long _tsSolve = System.Diagnostics.Stopwatch.GetTimestamp();
                                        var newtonDir = SolveNewtonDirection(caseModel);
                                        profSolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSolve;

                                        double currentRes = caseModel.NormsROnNormsFint;

                                        // バックトラッキングラインサーチで最適αを見つける
                                        long _tsLS = System.Diagnostics.Stopwatch.GetTimestamp();
                                        double optimalAlpha = BacktrackingLineSearch(
                                            caseModel, newtonDir, currentRes, iLC, loadCase.IsPileNonLinear, out int _lsTrials);
                                        profLineSearchTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsLS;
                                        profLineSearchCalls++;
                                        profLineSearchTrialsTotal += _lsTrials;
                                        if (_lsTrials > profLineSearchTrialsMax) profLineSearchTrialsMax = _lsTrials;

                                        // 最適αでの状態は既にEvaluateResidualAtAlpha内で適用済み
                                        usedRelaxFactor = optimalAlpha; // ログ用に記録
                                    }
                                    else
                                    {
                                        // 通常の更新（緩和係数適用）
                                        long _tsSolve = System.Diagnostics.Stopwatch.GetTimestamp();
                                        SolveDdAndUpdateX(caseModel, usedRelaxFactor);
                                        profSolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsSolve;

                                        // 割線剛性更新（FindTの前に実行して、最新のK_secで内力を計算）
                                        if (loadCase.IsPileNonLinear)
                                            UpdateBeamMPhiSecant(caseModel);

                                        // 断面力・T更新と残差更新
                                        long _tsFindT = System.Diagnostics.Stopwatch.GetTimestamp();
                                        FindT(iLC, caseModel);
                                        profFindTTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tsFindT;

                                        caseModel.FindR();
                                    }

                                    /* NaN診断: 反復ごとのチェック
                                    FEM.NaNDiagnostics.SetIteration(n_iteration);
                                    if (!double.IsFinite(caseModel.NormsROnNormsFint))
                                    {
                                        FEM.NaNDiagnostics.LogNaN($"NormsROnNormsFint is NaN at iteration {n_iteration}!");
                                        FEM.NaNDiagnostics.CheckNodeDisplacements(caseModel.Nodes);
                                        FEM.NaNDiagnostics.CheckBeamForces(caseModel.Beams);
                                    } */

                                    // v21 Phase 3 prep: ばね剛性 min/max は FindK の戻り値から直接取得するため
                                    // ここでの再代入は不要（FindK を呼ばない分岐では NaN のまま）

                                    // v17: 診断値K対角は重い処理なので、最初の反復と5反復ごとのみ計算
                                    // Modified NRモードの適応フェーズではKが更新されないため計算頻度を下げる
                                    if (n_iteration == 1 || n_iteration % 5 == 0)
                                    {
                                        (diagKMin, diagKMax) = GetKDiagonalMiNMax(caseModel, isTan: true);
                                    }

                                    // 診断値: 代表自由度の |d| 最大値（節点の増分変位から）
                                    dispMaxAbs = GetMaxAbsIncrementalDisp(caseModel);

                                    // v27: 振動診断用 — 上位 3 DOF を取得（Ux/Uy/Uz/Rx/Ry/Rz 全成分対象）
                                    dominantDofs = GetTopIncrementalDofs(caseModel, 3);

                                    // v27: 案 A — CumulativeDisp スナップショットをキューに追加（Aitken 平均化用）
                                    var snap = new Dictionary<string, NodeDisp>(caseModel.Nodes.Count);
                                    foreach (var nd in caseModel.Nodes)
                                        snap[nd.Name] = nd.CumulativeDisp.Clone();
                                    recentCumulativeDisp.Enqueue(snap);
                                    while (recentCumulativeDisp.Count > AITKEN_HISTORY)
                                        recentCumulativeDisp.Dequeue();

                                    // v28: Mcr 同期 Mode 切替 (ヒステリシス付き)
                                    // 場所打ち RC 杭の杭頭回転ばねで |M| が Mcr を初めて超えた瞬間を検出し、
                                    // HasCrackedXY = true にラッチ。以降は post-crack curve を使用 (除荷しても戻らない)。
                                    // 閾値 0.999×Mcr で若干緩めてヒステリシスラッチを安定化。
                                    if (caseModel.RotationalSprings != null)
                                    {
                                        foreach (var rs in caseModel.RotationalSprings)
                                        {
                                            if (rs?.McrXY is null || rs.HasCrackedXY) continue;
                                            double mx = rs.CumulativeForce?.Mxj ?? 0.0;
                                            double my = rs.CumulativeForce?.Myj ?? 0.0;
                                            double mRes = Math.Sqrt(mx * mx + my * my);
                                            if (mRes >= rs.McrXY.Value * 0.999)
                                            {
                                                // v28 アプローチ I: クラック発生時点のモーメント方向 (= 回転方向) を記録
                                                rs.MarkCracked(mx, my);
                                                newlyCrackedSprings.Add((rs.Name ?? "<unnamed>", mRes, rs.McrXY.Value));
                                            }
                                        }
                                    }

                                    // v28 問題 A 診断: 杭体 M-φ セグメント変化 と 土ばね降伏状態変化を収集
                                    // iter 22→23 の残差爆発原因を特定する。Beam.Name が共通のため List idx で識別。
                                    if (caseModel.Beams != null)
                                    {
                                        int beamCount = caseModel.Beams.Count;
                                        currentBeamSegments = new int[beamCount];
                                        for (int idx = 0; idx < beamCount; idx++)
                                        {
                                            var beam = caseModel.Beams[idx];
                                            int curSeg = (beam.ResolvedCombinedCurve != null) ? beam.CurrentMPhiSegmentIndex : -1;
                                            currentBeamSegments[idx] = curSeg;
                                            if (!isFirstIterSnapshot
                                                && prevBeamSegments != null && idx < prevBeamSegments.Length
                                                && prevBeamSegments[idx] != curSeg
                                                && curSeg >= 0 && prevBeamSegments[idx] >= 0)  // -1 (no curve) はスキップ
                                            {
                                                mphiChanges.Add((idx, prevBeamSegments[idx], curSeg));
                                            }
                                        }
                                    }

                                    // 土ばね降伏状態: 各杭ノードについて |y| が yy を超えているか判定
                                    if (InputModel?.PileLayoutItems != null)
                                    {
                                        foreach (var pli in InputModel.PileLayoutItems)
                                        {
                                            if (pli.PileNodes == null || pli.SoilNodes == null) continue;
                                            var reactions = InputModel.ElementDivision?.SoilPiles?[pli.SoilPileAltNo - 1]?.HorizontalSoilReactions;
                                            if (reactions == null) continue;
                                            // VL ケースは iLC=-1 となるため >=0 チェック必須
                                            bool isFront = pli.IsFrontPiles != null && iLC >= 0 && iLC < pli.IsFrontPiles.Count && pli.IsFrontPiles[iLC];
                                            // E3b: case-local な PileNodes / SoilNodes を取得
                                            var pliPileNodes = caseModel.GetPileNodes(pli);
                                            var pliSoilNodes = caseModel.GetSoilNodes(pli);
                                            for (int i = 0; i < pliPileNodes.Count && i < pliSoilNodes.Count; i++)
                                            {
                                                var pn = pliPileNodes[i];
                                                var sn = pliSoilNodes[i];
                                                if (pn?.CumulativeDisp is null || sn?.CumulativeDisp is null) continue;
                                                var rel = pn.CumulativeDisp - sn.CumulativeDisp;
                                                double abs = Math.Sqrt(rel.Ux * rel.Ux + rel.Uy * rel.Uy);
                                                // i-1 (bottom side) と i (top side) の 2 層
                                                if (i > 0 && i - 1 < reactions.Count && reactions[i - 1].IsYieldedAtY(abs, isTop: false, isFront, loadCase.SoilNonlinearityMode))
                                                {
                                                    string key = $"{pli.No}-{i}-btm";
                                                    currentYieldedSoilSprings.Add(key);
                                                }
                                                if (i < reactions.Count && reactions[i].IsYieldedAtY(abs, isTop: true, isFront, loadCase.SoilNonlinearityMode))
                                                {
                                                    string key = $"{pli.No}-{i}-top";
                                                    currentYieldedSoilSprings.Add(key);
                                                }
                                            }
                                        }
                                    }

                                    // 変化集計 (2 反復目以降)
                                    if (!isFirstIterSnapshot)
                                    {
                                        foreach (var key in currentYieldedSoilSprings)
                                        {
                                            if (!prevYieldedSoilSprings.Contains(key))
                                                newlyYieldedSoilSprings.Add(key);
                                        }
                                        foreach (var key in prevYieldedSoilSprings)
                                        {
                                            if (!currentYieldedSoilSprings.Contains(key))
                                                newlyUnyieldedSoilSprings.Add(key);
                                        }
                                    }

                                    // ループ内の要所で再チェック（重い処理の長い段階がある場合はここに複数入れる）
                                    token.ThrowIfCancellationRequested();

                                });

                                // v28: Task.Run 外で新規クラックを UI ログに出力 (AddLogAsync はキュー方式で非同期 flush)
                                // 複数杭が同時クラックする場合 (重荷重で 1 反復目から全杭が Mcr 到達する等) は
                                // 集約して 1 行に圧縮する。単独クラックは従来通り詳細行で出力。
                                if (newlyCrackedSprings.Count == 1)
                                {
                                    var (name, mRes, mcr) = newlyCrackedSprings[0];
                                    await AddLogAsync($"　　📌 杭頭 RotSpring {name}: Mcr 到達 → クラック判定 (|M|={mRes:E3} ≥ Mcr={mcr:E3} kNm)、以降 post-crack curve 使用");
                                }
                                else if (newlyCrackedSprings.Count > 1)
                                {
                                    double mMin = newlyCrackedSprings.Min(x => x.Item2);
                                    double mMax = newlyCrackedSprings.Max(x => x.Item2);
                                    double mcrRef = newlyCrackedSprings[0].Item3;
                                    await AddLogAsync($"　　📌 杭頭 Mcr 到達 → クラック判定 {newlyCrackedSprings.Count} 本 (|M|=[{mMin:E3}, {mMax:E3}] ≥ Mcr={mcrRef:E3} kNm)、以降 post-crack curve 使用");
                                }

                                // v28 問題 A 診断: 杭体 M-φ セグメント変化 (集計ログ、最大 5 件の詳細)
                                if (mphiChanges.Count > 0 && currentBeamSegments != null)
                                {
                                    // セグメント分布サマリ (curve 持ちの beam のみ集計、-1 はスキップ)
                                    var segDist = new Dictionary<int, int>();
                                    foreach (int seg in currentBeamSegments)
                                    {
                                        if (seg < 0) continue;
                                        if (!segDist.ContainsKey(seg)) segDist[seg] = 0;
                                        segDist[seg]++;
                                    }
                                    string distStr = string.Join(", ", segDist.OrderBy(k => k.Key).Select(kv => $"seg{kv.Key}:{kv.Value}本"));
                                    int advances = mphiChanges.Count(c => c.Curr > c.Prev);
                                    int recedes = mphiChanges.Count(c => c.Curr < c.Prev);
                                    var topChanges = mphiChanges.Take(5).Select(c => $"beam[{c.BeamIdx}]:{c.Prev}→{c.Curr}");
                                    string detailStr = string.Join(", ", topChanges);
                                    string suffix = mphiChanges.Count > 5 ? $" ...他 {mphiChanges.Count - 5} 件" : "";
                                    await AddLogAsync($"　　  ▼ M-φ セグメント変化 {mphiChanges.Count} 件 (進行:{advances}, 戻り:{recedes}): [{detailStr}{suffix}] / 分布 [{distStr}]");
                                }

                                // v28 問題 A 診断: 土ばね p-y 降伏状態変化
                                if (newlyYieldedSoilSprings.Count > 0 || newlyUnyieldedSoilSprings.Count > 0)
                                {
                                    string yieldedSample = newlyYieldedSoilSprings.Count > 0
                                        ? string.Join(", ", newlyYieldedSoilSprings.Take(3)) + (newlyYieldedSoilSprings.Count > 3 ? $" ...他 {newlyYieldedSoilSprings.Count - 3}" : "")
                                        : "-";
                                    string unyieldedSample = newlyUnyieldedSoilSprings.Count > 0
                                        ? string.Join(", ", newlyUnyieldedSoilSprings.Take(3)) + (newlyUnyieldedSoilSprings.Count > 3 ? $" ...他 {newlyUnyieldedSoilSprings.Count - 3}" : "")
                                        : "-";
                                    await AddLogAsync($"　　  ▼ 土ばね p-y 降伏状態変化: 新規降伏 {newlyYieldedSoilSprings.Count} 件 [{yieldedSample}], 降伏解除 {newlyUnyieldedSoilSprings.Count} 件 [{unyieldedSample}] / 現時点降伏 {currentYieldedSoilSprings.Count} 件");
                                }

                                // v29: 状態変化を検知したら次反復で K を強制再構築 (Modified NR の K 再利用を一時停止)
                                if (UseModifiedNewtonRaphson && !isFirstIterSnapshot &&
                                    (mphiChanges.Count > 0 || newlyYieldedSoilSprings.Count > 0 || newlyUnyieldedSoilSprings.Count > 0))
                                {
                                    forceFullNRNextIter = true;
                                }

                                // 2026-05-12: 周期的 K 再構築 (MNR plateau detection)
                                //   状態変化を伴わない緩慢な収束 (K 陳腐化) を検知して NR 復帰させる。
                                //   - 直前の NR 反復後の残差を基準値として保存
                                //   - MNR が MNR_PLATEAU_CHECK_INTERVAL 反復経過した時点で
                                //     残差比率が MNR_PLATEAU_RATIO を超えていれば「プラトー」と判定し
                                //     forceFullNRNextIter を立てて K を再構築させる。
                                if (UseModifiedNewtonRaphson && !forceFullNRNextIter)
                                {
                                    if (kRebuiltThisIter)
                                    {
                                        // この反復で NR 再構築済 → MNR フェーズ追跡をリセット
                                        residualAtMnrPhaseStart = caseModel.NormsROnNormsFint;
                                        mnrIterSincePhaseStart = 0;
                                    }
                                    else
                                    {
                                        // MNR フェーズ進行中 — 動的 interval で plateau 判定
                                        mnrIterSincePhaseStart++;
                                        int dynamicInterval = CalcMnrPlateauInterval(caseModel.NormsROnNormsFint, effectiveAlpha);
                                        if (mnrIterSincePhaseStart >= dynamicInterval
                                            && residualAtMnrPhaseStart > 1e-20
                                            && caseModel.NormsROnNormsFint > MNR_PLATEAU_RATIO * residualAtMnrPhaseStart)
                                        {
                                            forceFullNRNextIter = true;
                                            profPlateauRefreshCount++;
                                            // mnrIterSincePhaseStart は次反復の kRebuiltThisIter で 0 にリセットされる
                                        }
                                    }
                                }

                                // スナップショット更新
                                prevBeamSegments = currentBeamSegments;
                                prevYieldedSoilSprings = currentYieldedSoilSprings;
                                isFirstIterSnapshot = false;

                                // v28 アプローチ I 採用後の 2026-04-23 更新:
                                // ω=0.01 強制低減 + forceDisableLineSearchNextIter の workaround は削除済。
                                // 方向ロック + ヒステリシス (CrackNx/CrackNy/ThetaProjMax) で根本解決したため、
                                // Newton は通常のライン探索 + adaptive relaxation で安定動作する。

                                if (StopONMaxDisplacement && !double.IsNaN(dispMaxAbs) && Math.Abs(dispMaxAbs) > MaxAllowedDisplacement)
                                {
                                    // ログに残す
                                    await AddLogAsync($"解析中止: 代表変位が閾値を超えました max|d|={dispMaxAbs:E3} > threshold={MaxAllowedDisplacement:E3}");

                                    // View に警告表示を依頼（UI スレッドでイベントを発火、BeginInvoke で worker をブロックしない）
                                    string warnMsg = $"解析を中止しました。\n代表変位が閾値 {MaxAllowedDisplacement} m を超えました（{dispMaxAbs:E3}）。";
                                    Application.Current?.Dispatcher.BeginInvoke(() => RequestShowWarning?.Invoke(warnMsg));

                                    // キャンセルを発行して呼び出し側で OperationCanceledException を処理させる
                                    _cancellationTokenSource?.Cancel();
                                    RequestClearProgressAnimation?.Invoke();
                                    throw new OperationCanceledException(token);
                                }


                                // v29 (2026-04-27): 反復ログを 1 行に統合 (旧: 残差/診断/支配DOF の 3 行)。
                                //   ‖R‖² / max|δu| / K range / spring k range / 支配 DOF を 1 行にまとめる。
                                //   行数 1/3 化でログのスクロール量が大幅減、視認性向上。
                                string stepSuffix;
                                if (effectiveUseLineSearch && usedRelaxFactor < 0.99)
                                    stepSuffix = $"α={usedRelaxFactor:N2}";
                                else if (currentRelaxFactor < 0.99)
                                    stepSuffix = $"ω={currentRelaxFactor:N2}";
                                else
                                    stepSuffix = "ω=1.00";

                                bool isConverged = caseModel.NormsROnNormsFint < alpha;
                                string compareSym = isConverged ? "≦" : ">";
                                string convergeFlag = isConverged ? " ✓Converged" : "";

                                // 支配 DOF (flip 検出を含む) — リミットサイクル原因 DOF の特定
                                // 案 A (2026-05-06): flip カウントを「DOF 種別 (Ry/Rx/Rz/Ux/Uy/Uz)」単位で集計。
                                // 旧実装は「同一 DOF キー (例: "InputNode-6:Ry") のみで連続カウント」だったため、
                                // InputNode-3:Ry ↔ InputNode-6:Ry ↔ FoundationNode-P28:Ry の交互支配で
                                // カウンタが毎回リセットされ、Aitken トリガに到達しない事案があった。
                                // 同種別 DOF 間の符号反転もリミットサイクルとして数えるよう拡張。
                                string dominantStr = "";
                                if (dominantDofs != null && dominantDofs.Count > 0)
                                {
                                    const double FLIP_THRESHOLD = 1e-10;
                                    var top = dominantDofs[0];
                                    int curSign = Math.Sign(top.Value);
                                    string curDofType = ExtractDofType(top.Key);
                                    string? prevDofType = prevDominantDofKey != null ? ExtractDofType(prevDominantDofKey) : null;

                                    string flipInfo = "";
                                    if (Math.Abs(top.Value) > FLIP_THRESHOLD
                                        && prevDofType != null && prevDofType == curDofType
                                        && prevDominantSign != 0 && curSign != 0
                                        && curSign != prevDominantSign)
                                    {
                                        flipFlopCount++;
                                        flipInfo = $" ⚠flip#{flipFlopCount}";
                                    }
                                    else if (prevDofType != curDofType)
                                    {
                                        // DOF 種別境界 (例: Ry ⇄ Ux) でリセット
                                        flipFlopCount = 0;
                                    }
                                    prevDominantDofKey = top.Key;
                                    prevDominantSign = curSign;

                                    static string SignedExp(double v) => (v >= 0 ? "+" : "") + v.ToString("E2");
                                    dominantStr = $"  ▶{top.Key}={SignedExp(top.Value)}{flipInfo}";
                                }

                                // 案 C (2026-05-05): 並列実行時 (MDOP > 1) は iter ログを間引いて UI 負荷を軽減。
                                //   常時ログ: 最初 3 反復、収束時、flip 検出時、5 の倍数反復
                                //   それ以外は省略 (詳細トレースは MDOP=1 で逐次実行時に取得する)
                                bool isParallelMode = MaxCaseDegreeOfParallelism > 1;
                                bool isFlipDetected = !string.IsNullOrEmpty(dominantStr) && dominantStr.Contains("⚠flip");
                                bool shouldLogIter = !isParallelMode
                                    || isConverged
                                    || n_iteration <= 3
                                    || isFlipDetected
                                    || n_iteration % 5 == 0;

                                if (shouldLogIter)
                                {
                                    // 2026-05-06: NR モード可視化。K 行列を組み直した反復 (Full NR) は "[NR]"、
                                    // 再利用した反復 (Modified NR) は "[MNR]" を末尾に付与。
                                    string nrTag = kRebuiltThisIter ? "[NR]" : "[MNR]";
                                    await AddLogAsync(
                                        $"{caseTag}iter {n_iteration,2} {nrTag} {stepSuffix}  " +
                                        $"‖R‖²={caseModel.NormsROnNormsFint:E2}{compareSym}{alpha:E1}{convergeFlag}  " +
                                        $"max|δu|={dispMaxAbs:E2}  " +
                                        $"K=[{diagKMin:E1},{diagKMax:E1}]  " +
                                        $"k=[{springKMin:E1},{springKMax:E1}]" +
                                        dominantStr);
                                }

                                // v27: 案 A — リミットサイクル Aitken 平均化
                                // 支配 DOF の flip が AITKEN_FLIP_TRIGGER 回連続検出されたら、
                                // 直近 AITKEN_HISTORY 反復の CumulativeDisp を単純平均で置き換えて周期の中心点に飛ばす。
                                // これで周期 2〜3 のリミットサイクルを破って収束に向かわせる。
                                // 暴走防止のため AITKEN_MAX_FIRE 回までに制限。
                                if (flipFlopCount >= AITKEN_FLIP_TRIGGER
                                    && aitkenFiredCount < AITKEN_MAX_FIRE
                                    && recentCumulativeDisp.Count >= AITKEN_HISTORY)
                                {
                                    // E3c-3 tune: MDOP>1 時は RunWork が同期実行 (nesting 回避)
                                    await RunWork(() =>
                                    {
                                        int historyCount = recentCumulativeDisp.Count;
                                        foreach (var nd in caseModel.Nodes)
                                        {
                                            double ux = 0, uy = 0, uz = 0, rx = 0, ry = 0, rz = 0;
                                            foreach (var hist in recentCumulativeDisp)
                                            {
                                                if (!hist.TryGetValue(nd.Name, out var d)) continue;
                                                ux += d.Ux; uy += d.Uy; uz += d.Uz;
                                                rx += d.Rx; ry += d.Ry; rz += d.Rz;
                                            }
                                            nd.CumulativeDisp = new NodeDisp(
                                                ux / historyCount, uy / historyCount, uz / historyCount,
                                                rx / historyCount, ry / historyCount, rz / historyCount);
                                            // 増分変位は 0 にリセット（平均化後は新規スタート扱い）
                                            nd.IncrementalDisp = new NodeDisp(0, 0, 0, 0, 0, 0);
                                        }
                                        // 内力と残差を再計算（次の反復で新しい残差が評価される）
                                        FindT(iLC, caseModel);
                                        caseModel.FindR();
                                    });

                                    aitkenFiredCount++;
                                    flipFlopCount = 0;
                                    prevDominantDofKey = null;
                                    prevDominantSign = 0;
                                    recentCumulativeDisp.Clear();

                                    // 注: minResidualSeen / iterationsSinceMinUpdated はリセットしない。
                                    // A-rev で検証したが、リセットすると長期未改善検出が機能しなくなり、
                                    // Aitken 後に一時的に到達した低い残差がそのまま最終値として採用されなくなって悪化した。
                                    // 現状はリセット無しで、Aitken → 減衰 → 停滞 → 長期未改善検出 → 緩和基準の
                                    // シーケンスで最小残差が最終値になる。

                                    await AddLogAsync($"　　🔄 Aitken 平均化 #{aitkenFiredCount}/{AITKEN_MAX_FIRE} 発動: 直近 {AITKEN_HISTORY} 反復の CumulativeDisp 平均で書換 → 残差={caseModel.NormsROnNormsFint:E2}");
                                }

                                // 2026-05-13: リミットサイクル早期諦め検知
                                //   症状: max|δu| が極めて小さい (変位が動かない) のに残差が大きく、DOF flip が繰り返される。
                                //         典型例は RC 杭頭 Mcr 境界付近の oscillation で、Aitken 平均化でも回復しない。
                                //   判定: (1) Aitken の最大発動回数を消費済み
                                //         (2) flipFlopCount が新たに閾値到達 (Aitken 後の再リセットを通過してまだ flip 連続)
                                //         (3) max|δu| < LIMIT_CYCLE_DU_FLOOR で「動かない」確認
                                //         (4) iter ≥ LIMIT_CYCLE_MIN_ITER で「十分試した」確認
                                //   行動: 反復ループを break して NG 扱いにし、retry (細分化) へ送る。
                                //         100 反復まで粘ってから諦めるよりも 30-50% 時短になる見込み。
                                const int LIMIT_CYCLE_MIN_ITER = 30;
                                const int LIMIT_CYCLE_FLIP_TRIGGER = 8;       // Aitken (3) より大きく
                                const double LIMIT_CYCLE_DU_FLOOR = 1e-7;     // 0.1µm 未満で「動いていない」
                                if (aitkenFiredCount >= AITKEN_MAX_FIRE
                                    && flipFlopCount >= LIMIT_CYCLE_FLIP_TRIGGER
                                    && !double.IsNaN(dispMaxAbs) && Math.Abs(dispMaxAbs) < LIMIT_CYCLE_DU_FLOOR
                                    && n_iteration >= LIMIT_CYCLE_MIN_ITER
                                    && caseModel.NormsROnNormsFint >= effectiveAlpha)
                                {
                                    await AddLogAsync(
                                        $"    ⛔ リミットサイクル検知: iter={n_iteration}, flip#{flipFlopCount}, " +
                                        $"max|δu|={dispMaxAbs:E2}m, 残差={caseModel.NormsROnNormsFint:E2} " +
                                        $"→ 早期諦めて retry へ移行");
                                    break;
                                }

                                // 適応的緩和係数の更新（UseAdaptiveRelaxation=trueの場合のみ）
                                double currentResidual = caseModel.NormsROnNormsFint;
                                if (UseAdaptiveRelaxation)
                                {
                                    double residualRatio = prevResidual > 1e-20 ? currentResidual / prevResidual : 1.0;

                                    if (residualRatio < 0.8) // 残差が20%以上減少 → 良好な収束
                                    {
                                        consecutiveDecrease++;
                                        // 2回連続で大幅減少したらω回復（最大1.0）
                                        if (consecutiveDecrease >= 2 && currentRelaxFactor < 1.0)
                                        {
                                            currentRelaxFactor = Math.Min(currentRelaxFactor * 1.3, 1.0);
                                            consecutiveDecrease = 0;
                                        }
                                    }
                                    else if (residualRatio > 1.02) // 残差が2%以上増加 → 即座にω減少
                                    {
                                        // 増加量に応じてω減少幅を調整（より積極的に）
                                        double reductionFactor = residualRatio > 1.5 ? 0.25 : (residualRatio > 1.1 ? 0.5 : 0.7);
                                        currentRelaxFactor = Math.Max(currentRelaxFactor * reductionFactor, 0.1);
                                        consecutiveDecrease = 0;
                                    }
                                    else if (residualRatio < 1.0) // 微減（0-20%）
                                    {
                                        consecutiveDecrease++;
                                        // 3回連続微減でもω小幅回復
                                        if (consecutiveDecrease >= 3 && currentRelaxFactor < 0.7)
                                        {
                                            currentRelaxFactor = Math.Min(currentRelaxFactor * 1.1, 0.7);
                                            consecutiveDecrease = 0;
                                        }
                                    }
                                    else // 停滞（1.0-1.02）
                                    {
                                        consecutiveDecrease = 0;
                                        // 停滞時もωを少し下げる
                                        currentRelaxFactor = Math.Max(currentRelaxFactor * 0.85, 0.15);
                                    }
                                }

                                // v12/v13: 発散検出 - 残差が初期値や最小値から大幅に増加した場合
                                if (currentResidual < minResidualSeen)
                                {
                                    minResidualSeen = currentResidual;  // 最小残差を更新
                                    slowDivergenceCount = 0;  // 最小更新時にリセット
                                    iterationsSinceMinUpdated = 0;  // v18: 改善があったのでリセット
                                }
                                else
                                {
                                    iterationsSinceMinUpdated++;  // v18: 未改善カウント進行
                                }

                                // v18: 長期未改善検出 - 振動パターンで stagnation/slow-divergence の
                                // どちらも triggers しない場合に最後のセーフティネットとして働く
                                // 緩和値は「現在残差 × 1.1」「最小残差 × 1.2」の大きい方を採用し、
                                // 今この反復でループが確実に抜けられるようにする（振動残差では minSeen*1.2 が不十分）
                                if (iterationsSinceMinUpdated >= NO_IMPROVEMENT_LIMIT && !SkipIteration)
                                {
                                    double relaxed = Math.Max(
                                        Math.Max(minResidualSeen * 1.2, currentResidual * 1.1),
                                        RELAXED_ALPHA);
                                    if (relaxed > effectiveAlpha)
                                    {
                                        effectiveAlpha = relaxed;
                                        await AddLogAsync($"  ⚠ 長期未改善検出: {NO_IMPROVEMENT_LIMIT}反復で最小残差 {minResidualSeen:E2} が更新されません。収束基準を緩和します ({alpha:E2}→{effectiveAlpha:E2})");
                                        iterationsSinceMinUpdated = 0;  // 再カウント
                                    }
                                }

                                // 2026-05-06 (D): 改善率ベース緩和判定
                                // 「微減し続ける」ケース (毎反復 minSeen 更新でも改善が遅い) を救済する。
                                // 過去 SLOW_IMPROVEMENT_WINDOW 反復前の残差と比較し、改善率が閾値未満で停滞判定。
                                residualHistory.Enqueue(currentResidual);
                                if (residualHistory.Count > SLOW_IMPROVEMENT_WINDOW)
                                {
                                    double residualPast = residualHistory.Dequeue();
                                    if (!SkipIteration
                                        && currentResidual > effectiveAlpha
                                        && currentResidual <= SLOW_IMPROVEMENT_RELAX_CAP
                                        && residualPast > 1e-30)
                                    {
                                        double improvement = (residualPast - currentResidual) / residualPast;
                                        if (improvement < SLOW_IMPROVEMENT_THRESHOLD)
                                        {
                                            // 改善率不足 → 現状を accept できるよう緩和
                                            double relaxed = Math.Max(currentResidual * 1.1, RELAXED_ALPHA);
                                            if (relaxed > effectiveAlpha)
                                            {
                                                effectiveAlpha = relaxed;
                                                await AddLogAsync($"  ⚠ 改善率不足: {SLOW_IMPROVEMENT_WINDOW} 反復で残差 {residualPast:E2}→{currentResidual:E2} ({improvement * 100:F1}% 減のみ)。収束基準を緩和 ({alpha:E2}→{effectiveAlpha:E2})");
                                                residualHistory.Clear();  // 緩和発火後はリセット
                                            }
                                        }
                                    }
                                }

                                // v17: 長時間反復時の収束基準緩和
                                // 40反復以上で残差が RELAXED_ALPHA 以下なら、十分収束したとみなす
                                // （M-φ/M-θ非線形で残差が振動停滞するケースへの対策）
                                if (n_iteration >= 40 && currentResidual <= RELAXED_ALPHA
                                    && effectiveAlpha < RELAXED_ALPHA)
                                {
                                    effectiveAlpha = RELAXED_ALPHA;
                                    await AddLogAsync($"  → 長時間反復: 残差 {currentResidual:E2} ≦ {RELAXED_ALPHA:E2} で収束基準を緩和 ({alpha:E2}→{effectiveAlpha:E2})");
                                }

                                bool isDiverging = currentResidual > initialResidual * DIVERGENCE_RATIO ||
                                                   currentResidual > minResidualSeen * DIVERGENCE_RATIO;

                                // v13: 緩やかな発散の検出（残差が連続して増加）
                                if (currentResidual > prevResidual * 1.001)  // 0.1%以上の増加
                                {
                                    slowDivergenceCount++;
                                    if (slowDivergenceCount >= SLOW_DIVERGENCE_LIMIT && !SkipIteration)
                                    {
                                        await AddLogAsync($"  ⚠ 緩やかな発散検出: {slowDivergenceCount}回連続で残差が増加しています (最小:{minResidualSeen:E2} → 現在:{currentResidual:E2})");

                                        // すでにライン探索を使用している場合は、収束基準を緩和
                                        if (effectiveUseLineSearch)
                                        {
                                            // 最小残差から5%以内なら収束とみなす
                                            double relaxedTarget = minResidualSeen * 1.05;
                                            if (currentResidual <= relaxedTarget && relaxedTarget > effectiveAlpha)
                                            {
                                                effectiveAlpha = Math.Max(currentResidual * 1.1, relaxedTarget);
                                                await AddLogAsync($"  → 収束基準を緩和します ({alpha:E2}→{effectiveAlpha:E2})");
                                            }
                                        }
                                        slowDivergenceCount = 0;  // リセットして再カウント
                                    }
                                }
                                else if (currentResidual < prevResidual * 0.999)  // 0.1%以上の減少
                                {
                                    slowDivergenceCount = 0;  // 改善があればリセット
                                }

                                if (isDiverging && !autoSwitchedToLineSearch && !effectiveUseLineSearch && !SkipIteration)
                                {
                                    divergenceCount++;
                                    if (divergenceCount >= 2)  // 2回連続で発散検出
                                    {
                                        // 自動的にライン探索に切り替え（ステップ局所の effectiveUseLineSearch のみ更新）
                                        effectiveUseLineSearch = true;
                                        autoSwitchedToLineSearch = true;
                                        currentRelaxFactor = 1.0;  // ライン探索時は緩和係数をリセット
                                        await AddLogAsync($"  ⚠ 発散検出: 残差が大幅に増加しました (初期:{initialResidual:E2} → 現在:{currentResidual:E2})。ライン探索に自動切り替えします。");
                                    }
                                }
                                else if (!isDiverging)
                                {
                                    divergenceCount = 0;  // 発散していなければカウントリセット
                                }

                                // v11: 停滞検出 - 残差がほぼ変化しない場合をカウント
                                double residualRatioForStagnation = prevResidual > 1e-20 ? currentResidual / prevResidual : 1.0;
                                if (residualRatioForStagnation >= STAGNATION_RATIO && residualRatioForStagnation <= 1.05)
                                {
                                    stagnationCount++;
                                    // 停滞が続き、残差が緩和基準以下なら収束基準を緩和
                                    if (stagnationCount >= STAGNATION_LIMIT && currentResidual <= RELAXED_ALPHA)
                                    {
                                        effectiveAlpha = RELAXED_ALPHA;
                                        await AddLogAsync($"  ⚠ 停滞検出: {stagnationCount}回連続で残差が改善しません。収束基準を緩和します ({alpha:E2}→{effectiveAlpha:E2})");
                                    }
                                    else if (stagnationCount >= STAGNATION_LIMIT * 2)
                                    {
                                        // 長期停滞の場合は強制的に収束基準を緩和
                                        effectiveAlpha = Math.Max(currentResidual * 1.1, RELAXED_ALPHA);
                                        await AddLogAsync($"  ⚠ 長期停滞検出: 収束基準を現在の残差に合わせて緩和します ({alpha:E2}→{effectiveAlpha:E2})");
                                    }
                                }
                                else if (residualRatioForStagnation < STAGNATION_RATIO)
                                {
                                    // 改善があればカウントリセット
                                    stagnationCount = 0;
                                }

                                prevResidual = currentResidual;

                                await Task.Yield(); // UIスレッドを解放
                                n_iteration += 1;

                                // 並列モニタ: NR 反復数を更新 (BeginInvoke、ワーカー非ブロック)
                                {
                                    int iterDisplay = n_iteration;
                                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                                        monitorItem.CurrentIteration = iterDisplay);
                                }
                            }

                            // Maximum iteration check
                            string dispInfo = !double.IsNaN(dispMaxAbs) ? $", max|d|={dispMaxAbs:E3}m" : "";
                            bool converged = !(n_iteration > maxIterations && caseModel.NormsROnNormsFint >= effectiveAlpha);
                            if (!converged)
                            {
                                double finalResidual = caseModel.NormsROnNormsFint;
                                await AddLogAsync($"  → 未収束: 最大反復回数 {maxIterations} に到達。残差ノルム={finalResidual:E3} (許容値={effectiveAlpha:E3}){dispInfo}");

                                // Phase 3 step-level cut-back: 直近成功ステップに巻き戻して、失敗ステップだけ 1/M に分割して再試行。
                                // 条件: フラグ on, checkpoint 取得済 (= step >= 1 で前ステップ収束), cut-back 上限未達。
                                //
                                // インデックスは「連番」を採用。step は常に +1 ずつ進む (cut-back でジャンプしない)。
                                // 結果リストの Step 番号も連番で隙間なく埋まる (下流コンシューマ互換のため)。
                                // 内部の「ロード増分分母」は effectiveDenom が別途管理し cut-back で ×M される。
                                if (_useStepLevelCutback
                                    && lastSuccessCheckpoint != null
                                    && cutbackCount < MAX_CUTBACKS_PER_ATTEMPT)
                                {
                                    int oldStep = step;             // 失敗ステップの連番 (= 直近成功ステップ + 1)
                                    int curNStep = nStep;            // 連番上限 (cut-back で増える)
                                    int oldEffDenom = effectiveDenom; // ロード増分分母 (cut-back で ×M)
                                    int newEffDenom = effectiveDenom * CUTBACK_DIVISOR;
                                    int remainingSteps = curNStep - oldStep;  // 失敗時の残ステップ (失敗自身を含む)
                                    int addedSteps = (CUTBACK_DIVISOR - 1) * remainingSteps;  // 細分化で追加されるステップ数

                                    await AddLogAsync($"  ↩ cut-back ({cutbackCount + 1}/{MAX_CUTBACKS_PER_ATTEMPT}): step{step + 1}/{curNStep} 未収束 → end-of-step{step} へ巻き戻し、残 {remainingSteps} ステップを 1/{CUTBACK_DIVISOR} 細分化 (denom {oldEffDenom}→{newEffDenom}, 連番上限 {curNStep}→{curNStep + addedSteps})");

                                    // ① state を end-of-step{step-1} に巻き戻し
                                    lastSuccessCheckpoint.Restore(caseModel);

                                    // ② 結果リストを成功ステップ末尾までトリム (= step エントリ分残す。0-indexed で step 個)
                                    int keepCount = lastSuccessCheckpoint.StepIndex + 1;
                                    while (caseModel.AnalysisStepResults.Count > keepCount)
                                        caseModel.AnalysisStepResults.RemoveAt(caseModel.AnalysisStepResults.Count - 1);
                                    foreach (var n in caseModel.Nodes)
                                        while (n.NodeResults.Count > keepCount)
                                            n.NodeResults.RemoveAt(n.NodeResults.Count - 1);
                                    foreach (var b in caseModel.Beams)
                                        while (b.BeamResults.Count > keepCount)
                                            b.BeamResults.RemoveAt(b.BeamResults.Count - 1);
                                    foreach (var hs in caseModel.HorizontalSoilSprings)
                                        while (hs.HorizontalSpringResults.Count > keepCount)
                                            hs.HorizontalSpringResults.RemoveAt(hs.HorizontalSpringResults.Count - 1);
                                    if (caseModel.RotationalSprings != null)
                                        foreach (var rs in caseModel.RotationalSprings)
                                            while (rs.RotationalSpringResults.Count > keepCount)
                                                rs.RotationalSpringResults.RemoveAt(rs.RotationalSpringResults.Count - 1);

                                    // ③ ロード増分分母を ×M、連番上限を +addedSteps で更新
                                    effectiveDenom = newEffDenom;
                                    nStep = curNStep + addedSteps;

                                    // ④ 増分を新分母で書換 (累積側は保持)
                                    SetVectorDF(caseModel, loadCase, loadCombination, level, iLC, effectiveDenom, resetCumulative: false);
                                    caseModel.MapOnVectorDF();
                                    InitializeSoilDisplacementIncrement(caseModel, loadCase, loadCombination, level, isLiquefaction, effectiveDenom, resetCumulative: false);

                                    // ⑤ 進捗カウンタを追加ステップ分インクリメント
                                    System.Threading.Interlocked.Add(ref _bisectionExtraSteps, addedSteps);
                                    NotifyProgressPropertiesChanged();

                                    // ⑥ 予測器・トレンド検出履歴をクリア (増分が変わったので無効)
                                    prevStepDispIncrement = null;
                                    prevPrevStepDispIncrement = null;
                                    stepIterHistory.Clear();

                                    // ⑦ for-loop の step を 1 巻き戻し → post-incr で oldStep に戻り、失敗ステップを最初の substep として再実行
                                    step = oldStep - 1;

                                    cutbackCount++;
                                    continue;  // step++ → 次イテレーションで substep 開始
                                }

                                caseFailedThisAttempt = true;
                            }
                            else
                            {
                                string relaxedNote = effectiveAlpha > alpha ? $" (緩和基準α={effectiveAlpha:E2})" : "";
                                await AddLogAsync($"{caseTag}  → Converged in {n_iteration} iterations. Residual norm={caseModel.NormsROnNormsFint:E3}{relaxedNote}{dispInfo}");

                                // v28 D: プロファイリング情報をステップ収束時に出力
                                profStepTimer.Stop();
                                double _tickToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                                double _findKMs = profFindKCalls > 0 ? (profFindKTicks * _tickToMs) / profFindKCalls : 0.0;
                                double _solveMs = profLineSearchCalls > 0 ? (profSolveTicks * _tickToMs) / profLineSearchCalls : (profSolveTicks * _tickToMs);
                                double _lsMs = profLineSearchCalls > 0 ? (profLineSearchTicks * _tickToMs) / profLineSearchCalls : 0.0;
                                double _findTMs = profFindTTicks * _tickToMs;
                                double _lsTrialAvg = profLineSearchCalls > 0 ? profLineSearchTrialsTotal / (double)profLineSearchCalls : 0.0;
                                double _totalSec = profStepTimer.Elapsed.TotalSeconds;
                                string _solverTag = $"[{FEM.CsparseLinearSolver.LastSuccessfulSolver}]";
                                double _cscMs = FEM.CsparseLinearSolver.CscBuildTicks * _tickToMs;
                                double _factMs = FEM.CsparseLinearSolver.FactorizeTicks * _tickToMs;
                                double _backSubMs = FEM.CsparseLinearSolver.SolveBackSubTicks * _tickToMs;
                                long _cholReuse = FEM.CsparseLinearSolver.CholeskyReuseCount;
                                string lsOrFindT = profLineSearchCalls > 0
                                    ? $"LS {_lsMs:F0}ms×{profLineSearchCalls} (avg {_lsTrialAvg:F1}, max {profLineSearchTrialsMax})"
                                    : $"FindT {_findTMs:F0}ms";
                                string plateauTag = profPlateauRefreshCount > 0
                                    ? $" ┃ Plateau-K×{profPlateauRefreshCount}"
                                    : "";
                                await AddLogAsync(
                                    $"{caseTag}  ⏱ total {_totalSec:F1}s ┃ " +
                                    $"K組立 {_findKMs:F0}ms×{profFindKCalls} ┃ " +
                                    $"Solve {_solveMs:F0}ms {_solverTag} (CSC={_cscMs:F0} 分解={_factMs:F0} 代入={_backSubMs:F0} re={_cholReuse}) ┃ " +
                                    lsOrFindT + plateauTag);

                                // v19: 緩和基準が本来の許容値から大きく逸脱している場合は、再試行対象とする
                                // （長期未改善／停滞検出で緩和はしたが、物理的にはまだ収束していない状態）
                                // ただし最終試行でない場合に限る（最終試行では緩和収束を受け入れる）
                                const double RELAX_ACCEPT_THRESHOLD = 1e-2;  // 残差 1% 未満なら緩和収束を受け入れる

                                // v20 Phase 2: 物理的未収束の判定
                                // 緩和基準が極端に大きい（>10）かつ bisectionAttempt>=1 (既に 1 度倍増している)なら
                                // これ以上倍増しても収束する見込みなし → 中止（「耐力超過の可能性」として記録）
                                const double PHYSICALLY_UNCONVERGEABLE_THRESHOLD = 10.0;
                                if (effectiveAlpha > PHYSICALLY_UNCONVERGEABLE_THRESHOLD && bisectionAttempt >= 1)
                                {
                                    await AddLogAsync($"    ⛔ 緩和基準 {effectiveAlpha:E2} が物理的未収束閾値 {PHYSICALLY_UNCONVERGEABLE_THRESHOLD:E2} を超過。耐力超過の可能性があり、これ以上の倍増は無効と判断");
                                    await AddLogAsync($"    → このケースは物理的未収束として記録します（解析は続行）");
                                    physicallyUnconvergeable = true;
                                    caseFailedThisAttempt = false;  // 再試行しない
                                }
                                else if (effectiveAlpha > RELAX_ACCEPT_THRESHOLD && bisectionAttempt < MAX_STEP_BISECTIONS && !retryGateDisabled)
                                {
                                    await AddLogAsync($"    → 緩和基準 {effectiveAlpha:E2} が許容水準 {RELAX_ACCEPT_THRESHOLD:E2} を超過。ステップ分割を増やして再試行対象とします");
                                    caseFailedThisAttempt = true;
                                }
                            }

                            // v29 (2026-04-27): ステップ単位の収束サマリー記録 (解析終了時にレポート出力)
                            {
                                StepStatus _status = !converged ? StepStatus.Unconverged
                                    : (physicallyUnconvergeable ? StepStatus.PhysicallyUnconverged
                                        : StepStatus.Converged);
                                double _elapsedSec = profStepTimer.Elapsed.TotalSeconds;
                                _stepSummaries.Add(new StepSummary(
                                    CaseTag: caseTag,
                                    Level: level,
                                    LoadCaseNo: iLC + 1,
                                    ComboNo: iLCOM + 1,
                                    IsLiquefaction: isLiquefaction,
                                    Step: step + 1,
                                    NStep: nStep,
                                    BisectionAttempt: bisectionAttempt,
                                    Iterations: n_iteration > maxIterations ? maxIterations : n_iteration,
                                    FinalResidual: caseModel.NormsROnNormsFint,
                                    EffectiveAlpha: effectiveAlpha,
                                    MaxDisp: double.IsNaN(dispMaxAbs) ? 0.0 : dispMaxAbs,
                                    Status: _status,
                                    ElapsedSec: _elapsedSec,
                                    KRebuildCount: kRebuildCount,
                                    KReuseCount: kReuseCount));
                            }

                            // v21 Phase 3 prep: 自動ライン探索はステップ局所の effectiveUseLineSearch で
                            // 処理するため、インスタンスフィールド _useLineSearch の復元は不要

                            // v15/v23: このステップの変位増分を記録（次ステップの予測器用）
                            // 2 次外挿のため前々ステップの増分も保持する
                            if (vectorDAtStepStart != null && caseModel.VectorD != null)
                            {
                                prevPrevStepDispIncrement = prevStepDispIncrement;
                                prevStepDispIncrement = caseModel.VectorD - vectorDAtStepStart;
                            }

                            // デバッグ: 杭頭変位・M-θばねの確認
                            if (step == 0 || step == nStep - 1)
                            {
                                var actionPt = caseModel.Nodes[0];
                                // Serilog.Log.Debug(
                                //     $"[Step{step}] ActionPoint Ux={actionPt.CumulativeDisp?.Ux:E3} Rx={actionPt.CumulativeDisp?.Rx:E3} Ry={actionPt.CumulativeDisp?.Ry:E3}");
                                foreach (var pile in InputModel.PileLayoutItems.Take(2))
                                {
                                    var rxy = caseModel.GetPileTopRotationalSpring(pile);
                                    var capNode = rxy?.NodeI;
                                    var pileHead = caseModel.GetPileNodes(pile)?.FirstOrDefault();
                                    double capRx = capNode?.CumulativeDisp?.Rx ?? 0;
                                    double pileRx = pileHead?.CumulativeDisp?.Rx ?? 0;
                                    double kRx = rxy?.KeTan?[3, 3] ?? -1;
                                    double springMx = rxy?.CumulativeForce?.Mxi ?? 0;
                                    // Serilog.Log.Debug(
                                    //     $"[Step{step}] Pile{pile.No} " +
                                    //     $"CapRx={capRx:E3} PileRx={pileRx:E3} dRx={pileRx - capRx:E3} " +
                                    //     $"kRx={kRx:E3} CurveXY={(rxy?.CurveXY != null ? $"{rxy.CurveXY.Points.Count}pts" : "null")} " +
                                    //     $"KthetaXY={rxy?.KthetaXY:E3} " +
                                    //     $"SpringMxI={rxy?.CumulativeForce?.Mxi:E3} SpringMxJ={rxy?.CumulativeForce?.Mxj:E3} " +
                                    //     $"SpringMyI={rxy?.CumulativeForce?.Myi:E3} SpringMyJ={rxy?.CumulativeForce?.Myj:E3}");
                                }
                            }

                            caseModel.AnalysisStepResults.Add(new(loadCase, loadCombination, isLiquefaction, step, n_iteration, caseModel.NormsROnNormsFint));
                            foreach (var node in caseModel.Nodes)
                                node.NodeResults.Add(new(loadCase, loadCombination, isLiquefaction, step, node));
                            foreach (var beam in caseModel.Beams)
                                beam.BeamResults.Add(new(loadCase, loadCombination, isLiquefaction, step, beam));
                            foreach (var spring in caseModel.HorizontalSoilSprings)
                                spring.HorizontalSpringResults.Add(new(loadCase, loadCombination, isLiquefaction, step, spring));
                            //foreach (var rotationalSpring in caseModel.RotationalSprings)
                            //    rotationalSpring.RotationalSpringResults.Add(new(loadCase, loadCombination, isLiquefaction, step, rotationalSpring));
                            if (caseModel.RotationalSprings != null)
                            {
                                foreach (var rotationalSpring in caseModel.RotationalSprings)
                                {
                                    rotationalSpring.RotationalSpringResults.Add(new(loadCase, loadCombination, isLiquefaction, step, rotationalSpring));
                                    // else: この荷重ケースでは回転ばねは存在するが「使用されなかった」ため結果を保存しない
                                }
                            }

                            // v19: このステップが完了したことを記録
                            stepsExecutedInAttempt = step + 1;

                            // Phase 3: cut-back retry のため、収束したステップ終了時点を checkpoint として保持
                            // (フラグ off では capture しない — メモリ/CPU コストゼロ)
                            if (_useStepLevelCutback)
                            {
                                lastSuccessCheckpoint = FEM.StepCheckpoint.Capture(caseModel, step);
                            }

                            // v29 (2026-05-05): 退化トレンド検出 (複合条件版) — 詳細仕様は
                            // CheckDegenerationTrend のドキュメントコメント参照。
                            stepIterHistory.Add(Math.Min(n_iteration, maxIterations));
                            var trend = CheckDegenerationTrend(
                                stepIterHistory, caseFailedThisAttempt, retryGateDisabled,
                                physicallyUnconvergeable, bisectionAttempt, MAX_STEP_BISECTIONS,
                                prevAttemptAvgIter, nStep);
                            caseFailedThisAttempt = trend.CaseFailedThisAttempt;
                            retryGateDisabled = trend.RetryGateDisabled;
                            prevAttemptAvgIter = trend.PrevAttemptAvgIter;
                            if (trend.LogMessage != null)
                                await AddLogAsync(trend.LogMessage);

                            // v20 Phase 2: 物理的未収束なら直ちに中止（再試行しない）
                            if (physicallyUnconvergeable)
                            {
                                int remainingPhys = nStep - (step + 1);
                                if (remainingPhys > 0)
                                {
                                    await AddLogAsync($"  ⛔ 物理的未収束のため残り {remainingPhys} ステップをスキップ");
                                    // v25: 未実行ステップ分を総ステップ数から差し引き、進捗バーを 100% 到達させる
                                    System.Threading.Interlocked.Add(ref _bisectionExtraSteps, -remainingPhys);
                                    NotifyProgressPropertiesChanged();
                                }
                                break;
                            }

                            // v19: 早期脱出 - 既に再試行対象と判定されており、再試行回数に余裕がある場合
                            // 残りステップは truncate で破棄されるので計算時間の無駄。即座に再試行へ移行する
                            // （再試行時は後段の _bisectionExtraSteps 調整で計上/相殺されるので、ここでは減算しない）
                            if (caseFailedThisAttempt && bisectionAttempt < MAX_STEP_BISECTIONS)
                            {
                                int remaining = nStep - (step + 1);
                                if (remaining > 0)
                                {
                                    await AddLogAsync($"  ⏩ 残り {remaining} ステップをスキップして再試行へ移行");
                                }
                                break;
                            }
                        }

                        // v19: ケース完了判定と再試行ロジック
                        // v20 Phase 2: 物理的未収束が検出された場合は即中止（再試行しない）
                        if (physicallyUnconvergeable)
                        {
                            await AddLogAsync($"  ⛔ 物理的未収束として確定（耐力超過の可能性）。このケースは再試行せず次へ進みます");
                            break;  // 諦めて次のケースへ
                        }
                        if (!caseFailedThisAttempt)
                        {
                            caseConverged = true;
                            break;  // retry while-loop を抜けて次のケースへ
                        }
                        if (bisectionAttempt >= MAX_STEP_BISECTIONS)
                        {
                            await AddLogAsync($"  ❌ 最大再試行回数 ({MAX_STEP_BISECTIONS}) 到達。このケースは未収束で確定 (最終 nStep={nStep})");
                            // v25: 最終アテンプトで未実行に終わったステップ分を総ステップ数から差し引く
                            int remainingAtMax = nStep - stepsExecutedInAttempt;
                            if (remainingAtMax > 0)
                            {
                                System.Threading.Interlocked.Add(ref _bisectionExtraSteps, -remainingAtMax);
                                NotifyProgressPropertiesChanged();
                            }
                            break;  // 諦めて次のケースへ
                        }

                        // 失敗アテンプトで蓄積された Results をスナップショット長まで巻き戻す
                        RollbackAttemptResults(caseModel,
                            snapAnaStepResults, snapNodeResults, snapBeamResults,
                            snapHSpringResults, snapRotSpringResults);

                        // v19: 総ステップ数の調整
                        // baseline は旧 nStep (=oldNStep) を計上済み
                        // 実際にこのアテンプトで実行したのは stepsExecutedInAttempt ステップ
                        // 次のアテンプトで新 nStep ステップを実行する
                        // → 調整 = (実行済 + 新 nStep) - 旧 nStep = stepsExecutedInAttempt + newNStep - oldNStep
                        //
                        // 2026-05-06 cut-back 対応: cut-back で nStep が膨張した状態 (例 16→23) で
                        // ×2 すると 46 になり、cut-back の効果と複合して指数的に成長する問題があった。
                        // 修正: 標準 retry では baseNStep × 2^attempt で常に「クリーンな」 nStep を使う。
                        // cut-back 内の膨張は今回 attempt 内で完結し、次 attempt には引き継がない。
                        int oldNStep = nStep;
                        bisectionAttempt++;
                        nStep = baseNStep * (1 << bisectionAttempt);  // 2^attempt × base (16,32,64,128,…)
                        System.Threading.Interlocked.Add(ref _bisectionExtraSteps, stepsExecutedInAttempt + nStep - oldNStep);
                        NotifyProgressPropertiesChanged();
                    }  // end retry while-loop

                    _ = caseConverged; // 抑制: 未使用警告（将来診断に利用する可能性）

                        // NaN診断: 荷重ケース完了
                        // FEM.NaNDiagnostics.End();

                        // E3c-2: caseModel (DeepCopy) で蓄積した結果を targetModel (主モデル) に merge
                        // E3c-3: 複数ワーカーが targetModel の ObservableCollection に append する
                        // 可能性があるため lock で atomic 化。逐次モードでは no-op。
                        lock (_caseMergeLock)
                        {
                            AnaModel.AppendCaseResultsToMain(
                                targetModel, caseModel,
                                caseSnapAnaStepResults,
                                caseSnapNodeResultCounts,
                                caseSnapBeamResultCounts,
                                caseSnapHSpringResultCounts,
                                caseSnapRotSpringResultCounts);
                        }
                        } // end try
                        finally
                        {
                            // 並列モニタ: ケース終了 (正常/例外/キャンセルいずれも) で Active から除去し Completed++
                            // BeginInvoke でワーカーをブロックせずに UI 更新を要求
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                ActiveCases.Remove(monitorItem);
                                ActiveCasesCount = ActiveCases.Count;
                                CompletedCaseCount++;  // setter 内で PendingCaseCount PropertyChanged も発火
                            });
                            // 診断ログ (hang 調査用): ケース完了時刻と所要時間を記録
                            if (_caseMDOP > 1)
                            {
                                double elapsedSec = (DateTime.UtcNow - _diagCaseStartUtc).TotalSeconds;
                                int tid = System.Threading.Thread.CurrentThread.ManagedThreadId;
                                await AddLogAsync($"{caseTag} ⚪ 完了 (Tid={tid}, {elapsedSec:F1}s, Active={ActiveCasesCount - 1})");
                            }
                        }
                        } // end local function RunThisCaseAsync

                        // E3c-3-enable: MDOP>1 なら Task.Run に投げ semaphore で throttle、
                        // MDOP=1 なら直接 await して逐次挙動を維持
                        if (_caseSemaphore != null)
                        {
                            await _caseSemaphore.WaitAsync(token);
                            _caseTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                            {
                                try { await RunThisCaseAsync(); }
                                finally { _caseSemaphore.Release(); }
                            }, token));
                        }
                        else
                        {
                            await RunThisCaseAsync();
                        }
                    }
                }
            }

            // E3c-3-enable: 並列投入された全ケースの完了を待つ (MDOP=1 では _caseTasks は空)
            if (_caseTasks.Count > 0)
            {
                await System.Threading.Tasks.Task.WhenAll(_caseTasks);
            }
            }
            finally
            {
                // E3c-3: MathNet 並列度を元に戻す + semaphore 解放
                MathNet.Numerics.Control.MaxDegreeOfParallelism = _origMathNetMDOP;
                _caseSemaphore?.Dispose();
                // hang 対策 (2026-04-26): MKL/OMP 環境変数を元に戻す
                if (_caseMDOP > 1)
                {
                    Environment.SetEnvironmentVariable("MKL_NUM_THREADS", _origMklNumThreads);
                    Environment.SetEnvironmentVariable("OMP_NUM_THREADS", _origOmpNumThreads);
                }
            }

            token.ThrowIfCancellationRequested();

            // === LastRunConfig 更新 (追加実行の互換性比較に使用) ===
            // 解析が正常完了した場合のみ更新。中断/エラーは既存値を保持。
            if (preTargetModel != null)
            {
                lock (_caseMergeLock)
                {
                    var snap = CaptureCurrentRunSnapshot();
                    snap.ExecutedCaseKeys = preTargetModel.AnalysisStepResults
                        .Select(r => new FEM.AnalysisRunSnapshot.CaseKey(
                            r.LoadCase.LoadName, r.LoadCombination.Name, r.IsLiquefaction))
                        .Distinct()
                        .ToList();
                    preTargetModel.LastRunConfig = snap;
                }
                RefreshCompletedCaseKeys();
            }

            // v29: ステップ単位の収束サマリーをレポート出力
            await OutputStepSummaryReport();

            // v29: 経過時間タイマー停止 (これ以降は最終値で固定表示)
            StopElapsedTimer();
            OnPropertyChanged(nameof(ElapsedTimeText));
            OnPropertyChanged(nameof(EstimatedRemainingText));

            await AddLogAsync("計算終了");

            // 並列モニタを閉じる (開いていれば)
            System.Windows.Application.Current?.Dispatcher.Invoke(() => RequestHideParallelMonitor?.Invoke());

            // 検定比の計算（解析完了後に一括処理）- 未完成のため一時無効化
            try
            {
                //ComputeCapacityRatiosForAllResults(targetModel);
                //await AddLogAsync("検定比の計算完了");
            }
            catch (Exception ex)
            {
                await AddLogAsync($"検定比の計算でエラー: {ex.Message}");
            }

            // ペナルティばねの精度検証
            await VerifyPenaltySpringAccuracy(targetModel);

            // 最終進捗を報告（100%完了）
            progress?.Report(new Models.AnalysisProgress
            {
                Percentage = 100,
                CurrentStep = "解析計算が完了しました",
                CurrentStepNumber = TotalCalculationCount,
                TotalSteps = TotalCalculationCount,
                StartTime = startTime
            });

            // Pass logs to MainWindowViewModel & Ribbon Tab selection
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // 2026-05-06: サマリーレポートがメインウィンドウのログに反映されない問題対策。
                        // OutputStepSummaryReport は AddLogAsync でキューに追加するだけ (Timer 駆動 flush)。
                        // SetLatestAnalysisLogs 時点で _logQueue に未 flush 行が残っていると欠落するため、
                        // ここで同期的にキューをドレインして CalculationLog に確実に反映してから渡す。
                        while (_logQueue.TryDequeue(out var line))
                        {
                            CalculationLog.Add(line);
                            if (_logTextBuilder.Length > 0) _logTextBuilder.Append(Environment.NewLine);
                            _logTextBuilder.Append(line);
                        }
                        _cachedLogText = _logTextBuilder.ToString();
                        OnPropertyChanged(nameof(CalculationLogText));

                        _mainWindowViewModel.SetLatestAnalysisLogs(CalculationLog);

                        if (Application.Current.MainWindow is PileDesign.Views.MainWindow mainWin)
                        {
                            // 明示的にタブを選択
                            mainWin.AnalysisResultRibbonTab.IsSelected = true;

                            // 必要ならフォーカスも当てる（視覚的に選択状態を確実にする）
                            mainWin.AnalysisResultRibbonTab.Focus();

                            // 表示用テーブルを最新化（MainWindowViewModel のメソッドを呼ぶ）
                            if (mainWin.DataContext is PileDesign.ViewModels.MainWindowViewModel mwvm)
                                mwvm.RefreshResultTablesFromLastStep();
                        }
                    }
                    catch
                    {
                        // UI 更新失敗は非致命。
                    }
                });
            }
            catch
            {
                // Dispatcher が利用できない場面は無視
            }

        }

    }
}

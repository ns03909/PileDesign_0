using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 個別矩形（基礎梁考慮）モード用 反復沈下解析ウィンドウの ViewModel。
    /// VerticalBeamCalculationViewModel と類似のレイアウト/フローで
    /// IterativeBeamSettlementService.Run を複数の荷重ケースに対し実行し結果を蓄積する。
    /// </summary>
    public partial class GroupSettlementWithBeamCalculationViewModel : ObservableObject, ICloseable
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        public event EventHandler RequestClose;
        public bool IsSaved { get; private set; }

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }
        public IRelayCommand ExecuteAnalysisCommand { get; }
        public IRelayCommand CancelAnalysisCommand { get; }

        public GroupSettlementWithBeamCalculationViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
            // 保存ボタンの有効/無効は Visibility (IsAnalysisExecuted) で制御するため CanExecute は常に true。
            // 荷重がすべて 0 kN でも (=ケース自体が空でも) 保存できる。
            OkCommand = new PileDesign.ViewModels.RelayCommand(_ => SaveAndClose());
            CancelCommand = new PileDesign.ViewModels.RelayCommand(_ => DiscardAndClose());
            ExecuteAnalysisCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(ExecuteAnalysisAsync);
            CancelAnalysisCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(CancelAnalysis);

            // 既に CaseRecords が存在する場合 (前回保存した結果あり) は読み込んで表示する
            LoadFromExistingCaseRecords();
        }

        /// <summary>
        /// InputModel.PileGroupSettlement.CaseRecords が既にある場合、それらを CaseResults / CalculationLog に読み込む。
        /// 解析を再実行せずに以前の結果を再表示できる。
        /// </summary>
        private void LoadFromExistingCaseRecords()
        {
            var pgs = InputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null || pgs.CaseRecords.Count == 0) return;

            foreach (var rec in pgs.CaseRecords.Where(r => r.IsBeamAware))
            {
                // CaseRecord → GroupSettlementWithBeamCaseResult へ変換
                var caseResult = new GroupSettlementWithBeamCaseResult
                {
                    LoadCaseName = rec.LoadCaseName,
                    IsConverged = rec.IsConverged,
                    IterationCount = rec.IterationCount,
                    FinalResidual = rec.FinalResidual,
                    ConvergedRectLoads = [.. rec.RectLoads.Select(r => r.Clone())],
                    NodeResults = new ObservableCollection<FEM.VerticalBeamNodeResult>(rec.NodeResults ?? []),
                    BeamResults = new ObservableCollection<FEM.VerticalBeamBeamResult>(rec.BeamResults ?? []),
                    IterationLog = new List<string>(rec.IterationLog ?? []),
                };
                if (InputModel.PileLayoutItems != null)
                {
                    foreach (var pile in InputModel.PileLayoutItems)
                    {
                        rec.PileSettlements_mm.TryGetValue(pile.PileNo, out double s);
                        rec.PileReactions_kN.TryGetValue(pile.PileNo, out double pi);
                        rec.SpringStiffness.TryGetValue(pile.PileNo, out double k);
                        caseResult.PileResults.Add(new GroupSettlementWithBeamPileResult
                        {
                            PileNo = pile.PileNo,
                            X = pile.Point3D.X,
                            Y = pile.Point3D.Y,
                            Reaction_kN = pi,
                            Settlement_mm = s,
                            SpringStiffness_kN_per_m = k,
                        });
                    }
                }
                CaseResults.Add(caseResult);

                foreach (var line in rec.IterationLog ?? Enumerable.Empty<string>())
                    CalculationLog.Add(line);
                CalculationLog.Add("");
            }

            if (CaseResults.Count > 0)
            {
                IsAnalysisExecuted = true;
                int idx = pgs.ActiveCaseIndex;
                if (idx >= 0 && idx < CaseResults.Count) SelectedCaseResult = CaseResults[idx];
                else SelectedCaseResult = CaseResults[0];
            }
        }

        private void SaveAndClose()
        {
            if (InputModel?.PileGroupSettlement != null && CaseResults.Count > 0)
            {
                const string thisLoadingType = "個別矩形（基礎梁考慮）";
                var pgs = InputModel.PileGroupSettlement;

                // 同じ LoadingType の既存レコードのみ削除 (他タイプの結果は保持)
                if (pgs.CaseRecords == null)
                    pgs.CaseRecords = [];
                for (int i = pgs.CaseRecords.Count - 1; i >= 0; i--)
                {
                    if (pgs.CaseRecords[i].LoadingType == thisLoadingType
                        || (string.IsNullOrEmpty(pgs.CaseRecords[i].LoadingType) && pgs.CaseRecords[i].IsBeamAware))
                        pgs.CaseRecords.RemoveAt(i);
                }

                // 新規レコードを追加 (各ケースのグリッドコンタも事前計算)
                var addedRecords = new List<GroupSettlementCaseRecord>();
                foreach (var cr in CaseResults)
                {
                    var record = new GroupSettlementCaseRecord
                    {
                        LoadCaseName = cr.LoadCaseName,
                        LoadingType = thisLoadingType,
                        IsBeamAware = true,
                        IsConverged = cr.IsConverged,
                        IterationCount = cr.IterationCount,
                        FinalResidual = cr.FinalResidual,
                        RectLoads = [.. cr.ConvergedRectLoads.Select(r => r.Clone())],
                        NodeResults = new List<FEM.VerticalBeamNodeResult>(cr.NodeResults),
                        BeamResults = new List<FEM.VerticalBeamBeamResult>(cr.BeamResults),
                        IterationLog = new List<string>(cr.IterationLog ?? []),
                        PileSettlements_mm = cr.PileResults.ToDictionary(p => p.PileNo, p => p.Settlement_mm),
                        PileReactions_kN = cr.PileResults.ToDictionary(p => p.PileNo, p => p.Reaction_kN),
                        SpringStiffness = cr.PileResults.ToDictionary(p => p.PileNo, p => p.SpringStiffness_kN_per_m),
                    };

                    // ケース別のグリッドコンタを Steinbrenner で計算 (収束した矩形荷重を入力)。
                    // 反復計算は各杭頭でのみ収束判定するため、収束時点では土層グリッドの沈下値が
                    // 計算されていない。ここで最終段階の矩形荷重を使って改めてグリッド全体の
                    // Steinbrenner を回しコンタ図を得る。
                    record.SettlementGridData = ComputeGridData(cr.ConvergedRectLoads, pgs);
                    pgs.CaseRecords.Add(record);
                    addedRecords.Add(record);
                }

                // 表示中の (SelectedCaseResult) ケースをアクティブにして既存表示と互換維持
                int activeLocal = SelectedCaseResult != null
                    ? CaseResults.IndexOf(SelectedCaseResult) : 0;
                if (activeLocal < 0 || activeLocal >= addedRecords.Count) activeLocal = 0;
                var activeRec = addedRecords[activeLocal];
                pgs.ActiveLoadingType = thisLoadingType;
                pgs.ActiveCaseIndex = pgs.CaseRecords.IndexOf(activeRec);

                // 主画面 Canvas のコンタ描画用: SettlementGridX/Y を再計算
                // (反復解析を直接実行した場合 PerformSettlementAnalysis を経由しないため、
                //  pgs.SettlementGridX/Y が空のままになり Canvas の早期リターンでコンタが描けない)
                pgs.SetGridX(_mainWindowViewModel.GroupPileSettlementXMin,
                             _mainWindowViewModel.GroupPileSettlementXMax,
                             _mainWindowViewModel.GroupPileSettlementXOffset,
                             _mainWindowViewModel.GroupPileSettlementXSpacing,
                             InputModel.GridXItems);
                pgs.SetGridY(_mainWindowViewModel.GroupPileSettlementYMin,
                             _mainWindowViewModel.GroupPileSettlementYMax,
                             _mainWindowViewModel.GroupPileSettlementYOffset,
                             _mainWindowViewModel.GroupPileSettlementYSpacing,
                             InputModel.GridYItems);

                // ActiveCase の RectLoads / SettlementGridData / 各杭沈下を legacy フィールドへ反映
                ApplyActiveCaseToLegacyFields(pgs, activeRec, InputModel?.PileLayoutItems);
            }
            IsSaved = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 指定ケースの沈下量を pgs.SettlementGridData / pile.GroupPileSettlement へコピーする。
        ///
        /// 杭は引数で受け取る。以前は <c>Application.Current.MainWindow.DataContext</c> から
        /// ViewModel を辿っていたが、モデルを同期するだけの処理が実行中のウィンドウに依存するうえ、
        /// <c>MainWindow</c> は UI スレッドの持ち物なので<b>別スレッドから呼ぶと例外になる</b>
        /// (テストからも呼べない)。呼び出し側はいずれも入力モデルを持っている。
        /// </summary>
        public static void ApplyActiveCaseToLegacyFields(
            PileGroupSettlement pgs,
            GroupSettlementCaseRecord record,
            IEnumerable<PileLayoutDataItem>? piles = null)
        {
            if (pgs == null || record == null) return;
            // pgs.RectLoads は利用者の入力なので上書きしない。
            // 収束後の荷重を見せたい場面は pgs.ActiveRectLoads (= このケースの荷重) を読む。
            //
            // コンタの複製 (pgs.SettlementGridData) には書かない。読み手はもう居らず、
            // 書くと保存ファイルに複製が復活してケース側の要素が $ref になる。
            // 空のまま残すことで、新しく保存するファイルからは中身が消える。

            // 杭ごとの沈下量も書かない。表示は結果 (記録) から引くので、
            // 表示中のケースが変わったことだけ知らせる。
            if (piles == null) return;
            foreach (var pile in piles) pile.NotifyGroupPileSettlementChanged();
        }

        private ObservableCollection<SettlementGridDataItem> ComputeGridData(
            ObservableCollection<RectLoad> rects, PileGroupSettlement pgs)
        {
            if (pgs?.SettlementSoilLayers == null) return [];
            // メイン VM のグリッド座標を取得して Steinbrenner で各点の沈下を計算
            var xs = PileGroupSettlement.GetCoord(
                _mainWindowViewModel.GroupPileSettlementXMin,
                _mainWindowViewModel.GroupPileSettlementXMax,
                _mainWindowViewModel.GroupPileSettlementXOffset,
                _mainWindowViewModel.GroupPileSettlementXSpacing,
                InputModel.GridXItems);
            var ys = PileGroupSettlement.GetCoord(
                _mainWindowViewModel.GroupPileSettlementYMin,
                _mainWindowViewModel.GroupPileSettlementYMax,
                _mainWindowViewModel.GroupPileSettlementYOffset,
                _mainWindowViewModel.GroupPileSettlementYSpacing,
                InputModel.GridYItems);
            return SettlementAnalysisService.CalculateGridSettlementsPublic(
                xs, ys, rects, pgs.SettlementSoilLayers);
        }


        /// <summary>
        /// canonical な RectLoad ソースを選ぶ。優先順位:
        ///   1. ラベルに "VL" を含むケース
        ///   2. SelectedCaseResult
        ///   3. CaseResults の先頭
        /// </summary>
        private GroupSettlementWithBeamCaseResult PickCanonicalCase()
        {
            if (CaseResults == null || CaseResults.Count == 0) return null;
            var vl = CaseResults.FirstOrDefault(c => c.LoadCaseName.Contains("VL"));
            if (vl != null) return vl;
            return SelectedCaseResult ?? CaseResults[0];
        }

        private void DiscardAndClose()
        {
            IsSaved = false;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // ── 解析パラメータ ──

        [ObservableProperty]
        private string _loadSource = "矩形荷重"; // "矩形荷重" or "杭軸力"

        public string[] LoadSourceOptions { get; } = ["矩形荷重", "杭軸力"];

        partial void OnLoadSourceChanged(string value)
        {
            // 矩形荷重ソースは VL 1 ケースのみ対応 (RectLoad は荷重ケース別に持たない)
            if (value == "矩形荷重")
            {
                AnalyzeLevel1 = false;
                AnalyzeLevel2 = false;
            }
            OnPropertyChanged(nameof(IsAxialSource));
        }

        public bool IsAxialSource => LoadSource == "杭軸力";

        [ObservableProperty]
        private bool _analyzeVL = true;

        [ObservableProperty]
        private bool _analyzeLevel1;

        [ObservableProperty]
        private bool _analyzeLevel2;

        [ObservableProperty]
        private int _maxIterations = 100;

        [ObservableProperty]
        private double _convergenceTolerance = 1e-6;

        [ObservableProperty]
        private double _kMin = 1e3;

        [ObservableProperty]
        private double _kMax = 1e10;

        // ── ライブステータス ──

        [ObservableProperty]
        private bool _isAnalysisRunning;

        [ObservableProperty]
        private bool _isAnalysisExecuted;

        [ObservableProperty]
        private double _currentProgress;

        [ObservableProperty]
        private string _statusText = "";

        // ── 結果格納 ──

        public ObservableCollection<string> CalculationLog { get; } = [];

        public ObservableCollection<GroupSettlementWithBeamCaseResult> CaseResults { get; } = [];

        [ObservableProperty]
        private GroupSettlementWithBeamCaseResult _selectedCaseResult;

        private CancellationTokenSource _cancellationTokenSource;

        // ── 解析実行 ──

        private async Task ExecuteAnalysisAsync()
        {
            // 入力データの整合性ゲート (杭体・地盤・寸法・配筋など)
            if (!PileDesign.Models.CheckInputData.ValidateForAnalysis(InputModel, "群杭沈下解析"))
                return;

            string error = ValidateInput();
            if (error != null)
            {
                MessageService.Show(error, "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsAnalysisRunning = true;
            IsAnalysisExecuted = false;
            CurrentProgress = 0;
            CaseResults.Clear();
            CalculationLog.Clear();
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // 解析対象ケースを構築
                var cases = BuildCaseList();
                int total = cases.Count;
                if (total == 0)
                {
                    AddLog("[WARN] 解析対象ケースが 1 つもありません。");
                    return;
                }

                for (int i = 0; i < total; i++)
                {
                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                    var (label, ppi) = cases[i];
                    StatusText = $"反復解析: {label} ({i + 1}/{total})";

                    var serviceResult = await Task.Run(() =>
                        IterativeBeamSettlementService.Run(
                            InputModel, ppi, label,
                            MaxIterations, ConvergenceTolerance, KMin, KMax),
                        _cancellationTokenSource.Token);

                    foreach (var line in serviceResult.Log)
                        AddLog(line);
                    AddLog("");

                    var caseResult = ToCaseResult(label, ppi, serviceResult);
                    Application.Current?.Dispatcher.Invoke(() => CaseResults.Add(caseResult));

                    CurrentProgress = (i + 1.0) / total * 100;
                }

                IsAnalysisExecuted = true;
                StatusText = "解析完了";
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (CaseResults.Count > 0) SelectedCaseResult = CaseResults[0];
                });
            }
            catch (OperationCanceledException)
            {
                AddLog("解析がキャンセルされました。");
                StatusText = "キャンセル";
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] 解析中に例外: {ex.Message}");
                MessageService.ShowError($"解析エラー", ex, "エラー");
                StatusText = "エラー";
            }
            finally
            {
                IsAnalysisRunning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void CancelAnalysis() => _cancellationTokenSource?.Cancel();

        private void AddLog(string line)
            => Application.Current?.Dispatcher.Invoke(() => CalculationLog.Add(line));

        // ── 入力検証 ──

        private string ValidateInput()
        {
            if (InputModel?.PileLayoutItems == null || InputModel.PileLayoutItems.Count == 0)
                return "杭配置データが定義されていません。";
            if (InputModel.FoundationBeamInput?.Beams == null || InputModel.FoundationBeamInput.Beams.Count == 0)
                return "基礎梁が定義されていません。";
            if (InputModel.PileGroupSettlement?.SettlementSoilLayers == null
                || InputModel.PileGroupSettlement.SettlementSoilLayers.Count == 0)
                return "群杭沈下用土層が 1 層以上必要です。";

            if (LoadSource == "矩形荷重")
            {
                var rects = InputModel.PileGroupSettlement.RectLoads;
                if (rects == null || rects.Count == 0 || rects.All(r => r.QA == 0))
                    return "矩形荷重が定義されていません (または全て 0)。";
                if (!AnalyzeVL)
                    return "矩形荷重ソースでは VL ケースのみ対象です。VL を ON にしてください。";
            }
            else // 杭軸力
            {
                if (!AnalyzeVL && !AnalyzeLevel1 && !AnalyzeLevel2)
                    return "少なくとも 1 つの荷重ケースを選択してください。";
                if (AnalyzeLevel1 && (InputModel.LoadCasesInput?.LoadCasesLevel1?.Count ?? 0) == 0)
                    return "レベル 1 荷重ケースが未定義です。";
                if (AnalyzeLevel2 && (InputModel.LoadCasesInput?.LoadCasesLevel2?.Count ?? 0) == 0)
                    return "レベル 2 荷重ケースが未定義です。";
            }
            return null;
        }

        // ── 荷重ケース構築 ──

        private List<(string Label, Dictionary<int, double> Ppi)> BuildCaseList()
        {
            var list = new List<(string, Dictionary<int, double>)>();
            var piles = InputModel.PileLayoutItems;

            if (LoadSource == "矩形荷重")
            {
                // 各 RectLoad の LinkedPileNo (= pile.PileNo) → QA を集約
                var ppi = piles.ToDictionary(p => p.PileNo, p => 0.0);
                foreach (var r in InputModel.PileGroupSettlement.RectLoads)
                {
                    if (r.LinkedPileNo > 0 && ppi.ContainsKey(r.LinkedPileNo))
                        ppi[r.LinkedPileNo] += r.QA;
                }
                list.Add(("矩形荷重 (VL)", ppi));
                return list;
            }

            // 杭軸力ソース
            if (AnalyzeVL)
            {
                var ppi = piles.ToDictionary(p => p.PileNo, p => p.AxialForceVL);
                list.Add(("杭軸力 VL", ppi));
            }
            if (AnalyzeLevel1)
            {
                var l1 = InputModel.LoadCasesInput.LoadCasesLevel1;
                for (int i = 0; i < l1.Count; i++)
                {
                    int idx = i;
                    var ppi = piles.ToDictionary(
                        p => p.PileNo,
                        p => (idx < (p.AxialForceLevel1s?.Count ?? 0)) ? p.AxialForceLevel1s[idx] : 0.0);
                    list.Add(($"L1-{i + 1}: {l1[i].LoadName}", ppi));
                }
            }
            if (AnalyzeLevel2)
            {
                var l2 = InputModel.LoadCasesInput.LoadCasesLevel2;
                for (int i = 0; i < l2.Count; i++)
                {
                    int idx = i;
                    var ppi = piles.ToDictionary(
                        p => p.PileNo,
                        p => (idx < (p.AxialForceLevel2s?.Count ?? 0)) ? p.AxialForceLevel2s[idx] : 0.0);
                    list.Add(($"L2-{i + 1}: {l2[i].LoadName}", ppi));
                }
            }
            return list;
        }

        private GroupSettlementWithBeamCaseResult ToCaseResult(string label,
            Dictionary<int, double> ppi, IterativeBeamSettlementResult sr)
        {
            var caseResult = new GroupSettlementWithBeamCaseResult
            {
                LoadCaseName = label,
                IsConverged = sr.Converged,
                IterationCount = sr.IterationCount,
                FinalResidual = sr.FinalResidual,
                ConvergedRectLoads = [.. sr.ConvergedRectLoads.Select(r => r.Clone())],
                NodeResults = new ObservableCollection<FEM.VerticalBeamNodeResult>(sr.NodeResults),
                BeamResults = new ObservableCollection<FEM.VerticalBeamBeamResult>(sr.BeamResults),
                IterationLog = new List<string>(sr.Log),
            };

            foreach (var pile in InputModel.PileLayoutItems)
            {
                int pileNo = pile.PileNo;
                double inputLoad = ppi.TryGetValue(pileNo, out double pp) ? pp : 0;
                double reaction = sr.PileReactions.TryGetValue(pileNo, out double pi) ? pi : 0;
                double s2_m = sr.BeamSettlement.TryGetValue(pileNo, out double s2) ? s2 : 0;
                double s1_m = sr.SteinbrennerSettlement.TryGetValue(pileNo, out double s1) ? s1 : 0;
                double k = sr.SpringStiffness.TryGetValue(pileNo, out double kv) ? kv : 0;

                caseResult.PileResults.Add(new GroupSettlementWithBeamPileResult
                {
                    PileNo = pileNo,
                    X = pile.Point3D.X,
                    Y = pile.Point3D.Y,
                    InputLoad_kN = inputLoad,
                    Reaction_kN = reaction,
                    Settlement_mm = s2_m * 1000.0,
                    SteinbrennerSettlement_mm = s1_m * 1000.0,
                    SpringStiffness_kN_per_m = k,
                });
            }

            return caseResult;
        }
    }

    public class GroupSettlementWithBeamPileResult : ObservableObject
    {
        public int PileNo { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double InputLoad_kN { get; set; }
        public double Reaction_kN { get; set; }
        public double Settlement_mm { get; set; }
        public double SteinbrennerSettlement_mm { get; set; }
        public double SpringStiffness_kN_per_m { get; set; }
    }

    public class GroupSettlementWithBeamCaseResult : ObservableObject
    {
        public string LoadCaseName { get; set; } = "";
        public bool IsConverged { get; set; }
        public int IterationCount { get; set; }
        public double FinalResidual { get; set; }
        public ObservableCollection<GroupSettlementWithBeamPileResult> PileResults { get; set; } = [];
        public ObservableCollection<RectLoad> ConvergedRectLoads { get; set; } = [];
        public ObservableCollection<FEM.VerticalBeamNodeResult> NodeResults { get; set; } = [];
        public ObservableCollection<FEM.VerticalBeamBeamResult> BeamResults { get; set; } = [];
        /// <summary>反復ログ (1 行/エントリ)。永続化用。</summary>
        public List<string> IterationLog { get; set; } = [];

        public double TotalInputLoad_kN => PileResults?.Sum(p => p.InputLoad_kN) ?? 0;
        public double TotalReaction_kN => PileResults?.Sum(p => p.Reaction_kN) ?? 0;
        public double BalanceDifference_kN => TotalReaction_kN - TotalInputLoad_kN;
        public double BalanceRatio => TotalInputLoad_kN != 0 ? TotalReaction_kN / TotalInputLoad_kN : 0;
        public double MaxSettlement_mm => PileResults?.DefaultIfEmpty(new GroupSettlementWithBeamPileResult())
                                                     .Max(p => p.Settlement_mm) ?? 0;
        public double MinSettlement_mm => PileResults?.DefaultIfEmpty(new GroupSettlementWithBeamPileResult())
                                                     .Min(p => p.Settlement_mm) ?? 0;
    }
}

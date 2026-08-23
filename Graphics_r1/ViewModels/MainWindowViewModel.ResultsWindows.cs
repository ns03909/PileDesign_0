using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using static PileDesign.Views.AutoIsFrontPilesWindow;
using static PileDesign.Views.EditPileLayoutWindow;
using static PileDesign.Views.MoveCopyWindow;
using Point = System.Windows.Point;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Serilog;

namespace PileDesign.ViewModels
{
    // MainWindowViewModel partial: 解析結果の表示（計算書/グラフ/テーブル/ログ/検定ウィンドウ・結果テーブル構築）
    public partial class MainWindowViewModel
    {
        // 計算書出力ウィンドウ表示メソッド
        [RelayCommand]
        private void OpenDocxOutputWindow()
        {
            try
            {
                // StackOverflow 等のプロセス即死時に到達位置を特定するためのブレッドクラム
                // (File シンクはイベント毎フラッシュなので直前の行までは必ず残る)
                Log.Information("[Docx] 出力設定ウィンドウ: 解析済みフラグ更新開始");

                // 水平解析済みの荷重ケース・荷重組合せ・液状化条件を判定
                UpdateDocxOutputAnalyzedFlags();

                Log.Information("[Docx] 出力設定ウィンドウ: 生成・表示");
                var dockxOutputOptionWindow = new DocxOutputWindow(this)
                {
                    Owner = System.Windows.Application.Current?.MainWindow,
                };
                dockxOutputOptionWindow.ShowDialog();
                Log.Information("[Docx] 出力設定ウィンドウ: クローズ");
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "計算書出力ウィンドウ の表示に失敗", "計算書出力ウィンドウ");
                MessageService.Show(GuardMessages.WindowOpenFailed("計算書出力ウィンドウ"), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// AnalysisStepResults を参照し、各荷重ケース・荷重組合せ・液状化条件の解析済みフラグを設定。
        ///
        /// ⚠ 注意: IsApplicable には触らない。
        ///   - IsApplicable は「ユーザーが解析対象に含めたい」という入力意図 (CanExecuteAnalysis でも参照)
        ///   - IsAnalyzed は「実際に解析が済んでいるか」の結果状態
        ///   両者を混同して IsApplicable=false に強制すると、未解析時に DocxOutputWindow を開いただけで
        ///   F9 ボタンが永久に押せなくなる。docx 出力でフィルタする場合は IsAnalyzed (もしくは
        ///   解析結果存在チェック) のみで判定する。
        /// </summary>
        private void UpdateDocxOutputAnalyzedFlags()
        {
            var results = CurrentModel?.AnalysisStepResults;
            if (results == null || results.Count == 0)
            {
                // 解析結果なし → すべて IsAnalyzed=false に
                foreach (var lc in CurrentInputModel.LoadCasesInput.LoadCasesLevel1) lc.IsAnalyzed = false;
                foreach (var lc in CurrentInputModel.LoadCasesInput.LoadCasesLevel2) lc.IsAnalyzed = false;
                foreach (var comb in CurrentInputModel.LoadCasesInput.LoadCombinations) comb.IsAnalyzed = false;
                DocxOutput.IsLiquefactionYesAnalyzed = false;
                DocxOutput.IsLiquefactionNoAnalyzed = false;
                DocxOutput.IncludeOutputLiquefactionYes = false;
                DocxOutput.IncludeOutputLiquefactionNo = false;
                return;
            }

            // 解析済み荷重ケース名のセット
            var analyzedLoadCaseNames = new HashSet<string>(
                results.Where(r => r.LoadCase != null).Select(r => r.LoadCase.LoadName));

            foreach (var lc in CurrentInputModel.LoadCasesInput.LoadCasesLevel1)
                lc.IsAnalyzed = analyzedLoadCaseNames.Contains(lc.LoadName);
            foreach (var lc in CurrentInputModel.LoadCasesInput.LoadCasesLevel2)
                lc.IsAnalyzed = analyzedLoadCaseNames.Contains(lc.LoadName);

            // 解析済み荷重組合せ名のセット
            var analyzedCombNames = new HashSet<string>(
                results.Where(r => r.LoadCombination != null).Select(r => r.LoadCombination.Name));

            foreach (var comb in CurrentInputModel.LoadCasesInput.LoadCombinations)
                comb.IsAnalyzed = analyzedCombNames.Contains(comb.Name);

            // 液状化条件
            DocxOutput.IsLiquefactionYesAnalyzed = results.Any(r => r.IsLiquefaction);
            DocxOutput.IsLiquefactionNoAnalyzed = results.Any(r => !r.IsLiquefaction);
            DocxOutput.IncludeOutputLiquefactionYes = DocxOutput.IsLiquefactionYesAnalyzed;
            DocxOutput.IncludeOutputLiquefactionNo = DocxOutput.IsLiquefactionNoAnalyzed;
        }

        // オプション表示メソッド
        [RelayCommand]
        private static void OpenOptionWindow()
        {
            try
            {
                var optionWindow = new OptionWindow();
                optionWindow.Show();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "オプションウィンドウ の表示に失敗", "オプションウィンドウ");
                MessageService.Show(GuardMessages.WindowOpenFailed("オプションウィンドウ"), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 解析結果が1つでも存在するか（バインド用プロパティ）
        // 反復解析 (HasGroupSettlementBeamAwareCases) も含めて判定する
        public bool HasAnyAnalysisResult
            => IsHorizontalAnalysisDone || IsVerticalAnalysisDone
               || IsGroupPileSettlementAnalysisDone || IsVerticalBeamAnalysisDone
               || HasGroupSettlementBeamAwareCases;

        // コマンド状態一括更新ヘルパ
        private void RaiseResultCommandsCanExecute()
        {
            if (OpenTableWindowCommand is ToolkitRelayCommand tc) tc.NotifyCanExecuteChanged();
            OpenGraphWindowCommand?.NotifyCanExecuteChanged();
            OpenLogWindowCommand?.NotifyCanExecuteChanged();
            OpenEvaluationWindowCommand?.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanOpenGraphWindow))]
        private void OpenGraphWindow()
        {
            try
            {
                if (!HasAnyAnalysisResult) return;

                var viewModel = new GraphViewModel(this)
                {
                    IsHorizontalAnalysisDone = this.IsHorizontalAnalysisDone,
                    IsVerticalAnalysisDone = this.IsVerticalAnalysisDone,
                    IsGroupPileSettlementAnalysisDone = this.IsGroupPileSettlementAnalysisDone
                };
                viewModel.Initialize();

                var graphWindow = new GraphWindow(viewModel);
                graphWindow.Show();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "グラフウィンドウ の表示に失敗", "グラフウィンドウ");
                MessageService.Show(GuardMessages.WindowOpenFailed("グラフウィンドウ"), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private bool CanOpenGraphWindow() => HasAnyAnalysisResult;


        // フィールド追加
        private readonly AnalysisResultTableService _tableService = new();

        // プロパティ
        public IReadOnlyList<ResultTable> LatestResultTables { get; private set; } = [];

        // 解析ログ（種別別に保持）
        public ObservableCollection<string> LatestAnalysisLogs { get; private set; } = [];
        public ObservableCollection<string> HorizontalAnalysisLogs { get; private set; } = [];
        public ObservableCollection<string> VerticalBeamAnalysisLogs { get; private set; } = [];

        public void SetLatestAnalysisLogs(IReadOnlyList<string> logs)
        {
            HorizontalAnalysisLogs.Clear();
            foreach (var log in logs)
                HorizontalAnalysisLogs.Add(log);

            // 統合ログも更新
            RebuildLatestAnalysisLogs();
        }

        public void SetVerticalBeamAnalysisLogs(IReadOnlyList<string> logs)
        {
            VerticalBeamAnalysisLogs.Clear();
            foreach (var log in logs)
                VerticalBeamAnalysisLogs.Add(log);

            RebuildLatestAnalysisLogs();
        }

        private void RebuildLatestAnalysisLogs()
        {
            LatestAnalysisLogs.Clear();
            if (HorizontalAnalysisLogs.Count > 0)
            {
                foreach (var log in HorizontalAnalysisLogs)
                    LatestAnalysisLogs.Add(log);
            }
            if (VerticalBeamAnalysisLogs.Count > 0)
            {
                if (LatestAnalysisLogs.Count > 0) LatestAnalysisLogs.Add("");
                LatestAnalysisLogs.Add("=== 基礎梁考慮沈下解析 ===");
                foreach (var log in VerticalBeamAnalysisLogs)
                    LatestAnalysisLogs.Add(log);
            }
            OnPropertyChanged(nameof(LatestAnalysisLogs));
            OpenLogWindowCommand?.NotifyCanExecuteChanged();
        }

        // 解析完了後 (既存処理内末尾に追加)
        private void OnAnalysisFinished(AnalysisStepResult result)
        {
            // AnaModel が未セットなら結果テーブル生成をスキップ
            if (CurrentModel == null)
            {
                LatestResultTables = [];
                OnPropertyChanged(nameof(LatestResultTables));
                RaiseResultCommandsCanExecute();
                return;
            }

            LatestResultTables = _tableService.BuildTables(
                CurrentModel,
                result.LoadCase,
                result.LoadCombination,
                result.IsLiquefaction,
                result.Step,
                CurrentInputModel);

            OnPropertyChanged(nameof(LatestResultTables));
            RaiseResultCommandsCanExecute();
        }

        public ICommand OpenTableWindowCommand { get; private set; }

        private void OpenTableWindow()
        {
            try
            {
                var vm = new TableWindowViewModel();
                vm.AllSeismicLoadCases = CurrentInputModel.LoadCasesInput.AllSeismicLoadCases;
                vm.AnaModel = CurrentModel;

                // 水平解析等の結果テーブルと基礎梁鉛直解析の結果テーブルを統合
                var allTables = new List<ResultTable>(LatestResultTables);
                if (VerticalBeamCaseResults != null)
                {
                    allTables.AddRange(BuildVerticalBeamResultTables());
                }
                // 群杭沈下解析（反復）の結果テーブル
                allTables.AddRange(BuildGroupSettlementBeamAwareTables());
                // 群杭沈下解析（一般）の結果テーブル
                allTables.AddRange(BuildGroupSettlementNonBeamAwareTables());
                // 検定結果 (検定比の降順)
                allTables.AddRange(BuildEvaluationTables());
                vm.LoadTables(allTables);

                var w = new Views.TableWindow { DataContext = vm };
                w.Show();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "テーブルウィンドウ の表示に失敗", "テーブルウィンドウ");
                MessageService.Show(GuardMessages.WindowOpenFailed("テーブルウィンドウ"), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 検定結果を結果テーブルとして生成する (低減前 / 低減後 の 2 枚)。
        ///
        /// 結果を確認しに来た人が最初に開くのはこのテーブルウィンドウなので、
        /// 検定比もここから見られるようにする
        /// (検定ウィンドウを開いてボタンを押さないと分からない状態だった)。
        ///
        /// 荷重ケース・組合せ・液状化をまたいで 1 枚にまとめる。
        /// 支配ケースを探すには、全条件を検定比の降順で並べたものが要るため。
        /// </summary>
        private List<ResultTable> BuildEvaluationTables()
        {
            var tables = new List<ResultTable>();
            if (CurrentModel == null || !IsHorizontalAnalysisDone) return tables;

            var columns = ResultColumnReflectionCache.GetColumns(typeof(EvaluationItem));

            foreach (var (factored, label) in new[] { (false, "低減前"), (true, "低減後") })
            {
                EvaluationResult result;
                try
                {
                    result = EvaluationWindowViewModel.BuildEvaluationResult(this, factored);
                }
                catch (Exception ex)
                {
                    // 検定が組めなくても他の結果テーブルは出す
                    Serilog.Log.Warning(ex, "[検定テーブル] 生成に失敗 ({Label})", label);
                    continue;
                }

                if (result.IsEmpty) continue;

                tables.Add(new ResultTable
                {
                    Name = $"検定結果（{label}水平解析）",
                    Category = "検定",
                    Columns = columns,
                    Rows = result.ByRatioDescending.Cast<object>().ToList(),
                    SpansAllConditions = true,
                });
            }

            return tables;
        }

        /// <summary>
        /// 基礎梁鉛直解析結果からResultTableリストを生成する
        /// </summary>
        private List<ResultTable> BuildVerticalBeamResultTables()
        {
            var tables = new List<ResultTable>();
            if (VerticalBeamCaseResults == null) return tables;

            foreach (var caseResult in VerticalBeamCaseResults)
            {
                // 杭結果テーブル
                if (caseResult.PileResults?.Count > 0)
                {
                    var cols = new List<ResultColumnDescriptor>
                    {
                        new() { Header = "杭No", Order = 0, Property = typeof(FEM.VerticalBeamPileResult).GetProperty(nameof(FEM.VerticalBeamPileResult.PileNo))! },
                        new() { Header = "X (m)", Order = 1, Property = typeof(FEM.VerticalBeamPileResult).GetProperty(nameof(FEM.VerticalBeamPileResult.X))!, Format = "N3" },
                        new() { Header = "Y (m)", Order = 2, Property = typeof(FEM.VerticalBeamPileResult).GetProperty(nameof(FEM.VerticalBeamPileResult.Y))!, Format = "N3" },
                        new() { Header = "入力荷重 (kN)", Order = 3, Property = typeof(FEM.VerticalBeamPileResult).GetProperty(nameof(FEM.VerticalBeamPileResult.InputLoad_kN))!, Format = "N1" },
                        new() { Header = "杭反力 (kN)", Order = 4, Property = typeof(FEM.VerticalBeamPileResult).GetProperty(nameof(FEM.VerticalBeamPileResult.Reaction_kN))!, Format = "N1" },
                        new() { Header = "沈下量 (mm)", Order = 5, Property = typeof(FEM.VerticalBeamPileResult).GetProperty(nameof(FEM.VerticalBeamPileResult.Settlement_mm))!, Format = "N2" },
                    };
                    tables.Add(new ResultTable
                    {
                        Name = $"基礎梁考慮 杭結果 ({caseResult.LoadCaseName})",
                        Category = "基礎梁考慮沈下",
                        Columns = cols,
                        Rows = caseResult.PileResults.Cast<object>().ToList(),
                        LoadCaseName = caseResult.LoadCaseName
                    });
                }

                // 節点変位テーブル
                if (caseResult.NodeResults?.Count > 0)
                {
                    var cols = new List<ResultColumnDescriptor>
                    {
                        new() { Header = "節点名", Order = 0, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.NodeName))! },
                        new() { Header = "X (m)", Order = 1, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.X))!, Format = "N3" },
                        new() { Header = "Y (m)", Order = 2, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Y))!, Format = "N3" },
                        new() { Header = "Z (m)", Order = 3, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Z))!, Format = "N3" },
                        new() { Header = "Uz (mm)", Order = 4, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Uz_mm))!, Format = "N3" },
                        new() { Header = "Rx (rad)", Order = 5, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Rx_rad))!, Format = "F5" },
                        new() { Header = "Ry (rad)", Order = 6, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Ry_rad))!, Format = "F5" },
                    };
                    tables.Add(new ResultTable
                    {
                        Name = $"基礎梁考慮 節点変位 ({caseResult.LoadCaseName})",
                        Category = "基礎梁考慮沈下",
                        Columns = cols,
                        Rows = caseResult.NodeResults.Cast<object>().ToList(),
                        LoadCaseName = caseResult.LoadCaseName
                    });
                }

                // 梁応力テーブル
                if (caseResult.BeamResults?.Count > 0)
                {
                    var cols = new List<ResultColumnDescriptor>
                    {
                        new() { Header = "梁名", Order = 0, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.BeamName))! },
                        new() { Header = "Ni (kN)", Order = 1, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Ni))!, Format = "N1" },
                        new() { Header = "Qyi (kN)", Order = 2, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qyi))!, Format = "N1" },
                        new() { Header = "Qzi (kN)", Order = 3, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qzi))!, Format = "N1" },
                        new() { Header = "Mxi (kNm)", Order = 4, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mxi))!, Format = "N1" },
                        new() { Header = "Myi (kNm)", Order = 5, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Myi))!, Format = "N1" },
                        new() { Header = "Mzi (kNm)", Order = 6, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mzi))!, Format = "N1" },
                        new() { Header = "Nj (kN)", Order = 7, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Nj))!, Format = "N1" },
                        new() { Header = "Qyj (kN)", Order = 8, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qyj))!, Format = "N1" },
                        new() { Header = "Qzj (kN)", Order = 9, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qzj))!, Format = "N1" },
                        new() { Header = "Mxj (kNm)", Order = 10, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mxj))!, Format = "N1" },
                        new() { Header = "Myj (kNm)", Order = 11, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Myj))!, Format = "N1" },
                        new() { Header = "Mzj (kNm)", Order = 12, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mzj))!, Format = "N1" },
                    };
                    tables.Add(new ResultTable
                    {
                        Name = $"基礎梁考慮 梁応力 ({caseResult.LoadCaseName})",
                        Category = "基礎梁考慮沈下",
                        Columns = cols,
                        Rows = caseResult.BeamResults.Cast<object>().ToList(),
                        LoadCaseName = caseResult.LoadCaseName
                    });
                }
            }

            return tables;
        }

        /// <summary>
        /// 群杭沈下解析（反復）(個別矩形（基礎梁考慮）反復) の結果テーブルを生成。
        /// 杭結果 / 節点変位 / 梁応力 / 土層グリッド変位 を全ケース分。
        /// </summary>
        private List<ResultTable> BuildGroupSettlementBeamAwareTables()
        {
            var tables = new List<ResultTable>();
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null) return tables;

            const string category = "群杭沈下解析（反復）";
            const string prefix = "群杭沈下解析（反復）";

            foreach (var rec in pgs.CaseRecords.Where(r => r.IsBeamAware))
            {
                string caseName = rec.LoadCaseName ?? "";

                // 杭結果テーブル: PileSettlements_mm / PileReactions_kN / SpringStiffness を統合した行
                if (CurrentInputModel.PileLayoutItems != null && rec.PileSettlements_mm.Count > 0)
                {
                    var rows = new List<object>();
                    foreach (var pile in CurrentInputModel.PileLayoutItems)
                    {
                        rec.PileSettlements_mm.TryGetValue(pile.PileNo, out double s);
                        rec.PileReactions_kN.TryGetValue(pile.PileNo, out double r);
                        rec.SpringStiffness.TryGetValue(pile.PileNo, out double k);
                        rows.Add(new BeamAwarePileResultRow
                        {
                            PileNo = pile.PileNo,
                            X = pile.Point3D.X,
                            Y = pile.Point3D.Y,
                            Reaction_kN = r,
                            Settlement_mm = s,
                            SpringStiffness_kN_per_m = k,
                        });
                    }
                    tables.Add(new ResultTable
                    {
                        Name = $"{prefix} 杭結果",
                        Category = category,
                        Columns =
                        [
                            new() { Header = "杭No", Order = 0, Property = typeof(BeamAwarePileResultRow).GetProperty(nameof(BeamAwarePileResultRow.PileNo))! },
                            new() { Header = "X (m)", Order = 1, Property = typeof(BeamAwarePileResultRow).GetProperty(nameof(BeamAwarePileResultRow.X))!, Format = "N3" },
                            new() { Header = "Y (m)", Order = 2, Property = typeof(BeamAwarePileResultRow).GetProperty(nameof(BeamAwarePileResultRow.Y))!, Format = "N3" },
                            new() { Header = "基礎反力 (kN)", Order = 3, Property = typeof(BeamAwarePileResultRow).GetProperty(nameof(BeamAwarePileResultRow.Reaction_kN))!, Format = "N1" },
                            new() { Header = "沈下量 (mm)", Order = 4, Property = typeof(BeamAwarePileResultRow).GetProperty(nameof(BeamAwarePileResultRow.Settlement_mm))!, Format = "N2" },
                            new() { Header = "ばね (kN/m)", Order = 5, Property = typeof(BeamAwarePileResultRow).GetProperty(nameof(BeamAwarePileResultRow.SpringStiffness_kN_per_m))!, Format = "E2" },
                        ],
                        Rows = rows,
                        LoadCaseName = caseName,
                    });
                }

                // 節点変位
                if (rec.NodeResults?.Count > 0)
                {
                    tables.Add(new ResultTable
                    {
                        Name = $"{prefix} 節点変位",
                        Category = category,
                        Columns =
                        [
                            new() { Header = "節点名", Order = 0, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.NodeName))! },
                            new() { Header = "X (m)", Order = 1, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.X))!, Format = "N3" },
                            new() { Header = "Y (m)", Order = 2, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Y))!, Format = "N3" },
                            new() { Header = "Z (m)", Order = 3, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Z))!, Format = "N3" },
                            new() { Header = "Uz (mm)", Order = 4, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Uz_mm))!, Format = "N3" },
                            new() { Header = "Rx (rad)", Order = 5, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Rx_rad))!, Format = "F5" },
                            new() { Header = "Ry (rad)", Order = 6, Property = typeof(FEM.VerticalBeamNodeResult).GetProperty(nameof(FEM.VerticalBeamNodeResult.Ry_rad))!, Format = "F5" },
                        ],
                        Rows = rec.NodeResults.Cast<object>().ToList(),
                        LoadCaseName = caseName,
                    });
                }

                // 梁応力
                if (rec.BeamResults?.Count > 0)
                {
                    tables.Add(new ResultTable
                    {
                        Name = $"{prefix} 梁応力",
                        Category = category,
                        Columns =
                        [
                            new() { Header = "梁名", Order = 0, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.BeamName))! },
                            new() { Header = "Ni (kN)",   Order = 1,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Ni))!, Format = "N1" },
                            new() { Header = "Qyi (kN)",  Order = 2,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qyi))!, Format = "N1" },
                            new() { Header = "Qzi (kN)",  Order = 3,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qzi))!, Format = "N1" },
                            new() { Header = "Mxi (kNm)", Order = 4,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mxi))!, Format = "N1" },
                            new() { Header = "Myi (kNm)", Order = 5,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Myi))!, Format = "N1" },
                            new() { Header = "Mzi (kNm)", Order = 6,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mzi))!, Format = "N1" },
                            new() { Header = "Nj (kN)",   Order = 7,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Nj))!, Format = "N1" },
                            new() { Header = "Qyj (kN)",  Order = 8,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qyj))!, Format = "N1" },
                            new() { Header = "Qzj (kN)",  Order = 9,  Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Qzj))!, Format = "N1" },
                            new() { Header = "Mxj (kNm)", Order = 10, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mxj))!, Format = "N1" },
                            new() { Header = "Myj (kNm)", Order = 11, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Myj))!, Format = "N1" },
                            new() { Header = "Mzj (kNm)", Order = 12, Property = typeof(FEM.VerticalBeamBeamResult).GetProperty(nameof(FEM.VerticalBeamBeamResult.Mzj))!, Format = "N1" },
                        ],
                        Rows = rec.BeamResults.Cast<object>().ToList(),
                        LoadCaseName = caseName,
                    });
                }

                // 土層グリッド変位 (反復)
                if (rec.SettlementGridData?.Count > 0)
                {
                    tables.Add(BuildSettlementGridTable(
                        $"{prefix} 土層グリッド変位",
                        category, caseName, rec.SettlementGridData));
                }
            }
            return tables;
        }

        /// <summary>
        /// 群杭沈下解析（一般）(個別矩形（基礎梁考慮）以外) の結果テーブルを生成。
        /// 杭結果 / 節点変位 / 土層グリッド変位 を全ケース分。
        /// 一般解析は梁解析を行わないため、節点変位は杭頭沈下のみを示す簡易表となる。
        /// </summary>
        private List<ResultTable> BuildGroupSettlementNonBeamAwareTables()
        {
            var tables = new List<ResultTable>();
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null) return tables;

            const string category = "群杭沈下解析（一般）";
            const string prefix = "群杭沈下解析（一般）";

            foreach (var rec in pgs.CaseRecords.Where(r => !r.IsBeamAware))
            {
                string caseName = rec.LoadCaseName ?? "";

                // 杭結果: 杭ごとの 沈下量 (反力やばねは無いが LinkedPileNo に紐付く矩形荷重 QA を表示)
                if (CurrentInputModel.PileLayoutItems != null && rec.PileSettlements_mm.Count > 0)
                {
                    var loadByPile = new Dictionary<int, double>();
                    if (rec.RectLoads != null)
                    {
                        foreach (var rl in rec.RectLoads)
                        {
                            if (rl.LinkedPileNo > 0)
                            {
                                loadByPile.TryGetValue(rl.LinkedPileNo, out double agg);
                                loadByPile[rl.LinkedPileNo] = agg + rl.QA;
                            }
                        }
                    }
                    var rows = new List<object>();
                    foreach (var pile in CurrentInputModel.PileLayoutItems)
                    {
                        rec.PileSettlements_mm.TryGetValue(pile.PileNo, out double s);
                        loadByPile.TryGetValue(pile.PileNo, out double load);
                        rows.Add(new NonBeamAwarePileResultRow
                        {
                            PileNo = pile.PileNo,
                            X = pile.Point3D.X,
                            Y = pile.Point3D.Y,
                            Load_kN = load,
                            Settlement_mm = s,
                        });
                    }
                    tables.Add(new ResultTable
                    {
                        Name = $"{prefix} 杭結果",
                        Category = category,
                        Columns =
                        [
                            new() { Header = "杭No", Order = 0, Property = typeof(NonBeamAwarePileResultRow).GetProperty(nameof(NonBeamAwarePileResultRow.PileNo))! },
                            new() { Header = "X (m)", Order = 1, Property = typeof(NonBeamAwarePileResultRow).GetProperty(nameof(NonBeamAwarePileResultRow.X))!, Format = "N3" },
                            new() { Header = "Y (m)", Order = 2, Property = typeof(NonBeamAwarePileResultRow).GetProperty(nameof(NonBeamAwarePileResultRow.Y))!, Format = "N3" },
                            new() { Header = "荷重 (kN)", Order = 3, Property = typeof(NonBeamAwarePileResultRow).GetProperty(nameof(NonBeamAwarePileResultRow.Load_kN))!, Format = "N1" },
                            new() { Header = "沈下量 (mm)", Order = 4, Property = typeof(NonBeamAwarePileResultRow).GetProperty(nameof(NonBeamAwarePileResultRow.Settlement_mm))!, Format = "N2" },
                        ],
                        Rows = rows,
                        LoadCaseName = caseName,
                    });

                    // 節点変位 (一般): 梁解析を行わないため、杭頭位置 (= 節点) と Uz のみ。
                    var nodeRows = new List<object>();
                    foreach (var pile in CurrentInputModel.PileLayoutItems)
                    {
                        rec.PileSettlements_mm.TryGetValue(pile.PileNo, out double s);
                        nodeRows.Add(new NonBeamAwareNodeRow
                        {
                            NodeName = $"Pile-{pile.PileNo}",
                            X = pile.Point3D.X,
                            Y = pile.Point3D.Y,
                            Z = pile.Point3D.Z,
                            Uz_mm = s,
                        });
                    }
                    tables.Add(new ResultTable
                    {
                        Name = $"{prefix} 節点変位",
                        Category = category,
                        Columns =
                        [
                            new() { Header = "節点名", Order = 0, Property = typeof(NonBeamAwareNodeRow).GetProperty(nameof(NonBeamAwareNodeRow.NodeName))! },
                            new() { Header = "X (m)", Order = 1, Property = typeof(NonBeamAwareNodeRow).GetProperty(nameof(NonBeamAwareNodeRow.X))!, Format = "N3" },
                            new() { Header = "Y (m)", Order = 2, Property = typeof(NonBeamAwareNodeRow).GetProperty(nameof(NonBeamAwareNodeRow.Y))!, Format = "N3" },
                            new() { Header = "Z (m)", Order = 3, Property = typeof(NonBeamAwareNodeRow).GetProperty(nameof(NonBeamAwareNodeRow.Z))!, Format = "N3" },
                            new() { Header = "Uz (mm)", Order = 4, Property = typeof(NonBeamAwareNodeRow).GetProperty(nameof(NonBeamAwareNodeRow.Uz_mm))!, Format = "N3" },
                        ],
                        Rows = nodeRows,
                        LoadCaseName = caseName,
                    });
                }

                // 土層グリッド変位 (一般)
                if (rec.SettlementGridData?.Count > 0)
                {
                    tables.Add(BuildSettlementGridTable(
                        $"{prefix} 土層グリッド変位",
                        category, caseName, rec.SettlementGridData));
                }
            }
            return tables;
        }

        /// <summary>
        /// 土層グリッド変位テーブルを共通で構築 (X, Y, 沈下量 mm)。
        /// </summary>
        private static ResultTable BuildSettlementGridTable(
            string name, string category, string caseName,
            System.Collections.Generic.IEnumerable<Models.InputData.SettlementGridDataItem> grid)
        {
            return new ResultTable
            {
                Name = name,
                Category = category,
                Columns =
                [
                    new() { Header = "X (m)", Order = 0, Property = typeof(Models.InputData.SettlementGridDataItem).GetProperty(nameof(Models.InputData.SettlementGridDataItem.X))!, Format = "N3" },
                    new() { Header = "Y (m)", Order = 1, Property = typeof(Models.InputData.SettlementGridDataItem).GetProperty(nameof(Models.InputData.SettlementGridDataItem.Y))!, Format = "N3" },
                    new() { Header = "沈下量 (mm)", Order = 2, Property = typeof(Models.InputData.SettlementGridDataItem).GetProperty(nameof(Models.InputData.SettlementGridDataItem.Settlement))!, Format = "N3" },
                ],
                Rows = grid.Cast<object>().ToList(),
                LoadCaseName = caseName,
            };
        }

        /// <summary>テーブル出力用の杭結果行 (群杭沈下解析（反復）)。</summary>
        public class BeamAwarePileResultRow
        {
            public int PileNo { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Reaction_kN { get; set; }
            public double Settlement_mm { get; set; }
            public double SpringStiffness_kN_per_m { get; set; }
        }

        /// <summary>テーブル出力用の杭結果行 (群杭沈下解析（一般）)。</summary>
        public class NonBeamAwarePileResultRow
        {
            public int PileNo { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Load_kN { get; set; }
            public double Settlement_mm { get; set; }
        }

        /// <summary>テーブル出力用の節点行 (群杭沈下解析（一般）: 杭頭のみ Uz)。</summary>
        public class NonBeamAwareNodeRow
        {
            public string NodeName { get; set; } = "";
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public double Uz_mm { get; set; }
        }

        // ケースタグ抽出正規表現 (例: [L2-1.C1.Liq], [L1-2.C3.NoLq])
        // HorizontalCalculationWindow.xaml.cs の CaseTagPattern と同等。
        private static readonly System.Text.RegularExpressions.Regex CaseTagPattern =
            new(@"\[L\d+-\d+\.C\d+\.(?:Liq|NoLq)\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        [RelayCommand(CanExecute = nameof(CanOpenLogWindow))]
        private void OpenLogWindow()
        {
            if (!CanOpenLogWindow()) return;

            // ログ種別リストを構築。複数ケース解析時はケース別フィルタ済ビューも追加する。
            var logSources = new Dictionary<string, IEnumerable<string>>();
            if (HorizontalAnalysisLogs.Count > 0)
            {
                logSources["水平解析 (全体)"] = HorizontalAnalysisLogs;

                // ログ内のユニークなケースタグを抽出して、ケース別カテゴリを追加
                var caseTags = HorizontalAnalysisLogs
                    .Select(line => CaseTagPattern.Match(line))
                    .Where(m => m.Success)
                    .Select(m => m.Value)
                    .Distinct()
                    .OrderBy(t => t, System.StringComparer.Ordinal)
                    .ToList();

                foreach (var tag in caseTags)
                {
                    // 該当ケースタグを含む行のみ抽出 (open 時点のスナップショット)
                    var filtered = HorizontalAnalysisLogs
                        .Where(line => line.Contains(tag))
                        .ToList();
                    logSources[$"水平解析 {tag}"] = filtered;
                }
            }
            if (VerticalBeamAnalysisLogs.Count > 0)
                logSources["基礎梁考慮沈下解析"] = VerticalBeamAnalysisLogs;

            // 個別矩形（基礎梁考慮）反復解析の永続化ログ
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords != null)
            {
                foreach (var rec in pgs.CaseRecords.Where(r => r.IsBeamAware && r.IterationLog?.Count > 0))
                {
                    logSources[$"群杭沈下解析（反復） [{rec.LoadCaseName}]"] = rec.IterationLog;
                }
            }

            var vm = new LogWindowViewModel(logSources);
            var w = new Views.LogWindow { DataContext = vm };
            w.Show();
        }

        private bool CanOpenLogWindow()
        {
            if (HorizontalAnalysisLogs.Count > 0) return true;
            if (VerticalBeamAnalysisLogs.Count > 0) return true;
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords?.Any(r => r.IsBeamAware && r.IterationLog?.Count > 0) == true) return true;
            return false;
        }

        private Views.EvaluationWindow? _evaluationWindow;

        [RelayCommand(CanExecute = nameof(CanOpenEvaluationWindow))]
        private void OpenEvaluationWindow()
        {
            if (_evaluationWindow is { IsVisible: true })
            {
                _evaluationWindow.Activate();
                return;
            }

            var vm = new EvaluationWindowViewModel(this);
            _evaluationWindow = new Views.EvaluationWindow { DataContext = vm, Topmost = true };
            _evaluationWindow.Closed += (_, _) => _evaluationWindow = null;
            _evaluationWindow.Show();
        }

        private bool CanOpenEvaluationWindow()
        {
            if (IsHorizontalAnalysisDone && CurrentModel != null) return true;
            // 個別矩形（基礎梁考慮）反復解析の結果でも 傾斜角検定 を許可
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords?.Any(r => r.IsBeamAware) == true) return true;
            return false;
        }

        // 解析結果テーブル再生成
        public void RefreshResultTablesFromLastStep()
        {
            // AnaModel または AnalysisStepResults が null/空の場合は早期リターン
            if (CurrentModel == null ||
                CurrentModel.AnalysisStepResults == null ||
                CurrentModel.AnalysisStepResults.Count == 0 ||
                !HasAnyAnalysisResult)
            {
                LatestResultTables = [];
                OnPropertyChanged(nameof(LatestResultTables));
                if (OpenTableWindowCommand is ToolkitRelayCommand tc) tc.NotifyCanExecuteChanged();
                return;
            }

            // デバッグ: AnalysisStepResultsの内容を確認
            foreach (var r in CurrentModel.AnalysisStepResults)
            {
            }

            // 全ての解析結果から一意の組合せ（LoadCase, LoadCombination, IsLiquefaction）を取得
            // 各組合せについて最終ステップのテーブルを生成
            var allTables = new List<ResultTable>();

            var uniqueCombinations = CurrentModel.AnalysisStepResults
                .GroupBy(r => new
                {
                    LoadCaseName = r.LoadCase?.LoadName ?? "",
                    LoadCombinationName = r.LoadCombination?.Name ?? "",
                    r.IsLiquefaction
                })
                .Select(g => g.OrderByDescending(r => r.Step).First()) // 各組合せの最終ステップを取得
                .ToList();

            foreach (var c in uniqueCombinations)
            {
            }

            foreach (var stepResult in uniqueCombinations)
            {
                var tables = _tableService.BuildTables(
                    CurrentModel,
                    stepResult.LoadCase,
                    stepResult.LoadCombination,
                    stepResult.IsLiquefaction,
                    stepResult.Step,
                    CurrentInputModel);

                allTables.AddRange(tables);
            }

            foreach (var t in allTables)
            {
            }

            LatestResultTables = allTables;

            OnPropertyChanged(nameof(LatestResultTables));
            RaiseResultCommandsCanExecute();
        }

    }
}

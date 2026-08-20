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
    // MainWindowViewModel partial: ファイル入出力（保存/新規/読込プロトコル・材料オプション同期・解析状態復元・docx/3dm/DXF/MGT エクスポート）
    public partial class MainWindowViewModel
    {
        // 名前をつけて保存
        [RelayCommand]
        public async Task SaveInputModelFileAs()
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "PileDesign プロジェクト (*.pdj)|*.pdj|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "pdj"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                CurrentFilePath = saveFileDialog.FileName;
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    StatusMessage = "保存中...";
                    // 解析結果保存フラグ OFF の場合は AnaModel/VerticalBeamCaseResults を null にして
                    // 入力のみの軽量ファイルとして保存する
                    var anaModelToSave = IsSaveAnalysisResultsManual ? CurrentModel : null;
                    var vbcrToSave = IsSaveAnalysisResultsManual ? VerticalBeamCaseResults : null;
                    await _fileOperationService.SaveProjectDataAsync(CurrentFilePath, CurrentInputModel, anaModelToSave, vbcrToSave);
                    ShowToast("保存が完了しました。");

                    // MRUに追加
                    _mruService.AddFile(CurrentFilePath);

                    // 自動保存を開始 (自動保存は常に入力のみ = 軽量。結果は含めない)
                    _autoSaveService.Start(CurrentFilePath, CurrentInputModel, null, null);
                }
                catch (Exception ex)
                {
                    MessageService.Show($"保存に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StatusMessage = "準備完了";
                    Mouse.OverrideCursor = null;
                }
            }
        }

        [RelayCommand]
        public async Task SaveInputModelFile()
        {
            if (string.IsNullOrEmpty(CurrentFilePath))
                await SaveInputModelFileAs();
            else
            {
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    StatusMessage = "保存中...";
                    var anaModelToSave = IsSaveAnalysisResultsManual ? CurrentModel : null;
                    var vbcrToSave = IsSaveAnalysisResultsManual ? VerticalBeamCaseResults : null;
                    await _fileOperationService.SaveProjectDataAsync(CurrentFilePath, CurrentInputModel, anaModelToSave, vbcrToSave);
                    ShowToast("保存が完了しました。");
                }
                catch (Exception ex)
                {
                    MessageService.Show($"保存に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StatusMessage = "準備完了";
                    Mouse.OverrideCursor = null;
                }
            }
        }

        [RelayCommand]
        public void NewInputModelFile()
        {
            var result = MessageService.Show(
                "現在のデータを保存しますか？",
                "確認",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
                return;
            else if (result == MessageBoxResult.Yes)
                _ = SaveInputModelFile();

            // 自動保存を停止
            _autoSaveService.Stop();

            CurrentInputModel.Reset();
            this.CurrentModel = null; // AnaModelもリセット
            CurrentFilePath = null;
            LoadedExampleName = null;  // 新規作成時はタイトルバーを [新規] に戻す

            // バイリニアコンクリート・オプションを既定 (false) へ戻し、キャッシュを破棄
            ApplyConcreteModelOptions();

            // ここで初期状態をUndoスタックに積む
            SaveUndoState();

            UpdateWindowImmediate();
        }

        private void TrySaveUndoSnapshotSafely([System.Runtime.CompilerServices.CallerMemberName] string? description = null)
        {
            try
            {
                var snapshot = CurrentInputModel?.DeepCopy();
                if (snapshot != null)
                {
                    _undoManager.SaveState(snapshot, FormatHistoryDescription(description));
                    RaiseUndoStateChanged();
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[TrySaveUndoSnapshotSafely] Exception: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// ファイル読込後の共通プロトコル。CurrentInputModel と CurrentModel を事前に設定してから呼ぶ。
        /// ProjectData 経由/InputModel 単体の両パスで同一のセットアップを実行することで
        /// 呼び忘れによる不具合（ComboBox が空白になる・Undo 履歴が残る等）を防ぐ。
        /// </summary>
        /// <param name="projectData">ProjectData 由来ロードの場合は非 null。InputModel 単体ロードの場合は null。</param>
        /// <param name="filePath">UI 表示用ファイルパス。Untitled/未確定の場合は null 可。</param>
        /// <param name="successMessage">読込成功時にトースト表示するメッセージ。</param>
        /// <summary>
        /// 基本設定のバイリニアコンクリート・オプション（引張無視 / 圧縮 0.85·Gsi·Fc）を
        /// 静的な <see cref="Models.InputData.ConcreteModelOptions"/> へ同期し、影響する全キャッシュを破棄する。
        ///
        /// これらのオプションは安全限界 NM 曲線だけでなく M-φ（→ 非線形 FEM 解析）にも効くため、
        /// 値を反映するには M-φ 静的キャッシュと各断面の NM/降伏/ひび割れキャッシュの両方をクリアする必要がある。
        /// モデル読込・新規作成・例題読込・基本設定での変更時に呼ぶ。
        /// </summary>
        public void ApplyConcreteModelOptions()
        {
            var f = CurrentInputModel?.FundamentalInput;
            Models.InputData.ConcreteModelOptions.IgnoreTensileStrength = f?.IgnoreConcreteTensileStrength ?? false;
            Models.InputData.ConcreteModelOptions.UseReducedCompression = f?.UseReducedConcreteCompressiveStrength ?? false;
            Models.InputData.ConcreteModelOptions.RebarYieldAt11F = f?.RebarYieldAt11F ?? false;
            Models.InputData.ConcreteModelOptions.SteelPipeYieldAt11F = f?.SteelPipeYieldAt11F ?? false;
            Models.InputData.ConcreteModelOptions.UseUnitGsiForConcreteE = f?.UseUnitGsiForConcreteE ?? false;
            Models.InputData.ConcreteModelOptions.UseGuideYoungsModulus = f?.UseGuideYoungsModulus ?? false;
            Models.InputData.ConcreteModelOptions.UseNotification1113Compression = f?.UseNotification1113Compression ?? false;
            Models.InputData.ConcreteModelOptions.UseNotification1113Shear = f?.UseNotification1113Shear ?? false;
            Models.InputData.ConcreteModelOptions.UseInsituUltimateEFunction = f?.UseInsituUltimateEFunction ?? false;
            Models.InputData.ConcreteModelOptions.UseFiberMPhi = f?.UseFiberMPhi ?? false;
            Models.InputData.ConcreteModelOptions.Notification1113CompressionCase = f?.Notification1113CompressionCase ?? 1;

            // M-φ 静的キャッシュ（全断面共有）
            PileSection.ClearMphiCache();

            // 各断面インスタンスの NM/降伏/ひび割れキャッシュ
            if (CurrentInputModel?.PileBodies != null)
            {
                foreach (var pb in CurrentInputModel.PileBodies)
                {
                    if (pb?.PileBodySegments == null) continue;
                    foreach (var seg in pb.PileBodySegments)
                    {
                        var sec = seg?.PileSection;
                        if (sec == null) continue;
                        sec.InvalidateComputedCaches();
                        // ξ→Ec オプションは PileSection.ConcreteE（諸元表示・EA/EI）にも効くため、
                        // 場所打ち系（既製杭以外＝式ベース Ec）で再計算し諸元も更新する。
                        if (sec.PileBodyType != PileTypeNames.PrecastConcrete)
                        {
                            sec.RecalculateConcreteE();
                            sec.SetSpecs();
                        }
                    }
                }
            }
        }

        private void ApplyPostLoadProtocol(Models.ProjectData? projectData, string? filePath, string successMessage)
        {
            // ObservableCollection 変換（idempotent なので既に ObservableCollection なら維持）
            _fileOperationService.ConvertToObservableCollections(CurrentInputModel);

            // 旧データとの互換性マイグレーション
            CurrentInputModel.InputNodes ??= [];
            CurrentInputModel.GridXItems ??= [];
            CurrentInputModel.GridYItems ??= [];
            CurrentInputModel.EnsureFoundationBeamDefaults();
            CurrentInputModel.EnsureAnalysisTargetDefaults();

            // PileZ セマンティクス v1 → v2: pile.Z を「杭頭節点」から「接合節点」へシフト
            // 適用条件: ProjectData.FormatVersion < 2 (旧ファイル) または InputModel 単独ロード (projectData == null)
            if (projectData == null || projectData.FormatVersion < 2)
            {
                CurrentInputModel.MigratePileZSemantics_v1_to_v2();
            }

            // CaseRecord.LoadingType の旧データ互換マイグレーション
            // 旧ファイルでは LoadingType フィールドが空文字 → IsBeamAware から推定して補完
            MigrateCaseRecordLoadingType(CurrentInputModel.PileGroupSettlement);

            // 梁要素 ComboBox 用の節点候補リストを再構築 (deserialize 直後は空のため)
            CurrentInputModel.RefreshAvailableNodeReferenceOptions();

            OnPropertyChanged(nameof(CurrentInputModel));

            // ViewModel アタッチ・ファイルパス確定
            CurrentInputModel.AttachViewModel(this);
            CurrentFilePath = filePath;

            // ComboBox 用カウントリスト再構築・M-φ キャッシュクリア
            CurrentInputModel.UpdateCountLists();
            // バイリニアコンクリート・オプションを同期し M-φ/NM キャッシュを破棄
            ApplyConcreteModelOptions();

            // 杭配置番号の同期（PileNo が未設定の旧ファイルに備える）
            UpdatePileLayoutNo();

            // 地震時軸力モード (絶対 / 変動) を InputModel から復元し、AxialForceModeContext + UI に反映。
            // VL/L1/L2 の値は既にロード済みなので、Context フラグの設定で即時に変動列の表示も切替可能。
            // 各杭の変動軸力コレクションを絶対値ベースで再構築 (deserialize 順序に依存しない安定状態を作る)。
            Common.AxialForceModeContext.IsVariationMode = CurrentInputModel.IsAxialForceVariationMode;
            if (CurrentInputModel.PileLayoutItems != null)
            {
                foreach (var pile in CurrentInputModel.PileLayoutItems)
                    pile.RebuildVariationFromAbsolute();
            }
            OnPropertyChanged(nameof(IsAxialForceVariationMode));

            // 解析結果の復元（projectData=null の場合はフラグのみリセット）
            RestoreAnalysisState(projectData);

            // 水平解析結果を含むファイルをロードした場合、結果テーブル (LatestResultTables) を
            // AnaModel.AnalysisStepResults から再構築する。これがないと「テーブル出力」「グラフ」等の
            // 結果系コマンドの CanExecute が false のまま (ボタンが押せない) になる。
            // (RefreshResultTablesFromLastStep 内で CurrentModel/AnalysisStepResults/HasAnyAnalysisResult を
            //  チェックし、結果が無ければ LatestResultTables=[] にして安全にスキップする)
            RefreshResultTablesFromLastStep();
            RaiseResultCommandsCanExecute();

            // 結果タイプ ComboBox / バッジ用プロパティを再評価
            OnPropertyChanged(nameof(HasGroupSettlementCaseRecords));
            OnPropertyChanged(nameof(IsGroupSettlementActiveCaseBeamAware));
            OnPropertyChanged(nameof(HasGroupSettlementBeamAwareCases));
            OnPropertyChanged(nameof(AvailableActiveLoadingTypes));
            OnPropertyChanged(nameof(SelectedActiveLoadingType));
            OnPropertyChanged(nameof(GroupSettlementRouteOptions));
            OnPropertyChanged(nameof(GroupSettlementRouteSelector));
            // 群杭荷重「基礎梁:有/無」セレクタも基礎梁有無に連動するため再評価
            OnPropertyChanged(nameof(AvailableLoadingTypeOptions));
            OnPropertyChanged(nameof(AvailableLoadingTypeOptionsNonBeam));
            OnPropertyChanged(nameof(GroupSettlementBeamSelectorOptions));
            OnPropertyChanged(nameof(GroupSettlementBeamSelector));
            OnPropertyChanged(nameof(GroupSettlementLoadTypeOptions));
            OnPropertyChanged(nameof(GroupSettlementLoadType));
            OnPropertyChanged(nameof(IsManualRectLoadEditingEnabled));

            // 基礎梁考慮 群杭沈下解析リボンボタン等の CanExecute を再評価
            OpenVerticalBeamCalculationCommand?.NotifyCanExecuteChanged();
            OpenGroupSettlementWithBeamWindowCommand?.NotifyCanExecuteChanged();

            // Undo 履歴をクリアして読込状態を初期状態として保存
            _undoManager.Clear();
            SaveUndoState();

            // 最終描画＆通知
            UpdateWindowImmediate();
            ShowToast(successMessage);

            // 既製コンクリート杭ライブラリの整合性チェック (デフォルト径 1200mm にフォールバックして
            // 描画・解析が意図せず狂うのを防ぐため、ロード後に一括で検証して警告する)
            ShowPrecastPileNameWarningsIfAny(CurrentInputModel);
        }

        /// <summary>
        /// 全 PileSection の SelectedPrecastPile.Name がライブラリに存在するかチェックし、
        /// 不一致があれば箇所のリストを返す。副作用なし。
        /// </summary>
        private static List<string> ValidatePrecastPileNames(InputModel model)
        {
            var issues = new List<string>();
            if (model?.PileBodies == null) return issues;

            for (int i = 0; i < model.PileBodies.Count; i++)
            {
                var pb = model.PileBodies[i];
                if (pb?.PileBodyType != PileTypeNames.PrecastConcrete) continue; // 既製のみ対象
                if (pb.PileBodySegments == null) continue;

                for (int j = 0; j < pb.PileBodySegments.Count; j++)
                {
                    var seg = pb.PileBodySegments[j];
                    var sec = seg?.PileSection;
                    if (sec == null) continue;

                    if (!sec.IsSelectedPrecastPileInLibrary())
                    {
                        var name = sec.SelectedPrecastPile?.Name ?? "(empty)";
                        var refLabel = string.IsNullOrEmpty(pb.PileBodyRef) ? "" : $" {pb.PileBodyRef}";
                        issues.Add($"杭体 No.{i + 1}{refLabel} 区間 No.{j + 1}: 「{name}」が {sec.PileSectionType} ライブラリに存在しません。");
                    }
                }
            }
            return issues;
        }

        /// <summary>
        /// ValidatePrecastPileNames の結果を MessageBox で表示する。問題がなければ何もしない。
        /// </summary>
        private static void ShowPrecastPileNameWarningsIfAny(InputModel model)
        {
            var issues = ValidatePrecastPileNames(model);
            if (issues.Count == 0) return;

            string body =
                "次の杭断面名が既製杭ライブラリに存在しません。\n" +
                "PileDiameter はデフォルト値 1200mm のままになり、描画や応力検定が意図しない挙動になる可能性があります。\n" +
                "杭体ウィンドウで杭断面を選び直してください。\n\n" +
                string.Join("\n", issues);

            PileDesign.Services.MessageService.Show(
                body,
                "杭断面名の不一致",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }

        // InputModel 単体ロード（ProjectData ラッパーでない旧形式ファイル用フォールバック）
        public bool TryLoadInputModelFileUsingInputModelLoader(string filePath)
        {
            try
            {
                var loaded = InputModel.LoadFromFile(filePath, this);
                if (loaded == null)
                {
                    MessageService.Show($"ファイルの読込に失敗しました。\n{filePath}", "読込エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                CurrentInputModel = loaded;
                // InputModel 単体ロードには解析結果が含まれないため projectData=null で渡す。
                // CurrentModel と解析フラグは RestoreAnalysisState 内でリセットされる。
                ApplyPostLoadProtocol(projectData: null, filePath: filePath, successMessage: "読込が完了しました。");
                return true;
            }
            catch (Exception ex)
            {
                MessageService.Show($"ファイル読込中にエラーが発生しました。\n{ex.Message}", "読込エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        [RelayCommand]
        public void OpenInputModelFileSimple()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "PileDesign プロジェクト (*.pdj;*.json)|*.pdj;*.json|PileDesign プロジェクト (*.pdj)|*.pdj|JSON Files (*.json)|*.json", DefaultExt = "pdj" };
            if (ofd.ShowDialog() != true) return;
            // Undo 保存は安全ヘルパを使用
            TrySaveUndoSnapshotSafely();
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                TryLoadInputModelFileUsingInputModelLoader(ofd.FileName);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        [RelayCommand]
        public async Task OpenInputModelFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "PileDesign プロジェクト (*.pdj;*.json)|*.pdj;*.json|PileDesign プロジェクト (*.pdj)|*.pdj|JSON Files (*.json)|*.json",
                DefaultExt = "pdj"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    StatusMessage = "読込中...";
                    var projectData = await _fileOperationService.LoadProjectDataAsync(openFileDialog.FileName);

                    if (projectData != null)
                    {
                        CurrentInputModel = projectData.InputModel;
                        CurrentModel = projectData.AnaModel;
                        ApplyPostLoadProtocol(projectData, openFileDialog.FileName, "読込が完了しました。");
                    }
                    else
                    {
                        // ProjectDataでない場合を想定して InputModel 単体で読めるか試す
                        var ok = TryLoadInputModelFileUsingInputModelLoader(openFileDialog.FileName);
                        if (!ok)
                            throw new InvalidOperationException("ファイル形式が不正です。ProjectData でも InputModel でもありません。");
                        return;
                    }

                    // MRU に追加
                    _mruService.AddFile(CurrentFilePath);

                    // 自動保存を開始 (自動保存は常に入力のみ = 軽量。結果は含めない)
                    _autoSaveService.Start(CurrentFilePath, CurrentInputModel, null, null);
                }
                catch (Exception ex)
                {
                    HandleFileLoadError(ex, openFileDialog.FileName);
                }
                finally
                {
                    StatusMessage = "準備完了";
                    Mouse.OverrideCursor = null;
                }
            }
        }

        /// <summary>
        /// ファイル読込時に解析結果の状態を復元する
        /// </summary>
        private void RestoreAnalysisState(Models.ProjectData? projectData)
        {
            // まずすべてリセット
            IsElementSplit = false;
            IsHorizontalAnalysisDone = false;
            IsVerticalAnalysisDone = false;
            IsGroupPileSettlementAnalysisDone = false;
            IsVerticalBeamAnalysisDone = false;
            VerticalBeamCaseResults = null;

            // projectData が null（InputModel 単体ロード）の場合は
            // リセットのみで完了。CurrentModel の以前の値を消すために null を代入する。
            if (projectData == null)
            {
                CurrentModel = null;
                return;
            }

            var anaModel = CurrentModel;
            var soilPiles = CurrentInputModel?.ElementDivision?.SoilPiles;

            // 杭要素分割済み判定: AnaModelにノードが存在する
            if (anaModel?.Nodes != null && anaModel.Nodes.Count > 0)
            {
                IsElementSplit = true;
            }

            // 水平解析済み判定: AnalysisStepResultsが存在する
            if (anaModel?.AnalysisStepResults != null && anaModel.AnalysisStepResults.Count > 0)
            {
                IsHorizontalAnalysisDone = true;
            }

            // 単杭沈下解析済み判定: SoilPilesにLoadDisplacementsが存在する
            if (soilPiles != null && soilPiles.Count > 0)
            {
                bool anyHasLoadDisp = false;
                foreach (var sp in soilPiles)
                {
                    if (sp.LoadDisplacements != null && sp.LoadDisplacements.Count > 0)
                    {
                        anyHasLoadDisp = true;
                        break;
                    }
                }
                if (anyHasLoadDisp)
                    IsVerticalAnalysisDone = true;
            }

            // 群杭沈下解析済み判定: SettlementGridDataが存在する
            var settlement = CurrentInputModel?.PileGroupSettlement;
            if (settlement?.SettlementGridData != null && settlement.SettlementGridData.Count > 0)
            {
                IsGroupPileSettlementAnalysisDone = true;
            }

            // 基礎梁鉛直解析結果の復元
            if (projectData.VerticalBeamCaseResults != null && projectData.VerticalBeamCaseResults.Count > 0)
            {
                VerticalBeamCaseResults = new ObservableCollection<FEM.VerticalBeamCaseResult>(projectData.VerticalBeamCaseResults);
                IsVerticalBeamAnalysisDone = true;
            }
        }

        // docx 出力設定（Include* フラグ・一括選択/解除・出力前検証）は
        // MainWindowViewModel.DocxOutput.cs に分離した。

        // Word ファイルに保存するメソッド
        // 出力前チェック（未選択の確認）は OK ボタン（DocxOutputWindow.OkButton_Click）で行う。
        [RelayCommand]
        public void OutputWordFile()
        {
            // ファイル名: (yyMMdd)_(HHmm)_構造計算書_(本体ファイル名).docx
            // 本体ファイル名がない (未保存) 場合は「Untitled」をフォールバック。
            string projectBaseName = !string.IsNullOrEmpty(CurrentFilePath)
                ? System.IO.Path.GetFileNameWithoutExtension(CurrentFilePath)
                : "Untitled";
            string defaultDocxName = $"{DateTime.Now:yyMMdd_HHmm}_構造計算書_{projectBaseName}.docx";

            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
            {
                Filter = "Word documents (*.docx)|*.docx|All files (*.*)|*.*",
                FileName = defaultDocxName
            };

            Serilog.Log.Information("[Docx] 保存ダイアログ表示");
            if (saveFileDialog.ShowDialog() == true)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                // 砂時計カーソル + ステータス更新で「ビジー中」を可視化
                var prevCursor = System.Windows.Input.Mouse.OverrideCursor;
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                try
                {
                    StatusMessage = "計算書作成中... (大規模モデルでは数十秒〜数分かかる場合があります)";
                    Serilog.Log.Information("[Docx] 開始: {File}", System.IO.Path.GetFileName(saveFileDialog.FileName));

                    var doc = new Output.WordDocument(CurrentInputModel, CurrentModel, this);
                    doc.CreateWordDocument(CurrentInputModel, saveFileDialog.FileName);

                    sw.Stop();
                    Serilog.Log.Information("[Docx] 完了: {Elapsed:N1} 秒, ファイル: {File}",
                        sw.Elapsed.TotalSeconds, System.IO.Path.GetFileName(saveFileDialog.FileName));

                    ShowToast($"docxファイル作成完了 ({sw.Elapsed.TotalSeconds:N1}秒)。Wordで開き、目次上をクリック→F9 でフィールドを更新してください。");

                    // 作成したdocxファイルを自動的に開く
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = saveFileDialog.FileName,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Serilog.Log.Warning(ex, "[Docx] 失敗 ({Elapsed:N1}秒経過時点)", sw.Elapsed.TotalSeconds);
                    MessageService.Show($"Word出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    System.Windows.Input.Mouse.OverrideCursor = prevCursor;
                    StatusMessage = "準備完了";
                }
            }
        }

        // Rhino 3D (.3dm) ファイルにエクスポートするメソッド
        [RelayCommand]
        public void Export3dmFile()
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
            {
                Filter = "Rhino 3D (*.3dm)|*.3dm|All files (*.*)|*.*",
                DefaultExt = ".3dm",
                FileName = "PileModel_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".3dm"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var exporter = new Output.Rhino3dmExporter(CurrentInputModel);
                    exporter.Export(saveFileDialog.FileName);
                    ShowToast($"3dmファイルを作成しました。\n{saveFileDialog.FileName}");
                }
                catch (Exception ex)
                {
                    MessageService.Show($"3dm出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // DXF ファイルにエクスポートするメソッド
        [RelayCommand]
        public void ExportDxfFile()
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
            {
                Filter = "DXF (*.dxf)|*.dxf|All files (*.*)|*.*",
                DefaultExt = ".dxf",
                FileName = "PileModel_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".dxf"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var exporter = new Output.DxfExporter(CurrentInputModel);
                    exporter.Export(saveFileDialog.FileName);
                    ShowToast($"DXFファイルを作成しました。\n{saveFileDialog.FileName}");
                }
                catch (Exception ex)
                {
                    MessageService.Show($"DXF出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void ExportDxfPlanFile()
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
            {
                Filter = "DXF (*.dxf)|*.dxf|All files (*.*)|*.*",
                DefaultExt = ".dxf",
                FileName = "PilePlan_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".dxf"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var exporter = new Output.DxfPlanExporter(CurrentInputModel);
                    exporter.Export(saveFileDialog.FileName);
                    ShowToast($"伏図DXFファイルを作成しました。\n{saveFileDialog.FileName}");
                }
                catch (Exception ex)
                {
                    MessageService.Show($"伏図DXF出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // midas Gen MGT ファイルにエクスポートするメソッド
        [RelayCommand]
        public void ExportMgtFile()
        {
            if (CurrentModel == null)
            {
                MessageService.Show("水平解析が実行されていません。\n解析モデルをエクスポートするには、先に水平解析を実行してください。",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
            {
                Filter = "midas Gen MGT (*.mgt)|*.mgt|All files (*.*)|*.*",
                DefaultExt = ".mgt",
                FileName = "FEM_Model_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mgt"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var exporter = new Output.MgtExporter(CurrentModel);
                    exporter.Export(saveFileDialog.FileName);
                    ShowToast($"MGTファイルを作成しました。\n{saveFileDialog.FileName}");
                }
                catch (Exception ex)
                {
                    MessageService.Show($"MGT出力に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // プロパティのコピー
        private static void CopyProperties(object source, object destination)
        {
            if (source == null || destination == null)
            {
                throw new ArgumentNullException(nameof(source), "Source or destination cannot be null.");
            }

            Type sourceType = source.GetType();
            Type destinationType = destination.GetType();

            PropertyInfo[] properties = sourceType.GetProperties();

            foreach (PropertyInfo property in properties)
            {
                PropertyInfo destinationProperty = destinationType.GetProperty(property.Name);
                if (destinationProperty != null && property.CanRead && destinationProperty.CanWrite)
                {
                    object value = property.GetValue(source);
                    destinationProperty.SetValue(destination, value);
                }
            }
        }

    }
}

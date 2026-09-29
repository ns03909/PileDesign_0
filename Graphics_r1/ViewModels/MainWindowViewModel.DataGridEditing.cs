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
    /// <summary>
    /// MainWindowViewModel — 表の編集を受ける処理。
    ///
    /// 画面の各表 (杭配置・軸力・前後方杭・一般節点・根入部・杭要素・矩形荷重・
    /// 沈下地層) で、セルの編集が終わったとき / 編集が始まるときに呼ばれる。
    ///
    /// <b>編集を受けたら、要素分割の状態を見直すこと。</b> 杭の位置や本数を変えると
    /// 分割済みの結果と辻褄が合わなくなる。<see cref="CheckAndResetElementSplit"/> が
    /// 判断する。ここを通さずに書き換えると、古い分割のまま解析が走る。
    ///
    /// 以前は <c>MainWindowViewModel.cs</c> (4,251 行) の中にあった。
    /// </summary>
    public partial class MainWindowViewModel
    {
        private Action zoomFitAction;

        public Action? ZoomFitAction { get => zoomFitAction; set => zoomFitAction = value; }
        public Action<double, double>? AnimateViewAnglesAction { get; set; }
        public Action? ActivateSettlementSoilTabAction { get; set; }

        /// <summary>
        /// トースト通知を表示するデリゲート（code-behind で設定）
        /// type: 0=Success, 1=Info, 2=Warning
        /// </summary>
        public Action<string, int>? ShowToastAction { get; set; }

        /// <summary>
        /// トースト通知を表示します
        /// </summary>
        public void ShowToast(string message, int type = 0) => ShowToastAction?.Invoke(message, type);


        public ICommand OpenDoatsuGoryokuBaneWindowCommand { get; }
        public ICommand ComboBoxLabelSize_OnSelectionChangedCommand { get; }

        [RelayCommand]
        private static void DataGridPileLayout_OnLoadingRow(DataGridRowEventArgs e)
        {
            if (e.Row.Item is PileLayoutDataItem)
                e.Row.Header = (e.Row.GetIndex() + 1).ToString(); // 行番号を設定
        }

        // 杭配置更新時更新メソッド
        [RelayCommand]
        private void DataGridPileLayout_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            // 反復解析結果が保存されている場合は警告 (杭位置の変更で結果無効化)
            if (e.EditAction == DataGridEditAction.Commit
                && !ConfirmAnalysisConditionChange("反復", "杭配置編集"))
            {
                e.Cancel = true;
                return;
            }

            HandleDataGridCellEditEnding(e, () =>
            {
                IsElementSplit = false;
                RequestGenerateSoilPiles();

                // コレクション自体の変更通知
                OnPropertyChanged(nameof(GroupPileSettlementXMin));
                OnPropertyChanged(nameof(GroupPileSettlementXMax));
                OnPropertyChanged(nameof(GroupPileSettlementYMin));
                OnPropertyChanged(nameof(GroupPileSettlementYMax));
            });
        }

        // 杭軸力更新時更新メソッド
        // Undo はセル単位 (デバウンスなし) — Ctrl+Z で 1 セルずつ巻き戻し可能。
        // Phase D-2 のハイブリッド手書き Clone により DeepCopy は ~25ms と高速、セル単位でも体感無感。
        [RelayCommand]
        private void DataGridPileAxialForce_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // 反復解析 (群杭沈下「反復」ルート) の CaseRecord 確認
                if (!ConfirmAnalysisConditionChange("反復", "杭軸力編集"))
                {
                    e.Cancel = true;
                    return;
                }
                // 水平/単杭/基礎梁考慮鉛直 解析結果も同じ理由で陳腐化するため
                // 編集確定前にユーザー確認の上クリアする (要素分割は保持)。
                if (!CheckAndResetAnalysisResultsKeepingSplit("杭軸力編集"))
                {
                    e.Cancel = true;
                    return;
                }
            }

            HandleDataGridCellEditEnding(e);

            // ΣV は全杭配置の地震時軸力の合計。荷重ケース側から見ると外の値なので、
            // 編集したここから知らせないと、水平解析ウィンドウの ΣV / ΣH/ΣV 列が
            // 古いまま残る。
            if (CurrentInputModel?.LoadCasesInput?.AllSeismicLoadCases != null)
            {
                foreach (var lc in CurrentInputModel.LoadCasesInput.AllSeismicLoadCases)
                    lc?.RaiseForceSummaryChanged();
            }
        }

        // 前後杭更新メソッド
        [RelayCommand]
        private void DataGridIsFrontPile_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            HandleDataGridCellEditEnding(e);
        }

        // 一般節点更新メソッド
        [RelayCommand]
        private void DataGridInputNodes_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            HandleDataGridCellEditEnding(e);
        }

        // 杭配置表編集開始時メソッド
        // 編集開始時の処理は code-behind の DataGrid のイベントで行う (旧 DataGridPileLayout_OnBeginningEdit は参照が無かったので 2026-09-19 に撤去)。

        // 杭要素分割解除確認メソッド
        public bool CheckAndResetElementSplit(string text)
        {
            if (IsElementSplit == true)
            {
                MessageBoxResult result = MessageService.Show(
                    $"{text}を編集、確定するには、入力済みの杭要素分割および、" +
                    $"\n解析結果が存在する場合は解析結果を削除する必要があります。" +
                    $"\nよろしいですか。",
                    "確認",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Cancel)
                    return false;
                else
                {
                    // 群杭沈下・結果セット・入力モデル内の沈下結果まで含めて捨てる。
                    // フラグ 4 つだけ消していた頃は、消えたはずの結果が残っていた。
                    ClearAllAnalysisState(includeElementSplit: true);
                    // 変更後（以下の箇所で適用）
                    RequestUpdateWindow();
                }
            }
            return true;
        }

        // 杭配置表マウス右ボタン押メソッド
        [RelayCommand]
        private static void DataGridPileLayout_OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                // マウス位置で ContextMenu を表示
            }
        }
        // 列の自動生成の扱いは MainWindow.xaml.cs の同名ハンドラが持つ (XAML の AutoGeneratingColumn が指すのはそちら)。
        // ViewModel 側にあった写しは 2026-09-19 に撤去。

        [RelayCommand]
        private void ComboBoxEmbedmentNums_OnPreviewMouseDown(MouseButtonEventArgs e)
        { }
        [RelayCommand]
        private void ComboBoxEmbedmentGroundNo_OnPreviewMouseDown(MouseButtonEventArgs e)
        { }
        [RelayCommand]
        private void TextBoxBottomAltitude_OnPreviewMouseDown(MouseButtonEventArgs e)
        { }
        [RelayCommand]
        private void DataGridEmbedment_OnBeginningEdit(DataGridBeginningEditEventArgs e)
        {
            if (!CheckAndResetElementSplit("根入部"))
                e.Cancel = true;
        }
        [RelayCommand]
        private static void ButtonGround_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
        }
        [RelayCommand]
        private static void ButtonPileBody_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
        }
        [RelayCommand]
        private static void ButtonSettlement_OnPreviewMouseDown(MouseButtonEventArgs e)
        {
        }
        // 根入れ段数の選択の反映は入力側のセッターで行う (旧 ComboBoxEmbedmentNums_OnSelectionChanged は参照が無かったので 2026-09-19 に撤去)。

        [RelayCommand]
        private void TextBoxAltitude_OnTextChanged(TextChangedEventArgs e)
        {
            UpdateEmbedment();
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }


        private EmbedmentDataItem CreateNewEmbedmentDataItem(int index, int currentCollectionSize)
        {
            EmbedmentDataItem newItem;
            if (currentCollectionSize > 0 && index > 0)
            {
                EmbedmentDataItem lastItem = CurrentInputModel.EmbedmentInput.EmbedmentLayers[index - 1];
                newItem = new EmbedmentDataItem
                {
                    No = index + 1,
                    LayerThickness = lastItem.LayerThickness,
                    X1 = lastItem.X1,
                    X2 = lastItem.X2,
                    Y1 = lastItem.Y1,
                    Y2 = lastItem.Y2,
                };
            }
            else
            {
                newItem = new EmbedmentDataItem
                {
                    No = index + 1,
                    LayerThickness = 5.0,
                    X1 = 0.0,
                    X2 = 50.0,
                    Y1 = 0.0,
                    Y2 = 50.0,
                };
            }
            return newItem;
        }
        [RelayCommand]
        private void DataGridEmbedment_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            HandleDataGridCellEditEnding(e, () => UpdateEmbedment());
        }
        [RelayCommand]
        private void DataGridSoilPile_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            HandleDataGridCellEditEnding(e, () =>
            {
                // GroupPileLoadDia 等の編集後、個別十字系なら矩形荷重を再生成
                RebuildAutoCrossRectLoadsIfNeeded();
            });
        }

        // 根入部データグリッド更新メソッド
        public void UpdateEmbedment()
        {
            // EmbedmentCollection の更新
            for (int i = CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count - 1; i >= 0; i--)
            {
                if (i == CurrentInputModel.EmbedmentInput.EmbedmentLayers.Count - 1)
                    CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude = CurrentInputModel.EmbedmentInput.BottomAltitude;
                else
                    CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude = CurrentInputModel.EmbedmentInput.EmbedmentLayers[i + 1].TopAltitude;
                CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].TopAltitude = CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].BottomAltitude
                    + CurrentInputModel.EmbedmentInput.EmbedmentLayers[i].LayerThickness;
            }
        }
        [RelayCommand]
        private void DataGridRectLoads_OnCellEditEnding(DataGridCellEditEndingEventArgs e)
        {
            // 解析結果が保存されている場合は警告 (両ルートとも RectLoads を共有するため両方破棄)
            if (e.EditAction == DataGridEditAction.Commit
                && !ConfirmAnalysisConditionChange("両方", "矩形荷重 (編集)"))
            {
                e.Cancel = true;
                return;
            }

            // 矩形荷重は群杭沈下だけが読む。水平解析の結果を陳腐化させない
            HandleDataGridCellEditEnding(e, scope: AnalysisInputScope.Settlement, customAction: () =>
            {
                IsGroupPileSettlementAnalysisDone = false;
                // ユーザ編集時は個別十字系から「任意矩形」に切替
                SwitchToAnyRectIfCrossType();
                // 個別矩形系では編集後 GroupPileLoadDia DataGrid を非表示にするためフラグ更新
                var lt = CurrentInputModel?.PileGroupSettlement?.LoadingType;
                if (lt == "個別矩形" || lt == "個別矩形（基礎梁考慮）")
                {
                    IsRectLoadFreshFromAutoGen = false;
                }
            });
        }


        /// <summary>
        /// 沈下用土層の表のセル編集を確定する (群杭沈下ウィンドウの表から呼ばれる)。
        ///
        /// <para><b>値が変わらない確定では何もしない</b> (Undo の履歴・「再解析が必要」の印・解析結果の破棄の確認)。
        /// セルに入って出ただけ・同じ値を打ち直しただけで履歴が 1 段増えると、Ctrl+Z を押しても何も戻らない段が挟まる。
        /// 比べるのは確定の前後の値で、確定の前に控えを取り、書き込んだあとで値が同じなら控えを捨てる。</para>
        ///
        /// <para>以前はこの表の編集は画面側で受けており、Undo の履歴を作っていなかった (Ctrl+Z で戻らない)。
        /// 層厚の計算もバインディングの書き込みより前で、打ち込んだ下端の値ではなく 1 つ前の値で計算していた。
        /// ここにあった同名の処理 (値を比べずに毎回履歴を作る) はどこからも呼ばれていなかった。</para>
        /// </summary>
        internal void CommitSettlementSoilLayerCellEdit(DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row?.Item is not SettlementSoilLayer layer) return;
            if (e.EditingElement is not TextBox box) return;
            var binding = box.GetBindingExpression(TextBox.TextProperty);
            var property = binding?.ParentBinding?.Path?.Path is { } path
                ? typeof(SettlementSoilLayer).GetProperty(path)
                : null;
            if (binding == null || property == null || !property.CanWrite) return;

            object? oldValue = property.GetValue(layer);
            var culture = binding.ParentBinding.ConverterCulture
                          ?? box.Language?.GetSpecificCulture()
                          ?? System.Globalization.CultureInfo.InvariantCulture;

            // 触っていない (表示のまま) なら変わらない。表示は丸めてある (例: 変形係数は整数・3 桁区切り) ので、
            // そのまま書き戻すと値が丸められる。元の値を丸めずに書き戻すようにしてから抜ける
            if (oldValue is double oldNumber
                && box.Text == FormatCellText(oldNumber, binding.ParentBinding.StringFormat, culture))
            {
                box.Text = oldNumber.ToString("R", culture);
                return;
            }
            if (IsSameCellValue(oldValue, ParseCellText(box.Text, property.PropertyType))) return;

            // 下端Z は 1 つ上の層の下端より下でなければならない (上下は表の並びではなく土層の並び。表は並べ替えできる)
            var layers = CurrentInputModel?.PileGroupSettlement?.SettlementSoilLayers;
            if (property.Name == nameof(SettlementSoilLayer.BottomAltitude)
                && NumericText.TryParse(box.Text, out double newBottom)
                && layers != null && layers.IndexOf(layer) is int index and > 0
                && !(newBottom < layers[index - 1].BottomAltitude))
            {
                MessageService.Show("下端Zは一つ上のセルの値より小さくなければなりません。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                box.Text = FormatCellText(layer.BottomAltitude, binding.ParentBinding.StringFormat, culture);
                e.Cancel = true;
                return;
            }

            // 解析結果が保存されている場合は警告 (土層は両ルート共通入力)
            if (!ConfirmAnalysisConditionChange("両方", "土層 (編集)"))
            {
                e.Cancel = true;
                return;
            }

            var before = CaptureInputEdit();
            binding.UpdateSource();
            // 読めない文字などで書き込めなかったときも、値は変わっていないので履歴を作らない
            if (IsSameCellValue(oldValue, property.GetValue(layer))) return;

            // 層厚は下端から決まる。書き込んだあとの値で計算する
            UpdateSettlementSoilLayer();
            IsGroupPileSettlementAnalysisDone = false;
            // 沈下用の土層は群杭沈下だけが読む。水平解析の結果は陳腐化しない
            CompleteInputEdit(before, "沈下用土層の編集", AnalysisInputScope.Settlement);
        }

        /// <summary>セルの表示と同じ書式で値を文字にする (バインディングの StringFormat の規則に合わせる)。</summary>
        internal static string FormatCellText(double value, string? stringFormat, System.Globalization.CultureInfo culture)
        {
            if (string.IsNullOrEmpty(stringFormat)) return value.ToString(culture);
            string format = stringFormat.Contains('{') ? stringFormat : "{0:" + stringFormat + "}";
            return string.Format(culture, format, value);
        }

        /// <summary>セルに打ち込まれた文字を、書き込まれる値として読む (読めなければ null = 比べられない)。</summary>
        internal static object? ParseCellText(string? text, Type type)
        {
            if (type == typeof(string)) return text ?? "";
            if (type == typeof(double)) return NumericText.TryParse(text, out double d) ? d : null;
            if (type == typeof(int)) return NumericText.TryParse(text, out int i) ? i : null;
            return null;
        }

        /// <summary>
        /// セルの値が同じか。数値でない値 (NaN) どうしは同じとみなす。文字列は空と null を区別しない
        /// (空のセルを確定すると null が空文字になるが、利用者には同じに見える)。
        /// 比べられない (null) ときは変わったものとして扱う (安全側)。
        /// </summary>
        internal static bool IsSameCellValue(object? oldValue, object? newValue) => (oldValue, newValue) switch
        {
            (double a, double b) => a == b || (double.IsNaN(a) && double.IsNaN(b)),
            (string or null, string s) => (oldValue as string ?? "") == s,
            (_, null) => false,
            _ => Equals(oldValue, newValue),
        };
    }
}

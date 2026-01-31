using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel.Examples.cs
    ///
    /// 責任範囲:
    /// - 設計例集データの生成（Example3_1, Example3_2, Example3_3, Example3_4, ExampleK7, ExampleK8, Example9）
    /// - 各例集メソッドの再入防止制御
    /// - サンプルデータの自動設定（地盤、荷重ケース、杭体、杭配置など）
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        // 再入防止フラグ（partial クラス内に置く）
        private bool _isExampleRunning = false;

        // 共通の開始チェック（呼び出し側は true の場合のみ処理を続行）
        private bool TryStartExample(string exampleName)
        {
            if (_isExampleRunning)
            {
                Debug.WriteLine($"{exampleName}: reentrancy prevented.");
                return false;
            }
            _isExampleRunning = true;
            Debug.WriteLine($"{exampleName}: start");
            return true;
        }

        // 共通の終了処理（例外でも finally で必ず呼ぶ）
        private void EndExample(string exampleName)
        {
            _isExampleRunning = false;
            Debug.WriteLine($"{exampleName}: end");
        }

        /// <summary>
        /// 杭例題データをJSONから読み込んで適用する共通ヘルパーメソッド
        /// </summary>
        /// <param name="pileJsonFileName">杭例題JSONファイル名（拡張子なし）</param>
        /// <param name="displayName">表示名（メッセージボックス用）</param>
        private async Task LoadPileExampleAsync(string pileJsonFileName, string displayName)
        {
            // 砂時計にする（UI スレッドで設定）
            Mouse.OverrideCursor = Cursors.Wait;
            // UI を一度描画させる（カーソル表示のために短時間yield）
            await Task.Delay(50);

            // Undoポイントを追加
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            // 新規作成状態にリセット
            CurrentFilePath = null;
            IsElementSplit = false;
            IsHorizontalAnalysisDone = false;
            IsVerticalAnalysisDone = false;
            IsGroupPileSettlementAnalysisDone = false;
            UpdateWindowImmediate();
            UpdateTreeView();

            // JSONから杭例題データを読み込む
            var pileData = PileExampleLoader.LoadFromFile(pileJsonFileName);

            // 地盤例題を読み込む
            var groundLayerViewModel = new GroundLayerViewModel(this);
            var groundData = GroundExampleLoader.LoadFromFile(pileData.GroundExampleName);
            GroundExampleLoader.ApplyToGroundInput(groundLayerViewModel.GroundInput, groundData);
            groundLayerViewModel.Update(); // 土層プロパティを再計算
            CurrentInputModel.GroundsInput[0] = groundLayerViewModel.GroundInput.DeepCopy();

            // 杭例題データを適用
            PileExampleLoader.ApplyToInputModel(CurrentInputModel, pileData, this);

            // 各 PileSection を再計算してプロパティ反映
            foreach (var pb in CurrentInputModel.PileBodies)
            {
                foreach (var seg in pb.PileBodySegments)
                {
                    var sec = seg.PileSection;
                    if (!string.IsNullOrWhiteSpace(sec.SelectedPrecastPile?.Name))
                    {
                        sec.RecalculateSelectedPrecastPile();
                    }
                    sec.RecalculatePileDia();
                    sec.RecalculateConcreteE();
                    sec.SetSpecs();
                }
            }

            // 杭体数リストを更新（UIのコンボボックス用）
            CurrentInputModel.UpdateCountLists();

            UpdateWindowImmediate();
            UpdateTreeView();

            // 読み込み完了メッセージ
            MessageBox.Show($"{displayName}を読み込みました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 群杭沈下解析例題データをJSONから読み込んで適用する共通ヘルパーメソッド
        /// </summary>
        /// <param name="jsonFileName">JSONファイル名（拡張子なし）</param>
        /// <param name="displayName">表示名（メッセージボックス用）</param>
        private async Task LoadGroupSettlementExampleAsync(string jsonFileName, string displayName)
        {
            // 砂時計にする（UI スレッドで設定）
            Mouse.OverrideCursor = Cursors.Wait;
            // UI を一度描画させる（カーソル表示のために短時間yield）
            await Task.Delay(50);

            // Undoポイントを追加
            _undoManager.SaveState(CurrentInputModel.DeepCopy());

            // 要素分割・解析状態をリセット
            IsElementSplit = false;
            IsHorizontalAnalysisDone = false;
            IsVerticalAnalysisDone = false;

            // JSONから群杭沈下解析例題データを読み込む
            var data = GroupSettlementExampleLoader.LoadFromFile(jsonFileName);

            // 地盤例題を読み込む（指定がある場合）
            if (!string.IsNullOrEmpty(data.GroundExampleName))
            {
                var groundLayerViewModel = new GroundLayerViewModel(this);
                var groundData = GroundExampleLoader.LoadFromFile(data.GroundExampleName);
                GroundExampleLoader.ApplyToGroundInput(groundLayerViewModel.GroundInput, groundData);
                groundLayerViewModel.Update(); // 土層プロパティを再計算
                CurrentInputModel.GroundsInput[0] = groundLayerViewModel.GroundInput.DeepCopy();
            }

            // 例題データを適用
            GroupSettlementExampleLoader.ApplyToInputModel(CurrentInputModel, data, this);

            // 追加：LoadPileExampleAsync と同様に各 PileSection を再計算してプロパティ反映
            foreach (var pb in CurrentInputModel.PileBodies)
            {
                foreach (var seg in pb.PileBodySegments)
                {
                    var sec = seg.PileSection;
                    if (!string.IsNullOrWhiteSpace(sec.SelectedPrecastPile?.Name))
                    {
                        sec.RecalculateSelectedPrecastPile();
                    }
                    sec.RecalculatePileDia();
                    sec.RecalculateConcreteE();
                    sec.SetSpecs();
                }
            }

            // 追加：杭体数リストを更新（UIのコンボボックス用）
            CurrentInputModel.UpdateCountLists();

            UpdatePileLayoutNo();
            IsGroupPileSettlementAnalysisDone = false;
            UpdateWindowImmediate();
            UpdateTreeView();

            // 読み込み完了メッセージ
            MessageBox.Show($"{displayName}を読み込みました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 設計例集3.1
        [RelayCommand]
        private async Task Example3_1()
        {
            if (!TryStartExample(nameof(Example3_1))) return;
            try
            {
                await LoadPileExampleAsync("PileExample3_1", "設計例3.1");
            }
            finally
            {
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example3_1));
            }
        }

        // 設計例集3.2
        [RelayCommand]
        private async Task Example3_2()
        {
            if (!TryStartExample(nameof(Example3_2))) return;
            try
            {
                await LoadPileExampleAsync("PileExample3_2", "設計例3.2");
            }
            finally
            {
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example3_2));
            }
        }


        // 設計例集3.3
        [RelayCommand]
        private async Task Example3_3()
        {
            if (!TryStartExample(nameof(Example3_3))) return;
            try
            {
                await LoadPileExampleAsync("PileExample3_3", "設計例3.3");
            }
            finally
            {
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example3_3));
            }
        }


        // 設計例集3.4
        [RelayCommand]
        private async Task Example3_4()
        {
            if (!TryStartExample(nameof(Example3_4))) return;
            try
            {
                await LoadPileExampleAsync("PileExample3_4", "設計例3.4");
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example3_4));
            }
        }

        // 関東支部 計算例8
        [RelayCommand]
        private async Task ExampleK8()
        {
            if (!TryStartExample(nameof(ExampleK8))) return;
            try
            {
                await LoadPileExampleAsync("PileExampleK8", "関東支部8");
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(ExampleK8));
            }
        }




        // 基礎指針'19 計算例9
        [RelayCommand]
        private async Task Example9()
        {
            if (!TryStartExample(nameof(Example9))) return;
            try
            {
                await LoadPileExampleAsync("PileExample9", "基礎指針'19 計算例9");
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example9));
            }
        }


        // 関東支部　設計例7
        [RelayCommand]
        private async Task ExampleK7()
        {
            if (!TryStartExample(nameof(ExampleK7))) return;
            try
            {
                await LoadGroupSettlementExampleAsync("GroupSettlementK7", "設計例7");
            }
            finally
            {
                Mouse.OverrideCursor = null;
                EndExample(nameof(ExampleK7));
            }
        }

        // 設計例5
        [RelayCommand]
        private async Task OnExample5()
        {
            if (!TryStartExample(nameof(OnExample5))) return;
            try
            {
                await LoadGroupSettlementExampleAsync("GroupSettlement5", "設計例5");
            }
            finally
            {
                Mouse.OverrideCursor = null;
                EndExample(nameof(OnExample5));
            }
        }

        // 設計例集2.1
        [RelayCommand]
        private async Task OnExample2_1()
        {
            if (!TryStartExample(nameof(OnExample2_1))) return;
            try
            {
                await LoadGroupSettlementExampleAsync("GroupSettlement2_1", "設計例2.1");
            }
            finally
            {
                Mouse.OverrideCursor = null;
                EndExample(nameof(OnExample2_1));
            }
        }
    }
}

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
                return false;
            }
            _isExampleRunning = true;
            return true;
        }

        // 共通の終了処理（例外でも finally で必ず呼ぶ）
        private void EndExample(string exampleName)
        {
            _isExampleRunning = false;
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
            await Task.Delay(10);

            // Undoポイントを追加（バックグラウンドでDeepCopy）
            var undoCopy = await Task.Run(() => CurrentInputModel.DeepCopy());
            _undoManager.SaveState(undoCopy);

            // SoilPile再生成通知を抑制（読み込み完了後に一括で行う）
            CurrentInputModel.SuppressNotifications();

            // 新規作成状態にリセット（UI更新はデータ読み込み後にまとめて実行）
            CurrentFilePath = null;
            IsElementSplit = false;
            IsHorizontalAnalysisDone = false;
            IsVerticalAnalysisDone = false;
            IsGroupPileSettlementAnalysisDone = false;
            IsVerticalBeamAnalysisDone = false;
            IsAnalysisResultVisible = false;
            CurrentModel = null;

            // JSON読み込み＋地盤データ準備をバックグラウンドで実行（UIバインド済みコレクションには触れない）
            var (pileData, groundInputCopies) = await Task.Run(() =>
            {
                // JSONから杭例題データを読み込む
                var pd = PileExampleLoader.LoadFromFile(pileJsonFileName);

                // 地盤例題を読み込む (Ground No1)
                var copies = new System.Collections.Generic.List<GroundInput>();

                var glvm = new GroundLayerViewModel(this);
                var groundData = GroundExampleLoader.LoadFromFile(pd.GroundExampleName);
                GroundExampleLoader.ApplyToGroundInput(glvm.GroundInput, groundData);
                glvm.Update();
                copies.Add(glvm.GroundInput.DeepCopy());

                // 追加地盤 (Ground No2 以降)
                if (pd.AdditionalGroundExampleNames != null)
                {
                    foreach (var name in pd.AdditionalGroundExampleNames)
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        var glvm2 = new GroundLayerViewModel(this);
                        var data2 = GroundExampleLoader.LoadFromFile(name);
                        GroundExampleLoader.ApplyToGroundInput(glvm2.GroundInput, data2);
                        glvm2.Update();
                        copies.Add(glvm2.GroundInput.DeepCopy());
                    }
                }

                return (pd, copies);
            });

            // モデルへの適用はUIスレッドで実行（CollectionView のスレッド制約を回避）
            CurrentInputModel.GroundsInput[0] = groundInputCopies[0];
            // 追加地盤は GroundsInput に Add (Index 1 以降)
            while (CurrentInputModel.GroundsInput.Count > 1)
                CurrentInputModel.GroundsInput.RemoveAt(CurrentInputModel.GroundsInput.Count - 1);
            for (int i = 1; i < groundInputCopies.Count; i++)
                CurrentInputModel.GroundsInput.Add(groundInputCopies[i]);

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

            // SoilPile を一括再生成（SuppressNotifications で抑制していた分）
            CurrentInputModel.GenerateSoilPiles();

            // 通知を再開（ここでは再描画をトリガーしない）
            CurrentInputModel.ResumeNotificationsQuiet();

            // ステータスバーの杭本数を更新（バッチ代入ではCollectionChangedが発火しないため）
            OnPropertyChanged(nameof(PileCountText));

            // 集計値・OTM・重心を更新（バッチ代入ではPropertyChangedが発火しないため）
            UpdateSumAndOTM();

            // 最終描画（UpdateWindow() 内で UpdateTreeView() も実行されるため別途呼ばない）
            UpdateWindowImmediate();

            // タイトルバーに例題名を表示 (ファイル保存または別ファイル読込でクリアされる)
            CurrentFilePath = null;
            LoadedExampleName = displayName;

            // 読み込み完了メッセージ
            ShowToast($"{displayName}を読み込みました。");
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
            await Task.Delay(10);

            // Undoポイントを追加（バックグラウンドでDeepCopy）
            var undoCopy = await Task.Run(() => CurrentInputModel.DeepCopy());
            _undoManager.SaveState(undoCopy);

            // SoilPile再生成通知を抑制（読み込み完了後に一括で行う）
            CurrentInputModel.SuppressNotifications();

            // 杭要素分割・解析状態をリセット
            IsElementSplit = false;
            IsHorizontalAnalysisDone = false;
            IsVerticalAnalysisDone = false;
            IsVerticalBeamAnalysisDone = false;

            // JSON読み込み＋地盤データ準備をバックグラウンドで実行（UIバインド済みコレクションには触れない）
            var (data, groundInputCopy) = await Task.Run(() =>
            {
                // JSONから群杭沈下解析例題データを読み込む
                var d = GroupSettlementExampleLoader.LoadFromFile(jsonFileName);

                // 地盤例題を読み込む（指定がある場合）
                GroundInput? gi = null;
                if (!string.IsNullOrEmpty(d.GroundExampleName))
                {
                    var groundLayerViewModel = new GroundLayerViewModel(this);
                    var groundData = GroundExampleLoader.LoadFromFile(d.GroundExampleName);
                    GroundExampleLoader.ApplyToGroundInput(groundLayerViewModel.GroundInput, groundData);
                    groundLayerViewModel.Update(); // 土層プロパティを再計算
                    gi = groundLayerViewModel.GroundInput.DeepCopy();
                }

                return (d, gi);
            });

            // モデルへの適用はUIスレッドで実行（CollectionView のスレッド制約を回避）
            if (groundInputCopy != null)
                CurrentInputModel.GroundsInput[0] = groundInputCopy;

            // 例題データを適用
            GroupSettlementExampleLoader.ApplyToInputModel(CurrentInputModel, data, this);

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

            UpdatePileLayoutNo();

            // SoilPile を一括再生成（SuppressNotifications で抑制していた分）
            CurrentInputModel.GenerateSoilPiles();

            IsGroupPileSettlementAnalysisDone = false;

            // 通知を再開（ここでは再描画をトリガーしない）
            CurrentInputModel.ResumeNotificationsQuiet();

            // ステータスバーの杭本数を更新（バッチ代入ではCollectionChangedが発火しないため）
            OnPropertyChanged(nameof(PileCountText));

            // 集計値・OTM・重心を更新（バッチ代入ではPropertyChangedが発火しないため）
            UpdateSumAndOTM();

            // 最終描画（UpdateWindow() 内で UpdateTreeView() も実行されるため別途呼ばない）
            UpdateWindowImmediate();

            // タイトルバーに例題名を表示 (ファイル保存または別ファイル読込でクリアされる)
            CurrentFilePath = null;
            LoadedExampleName = displayName;

            // 読み込み完了メッセージ
            ShowToast($"{displayName}を読み込みました。");
        }

        // 設計例集3.1
        [RelayCommand]
        private async Task Example3_1()
        {
            if (!TryStartExample(nameof(Example3_1))) return;
            try
            {
                await LoadPileExampleAsync("PileExample3_1", "設計例集3.1");
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
                await LoadPileExampleAsync("PileExample3_2", "設計例集3.2");
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
                await LoadPileExampleAsync("PileExample3_3", "設計例集3.3");
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
                await LoadPileExampleAsync("PileExample3_4", "設計例集3.4");
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example3_4));
            }
        }

        // 設計例集3.5
        [RelayCommand]
        private async Task Example3_5()
        {
            if (!TryStartExample(nameof(Example3_5))) return;
            try
            {
                await LoadPileExampleAsync("PileExample3_5", "設計例集3.5");
            }
            finally
            {
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example3_5));
            }
        }

        // 関東支部 計算例8
        [RelayCommand]
        private async Task ExampleK8()
        {
            if (!TryStartExample(nameof(ExampleK8))) return;
            try
            {
                await LoadPileExampleAsync("PileExampleK8", "関東支部 計算例8");
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(ExampleK8));
            }
        }




        // 基礎指針'19 計算例5 PHC杭
        // 杭例題に加えて、計算例5の群杭沈下検討用条件（載荷面・矩形荷重・沈下計算用地層）も同時に読み込む
        [RelayCommand]
        private async Task Example5Pile()
        {
            if (!TryStartExample(nameof(Example5Pile))) return;
            try
            {
                await LoadPileExampleAsync("PileExample5", "基礎指針'19 計算例5");

                // 群杭沈下検討用条件のみを追加ロード（PileBodies / PileLayout 等は上書きしない）
                var groupData = await Task.Run(() => GroupSettlementExampleLoader.LoadFromFile("GroupSettlement5"));
                GroupSettlementExampleLoader.ApplySettlementConditionsOnly(CurrentInputModel, groupData);
                UpdateWindowImmediate();
            }
            finally
            {
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example5Pile));
            }
        }

        // 計算例7 場所打ちRC拡底杭
        [RelayCommand]
        private async Task Example7()
        {
            if (!TryStartExample(nameof(Example7))) return;
            try
            {
                await LoadPileExampleAsync("PileExample7", "基礎指針'19 計算例7");
            }
            finally
            {
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example7));
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

        // 基礎指針'19 計算例10
        [RelayCommand]
        private async Task Example10()
        {
            if (!TryStartExample(nameof(Example10))) return;
            try
            {
                await LoadPileExampleAsync("PileExample10", "基礎指針'19 計算例10");
            }
            finally
            {
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example10));
            }
        }


        // 関東支部　設計例7
        [RelayCommand]
        private async Task ExampleK7()
        {
            if (!TryStartExample(nameof(ExampleK7))) return;
            try
            {
                await LoadGroupSettlementExampleAsync("GroupSettlementK7", "関東支部 設計例7");
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
                await LoadGroupSettlementExampleAsync("GroupSettlement2_1", "設計例集2.1");
            }
            finally
            {
                Mouse.OverrideCursor = null;
                EndExample(nameof(OnExample2_1));
            }
        }
    }
}

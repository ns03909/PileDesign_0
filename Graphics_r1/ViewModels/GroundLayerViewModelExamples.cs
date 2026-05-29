using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.Views;
using System;
using System.Diagnostics;
using System.IO;

namespace PileDesign.ViewModels
{
    public partial class GroundLayerViewModel : ObservableObject, ICloseable
    {
        public ChangWindow ChangWindowInstance { get; internal set; }

        /// <summary>
        /// 例題メニュー項目を初期化
        /// </summary>
        private void InitializeExampleItems()
        {
            ExampleItems.Clear();

            ExampleItems.Add(new ExampleItem("基礎指針'19 計算例1", Example1Command));
            ExampleItems.Add(new ExampleItem("基礎指針'19 計算例2", Example2Command));
            ExampleItems.Add(new ExampleItem("基礎指針'19 計算例3", Example3Command));
            ExampleItems.Add(new ExampleItem("基礎指針'19 計算例5", Example5Command));
            ExampleItems.Add(new ExampleItem("基礎指針'19 計算例7", Example7Command));
            ExampleItems.Add(new ExampleItem("基礎指針'19 計算例9", Example9Command));
            ExampleItems.Add(new ExampleItem("設計例集3.1", Example3_1Command));
            ExampleItems.Add(new ExampleItem("設計例集3.2", Example3_2Command));
            ExampleItems.Add(new ExampleItem("設計例集3.3", Example3_3Command));
            ExampleItems.Add(new ExampleItem("設計例集3.4", Example3_4Command));
            ExampleItems.Add(new ExampleItem("設計例集3.8", Example3_8Command));
            ExampleItems.Add(new ExampleItem("関東支部5.5", ExampleK5_5Command));
            ExampleItems.Add(new ExampleItem("関東支部7章", ExampleK7Command));
            ExampleItems.Add(new ExampleItem("関東支部8章", ExampleK8Command));
            ExampleItems.Add(new ExampleItem("八重洲二丁目No.1", ExampleYeasu2Command));
        }

        /// <summary>
        /// JSONファイルから例題データを読み込み、GroundInputに適用する共通メソッド
        /// </summary>
        /// <param name="jsonFileName">JSONファイル名（拡張子なし）</param>
        /// <param name="displayName">表示名（エラーメッセージ用）</param>
        private void LoadExampleFromJson(string jsonFileName, string displayName)
        {
            try
            {
                var data = GroundExampleLoader.LoadFromFile(jsonFileName);
                GroundExampleLoader.ApplyToGroundInput(GroundInput, data);

                TextBoxGroundWaterGLDepth_LostFocus();
                TextBoxStressGLDepth_LostFocus();

                Update();
            }
            catch (FileNotFoundException ex)
            {
                throw new InvalidOperationException($"例題「{displayName}」のデータファイルが見つかりません。\nExamplesフォルダに {jsonFileName}.json が存在することを確認してください。", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"例題「{displayName}」のデータ読み込み中にエラーが発生しました。\n{ex.Message}", ex);
            }
        }

        // 基礎指針'19 計算例1
        [RelayCommand]
        private void Example1()
        {
            LoadExampleFromJson("Example1", "基礎指針'19 計算例1");
        }

        // 基礎指針'19 計算例2
        [RelayCommand]
        private void Example2()
        {
            LoadExampleFromJson("Example2", "基礎指針'19 計算例");
        }

        // 基礎指針'19 計算例3
        [RelayCommand]
        private void Example3()
        {
            LoadExampleFromJson("Example3", "基礎指針'19 計算例3");
        }

        // 基礎指針'19 計算例5
        [RelayCommand]
        private void Example5()
        {
            LoadExampleFromJson("Example5", "基礎指針'19 計算例");
        }


        // 基礎指針'19 計算例7
        [RelayCommand]
        private void Example7()
        {
            LoadExampleFromJson("Example7", "基礎指針'19 計算例7");
        }

        // 基礎指針'19 計算例9
        [RelayCommand]
        private void Example9()
        {
            LoadExampleFromJson("Example9", "基礎指針'19 計算例9");
        }

        // 設計例集3.1
        [RelayCommand]
        private void Example3_1()
        {
            LoadExampleFromJson("Example3_1", "設計例集3.1");
        }

        // 設計例集3.2
        [RelayCommand]
        private void Example3_2()
        {
            LoadExampleFromJson("Example3_2", "設計例集3.2");
        }

        // 設計例集3.3
        [RelayCommand]
        private void Example3_3()
        {
            LoadExampleFromJson("Example3_3", "設計例集3.3");
        }

        // 設計例集3.4
        [RelayCommand]
        private void Example3_4()
        {
            LoadExampleFromJson("Example3_4", "設計例集3.4");
        }

        // 設計例集3.8 (代表地盤 = 地盤1)
        [RelayCommand]
        private void Example3_8()
        {
            LoadExampleFromJson("Example3_8_1", "設計例集3.8");
        }

        // 関東支部5.5
        [RelayCommand]
        private void ExampleK5_5()
        {
            LoadExampleFromJson("ExampleK5_5", "関東支部5.5");
        }

        // 関東支部7章
        [RelayCommand]
        private void ExampleK7()
        {
            LoadExampleFromJson("ExampleK7", "関東支部7章");
        }

        // 関東支部8章
        [RelayCommand]
        private void ExampleK8()
        {
            LoadExampleFromJson("ExampleK8", "関東支部8章");
        }

        // 矢板二丁目No.1
        [RelayCommand]
        private void ExampleYeasu2()
        {
            LoadExampleFromJson("ExampleYeasu2", "八重洲二丁目No.1");
        }

#if DEBUG
        /// <summary>
        /// 現在のGroundInputデータをJSONファイルとしてエクスポート（開発用）
        /// </summary>
        [RelayCommand]
        private void ExportCurrentToJson()
        {
            var fileName = $"Export_{DateTime.Now:yyyyMMdd_HHmmss}";
            var displayName = GroundInput.GroundRef ?? "Unknown";

            try
            {
                GroundExampleLoader.ExportToJson(GroundInput, fileName, displayName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GroundExampleExport] エクスポート失敗: {ex.GetType().Name}: {ex.Message}");
            }
        }
#endif
    }
}

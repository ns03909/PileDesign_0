using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace PileDesign.Services
{
    /// <summary>
    /// ファイル操作に関するサービスクラス
    /// JSON シリアライズ、デシリアライズ、コレクション型変換を担当
    /// </summary>
    public class FileOperationService
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public FileOperationService(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        /// <summary>
        /// ProjectData を JSON ファイルに保存
        /// </summary>
        public void SaveProjectData(string filePath, InputModel inputModel, AnaModel anaModel,
            IList<FEM.VerticalBeamCaseResult> verticalBeamCaseResults = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            var projectData = new ProjectData
            {
                InputModel = inputModel,
                AnaModel = anaModel,
                VerticalBeamCaseResults = verticalBeamCaseResults != null
                    ? new List<FEM.VerticalBeamCaseResult>(verticalBeamCaseResults)
                    : null
            };

            string json = JsonSerializer.Serialize(projectData, _jsonOptions);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// JSON ファイルから ProjectData を読み込み
        /// </summary>
        public ProjectData LoadProjectData(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("ファイルが見つかりません。", filePath);

            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (IOException ex)
            {
                throw new IOException($"ファイルの読込に失敗しました。\n別のプロセスで使用中の可能性があります。\n{filePath}", ex);
            }

            ProjectData projectData;
            try
            {
                projectData = JsonSerializer.Deserialize<ProjectData>(json, _jsonOptions);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidOperationException(
                    $"ファイルのJSON形式が不正です。ファイルが破損しているか、対応していない形式です。\n{filePath}", ex);
            }

            if (projectData == null)
                throw new InvalidOperationException("ファイル形式が不正です。");

            // バージョン0はFormatVersionプロパティ追加前の旧ファイル → 互換あり
            const int currentVersion = 1;
            if (projectData.FormatVersion > currentVersion)
            {
                throw new InvalidOperationException(
                    $"このファイルは新しいバージョン（v{projectData.FormatVersion}）で保存されています。\n" +
                    $"現在のプログラム（v{currentVersion}）では読み込めません。プログラムを更新してください。");
            }

            return projectData;
        }

        /// <summary>
        /// InputModel の全コレクションを ObservableCollection に変換
        /// </summary>
        public void ConvertToObservableCollections(InputModel inputModel)
        {
            if (inputModel == null)
                throw new ArgumentNullException(nameof(inputModel));

            // PileGroupSettlement のコレクション変換
            if (inputModel.PileGroupSettlement != null)
            {
                inputModel.PileGroupSettlement.SettlementSoilLayers =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.SettlementSoilLayers);
                inputModel.PileGroupSettlement.RectLoads =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.RectLoads);
                inputModel.PileGroupSettlement.SettlementGridX =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.SettlementGridX);
                inputModel.PileGroupSettlement.SettlementGridY =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.SettlementGridY);
                inputModel.PileGroupSettlement.SettlementGridData =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.SettlementGridData);
            }

            // トップレベルのコレクション変換
            inputModel.PileLayoutItems = EnsureObservableCollection(inputModel.PileLayoutItems);
            inputModel.InputNodes = EnsureObservableCollection(inputModel.InputNodes);

            inputModel.GridXItems = EnsureObservableCollection(inputModel.GridXItems);
            inputModel.GridYItems = EnsureObservableCollection(inputModel.GridYItems);
            inputModel.PileBodies = EnsureObservableCollection(inputModel.PileBodies);
            inputModel.GroundsInput = EnsureObservableCollection(inputModel.GroundsInput);

            // EmbedmentInput のコレクション変換
            if (inputModel.EmbedmentInput != null)
            {
                inputModel.EmbedmentInput.EmbedmentLayers =
                    EnsureObservableCollection(inputModel.EmbedmentInput.EmbedmentLayers);
            }

            // LoadCasesInput のコレクション変換
            if (inputModel.LoadCasesInput != null)
            {
                inputModel.LoadCasesInput.LoadCasesLevel1 =
                    EnsureObservableCollection(inputModel.LoadCasesInput.LoadCasesLevel1);
                inputModel.LoadCasesInput.LoadCasesLevel2 =
                    EnsureObservableCollection(inputModel.LoadCasesInput.LoadCasesLevel2);
                inputModel.LoadCasesInput.LoadCombinations =
                    EnsureObservableCollection(inputModel.LoadCasesInput.LoadCombinations);
                inputModel.LoadCasesInput.LoadCombinationsPlus =
                    EnsureObservableCollection(inputModel.LoadCasesInput.LoadCombinationsPlus);
            }

            // ネストされたコレクションの変換
            if (inputModel.GroundsInput != null)
            {
                foreach (var ground in inputModel.GroundsInput)
                {
                    ground.GroundLayers = EnsureObservableCollection(ground.GroundLayers);
                    ground.GroundMassesData = EnsureObservableCollection(ground.GroundMassesData);
                }
            }

            if (inputModel.PileBodies != null)
            {
                foreach (var pileBody in inputModel.PileBodies)
                {
                    pileBody.PileBodySegments = EnsureObservableCollection(pileBody.PileBodySegments);
                }
            }

            // FoundationBeamInput のコレクション変換
            if (inputModel.FoundationBeamInput != null)
            {
                inputModel.FoundationBeamInput.Materials =
                    EnsureObservableCollection(inputModel.FoundationBeamInput.Materials);
                inputModel.FoundationBeamInput.Sections =
                    EnsureObservableCollection(inputModel.FoundationBeamInput.Sections);
                inputModel.FoundationBeamInput.Nodes =
                    EnsureObservableCollection(inputModel.FoundationBeamInput.Nodes);
                inputModel.FoundationBeamInput.Beams =
                    EnsureObservableCollection(inputModel.FoundationBeamInput.Beams);
            }

            // Null チェック・空コレクション初期化
            inputModel.GridXItems ??= new ObservableCollection<GridDataItem>();
            inputModel.GridYItems ??= new ObservableCollection<GridDataItem>();
        }

        /// <summary>
        /// IEnumerable を ObservableCollection に変換（既に ObservableCollection の場合はそのまま返す）
        /// </summary>
        private static ObservableCollection<T> EnsureObservableCollection<T>(IEnumerable<T> source)
        {
            if (source is ObservableCollection<T> observableCollection)
                return observableCollection;

            return source != null ? new ObservableCollection<T>(source) : new ObservableCollection<T>();
        }
    }
}

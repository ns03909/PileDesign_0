using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace PileDesign.Services
{
    /// <summary>
    /// 地盤例題データのJSONローダー
    /// </summary>
    public static class GroundExampleLoader
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>
        /// JSONファイルから例題データを読み込む
        /// </summary>
        /// <param name="fileName">JSONファイル名（拡張子なし）</param>
        /// <returns>例題データ</returns>
        public static GroundExampleData LoadFromFile(string fileName)
        {
            var examplesPath = GetExamplesPath();
            var filePath = Path.Combine(examplesPath, $"{fileName}.json");

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"例題ファイルが見つかりません: {filePath}");
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<GroundExampleData>(json, _jsonOptions)
                ?? throw new InvalidOperationException($"JSONのデシリアライズに失敗しました: {filePath}");
        }

        /// <summary>
        /// 例題データをGroundInputに適用する
        /// </summary>
        public static void ApplyToGroundInput(GroundInput groundInput, GroundExampleData data)
        {
            groundInput.GroundRef = data.GroundRef;
            groundInput.GroundTopAltitude = data.GroundTopAltitude;
            groundInput.GroundWaterGLDepth = data.GroundWaterGLDepth;
            groundInput.StressGLDepth = data.StressGLDepth;
            groundInput.GroundAcceleration1 = data.GroundAcceleration1;

            if (data.BedrockDensity.HasValue)
                groundInput.BedrockDensity = data.BedrockDensity.Value;

            if (data.BedrockShearWaveVelocity.HasValue)
                groundInput.BedrockShearWaveVelocity = data.BedrockShearWaveVelocity.Value;

            if (!string.IsNullOrEmpty(data.ShallowSoilType))
                groundInput.ShallowSoilType = data.ShallowSoilType;

            // 地層データを適用
            groundInput.GroundLayers = new ObservableCollection<GroundLayerInput>();
            foreach (var layerDto in data.GroundLayers)
            {
                groundInput.GroundLayers.Add(layerDto.ToGroundLayerInput());
            }

            // 地盤質量データを適用
            // groundTopAltitudeとglDepthから標高(AltitudeDepth)を計算
            groundInput.GroundMassesData = new ObservableCollection<GroundMassDataInput>();
            foreach (var massDto in data.GroundMassesData)
            {
                var massInput = massDto.ToGroundMassDataInput();
                // 標高 = 地盤天端標高 + GL深度（GL深度は負値）
                massInput.AltitudeDepth = data.GroundTopAltitude + massDto.GLDepth;
                groundInput.GroundMassesData.Add(massInput);
            }
        }

        /// <summary>
        /// Examplesフォルダのパスを取得
        /// </summary>
        private static string GetExamplesPath()
        {
            // 実行ファイルのディレクトリからExamplesフォルダを探す
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var assemblyDir = Path.GetDirectoryName(assemblyLocation) ?? ".";
            var examplesPath = Path.Combine(assemblyDir, "Examples");

            if (Directory.Exists(examplesPath))
            {
                return examplesPath;
            }

            // 開発時: プロジェクトディレクトリのExamplesフォルダ
            var projectDir = FindProjectDirectory(assemblyDir);
            if (projectDir != null)
            {
                var devExamplesPath = Path.Combine(projectDir, "Examples");
                if (Directory.Exists(devExamplesPath))
                {
                    return devExamplesPath;
                }
            }

            throw new DirectoryNotFoundException($"Examplesフォルダが見つかりません: {examplesPath}");
        }

        /// <summary>
        /// プロジェクトディレクトリを探す
        /// </summary>
        private static string? FindProjectDirectory(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                // .csprojファイルがあればプロジェクトディレクトリ
                if (Directory.GetFiles(dir.FullName, "*.csproj").Length > 0)
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return null;
        }

        #region JSON Export Helper (開発用)

#if DEBUG
        /// <summary>
        /// GroundInputの現在の状態をJSONファイルとしてエクスポート（開発用）
        /// </summary>
        public static void ExportToJson(GroundInput groundInput, string fileName, string displayName)
        {
            var data = new GroundExampleData
            {
                DisplayName = displayName,
                GroundRef = groundInput.GroundRef,
                GroundTopAltitude = groundInput.GroundTopAltitude,
                GroundWaterGLDepth = groundInput.GroundWaterGLDepth,
                StressGLDepth = groundInput.StressGLDepth,
                GroundAcceleration1 = groundInput.GroundAcceleration1,
                BedrockDensity = groundInput.BedrockDensity,
                BedrockShearWaveVelocity = groundInput.BedrockShearWaveVelocity,
                ShallowSoilType = groundInput.ShallowSoilType
            };

            // 地層データをDTO変換
            foreach (var layer in groundInput.GroundLayers)
            {
                data.GroundLayers.Add(new GroundLayerDto
                {
                    No = layer.No,
                    BottomGLDepth = layer.BottomGLDepth,
                    LayerThickness = layer.LayerThickness,
                    BottomAltitude = layer.BottomAltitude,
                    Name = layer.Name,
                    GranularityClass = layer.GranularityClass,
                    Density = layer.Density,
                    AgeCategory = layer.AgeCategory,
                    IsEngineeringBedrock = layer.IsEngineeringBedrock,
                    NValue = layer.NValue,
                    Cohesive = layer.Cohesive,
                    Vs = layer.Vs,
                    Es = layer.Es,
                    IsPositiveCircumResistance = layer.IsPositiveCircumResistance,
                    IsNegativeCircumResistance = layer.IsNegativeCircumResistance
                });
            }

            // 地盤質量データをDTO変換
            foreach (var mass in groundInput.GroundMassesData)
            {
                data.GroundMassesData.Add(new GroundMassDataDto
                {
                    GLDepth = mass.GLDepth,
                    NValue = mass.NValue,
                    Fc = mass.Fc,
                    Density = mass.Density,
                    VS0 = mass.VS0
                });
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(data, options);
            var examplesPath = GetExamplesPath();
            var filePath = Path.Combine(examplesPath, $"{fileName}.json");
            File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
        }
#endif

        #endregion
    }
}

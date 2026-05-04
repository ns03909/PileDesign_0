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

            if (!string.IsNullOrEmpty(data.CalculationMethod))
                groundInput.CalculationMethod = data.CalculationMethod;

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

            // 整合性確保: 各 mass 点の VS0 / Density が、その点が含まれる土層の Vs / Density と
            // 異なる場合は土層の値で上書きする (土層側を正と扱う)。
            // 層の glDepth は負値・降順 (浅→深) で並んでいることを前提とする。
            ApplyLayerValuesToMasses(groundInput);
        }

        /// <summary>
        /// 各 GroundMassData の VS0/Density を、その depth に対応する GroundLayer の値で上書きする。
        /// 土層 (Vs, Density) を正、Mass (VS0, Density) を従とみなす整合化処理。
        /// </summary>
        private static void ApplyLayerValuesToMasses(GroundInput groundInput)
        {
            if (groundInput.GroundLayers == null || groundInput.GroundLayers.Count == 0) return;
            if (groundInput.GroundMassesData == null) return;

            foreach (var mass in groundInput.GroundMassesData)
            {
                // depth (負値) から該当層を線形検索: 最初に bottomGLDepth <= mass.GLDepth となる層
                // (層の bottomGLDepth は降順、massのGLDepth=-0.83はlayer1.bottomGLDepth=-2.5以上なのでlayer1にヒット)
                GroundLayerInput? hitLayer = null;
                foreach (var layer in groundInput.GroundLayers)
                {
                    if (layer.BottomGLDepth <= mass.GLDepth)
                    {
                        hitLayer = layer;
                        break;
                    }
                }
                // 最深層より深い場合は最後の層を使用
                hitLayer ??= groundInput.GroundLayers[groundInput.GroundLayers.Count - 1];

                if (mass.VS0 != hitLayer.Vs)
                    mass.VS0 = hitLayer.Vs;
                if (mass.Density != hitLayer.Density)
                    mass.Density = hitLayer.Density;
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
                ShallowSoilType = groundInput.ShallowSoilType,
                CalculationMethod = groundInput.CalculationMethod
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

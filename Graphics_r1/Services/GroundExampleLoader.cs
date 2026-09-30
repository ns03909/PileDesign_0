using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
            var data = JsonSerializer.Deserialize<GroundExampleData>(json, _jsonOptions)
                ?? throw new InvalidOperationException($"JSONのデシリアライズに失敗しました: {filePath}");

            // 地盤に当てる前に中身を確かめる。当てる処理は地層・地盤質量を丸ごと差し替えるので、
            // 途中で止まったり、おかしな値のまま当てたりすると、画面の地盤が半端に書き換わる。
            var problems = Validate(data);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    $"例題ファイルの内容に問題があるため読み込みませんでした (地盤は変更していません)。\n{filePath}\n"
                    + string.Join("\n", problems.Take(10).Select(p => "・" + p))
                    + (problems.Count > 10 ? $"\n…ほか {problems.Count - 10} 件" : ""));
            }
            return data;
        }

        /// <summary>
        /// 例題の中身の点検。地層が 1 層以上あること・数値が有限であること・地層の下端が浅い方から深い方へ並ぶこと・
        /// 層厚・単位体積重量・せん断波速度が正であること。問題が無ければ空。
        ///
        /// <para>下端の並びは地盤質量の点を地層に割り当てるときの前提 (<see cref="ApplyLayerValuesToMasses"/>)。
        /// 崩れていると、点が別の地層の値で上書きされる。</para>
        /// </summary>
        public static IReadOnlyList<string> Validate(GroundExampleData? data)
        {
            var problems = new List<string>();
            if (data == null) { problems.Add("例題の中身が空です。"); return problems; }

            void Finite(double value, string what)
            {
                if (!double.IsFinite(value)) problems.Add($"{what} が数ではありません ({value})。");
            }
            void FiniteOpt(double? value, string what)
            {
                if (value is { } v) Finite(v, what);
            }

            Finite(data.GroundTopAltitude, "地盤天端の標高");
            Finite(data.GroundWaterGLDepth, "地下水位");
            Finite(data.StressGLDepth, "応力の基準深さ");
            Finite(data.GroundAcceleration1, "地表面加速度");
            FiniteOpt(data.BedrockDensity, "基盤の単位体積重量");
            FiniteOpt(data.BedrockShearWaveVelocity, "基盤のせん断波速度");

            if (data.GroundLayers == null || data.GroundLayers.Count == 0)
            {
                problems.Add("地層が 1 層もありません。");
            }
            else
            {
                for (int i = 0; i < data.GroundLayers.Count; i++)
                {
                    var layer = data.GroundLayers[i];
                    string name = $"地層 {i + 1}";
                    if (layer == null) { problems.Add($"{name} が空です。"); continue; }
                    Finite(layer.BottomGLDepth, $"{name} の下端深さ");
                    Finite(layer.NValue, $"{name} の N 値");
                    Finite(layer.Cohesive, $"{name} の粘着力");
                    Finite(layer.Es, $"{name} の変形係数");
                    if (!(layer.LayerThickness > 0)) problems.Add($"{name} の層厚が正ではありません ({layer.LayerThickness})。");
                    if (!(layer.Density > 0)) problems.Add($"{name} の単位体積重量が正ではありません ({layer.Density})。");
                    if (!(layer.Vs > 0)) problems.Add($"{name} のせん断波速度が正ではありません ({layer.Vs})。");
                    var previous = i > 0 ? data.GroundLayers[i - 1] : null;
                    if (previous != null && !(layer.BottomGLDepth < previous.BottomGLDepth))
                        problems.Add($"{name} の下端深さ ({layer.BottomGLDepth}) が上の地層 ({previous.BottomGLDepth}) より深くありません (浅い方から順に並べてください)。");
                }
            }

            if (data.GroundMassesData == null)
            {
                problems.Add("地盤質量の一覧がありません。");
            }
            else
            {
                for (int i = 0; i < data.GroundMassesData.Count; i++)
                {
                    var mass = data.GroundMassesData[i];
                    string name = $"地盤質量の点 {i + 1}";
                    if (mass == null) { problems.Add($"{name} が空です。"); continue; }
                    Finite(mass.GLDepth, $"{name} の深さ");
                    Finite(mass.NValue, $"{name} の N 値");
                    Finite(mass.Fc, $"{name} の細粒分含有率");
                    Finite(mass.Density, $"{name} の単位体積重量");
                    Finite(mass.VS0, $"{name} のせん断波速度");
                    FiniteOpt(mass.H, $"{name} の減衰定数");
                    FiniteOpt(mass.Gamma05, $"{name} の基準ひずみ");
                    FiniteOpt(mass.HMax, $"{name} の最大減衰定数");
                }
            }
            return problems;
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

            // 地盤変位を「考慮しない」モード (例題で明示指定された場合のみ)
            if (data.IsGroundDisplacementIgnored.HasValue)
                groundInput.IsGroundDisplacementIgnored = data.IsGroundDisplacementIgnored.Value;

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
            // 単一ファイル発行 (PublishSingleFile=true) では Assembly.Location が空文字を返すため AppContext.BaseDirectory を使用
            var assemblyDir = AppContext.BaseDirectory;
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

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 地盤例題データのDTO（JSON用）
    /// </summary>
    public class GroundExampleData
    {
        /// <summary>
        /// 例題名（表示用）
        /// </summary>
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; }

        /// <summary>
        /// 地盤参照名
        /// </summary>
        [JsonPropertyName("groundRef")]
        public string GroundRef { get; set; }

        /// <summary>
        /// 地盤天端標高
        /// </summary>
        [JsonPropertyName("groundTopAltitude")]
        public double GroundTopAltitude { get; set; }

        /// <summary>
        /// 地下水位深度
        /// </summary>
        [JsonPropertyName("groundWaterGLDepth")]
        public double GroundWaterGLDepth { get; set; }

        /// <summary>
        /// 応力計算基準深度
        /// </summary>
        [JsonPropertyName("stressGLDepth")]
        public double StressGLDepth { get; set; }

        /// <summary>
        /// 地盤加速度
        /// </summary>
        [JsonPropertyName("groundAcceleration1")]
        public double GroundAcceleration1 { get; set; }

        /// <summary>
        /// 基盤密度
        /// </summary>
        [JsonPropertyName("bedrockDensity")]
        public double? BedrockDensity { get; set; }

        /// <summary>
        /// 基盤S波速度
        /// </summary>
        [JsonPropertyName("bedrockShearWaveVelocity")]
        public double? BedrockShearWaveVelocity { get; set; }

        /// <summary>
        /// 浅層土質種別
        /// </summary>
        [JsonPropertyName("shallowSoilType")]
        public string ShallowSoilType { get; set; }

        /// <summary>
        /// 地盤変位算定法（"a1(b1)" または "a2(b2)"）
        /// </summary>
        [JsonPropertyName("calculationMethod")]
        public string CalculationMethod { get; set; }

        /// <summary>
        /// 地層データリスト
        /// </summary>
        [JsonPropertyName("groundLayers")]
        public List<GroundLayerDto> GroundLayers { get; set; } = new();

        /// <summary>
        /// 地盤質量データリスト
        /// </summary>
        [JsonPropertyName("groundMassesData")]
        public List<GroundMassDataDto> GroundMassesData { get; set; } = new();
    }

    /// <summary>
    /// 地層データのDTO
    /// </summary>
    public class GroundLayerDto
    {
        [JsonPropertyName("no")]
        public int No { get; set; }

        [JsonPropertyName("bottomGLDepth")]
        public double BottomGLDepth { get; set; }

        [JsonPropertyName("layerThickness")]
        public double LayerThickness { get; set; }

        [JsonPropertyName("bottomAltitude")]
        public double BottomAltitude { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("granularityClass")]
        public string GranularityClass { get; set; }

        [JsonPropertyName("density")]
        public double Density { get; set; }

        [JsonPropertyName("ageCategory")]
        public string AgeCategory { get; set; }

        [JsonPropertyName("isEngineeringBedrock")]
        public bool IsEngineeringBedrock { get; set; }

        [JsonPropertyName("nValue")]
        public double NValue { get; set; }

        [JsonPropertyName("cohesive")]
        public double Cohesive { get; set; }

        [JsonPropertyName("vs")]
        public double Vs { get; set; }

        [JsonPropertyName("es")]
        public double Es { get; set; }

        [JsonPropertyName("isPositiveCircumResistance")]
        public bool IsPositiveCircumResistance { get; set; }

        [JsonPropertyName("isNegativeCircumResistance")]
        public bool IsNegativeCircumResistance { get; set; }

        /// <summary>
        /// DTOからGroundLayerInputへ変換
        /// </summary>
        public GroundLayerInput ToGroundLayerInput()
        {
            return new GroundLayerInput
            {
                No = No,
                BottomGLDepth = BottomGLDepth,
                LayerThickness = LayerThickness,
                BottomAltitude = BottomAltitude,
                Name = Name,
                GranularityClass = GranularityClass,
                Density = Density,
                AgeCategory = AgeCategory,
                IsEngineeringBedrock = IsEngineeringBedrock,
                NValue = NValue,
                Cohesive = Cohesive,
                Vs = Vs,
                Es = Es,
                IsPositiveCircumResistance = IsPositiveCircumResistance,
                IsNegativeCircumResistance = IsNegativeCircumResistance
            };
        }
    }

    /// <summary>
    /// 地盤質量データのDTO
    /// </summary>
    public class GroundMassDataDto
    {
        [JsonPropertyName("glDepth")]
        public double GLDepth { get; set; }

        [JsonPropertyName("nValue")]
        public double NValue { get; set; }

        [JsonPropertyName("fc")]
        public double Fc { get; set; }

        [JsonPropertyName("density")]
        public double Density { get; set; }

        [JsonPropertyName("vs0")]
        public double VS0 { get; set; }

        [JsonPropertyName("h")]
        public double? H { get; set; }

        // 応答スペクトル法 (Hardin-Drnevich) パラメータ
        [JsonPropertyName("gamma05")]
        public double? Gamma05 { get; set; }

        [JsonPropertyName("hMax")]
        public double? HMax { get; set; }

        /// <summary>
        /// DTOからGroundMassDataInputへ変換
        /// </summary>
        public GroundMassDataInput ToGroundMassDataInput()
        {
            var input = new GroundMassDataInput
            {
                GLDepth = GLDepth,
                NValue = NValue,
                Fc = Fc,
                Density = Density,
                VS0 = VS0,
                H = H  // JSONに "h" がある場合はそのまま使用、なければnull→RecalculateH()でSpacingをデフォルト設定
            };
            // JSON に gamma05 / hMax があれば上書き (なければコンストラクタの既定値 7e-4 / 0.20 を維持)
            if (Gamma05.HasValue) input.Gamma05 = Gamma05.Value;
            if (HMax.HasValue) input.HMax = HMax.Value;
            return input;
        }
    }
}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 杭例題データのJSONシリアライズ用DTO
    /// </summary>
    public class PileExampleData
    {
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = "";

        [JsonPropertyName("groundExampleName")]
        public string GroundExampleName { get; set; } = "";

        [JsonPropertyName("loadCaseLevel1")]
        public LoadCaseSettingDto LoadCaseLevel1 { get; set; } = new();

        [JsonPropertyName("loadCaseLevel2")]
        public LoadCaseSettingDto LoadCaseLevel2 { get; set; } = new();

        [JsonPropertyName("loadCaseLevel2Alt")]
        public LoadCaseSettingDto? LoadCaseLevel2Alt { get; set; }

        [JsonPropertyName("pileBodies")]
        public List<PileBodyDto> PileBodies { get; set; } = [];

        [JsonPropertyName("embedment")]
        public EmbedmentDto? Embedment { get; set; }

        [JsonPropertyName("pileLayoutItems")]
        public List<PileLayoutItemDto> PileLayoutItems { get; set; } = [];

        [JsonPropertyName("gridXItems")]
        public List<GridItemDto> GridXItems { get; set; } = [];

        [JsonPropertyName("gridYItems")]
        public List<GridItemDto> GridYItems { get; set; } = [];
    }

    /// <summary>
    /// 荷重ケース設定DTO
    /// </summary>
    public class LoadCaseSettingDto
    {
        [JsonPropertyName("forceActionPointX")]
        public double ForceActionPointX { get; set; }

        [JsonPropertyName("forceActionPointY")]
        public double ForceActionPointY { get; set; }

        [JsonPropertyName("forceActionPointZ")]
        public double ForceActionPointZ { get; set; }

        [JsonPropertyName("upperMassForce")]
        public double UpperMassForce { get; set; }

        [JsonPropertyName("foundationMassForce")]
        public double FoundationMassForce { get; set; }
    }

    /// <summary>
    /// 杭体DTO
    /// </summary>
    public class PileBodyDto
    {
        [JsonPropertyName("pileBodyRef")]
        public string PileBodyRef { get; set; } = "";

        [JsonPropertyName("pileBodyType")]
        public string PileBodyType { get; set; } = "既製コンクリート杭";

        [JsonPropertyName("pileTopType")]
        public string PileTopType { get; set; } = "鉄筋定着工法";

        [JsonPropertyName("pileConstructionType")]
        public string PileConstructionType { get; set; } = "埋込み杭（プレボーリング杭）";

        [JsonPropertyName("pileToeDia")]
        public double PileToeDia { get; set; }

        [JsonPropertyName("tipNonPermability")]
        public double TipNonPermability { get; set; }

        [JsonPropertyName("embedmentIntoBearingSoil")]
        public double EmbedmentIntoBearingSoil { get; set; } = 1.0;

        [JsonPropertyName("pileInnerDia")]
        public double PileInnerDia { get; set; }

        [JsonPropertyName("pileTipStyle")]
        public string PileTipStyle { get; set; } = "閉端杭";

        [JsonPropertyName("settlePileToeDia")]
        public double SettlePileToeDia { get; set; } = 700;

        [JsonPropertyName("settleAlpha")]
        public double SettleAlpha { get; set; } = 0.3;

        [JsonPropertyName("settleN")]
        public int SettleN { get; set; } = 2;

        [JsonPropertyName("segments")]
        public List<PileBodySegmentDto> Segments { get; set; } = [];
    }

    /// <summary>
    /// 杭体セグメントDTO
    /// </summary>
    public class PileBodySegmentDto
    {
        [JsonPropertyName("no")]
        public int No { get; set; } = 1;

        [JsonPropertyName("segmentLength")]
        public double SegmentLength { get; set; }

        [JsonPropertyName("segmentDepth")]
        public double SegmentDepth { get; set; }

        [JsonPropertyName("pileSectionType")]
        public string PileSectionType { get; set; } = "";

        [JsonPropertyName("precastPileName")]
        public string PrecastPileName { get; set; } = "";

        // 場所打ち杭用プロパティ（鉄筋コンクリート部）
        [JsonPropertyName("concreteOutDia")]
        public double? ConcreteOutDia { get; set; }

        [JsonPropertyName("concreteThickness")]
        public double? ConcreteThickness { get; set; }

        [JsonPropertyName("mainBarDr")]
        public double? MainBarDr { get; set; }

        [JsonPropertyName("pipeTs")]
        public double? PipeTs { get; set; }

        [JsonPropertyName("pipeDia")]
        public double? PipeDia { get; set; }

        [JsonPropertyName("mainBarNum")]
        public int? MainBarNum { get; set; }

        [JsonPropertyName("mainBarSize")]
        public string? MainBarSize { get; set; }

        [JsonPropertyName("concreteFc")]
        public double? ConcreteFc { get; set; }

        [JsonPropertyName("concreteGsi")]
        public double? ConcreteGsi { get; set; }

        [JsonPropertyName("concreteGamma")]
        public double? ConcreteGamma { get; set; }
    }

    /// <summary>
    /// 杭配置DTO
    /// </summary>
    public class PileLayoutItemDto
    {
        [JsonPropertyName("pileNo")]
        public int PileNo { get; set; }

        [JsonPropertyName("pileBodyNo")]
        public int PileBodyNo { get; set; } = 1;

        [JsonPropertyName("groundNo")]
        public int GroundNo { get; set; } = 1;

        [JsonPropertyName("soilPileAltNo")]
        public int SoilPileAltNo { get; set; } = 1;

        [JsonPropertyName("groupPileFactor")]
        public double GroupPileFactor { get; set; } = 1.0;

        [JsonPropertyName("pileSpacingFactor")]
        public double PileSpacingFactor { get; set; } = 10;

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("z")]
        public double Z { get; set; }

        [JsonPropertyName("axialForceVL0")]
        public double AxialForceVL0 { get; set; }

        [JsonPropertyName("axialForceLevel1s")]
        public double[] AxialForceLevel1s { get; set; } = [];

        [JsonPropertyName("axialForceLevel2s")]
        public double[] AxialForceLevel2s { get; set; } = [];
    }

    /// <summary>
    /// グリッドDTO
    /// </summary>
    public class GridItemDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("coord")]
        public double Coord { get; set; }

        [JsonPropertyName("spacing")]
        public double Spacing { get; set; }
    }

    /// <summary>
    /// 根入れDTO
    /// </summary>
    public class EmbedmentDto
    {
        [JsonPropertyName("groundNo")]
        public int GroundNo { get; set; } = 1;

        [JsonPropertyName("embedmentTopAltitude")]
        public double EmbedmentTopAltitude { get; set; }

        [JsonPropertyName("embedmentBottomAltitude")]
        public double EmbedmentBottomAltitude { get; set; }

        [JsonPropertyName("zs")]
        public List<double>? Zs { get; set; }

        [JsonPropertyName("embedmentLayers")]
        public List<EmbedmentLayerDto>? EmbedmentLayers { get; set; }
    }

    /// <summary>
    /// 根入れ層DTO
    /// </summary>
    public class EmbedmentLayerDto
    {
        [JsonPropertyName("topAltitude")]
        public double TopAltitude { get; set; }

        [JsonPropertyName("bottomAltitude")]
        public double BottomAltitude { get; set; }

        [JsonPropertyName("x1")]
        public double X1 { get; set; }

        [JsonPropertyName("x2")]
        public double X2 { get; set; }

        [JsonPropertyName("y1")]
        public double Y1 { get; set; }

        [JsonPropertyName("y2")]
        public double Y2 { get; set; }
    }
}

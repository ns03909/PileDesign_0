using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 群杭沈下解析例題データのDTO（JSON用）
    /// </summary>
    public class GroupSettlementExampleData
    {
        /// <summary>
        /// 例題名（表示用）
        /// </summary>
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// 地盤例題名（地盤データJSONファイル名、拡張子なし）
        /// </summary>
        [JsonPropertyName("groundExampleName")]
        public string? GroundExampleName { get; set; }

        /// <summary>
        /// 慣性力中心点
        /// </summary>
        [JsonPropertyName("inertiaPoint")]
        public PointDto? InertiaPoint { get; set; }

        /// <summary>
        /// 載荷面標高
        /// </summary>
        [JsonPropertyName("loadingPlaneAltitude")]
        public double LoadingPlaneAltitude { get; set; }

        /// <summary>
        /// 矩形荷重リスト
        /// </summary>
        [JsonPropertyName("rectLoads")]
        public List<RectLoadDto> RectLoads { get; set; } = [];

        /// <summary>
        /// 沈下計算用地層リスト
        /// </summary>
        [JsonPropertyName("settlementSoilLayers")]
        public List<SettlementSoilLayerDto> SettlementSoilLayers { get; set; } = [];

        /// <summary>
        /// 杭体リスト
        /// </summary>
        [JsonPropertyName("pileBodies")]
        public List<PileBodyDto>? PileBodies { get; set; }

        /// <summary>
        /// 杭位置リスト（X,Y座標のみ）
        /// </summary>
        [JsonPropertyName("pileLayoutPositions")]
        public List<PointDto>? PileLayoutPositions { get; set; }

        /// <summary>
        /// 要素接続情報（杭インデックスのペア）
        /// </summary>
        [JsonPropertyName("elementConnections")]
        public List<int[]>? ElementConnections { get; set; }

        /// <summary>
        /// Xグリッド
        /// </summary>
        [JsonPropertyName("gridXItems")]
        public List<GridItemDto>? GridXItems { get; set; }

        /// <summary>
        /// Yグリッド
        /// </summary>
        [JsonPropertyName("gridYItems")]
        public List<GridItemDto>? GridYItems { get; set; }

        /// <summary>
        /// 沈下解析オフセット設定
        /// </summary>
        [JsonPropertyName("settlementOffsets")]
        public SettlementOffsetsDto? SettlementOffsets { get; set; }

        /// <summary>
        /// 基礎梁入力（FoundationBeamInput系）
        /// </summary>
        [JsonPropertyName("foundationBeamInput")]
        public FoundationBeamInputDto? FoundationBeamInput { get; set; }
    }

    /// <summary>
    /// 座標点DTO
    /// </summary>
    public class PointDto
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("z")]
        public double Z { get; set; }
    }

    /// <summary>
    /// 矩形荷重DTO
    /// </summary>
    public class RectLoadDto
    {
        [JsonPropertyName("x1")]
        public double X1 { get; set; }

        [JsonPropertyName("x2")]
        public double X2 { get; set; }

        [JsonPropertyName("y1")]
        public double Y1 { get; set; }

        [JsonPropertyName("y2")]
        public double Y2 { get; set; }

        [JsonPropertyName("qa")]
        public double QA { get; set; }
    }

    /// <summary>
    /// 沈下計算用地層DTO
    /// </summary>
    public class SettlementSoilLayerDto
    {
        [JsonPropertyName("bottomAltitude")]
        public double BottomAltitude { get; set; }

        [JsonPropertyName("ek")]
        public double Ek { get; set; }

        [JsonPropertyName("poissonsRatio")]
        public double PoissonsRatio { get; set; }

        [JsonPropertyName("thickness")]
        public double Thickness { get; set; }
    }

    /// <summary>
    /// 沈下解析オフセット設定DTO
    /// </summary>
    public class SettlementOffsetsDto
    {
        [JsonPropertyName("xOffset")]
        public double XOffset { get; set; }

        [JsonPropertyName("yOffset")]
        public double YOffset { get; set; }

        [JsonPropertyName("xSpacing")]
        public double XSpacing { get; set; }

        [JsonPropertyName("ySpacing")]
        public double YSpacing { get; set; }
    }
}

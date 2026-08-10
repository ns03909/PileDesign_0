using PileDesign.Constants;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    /// </summary>
    // 断面プロファイルの材料種別 (描画色分け用)
    internal enum SectionMaterialKind { Concrete, MainBar, Tendon, SteelPipe }

    // 1 材料分のひずみ度・応力度プロファイル (z 配列ごとの ε, σ)。
    // 中空部などデータの無い区間は ε,σ に double.NaN を入れて線を分断する。
    internal class MaterialProfile
    {
        public SectionMaterialKind Kind { get; set; }
        public string Name { get; set; } = "";
        public List<double> Z { get; } = new();       // 断面高さ z [mm]
        public List<double> Strain { get; } = new();   // 断面の平面保持ひずみ ε [-]
        public List<double> Stress { get; } = new();   // 材料応力度 σ [N/mm2]
    }

    /// <summary>
    /// 杭断面のひずみ度・応力度分布 (描画用)。各材料を 1 本の MaterialProfile として持つ。
    /// z は断面高さ [mm]（圧縮縁 z=-Radius、引張縁 z=+Radius、平面保持で ε(z)=ε0-φz）。
    /// </summary>
    internal class SectionStrainStressProfile
    {
        public double Radius { get; set; }                       // 断面外縁半径 [mm]
        public List<MaterialProfile> Materials { get; } = new();
        public double CompressionEdgeStrain { get; set; }        // 圧縮縁ひずみ (z=-Radius)
        public double TensionEdgeStrain { get; set; }            // 引張縁ひずみ (z=+Radius)
    }
}

using System;

namespace PileDesign.Common;

/// <summary>
/// 単位の換算を 1 か所に置く (入力の単位と内部の単位の境目を名前で示す)。
///
/// <para>角度 (度 ↔ ラジアン) の換算が式ごとに <c>θ * Math.PI / 180</c>・<c>θ / 180 * Math.PI</c> と書き分けられ、
/// 45 か所に散らばっていた。書き方が揃っていないと、換算の漏れ (度のまま三角関数に渡す) や二重の換算を
/// 読んで見つけにくい。角度はここを通す (形で見張っている)。</para>
///
/// <para>長さ (mm ↔ m) と力 (kN ↔ N) の 1000 倍は、同じ「1000」が mm↔m・kN↔N・表示の桁の換算に混ざっていて、
/// 機械的には見分けられない。新しく書くときはここを使い、変数名に単位を付ける (例: <c>diameterMm</c>)。</para>
/// </summary>
internal static class Units
{
    // 計算の順は、置き換える前にもっとも多かった書き方 (θ * Math.PI / 180) と同じにしてある。
    // 係数を先にまとめる (θ * (π/180)) と丸めが変わり、解析の結果が最後の桁で動く (計算書の値が 0.1 変わった例がある)。

    /// <summary>度 → ラジアン。</summary>
    internal static double DegToRad(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>ラジアン → 度。</summary>
    internal static double RadToDeg(double radians) => radians * 180.0 / Math.PI;

    /// <summary>mm → m。</summary>
    internal static double MmToM(double millimetres) => millimetres / 1000.0;

    /// <summary>m → mm。</summary>
    internal static double MToMm(double metres) => metres * 1000.0;

    /// <summary>kN → N。</summary>
    internal static double KnToN(double kilonewtons) => kilonewtons * 1000.0;

    /// <summary>N → kN。</summary>
    internal static double NToKn(double newtons) => newtons / 1000.0;
}

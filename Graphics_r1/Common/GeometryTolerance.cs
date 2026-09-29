namespace PileDesign.Common;

/// <summary>
/// 幾何の判定に使う共通の基準 (長さは m)。
///
/// <para>部材の長さを「0 とみなす」しきい値が処理ごとに違っていた。基礎梁の検査は 1e-6 m 未満を誤りとし、
/// 基礎梁の自動生成は 1e-9 m 以下だけを飛ばし、杭要素の生成は 1e-10 m 未満だけを止め、杭頭変形角は 1e-9 m、
/// 傾斜角の検定は 1e-6 m で切っていた。そのため自動生成した梁 (1e-9〜1e-6 m) を検査が止める、
/// 検査を通った要素が生成で特異な要素になる、といった食い違いが起き得た。
/// 警告・誤り・要素の生成・検定で、ここを共有する。</para>
///
/// <para>描画だけの保護 (0 で割らないための 1e-9〜1e-12 など) は、判定ではないので対象にしない。</para>
/// </summary>
internal static class GeometryTolerance
{
    /// <summary>
    /// 部材 (杭要素・基礎梁) の長さ・節点どうしの距離の下限 [m]。これ未満は長さ 0 (同じ位置) として扱う。
    /// 1 μm。入力の座標は mm の桁で扱うので、これより短い部材は入力の誤りか丸めの残りしか無い。
    /// </summary>
    internal const double MinMemberLength = 1e-6;

    /// <summary>長さ 0 とみなすか (数値でない長さは別に扱うこと)。</summary>
    internal static bool IsZeroLength(double length) => length < MinMemberLength;
}

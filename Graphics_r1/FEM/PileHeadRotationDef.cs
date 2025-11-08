

namespace PileDesign.FEM
{
    public enum PileHeadRotationMode
    {
        Rigid,        // 完全剛（近似的に大剛性直線で表現）
        CombinedXY,   // 合成XY（θres=√(θx^2+θy^2)）
        Separate      // 単独DOF（Rx/Ry）
    }

    public sealed class PileHeadRotationDef
    {
        public PileHeadRotationMode Mode { get; init; }

        // 合成XY
        public MomentRotationCurve? CurveXY { get; init; }

        // 単独DOF
        public MomentRotationCurve? CurveX { get; init; }
        public MomentRotationCurve? CurveY { get; init; }

        // フォールバック（線形）
        public double? KthetaXY { get; init; }
        public double? Kx { get; init; }
        public double? Ky { get; init; }

        // Factory
        public static PileHeadRotationDef Rigid() => new() { Mode = PileHeadRotationMode.Rigid };
        public static PileHeadRotationDef Combined(MomentRotationCurve curve) =>
            new() { Mode = PileHeadRotationMode.CombinedXY, CurveXY = curve };
        public static PileHeadRotationDef CombinedLinear(double k) =>
            new() { Mode = PileHeadRotationMode.CombinedXY, KthetaXY = k };
        public static PileHeadRotationDef Separate(MomentRotationCurve? cx, MomentRotationCurve? cy, double? kx = null, double? ky = null) =>
            new() { Mode = PileHeadRotationMode.Separate, CurveX = cx, CurveY = cy, Kx = kx, Ky = ky };
    }
}
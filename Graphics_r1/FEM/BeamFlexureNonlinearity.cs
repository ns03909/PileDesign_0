using System;

namespace PileDesign.FEM
{
    public enum FlexureAxis
    {
        My, // ローカルy回り
        Mz  // ローカルz回り
    }

    public enum FlexureMode
    {
        SeparateAxes,   // My用/Mz用を個別に設定
        CombinedMyMz    // 合成 Mres=√(My^2+Mz^2) 用の単一曲線
    }

    // 「どの梁に、どの軸/モードで、どのM–φ曲線を適用するか」
    public sealed class BeamFlexureNonlinearity
    {
        public Beam Beam { get; }
        public FlexureMode Mode { get; }
        public FlexureAxis? Axis { get; }  // SeparateAxes時のみ
        public MomentCurvatureCurve Curve { get; }

        // Solverでの状態管理用（必要に応じて）
        public double CurrentCurvature { get; set; }

        // 個別軸用
        public BeamFlexureNonlinearity(Beam beam, FlexureAxis axis, MomentCurvatureCurve curve)
        {
            Beam = beam ?? throw new ArgumentNullException(nameof(beam));
            Mode = FlexureMode.SeparateAxes;
            Axis = axis;
            Curve = curve ?? throw new ArgumentNullException(nameof(curve));
        }

        // 合成用
        public BeamFlexureNonlinearity(Beam beam, MomentCurvatureCurve combinedCurve)
        {
            Beam = beam ?? throw new ArgumentNullException(nameof(beam));
            Mode = FlexureMode.CombinedMyMz;
            Curve = combinedCurve ?? throw new ArgumentNullException(nameof(combinedCurve));
        }
    }
}
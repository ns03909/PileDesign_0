using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PileDesign.ViewModels
{
    public record SettlementGridFingerprint(
        int Nx, int Ny, int Nitems,
        double MinS, double MaxS,
        double Z, double Multiplier,
        double SumX, double SumY, double SumS);

    public class SettlementIsoBand
    {
        public List<Point3D> Points { get; set; } = new();
        public Color Color { get; set; }
    }

    public class SettlementGridRenderCache
    {
        // 変形グリッド線分（3D）
        public List<(Point3D Start, Point3D End)> GridSegments3D { get; set; } = new();
        // 等値帯ポリゴン（3D）
        public List<SettlementIsoBand> IsoBands3D { get; set; } = new();
        // 等高線（3D）
        public List<List<Point3D>> Contours3D { get; set; } = new();
        // カラーバー帯（色と範囲のみ）
        public List<(double Bottom, double Top, Color Color)> ColorBands { get; set; } = new();
        // フィンガープリント
        public SettlementGridFingerprint? Fingerprint { get; set; }
    }

    public partial class MainWindowViewModel
    {
        // ワールド形状キャッシュ
        public SettlementGridRenderCache SettlementWorldCache { get; set; } = new();
    }
}
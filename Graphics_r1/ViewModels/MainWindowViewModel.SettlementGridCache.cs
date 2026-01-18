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
        // 矩形グリッド線群(3D)
        public List<(Point3D Start, Point3D End)> GridSegments3D { get; set; } = new();
        // 等値線ポリゴン(3D)
        public List<SettlementIsoBand> IsoBands3D { get; set; } = new();
        // 等高線(3D)
        public List<List<Point3D>> Contours3D { get; set; } = new();
        // カラーバー(色と範囲のみ)
        public List<(double Bottom, double Top, Color Color)> ColorBands { get; set; } = new();
        // フィンガープリント
        public SettlementGridFingerprint? Fingerprint { get; set; }
    }

    /// <summary>
    /// MainWindowViewModel.SettlementGridCache.cs
    ///
    /// 責任範囲:
    /// - 群杭沈下グリッドの描画キャッシュ管理
    /// - 等値線バンド、グリッド線、コンターのキャッシュデータ構造
    /// - フィンガープリント（キャッシュ検証用）
    /// </summary>
    public partial class MainWindowViewModel
    {
        // ワールド描画キャッシュ
        public SettlementGridRenderCache SettlementWorldCache { get; set; } = new();
    }
}
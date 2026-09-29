using System.Collections.Generic;
using System.Linq;
using PileDesign.Models.InputData;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PileDesign.ViewModels
{
    /// <summary>各格子点の位置と沈下量を値で保持し、入れ替えも変更として検出する。</summary>
    public sealed class SettlementGridFingerprint : System.IEquatable<SettlementGridFingerprint>
    {
        private readonly (double X, double Y, double Settlement)[] _points;
        private readonly double _z;
        private readonly double _multiplier;

        public SettlementGridFingerprint(IEnumerable<SettlementGridDataItem> items, double z, double multiplier)
        {
            _points = items.Select(p => (p.X, p.Y, p.Settlement)).ToArray();
            _z = z;
            _multiplier = multiplier;
        }

        public bool Equals(SettlementGridFingerprint? other) =>
            other != null && _z.Equals(other._z) && _multiplier.Equals(other._multiplier) &&
            _points.SequenceEqual(other._points);

        public override bool Equals(object? obj) => Equals(obj as SettlementGridFingerprint);

        public override int GetHashCode() => System.HashCode.Combine(_z, _multiplier, _points.Length);
    }

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
        // 等高線のレベル値（Contours3Dと同じインデックス）
        public List<double> ContourLevels3D { get; set; } = new();
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

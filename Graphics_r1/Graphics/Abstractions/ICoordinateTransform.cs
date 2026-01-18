using System.Windows;
using System.Windows.Media.Media3D;

namespace PileDesign.Graphics.Abstractions
{
    /// <summary>
    /// 3D→2D座標変換のインターフェース
    /// </summary>
    public interface ICoordinateTransform
    {
        /// <summary>
        /// 3D座標を2D画面座標に変換
        /// </summary>
        Point Transform(Point3D point3D);

        /// <summary>
        /// 透視変換による楕円の潰し係数（0〜1、1で真円）
        /// </summary>
        double Flattening { get; }

        /// <summary>
        /// 描画スケール（m→px変換係数）
        /// </summary>
        double Scale { get; }
    }

    /// <summary>
    /// 静的座標変換（Word出力等の固定ビューポート用）
    /// </summary>
    public class StaticCoordinateTransform : ICoordinateTransform
    {
        private readonly double _widthPx;
        private readonly double _heightPx;
        private readonly double _minX;
        private readonly double _maxX;
        private readonly double _minY;
        private readonly double _maxY;
        private readonly double _minZ;
        private readonly double _maxZ;
        private readonly double _scale;
        private readonly double _flattening;
        private readonly ViewDirection _viewDirection;

        public enum ViewDirection
        {
            TopDown,    // XY平面を上から（平面図）
            FrontView,  // XZ平面を正面から
            SideView    // YZ平面を側面から
        }

        public StaticCoordinateTransform(
            double widthPx,
            double heightPx,
            double minX, double maxX,
            double minY, double maxY,
            double minZ, double maxZ,
            ViewDirection viewDirection = ViewDirection.FrontView,
            double marginRatio = 0.1,
            double flattening = 1.0)
        {
            _widthPx = widthPx;
            _heightPx = heightPx;
            _minX = minX;
            _maxX = maxX;
            _minY = minY;
            _maxY = maxY;
            _minZ = minZ;
            _maxZ = maxZ;
            _viewDirection = viewDirection;
            _flattening = flattening;

            // ビュー方向に応じたスケール計算
            double rangeH, rangeV;
            switch (viewDirection)
            {
                case ViewDirection.TopDown:
                    rangeH = maxX - minX;
                    rangeV = maxY - minY;
                    break;
                case ViewDirection.SideView:
                    rangeH = maxY - minY;
                    rangeV = maxZ - minZ;
                    break;
                case ViewDirection.FrontView:
                default:
                    rangeH = maxX - minX;
                    rangeV = maxZ - minZ;
                    break;
            }

            // マージンを考慮したスケール計算
            double usableWidth = widthPx * (1 - 2 * marginRatio);
            double usableHeight = heightPx * (1 - 2 * marginRatio);

            if (rangeH > 0 && rangeV > 0)
            {
                _scale = Math.Min(usableWidth / rangeH, usableHeight / rangeV);
            }
            else
            {
                _scale = 1.0;
            }
        }

        public double Flattening => _flattening;
        public double Scale => _scale;

        public Point Transform(Point3D point3D)
        {
            double screenX, screenY;

            switch (_viewDirection)
            {
                case ViewDirection.TopDown:
                    // 平面図: X→右、Y→上
                    screenX = _widthPx / 2 + (point3D.X - (_minX + _maxX) / 2) * _scale;
                    screenY = _heightPx / 2 - (point3D.Y - (_minY + _maxY) / 2) * _scale;
                    break;

                case ViewDirection.SideView:
                    // 側面図: Y→右、Z→上
                    screenX = _widthPx / 2 + (point3D.Y - (_minY + _maxY) / 2) * _scale;
                    screenY = _heightPx / 2 - (point3D.Z - (_minZ + _maxZ) / 2) * _scale;
                    break;

                case ViewDirection.FrontView:
                default:
                    // 正面図: X→右、Z→上
                    screenX = _widthPx / 2 + (point3D.X - (_minX + _maxX) / 2) * _scale;
                    screenY = _heightPx / 2 - (point3D.Z - (_minZ + _maxZ) / 2) * _scale;
                    break;
            }

            return new Point(screenX, screenY);
        }
    }
}

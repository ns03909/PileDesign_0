using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PileDesign.Graphics.Abstractions
{
    /// <summary>
    /// 描画先の抽象インターフェース
    /// Canvas用とDrawingContext用の実装で共通のAPIを提供
    /// </summary>
    public interface IDrawingTarget
    {
        /// <summary>
        /// 線分を描画
        /// </summary>
        void DrawLine(Point start, Point end, DrawingStyle style);

        /// <summary>
        /// 楕円を描画（透視変換で潰れた楕円も含む）
        /// </summary>
        void DrawEllipse(Point center, double radiusX, double radiusY, DrawingStyle style);

        /// <summary>
        /// 円を描画
        /// </summary>
        void DrawCircle(Point center, double radius, DrawingStyle style)
            => DrawEllipse(center, radius, radius, style);

        /// <summary>
        /// ポリラインを描画
        /// </summary>
        void DrawPolyLine(IEnumerable<Point> points, bool isClosed, DrawingStyle style);

        /// <summary>
        /// 塗りつぶしポリゴンを描画
        /// </summary>
        void DrawFilledPolygon(IEnumerable<Point> points, DrawingStyle style);

        /// <summary>
        /// 矩形を描画
        /// </summary>
        void DrawRectangle(Rect rect, DrawingStyle style);

        /// <summary>
        /// テキストを描画
        /// </summary>
        void DrawText(string text, Point position, TextStyle style);

        /// <summary>
        /// 既存のGeometryを描画
        /// </summary>
        void DrawGeometry(Geometry geometry, DrawingStyle style);

        /// <summary>
        /// レイヤー開始（オプション：描画をカテゴリ分けする場合）
        /// </summary>
        void BeginLayer(string layerName);

        /// <summary>
        /// レイヤー終了
        /// </summary>
        void EndLayer();

        /// <summary>
        /// 描画完了（バッファリングされた描画を確定）
        /// </summary>
        void Flush();

        /// <summary>
        /// 描画領域のサイズ
        /// </summary>
        Size Size { get; }
    }

    /// <summary>
    /// レイヤー情報（PathGeometryとスタイルのセット）
    /// </summary>
    public class DrawingLayer
    {
        public string Name { get; set; } = "default";
        public PathGeometry Geometry { get; set; } = new();
        public DrawingStyle Style { get; set; } = new();
        public List<TextInfo> Texts { get; set; } = [];
    }

    /// <summary>
    /// テキスト情報（遅延レンダリング用）
    /// </summary>
    public class TextInfo
    {
        public string Text { get; set; } = string.Empty;
        public Point Position { get; set; }
        public TextStyle Style { get; set; } = new();
    }
}

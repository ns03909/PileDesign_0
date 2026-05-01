using SkiaSharp;
using Svg.Skia;
using System.IO;

namespace TestProject1.Tools
{
    /// <summary>
    /// 一回限り (またはアイコン更新時) に手動実行する PNG 再生成ツール。
    /// Graphics_r1/Icons/*.svg から同名 *.png を 64×64 ピクセルで再描画して上書きする。
    ///
    /// 既存 PNG が存在する SVG のみが対象 (PNG が無い SVG はスキップ)。
    /// PNG しか無いアイコン (Check, SwitchIJ, cover, dxf, rhino8-, *@2x など 8 件) は触らない。
    ///
    /// 実行方法:
    ///   1) [Ignore] 属性を一時的に外す
    ///   2) dotnet test --filter "FullyQualifiedName~RegenerateAllIcons"
    ///   3) 実行後 [Ignore] を戻してコミット
    /// 既定では [Ignore] により通常テスト実行ではスキップされる。
    /// </summary>
    [TestClass]
    public class IconRegenerator
    {
        private const int TargetSize = 64;

        [TestMethod]
        [Ignore("One-shot tool: comment this out and run with --filter to regenerate Icons/*.png from Icons/*.svg")]
        public void RegenerateAllIcons()
        {
            // TestProject1/bin/Debug/net8.0-windows7.0/ から repo ルートを辿って Graphics_r1/Icons へ
            var iconsDir = ResolveIconsDirectory();
            Assert.IsTrue(Directory.Exists(iconsDir), $"Icons folder not found: {iconsDir}");

            var svgFiles = Directory.GetFiles(iconsDir, "*.svg");
            var failures = new List<(string file, string reason)>();
            int converted = 0, skippedNoPng = 0;

            foreach (var svgPath in svgFiles)
            {
                var pngPath = Path.ChangeExtension(svgPath, ".png");
                if (!File.Exists(pngPath))
                {
                    skippedNoPng++;
                    continue;
                }
                try
                {
                    ConvertSvgToPng(svgPath, pngPath, TargetSize, TargetSize);
                    converted++;
                }
                catch (Exception ex)
                {
                    failures.Add((Path.GetFileName(svgPath), ex.Message));
                }
            }

            // 結果を Console.WriteLine + TestContext で見えるように
            Console.WriteLine($"Converted: {converted} / {svgFiles.Length}");
            Console.WriteLine($"Skipped (no PNG counterpart): {skippedNoPng}");
            Console.WriteLine($"Failures: {failures.Count}");
            foreach (var (file, reason) in failures)
                Console.WriteLine($"  FAILED: {file}: {reason}");

            Assert.AreEqual(0, failures.Count, $"{failures.Count} files failed conversion");
            Assert.IsTrue(converted > 100, $"Expected >100 conversions, got {converted}");
        }

        private static string ResolveIconsDirectory()
        {
            // bin/Debug/net8.0-windows7.0 → 4 階層上が repo ルート
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 4 && dir.Parent != null; i++)
                dir = dir.Parent;
            return Path.Combine(dir.FullName, "Graphics_r1", "Icons");
        }

        private static void ConvertSvgToPng(string svgPath, string pngPath, int width, int height)
        {
            using var svg = new SKSvg();
            if (svg.Load(svgPath) is null)
                throw new InvalidOperationException("SKSvg.Load returned null");

            var pic = svg.Picture
                ?? throw new InvalidOperationException("SKSvg.Picture is null");

            var bounds = pic.CullRect;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidOperationException(
                    $"Invalid SVG bounds: {bounds.Width} x {bounds.Height}");

            // アスペクト比を維持して指定サイズに収まるよう縮尺
            float scaleX = width / bounds.Width;
            float scaleY = height / bounds.Height;
            float scale = Math.Min(scaleX, scaleY);

            // 中央寄せ用のオフセット
            float scaledW = bounds.Width * scale;
            float scaledH = bounds.Height * scale;
            float offsetX = (width - scaledW) / 2f - bounds.Left * scale;
            float offsetY = (height - scaledH) / 2f - bounds.Top * scale;

            using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Translate(offsetX, offsetY);
                canvas.Scale(scale);
                canvas.DrawPicture(pic);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(pngPath);
            data.SaveTo(stream);
        }
    }
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PileDesign.Common
{
    /// <summary>
    /// クリップボード書込みの安全ラッパー。
    /// Windows のクリップボードは他プロセス (クリップボード管理ツール / リモートデスクトップ /
    /// Office / Teams 等) に一時的にロックされ、Clipboard.SetText / SetDataObject が
    /// COMException (CLIPBRD_E_CANT_OPEN, 0x800401D0) を投げることがある。
    /// 短い間隔でリトライし、最終的に失敗しても例外を投げず false を返す。
    /// </summary>
    public static class ClipboardHelper
    {
        private const int RetryCount = 3;
        private const int RetryDelayMs = 50;

        /// <summary>テキストをクリップボードへコピーする。空文字/失敗時は false。</summary>
        public static bool TrySetText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            for (int retry = 0; retry < RetryCount; retry++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }
            return false;
        }

        /// <summary>データオブジェクト (画像等) をクリップボードへコピーする。null/失敗時は false。</summary>
        public static bool TrySetDataObject(object data, bool copy)
        {
            if (data == null) return false;

            for (int retry = 0; retry < RetryCount; retry++)
            {
                try
                {
                    Clipboard.SetDataObject(data, copy);
                    return true;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }
            return false;
        }

        /// <summary>
        /// Canvas の内容を画像 (PNG + DIB) としてクリップボードへコピーする。サイズ0/失敗時は false。
        /// Clipboard.SetImage は BitmapSource メタデータ非対応環境で例外になることがあるため、
        /// 生バイトストリーム (PNG / DIB) のみを DataObject に載せて TrySetDataObject 経由で設定する。
        /// </summary>
        public static bool TrySetCanvasImage(Canvas canvas)
        {
            if (canvas == null) return false;
            int w = (int)canvas.ActualWidth;
            int h = (int)canvas.ActualHeight;
            if (w <= 0 || h <= 0) return false;

            var renderBitmap = new RenderTargetBitmap(w, h, 96d, 96d, PixelFormats.Default);
            renderBitmap.Render(canvas);

            var pngEnc = new PngBitmapEncoder();
            pngEnc.Frames.Add(BitmapFrame.Create(renderBitmap));
            var pngStream = new MemoryStream();
            pngEnc.Save(pngStream);

            var bmpEnc = new BmpBitmapEncoder();
            bmpEnc.Frames.Add(BitmapFrame.Create(renderBitmap));
            var bmpStream = new MemoryStream();
            bmpEnc.Save(bmpStream);
            bmpStream.Position = 14; // BMP ファイルヘッダ (14B) をスキップ → DIB
            var dibBytes = new byte[bmpStream.Length - 14];
            bmpStream.Read(dibBytes, 0, dibBytes.Length);

            var dataObject = new DataObject();
            dataObject.SetData("PNG", pngStream, false);
            dataObject.SetData(DataFormats.Dib, new MemoryStream(dibBytes), false);
            return TrySetDataObject(dataObject, true);
        }
    }
}

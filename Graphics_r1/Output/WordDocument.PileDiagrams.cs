using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.Models.PileLibrary;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;

namespace PileDesign.Output
{
    // 杭の図・諸元 (杭姿図 / 杭頭諸元 / 軸力制限) の docx 出力
    // 杭断面図・杭頭上面図 (Canvas ベース) は Phase 1c で別途実装予定
    internal partial class WordDocument
    {
        // 杭姿図サイズ — 標準的な技術文書 (A4 約 170mm 利用幅) の中で、
        // ページを過度に占有せず、深い杭の縦方向スケールも確保できる中間サイズ。
        // 内部の Canvas は ShapeDrawer.DrawPileElevation が自動でスケールフィットする。
        private const double PileElevationDocxWidthMm = 100;
        private const double PileElevationDocxHeightMm = 130;
        // ShapeDrawer.DrawPileElevation は Z 軸ラベルを canvasWidth - textWidth に右詰めで配置するため、
        // bitmap 右端の anti-aliasing で 1〜2 桁分の数値が欠ける問題がある。
        // Canvas 自体を右に少し広げてラベル用余白を作る (描画後 PNG の幅も同じ比率で広がる)。
        private const double PileElevationRightLabelMarginMm = 5;

        private void AddPileDiagramsSection(MainDocumentPart mainPart, Body body)
        {
            if (inputModel?.PileBodies == null || mainWindowViewModel == null) return;

            bool any = mainWindowViewModel.IncludePileElevation
                    || mainWindowViewModel.IncludePileSectionDiagram
                    || mainWindowViewModel.IncludePileTopView
                    || mainWindowViewModel.IncludePileTopSpecs
                    || mainWindowViewModel.IncludeAxialLimitTable;
            if (!any) return;

            AddHeader1(body, "杭の図・諸元", 1);

            if (mainWindowViewModel.IncludePileElevation)
                AddPileElevationDiagrams(mainPart, body);

            if (mainWindowViewModel.IncludePileSectionDiagram)
                AddPileSectionDiagrams(mainPart, body);

            if (mainWindowViewModel.IncludePileTopView)
                AddPileTopViewDiagrams(mainPart, body);

            if (mainWindowViewModel.IncludePileTopSpecs)
                AddPileTopSpecsTables(body);

            if (mainWindowViewModel.IncludeAxialLimitTable)
                AddAxialLimitTables(body);
        }

        // ---- 杭姿図 (各 SoilPile につき 1 図) -----------------------------------------

        private void AddPileElevationDiagrams(MainDocumentPart mainPart, Body body)
        {
            var soilPiles = inputModel.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0) return;

            foreach (var soilPile in soilPiles)
            {
                if (soilPile?.PileBodySegments == null || soilPile.PileBodySegments.Count == 0) continue;
                int pileBodyIdx = soilPile.PileBodyNo - 1;
                if (pileBodyIdx < 0 || pileBodyIdx >= inputModel.PileBodies.Count) continue;
                var pileBody = inputModel.PileBodies[pileBodyIdx];

                try
                {
                    var pngBytes = RenderPileElevationViaShapeDrawer(soilPile, pileBody);
                    if (pngBytes == null || pngBytes.Length == 0) continue;
                    // 画像幅 = 杭描画幅 + Z ラベル右マージン
                    WordDrawingBuilder.AddPngBytesToBody(mainPart, body, pngBytes,
                        PileElevationDocxWidthMm + PileElevationRightLabelMarginMm, PileElevationDocxHeightMm);

                    string caption = $"杭姿図 — 杭体 {soilPile.PileBodyNo} × 地盤 {soilPile.GroundNo}";
                    AddAutoFigureCaption(body, caption, "図");
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "杭姿図 (PileBodyNo={PB}, GroundNo={GN}) の docx 出力中にエラー",
                        soilPile.PileBodyNo, soilPile.GroundNo);
                }
            }
        }

        // PileBodyWindow が表示している ShapeDrawer.DrawPileElevation を新規 Canvas に
        // 適用して PNG 化する。これにより「真横から見た 杭姿図」(ノードマーカーなし、
        // 円形の杭断面なし、土層配色 + Z 目盛り + N 値プロットの統一表示) を docx に出力できる。
        //
        // 単位ハンドリング:
        //   - Canvas は DIP (= 96 DPI px) で測られる
        //   - RenderTargetBitmap は (pxW, pxH, dpiX, dpiY) で、内部 DIP 範囲は pxW * 96 / dpiX
        //   - HiResScale=2.0 を適用するため bitmap dpi=192 とし、bitmap pixel=DIPs×2 で作る
        //   - Canvas dimensions は DIPs (mm * 96 / 25.4) で設定
        // VisualBrush は不要 (canvas を Measure/Arrange/UpdateLayout すれば直接 Render 可)
        private byte[] RenderPileElevationViaShapeDrawer(SoilPile soilPile, PileBodyInput pileBody)
        {
            // DIP (デバイス独立ピクセル、96 DPI)
            // 右側 Z ラベル余白 (PileElevationRightLabelMarginMm) を含めた Canvas 幅で描画。
            // ShapeDrawer が Canvas.ActualWidth を基準にラベルを右詰めするため、
            // この余白分だけ右側に空白ができ、ラベルが切れずに収まる。
            double dipW = (PileElevationDocxWidthMm + PileElevationRightLabelMarginMm) * 96.0 / 25.4;
            double dipH = PileElevationDocxHeightMm * 96.0 / 25.4;
            // 最終 PNG 解像度 (HiResScale 倍密度)
            int pxW = (int)Math.Round(dipW * Layout.HiResScale);
            int pxH = (int)Math.Round(dipH * Layout.HiResScale);

            // Canvas を DIP サイズで生成し、レイアウトを確定
            var canvas = new Canvas
            {
                Width = dipW,
                Height = dipH,
                Background = Brushes.White,
            };
            canvas.Measure(new Size(dipW, dipH));
            canvas.Arrange(new Rect(0, 0, dipW, dipH));
            canvas.UpdateLayout();

            // PileBodyWindow と完全に同一の描画関数を呼び出す (canvas.ActualWidth/Height 必須)
            ShapeDrawer.DrawPileElevation(
                canvas,
                soilPile.PileBodySegments,
                pileBody.PileToeDia,
                pileBody.InsituPileToeHeight,
                pileBody.InsituPileToeAngle,
                pileBody.PrecastConcretePileToeHeightRatio,
                pileBody.PileConstructionType,
                pileTopAltitude: soilPile.Z,
                groundInput: soilPile.GroundInput,
                isElementDivision: false);

            // 子要素追加後に再レイアウト
            canvas.Measure(new Size(dipW, dipH));
            canvas.Arrange(new Rect(0, 0, dipW, dipH));
            canvas.UpdateLayout();

            // VisualBrush 経由で DrawingVisual に投影 (visual tree 未ロード対策)
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, dipW, dipH));
                var brush = new VisualBrush(canvas)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                };
                dc.DrawRectangle(brush, null, new Rect(0, 0, dipW, dipH));
            }

            // bitmap は HiRes DPI で作成 → DIP サイズ = pxW/2 = dipW (Canvas と一致)
            var rtb = new RenderTargetBitmap(pxW, pxH, 96.0 * Layout.HiResScale, 96.0 * Layout.HiResScale, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        // ---- 杭断面図 (各 PileBody の各 PileSection につき 1 図) -----------------------

        private const double PileSectionDiagramSizeMm = 70; // 正方形

        private void AddPileSectionDiagrams(MainDocumentPart mainPart, Body body)
        {
            for (int pbIdx = 0; pbIdx < inputModel.PileBodies.Count; pbIdx++)
            {
                var pileBody = inputModel.PileBodies[pbIdx];
                if (pileBody?.PileBodySegments == null) continue;

                var seenSections = new HashSet<PileSection>();
                for (int segIdx = 0; segIdx < pileBody.PileBodySegments.Count; segIdx++)
                {
                    var section = pileBody.PileBodySegments[segIdx]?.PileSection;
                    if (section == null) continue;
                    if (!seenSections.Add(section)) continue;

                    try
                    {
                        var pngBytes = RenderPileSectionDiagramViaViewModel(section, pbIdx + 1, segIdx + 1);
                        if (pngBytes == null || pngBytes.Length == 0) continue;
                        WordDrawingBuilder.AddPngBytesToBody(mainPart, body, pngBytes,
                            PileSectionDiagramSizeMm, PileSectionDiagramSizeMm);

                        string caption = $"杭断面図 — 杭体 {pbIdx + 1} 区間 {segIdx + 1} ({section.PileSectionType ?? section.PileBodyType ?? ""})";
                        AddAutoFigureCaption(body, caption, "図");
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "杭断面図 (杭体 {PB}, 区間 {SEG}) の docx 出力中にエラー", pbIdx + 1, segIdx + 1);
                    }
                }
            }
        }

        private byte[] RenderPileSectionDiagramViaViewModel(PileSection section, int pileBodyNo, int segmentNo)
        {
            double dipW = PileSectionDiagramSizeMm * 96.0 / 25.4;
            double dipH = PileSectionDiagramSizeMm * 96.0 / 25.4;
            int pxW = (int)Math.Round(dipW * Layout.HiResScale);
            int pxH = (int)Math.Round(dipH * Layout.HiResScale);

            var canvas = new Canvas { Width = dipW, Height = dipH, Background = Brushes.White };
            canvas.Measure(new Size(dipW, dipH));
            canvas.Arrange(new Rect(0, 0, dipW, dipH));
            canvas.UpdateLayout();

            // PileSectionViewModel を一時的に組み立て、Canvas を貸す
            var psVm = new PileSectionViewModel(mainWindowViewModel, section, pileBodyNo, segmentNo)
            {
                Canvas = canvas
            };
            psVm.RedrawShapes();

            canvas.Measure(new Size(dipW, dipH));
            canvas.Arrange(new Rect(0, 0, dipW, dipH));
            canvas.UpdateLayout();

            return RenderCanvasToPng(canvas, dipW, dipH, pxW, pxH);
        }

        // ---- 杭頭上面図 (各 PileBody につき 1 図) ----------------------------------

        private const double PileTopViewSizeMm = 80; // 正方形

        private void AddPileTopViewDiagrams(MainDocumentPart mainPart, Body body)
        {
            for (int pbIdx = 0; pbIdx < inputModel.PileBodies.Count; pbIdx++)
            {
                var pileBody = inputModel.PileBodies[pbIdx];
                if (pileBody?.PileTop == null) continue;

                // 杭頭上面図はキャプテンパイル工法 / FT-Pile 構法 / キャプリングパイル工法 で意味を持つ
                bool isTargetType = pileBody.PileTopType == "キャプテンパイル工法"
                                 || pileBody.PileTopType == "FT-Pile構法"
                                 || pileBody.PileTopType == "キャプリングパイル工法";
                if (!isTargetType) continue;

                var firstSection = pileBody.PileBodySegments?.FirstOrDefault()?.PileSection;

                try
                {
                    var pngBytes = RenderPileTopViewViaDrawer(pileBody, firstSection, pbIdx + 1);
                    if (pngBytes == null || pngBytes.Length == 0) continue;
                    WordDrawingBuilder.AddPngBytesToBody(mainPart, body, pngBytes,
                        PileTopViewSizeMm, PileTopViewSizeMm);

                    string caption = $"杭頭上面図 — 杭体 {pbIdx + 1} ({pileBody.PileTopType ?? ""})";
                    AddAutoFigureCaption(body, caption, "図");
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "杭頭上面図 (杭体 {PB}) の docx 出力中にエラー", pbIdx + 1);
                }
            }
        }

        private byte[] RenderPileTopViewViaDrawer(PileBodyInput pileBody, PileSection firstSection, int pileBodyNo)
        {
            double dipW = PileTopViewSizeMm * 96.0 / 25.4;
            double dipH = PileTopViewSizeMm * 96.0 / 25.4;
            int pxW = (int)Math.Round(dipW * Layout.HiResScale);
            int pxH = (int)Math.Round(dipH * Layout.HiResScale);

            var canvas = new Canvas { Width = dipW, Height = dipH, Background = Brushes.White };
            canvas.Measure(new Size(dipW, dipH));
            canvas.Arrange(new Rect(0, 0, dipW, dipH));
            canvas.UpdateLayout();

            var ptVm = new PileTopViewModel(
                mainWindowViewModel,
                pileBody.PileTop,
                pileBodyNo,
                pileBody.PileBodyType,
                pileBody.PileTopType,
                pileBody.PileConstructionType,
                firstSection);

            var drawer = new PileTopTopViewDrawer(ptVm, canvas);
            drawer.RedrawShapes();

            canvas.Measure(new Size(dipW, dipH));
            canvas.Arrange(new Rect(0, 0, dipW, dipH));
            canvas.UpdateLayout();

            return RenderCanvasToPng(canvas, dipW, dipH, pxW, pxH);
        }

        // ---- 共通ヘルパ: Canvas → PNG (VisualBrush + RenderTargetBitmap) -----------

        private static byte[] RenderCanvasToPng(Canvas canvas, double dipW, double dipH, int pxW, int pxH)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, dipW, dipH));
                var brush = new VisualBrush(canvas)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                };
                dc.DrawRectangle(brush, null, new Rect(0, 0, dipW, dipH));
            }

            var rtb = new RenderTargetBitmap(pxW, pxH, 96.0 * Layout.HiResScale, 96.0 * Layout.HiResScale, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        // ---- 杭頭諸元テーブル (各 PileBody の杭頭につき 1 表) -------------------------

        private void AddPileTopSpecsTables(Body body)
        {
            for (int i = 0; i < inputModel.PileBodies.Count; i++)
            {
                var pileBody = inputModel.PileBodies[i];
                var pileTop = pileBody?.PileTop;
                if (pileTop == null) continue;

                // SelectedPileTopSpecification が空 (PileTopWindow を開かずに F9→計算書出力した等) の場合、
                // 工法別ライブラリから動的に諸元を生成する。
                var specs = pileTop.SelectedPileTopSpecification;
                if (specs == null || specs.Count == 0)
                {
                    if (pileBody.PileTopType == "キャプリングパイル工法" && pileTop.CapringPile?.PCRing != null)
                    {
                        specs = pileTop.CapringPile.GetCombinedSpecs();
                    }
                    else if (pileBody.PileTopType == "キャプテンパイル工法" && pileTop.CaptainPile?.PCRing != null)
                    {
                        specs = pileTop.CaptainPile.GetCombinedSpecs();
                    }
                    else if (pileBody.PileTopType == "FT-Pile構法" && pileTop.FTPile?.FTCap != null)
                    {
                        specs = pileTop.FTPile.GetCombinedSpecs();
                    }
                }
                if (specs == null || specs.Count == 0) continue;

                try
                {
                    string caption = $"杭頭諸元 — 杭体 {i + 1} ({pileBody.PileTopType ?? ""})";
                    AddAutoFigureCaption(body, caption, "表");
                    AddSpecsTable(body, specs);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "杭頭諸元 (PileBodyNo={PB}) の docx 出力中にエラー", i + 1);
                }
            }
        }

        // 4 列 (項目 / 記号 / 値 / 単位) の Spec テーブル
        // 項目列を広く取り、記号・単位列を狭くして長い項目名 (引張定着筋関連) でも 1 行に収まるよう配分
        private static void AddSpecsTable(Body body, IEnumerable<Spec> specs)
        {
            int[] widths = { 75, 18, 25, 12 }; // mm
            var table = CreateTableWithBordersAndWidths(widths);

            // ヘッダ
            table.Append(CreateHeaderRow(
                CreateTableCellWithWidth("項目", "center", widths[0]),
                CreateTableCellWithWidth("記号", "center", widths[1]),
                CreateTableCellWithWidth("値",   "center", widths[2]),
                CreateTableCellWithWidth("単位", "center", widths[3])
            ));

            // データ行
            foreach (var s in specs)
            {
                var row = new TableRow();
                row.Append(CreateTableCellWithWidth(s.Item ?? "",  "left",  widths[0]));
                row.Append(CreateTableCellWithWidth(s.Mark ?? "",  "left",  widths[1]));
                row.Append(CreateTableCellWithWidth(s.Value ?? "", "right", widths[2]));
                row.Append(CreateTableCellWithWidth(s.Unit ?? "",  "left",  widths[3]));
                table.Append(row);
            }

            body.Append(table);
        }

        // ---- 軸力制限テーブル (各 PileBody の各 PileSection につき 1 表) ----------------

        private void AddAxialLimitTables(Body body)
        {
            for (int pbIdx = 0; pbIdx < inputModel.PileBodies.Count; pbIdx++)
            {
                var pileBody = inputModel.PileBodies[pbIdx];
                if (pileBody?.PileBodySegments == null) continue;

                var seenSections = new HashSet<PileSection>(); // 同一セクションを重複出力しない
                for (int segIdx = 0; segIdx < pileBody.PileBodySegments.Count; segIdx++)
                {
                    var section = pileBody.PileBodySegments[segIdx]?.PileSection;
                    if (section == null) continue;
                    if (!seenSections.Add(section)) continue;

                    try
                    {
                        string caption = $"軸力制限値 — 杭体 {pbIdx + 1} 区間 {segIdx + 1} ({section.PileSectionType ?? section.PileBodyType ?? ""})";
                        AddAutoFigureCaption(body, caption, "表");
                        AddAxialLimitTable(body, section, section.PileBodyType, section.PileSectionType);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "軸力制限テーブル (杭体 {PB}, 区間 {SEG}) の docx 出力中にエラー",
                            pbIdx + 1, segIdx + 1);
                    }
                }
            }
        }

        // 損傷限界・安全限界の軸力閾値を「閾値定義」付きで一覧化
        private static void AddAxialLimitTable(Body body, PileSection section, string pileBodyType, string pileSectionType)
        {
            // PileSection.DamageLimit/UltimateLimitAxialForceThresholds は NM 曲線計算
            // (GetNMRaw) の副作用として更新される。docx 出力時に UI 未表示の場合に
            // 空のままになるため、ここで NM 曲線を強制取得して閾値を populate する。
            try { _ = section.UnfactoredUltimateNMRaw; } catch { /* 計算失敗時は空のまま */ }
            try { _ = section.UnfactoredDamageNMRaw; } catch { /* 同上 */ }

            int[] widths = { 25, 60, 25 }; // mm
            var table = CreateTableWithBordersAndWidths(widths);

            table.Append(CreateHeaderRow(
                CreateTableCellWithWidth("限界状態",   "center", widths[0]),
                CreateTableCellWithWidth("閾値定義",   "center", widths[1]),
                CreateTableCellWithWidth("軸力 (kN)", "center", widths[2])
            ));

            int rowsAdded = 0;
            void AppendRows(string limitName, IList<double> values, bool isUltimate)
            {
                if (values == null || values.Count == 0) return;
                for (int i = 0; i < values.Count; i++)
                {
                    var row = new TableRow();
                    row.Append(CreateTableCellWithWidth(ConcreteModelOptions.MapLimitStateText(limitName), "left", widths[0]));
                    row.Append(CreateTableCellWithWidth(ConcreteModelOptions.MapLimitStateText(GetAxialLimitMeaning(pileBodyType, pileSectionType, isUltimate, i)), "left", widths[1]));
                    // 内部単位 N → kN 表示 (×0.001)
                    string val = (values[i] * 0.001).ToString("N0");
                    row.Append(CreateTableCellWithWidth(val, "right", widths[2]));
                    table.Append(row);
                    rowsAdded++;
                }
            }

            AppendRows("損傷限界", section.DamageLimitAxialForceThresholds, isUltimate: false);
            AppendRows("安全限界", section.UltimateLimitAxialForceThresholds, isUltimate: true);

            // データなしの場合は説明行を追加
            if (rowsAdded == 0)
            {
                var row = new TableRow();
                row.Append(CreateTableCellWithWidth("(該当データなし)", "left", widths[0] + widths[1] + widths[2]));
                table.Append(row);
            }

            body.Append(table);
        }

        // インデックスの意味を型別に解決 (PileSectionWindow の AxialLimitTab の表記と一致させる)
        private static string GetAxialLimitMeaning(string pileBodyType, string pileSectionType, bool isUltimate, int index)
        {
            // 場所打ち RC 系: 場所打ち鉄筋コンクリート杭, 場所打ち鋼管コンクリート杭(鉄筋コンクリート部)
            bool isInsituRcLike = pileBodyType == "場所打ち鉄筋コンクリート杭"
                || (pileBodyType == "場所打ち鋼管コンクリート杭" && pileSectionType == "鉄筋コンクリート部");

            if (isInsituRcLike)
            {
                if (isUltimate)
                {
                    return index switch
                    {
                        0 => "引張側制限値 (σ₀ = -0.05ξFc)",
                        1 => "変形性能考慮最大値 (σ₀ = (1/4)ξFc)",
                        2 => "β₂閾値 (σ₀ = (1/3)ξFc)",
                        3 => "圧縮側制限値 (σ₀ = 0.4ξFc)",
                        _ => $"[{index}]"
                    };
                }
                return index switch
                {
                    0 => "引張側制限値 (σ₀ = -0.05ξFc)",
                    1 => "β₂閾値 (σ₀ = (1/3)ξFc)",
                    2 => "圧縮側制限値 (σ₀ = 0.4ξFc)",
                    _ => $"[{index}]"
                };
            }

            // 場所打ち鋼管コンクリート杭(鋼管コンクリート部) 安全限界
            if (pileBodyType == "場所打ち鋼管コンクリート杭" && pileSectionType == "鋼管コンクリート部" && isUltimate)
            {
                return index switch
                {
                    0 => "引張側制限値 (N = -0.2(ᵣσy·Aᵣ + f_cy·Aₛ))",
                    1 => "圧縮側制限値 (σ₀ = 0.4ξFc)",
                    _ => $"[{index}]"
                };
            }

            // 鋼管杭 (鋼管部 / コンクリート充填鋼管部)
            if (pileBodyType == "鋼管杭")
            {
                if (isUltimate)
                {
                    return index switch
                    {
                        0 => "Nut (引張側安全限界)",
                        1 => "Nuc (圧縮側安全限界)",
                        _ => $"[{index}]"
                    };
                }
                return index switch
                {
                    0 => "Ndt (引張側損傷限界)",
                    1 => "Ndc (圧縮側損傷限界)",
                    _ => $"[{index}]"
                };
            }

            // 既製コンクリート杭 (PHC / PRC / SC)
            // SC杭 (4 entries): [0]曲げ引張、[1]曲げ圧縮、[2]せん断引張、[3]せん断圧縮
            if (pileSectionType == "SC杭")
            {
                return index switch
                {
                    0 => isUltimate ? "曲げ引張側 (安全限界)" : "曲げ引張側 (損傷限界)",
                    1 => isUltimate ? "曲げ圧縮側 (安全限界)" : "曲げ圧縮側 (損傷限界)",
                    2 => isUltimate ? "せん断引張側 (安全限界)" : "せん断引張側 (損傷限界)",
                    3 => isUltimate ? "せん断圧縮側 (安全限界)" : "せん断圧縮側 (損傷限界)",
                    _ => $"[{index}]"
                };
            }

            // PHC杭 / PRC杭 (3 entries):
            // 損傷限界: [0]ひび割れ限界 (4-σE)Ae、[1]弾性限界 (10-σE)Ae、[2]圧壊限界 (35-σE)Ae
            // 安全限界 PHC: [0](4-σE)Ae、[1](10-σE)Ae、[2]圧壊限界 (65-σE)Ae
            // 安全限界 PRC: [0]主筋・テンドン破断限界、[1](10-σE)Ae、[2]圧壊限界 (60-σE)Ae
            if (pileSectionType == "PHC杭" || pileSectionType == "PRC杭")
            {
                if (isUltimate)
                {
                    if (pileSectionType == "PRC杭" && index == 0)
                        return "主筋・テンドン破断限界 (-0.27(Ag·rσy + Ap·fpy))";
                    return index switch
                    {
                        0 => "引張側ひび割れ限界 ((4-σE)·Ae)",
                        1 => "弾性限界 ((10-σE)·Ae)",
                        2 => $"圧壊限界 (({(pileSectionType == "PRC杭" ? "60" : "65")}-σE)·Ae)",
                        _ => $"[{index}]"
                    };
                }
                return index switch
                {
                    0 => "引張側ひび割れ限界 ((4-σE)·Ae)",
                    1 => "弾性限界 ((10-σE)·Ae)",
                    2 => "圧壊限界 ((35-σE)·Ae)",
                    _ => $"[{index}]"
                };
            }

            // それ以外: 既定 (Nut / Nuc / 追加 index は不明)
            return index switch
            {
                0 => isUltimate ? "Nut (引張側安全限界)" : "Ndt (引張側損傷限界)",
                1 => isUltimate ? "Nuc (圧縮側安全限界)" : "Ndc (圧縮側損傷限界)",
                _ => $"[{index}]"
            };
        }
    }
}

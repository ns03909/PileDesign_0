using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Output;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace TestProject1
{
    /// <summary>
    /// 計算書の「沈下解析杭モデル」「水平抵抗解析杭モデル」の図。
    ///
    /// この図は倍密度 (HiResScale) で描いて 150mm 幅に貼る。線の太さや節点の大きさは
    /// その倍率に追従していたのに<b>文字の大きさだけが追従しておらず</b>、
    /// 10px で描いた文字が紙面では実効 3.7pt になっていた。ビルドもテストも通るので、
    /// 印刷して初めて分かる種類の不具合になる。ここでソースを走査して回帰を止める。
    /// </summary>
    [TestClass]
    public class PileModelDiagramTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(PileModelDiagramTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        private static string OutputFile(string name)
            => File.ReadAllText(Path.Combine(FindSolutionRoot(), "Graphics_r1", "Output", name));

        /// <summary>行頭コメントを落とす。コメントアウトされた旧コードまで検査しないため。</summary>
        private static string WithoutCommentedOutCode(string code)
            => string.Join('\n', code.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        /// <summary>
        /// <c>RenderPileForceElevationPngBytes</c> の本体を切り出す。
        /// 次の <c>public static</c> の手前までを見る。
        /// </summary>
        private static string DiagramMethodBody()
        {
            string code = OutputFile("DiagramRenderer.cs");
            int start = code.IndexOf("public static byte[] RenderPileForceElevationPngBytes", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "RenderPileForceElevationPngBytes が見つかりません");

            int next = code.IndexOf("public static byte[] ", start + 40, StringComparison.Ordinal);
            return next < 0 ? code[start..] : code[start..next];
        }

        // ------------------------------------------------------------------
        // ソース走査型 (STA 不要)
        // ------------------------------------------------------------------

        /// <summary>
        /// 図中の文字が画像の倍率に追従していること。
        /// <c>FormattedText</c> の em サイズに数値を直書きすると、
        /// 画像だけが倍密度になり紙面で文字が読めなくなる。
        /// </summary>
        [TestMethod]
        public void TheDiagramTextScalesWithTheImage()
        {
            string body = DiagramMethodBody();
            var violations = new List<string>();

            foreach (Match m in Regex.Matches(body, @"new FormattedText\((?<args>[^;]*?)\);", RegexOptions.Singleline))
            {
                string args = m.Groups["args"].Value;
                if (!args.Contains("PtToPx(", StringComparison.Ordinal))
                    violations.Add(Regex.Replace(args.Trim(), @"\s+", " "));
            }

            Assert.AreEqual(0, violations.Count,
                "図中の文字サイズが px 直書きです (PtToPx を通すこと):\n  " + string.Join("\n  ", violations));
        }

        /// <summary>図中の書体を直書きしていないこと (Layout の規約から渡す)。</summary>
        [TestMethod]
        public void TheDiagramDoesNotHardcodeATypeface()
        {
            string body = DiagramMethodBody();
            Assert.IsFalse(body.Contains("new Typeface(\"", StringComparison.Ordinal),
                "図中の書体が直書きされています (style.FontName を使うこと)");
        }

        /// <summary>
        /// 図の書体が本文と分かれていること。本文は明朝で、小さくすると潰れる。
        /// </summary>
        [TestMethod]
        public void TheDiagramFontIsSplitFromTheBodyFont()
        {
            string code = OutputFile("WordDocument.cs");

            string Face(string name)
            {
                var m = Regex.Match(code, $@"public const string {name} = ""(?<v>[^""]+)""");
                Assert.IsTrue(m.Success, $"{name} が見つかりません");
                return m.Groups["v"].Value;
            }

            Assert.AreNotEqual(Face("FontName"), Face("DiagramFontName"), "図の書体が本文と同じです");

            var size = Regex.Match(code, @"public const double DiagramFontSizePt = (?<v>[\d.]+)");
            Assert.IsTrue(size.Success && double.Parse(size.Groups["v"].Value) < 10.5,
                "図の文字が本文より小さくなっていません");
        }

        /// <summary>
        /// 図の寸法が Layout の定数から来ていること。
        /// 呼び出し側に mm を直書きすると、寸法を変えたときに片方だけ取り残される。
        /// </summary>
        [TestMethod]
        public void ThePileModelFigureUsesTheLayoutSize()
        {
            string code = WithoutCommentedOutCode(OutputFile("WordDocument.cs"));

            var h = Regex.Match(code, @"public const double PileElevationHeightMm = (?<v>[\d.]+)");
            Assert.IsTrue(h.Success && double.Parse(h.Groups["v"].Value) >= 150,
                "解析杭モデル図の高さが足りません (杭長 30m が読める高さが要る)");

            foreach (Match call in Regex.Matches(code, @"AddPileForceDiagramByMm\((?<args>[^;]*?)\);", RegexOptions.Singleline))
            {
                string args = call.Groups["args"].Value;
                StringAssert.Contains(args, "Layout.PileElevationWidthMm", "図の幅が直書きされています");
                StringAssert.Contains(args, "Layout.PileElevationHeightMm", "図の高さが直書きされています");
            }
        }

        /// <summary>
        /// 土層の色帯の上端が<b>地表面標高</b>であること。
        /// 杭頭標高を上端にしていたため、杭頭が地表と一致しないモデルで
        /// 色帯が層境界からずれていた。
        /// </summary>
        [TestMethod]
        public void TheSoilLayerBackgroundStartsAtTheGroundSurface()
        {
            string code = OutputFile("DiagramRenderer.cs");
            int start = code.IndexOf("private static void DrawSoilLayerBackground", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "DrawSoilLayerBackground が見つかりません");

            int next = code.IndexOf("\n        // Example:", start, StringComparison.Ordinal);
            string body = next < 0 ? code[start..] : code[start..next];

            StringAssert.Contains(body, "GroundTopAltitude", "色帯の上端が地表面標高になっていません");
            Assert.IsFalse(body.Contains("soilPile", StringComparison.OrdinalIgnoreCase),
                "色帯の上端に杭頭標高を使っています");
        }

        // ------------------------------------------------------------------
        // レイアウト判断の純粋関数 (STA 不要)
        // ------------------------------------------------------------------

        /// <summary>N値柱状図の軸は、支持層の N 値が入るところまで自動で伸びること。</summary>
        [TestMethod]
        public void TheNAxisGrowsToFitTheData()
        {
            Assert.AreEqual(60.0, DiagramRenderer.NAxisMax(0));
            Assert.AreEqual(60.0, DiagramRenderer.NAxisMax(45));
            Assert.AreEqual(60.0, DiagramRenderer.NAxisMax(60));
            Assert.AreEqual(70.0, DiagramRenderer.NAxisMax(63));
            Assert.AreEqual(130.0, DiagramRenderer.NAxisMax(130));
            Assert.AreEqual(300.0, DiagramRenderer.NAxisMax(9999), "上限で頭打ちになっていません");
            Assert.AreEqual(60.0, DiagramRenderer.NAxisMax(double.NaN), "NaN で軸が壊れます");
        }

        /// <summary>軸が伸びたら目盛の刻みも粗くなること (ラベルの重なり防止)。</summary>
        [TestMethod]
        public void TheNAxisStepCoarsensWithTheRange()
        {
            Assert.AreEqual(10.0, DiagramRenderer.NAxisStep(60));
            Assert.AreEqual(20.0, DiagramRenderer.NAxisStep(120));
            Assert.AreEqual(50.0, DiagramRenderer.NAxisStep(200));
        }

        [TestMethod]
        public void TheCircledNumberFallsBackToParenthesesPastTwenty()
        {
            Assert.AreEqual("", DiagramRenderer.CircledNumber(0));
            Assert.AreEqual("", DiagramRenderer.CircledNumber(-3));
            Assert.AreEqual("①", DiagramRenderer.CircledNumber(1));
            Assert.AreEqual("④", DiagramRenderer.CircledNumber(4));
            Assert.AreEqual("⑳", DiagramRenderer.CircledNumber(20));
            Assert.AreEqual("(21)", DiagramRenderer.CircledNumber(21));
        }

        /// <summary>
        /// 土層情報帯の行数。境界のすぐ上下で 3 → 2 → 1 → 0 と落ちること。
        /// </summary>
        [TestMethod]
        public void TheLayerInfoDegradesAtEachHeightThreshold()
        {
            const double lh = 20.0;   // 通常の行高
            const double lhs = 16.0;  // 小さい書体の行高

            Assert.AreEqual(3, DiagramRenderer.LayerInfoLineCount(3 * lh + 4, lh, lhs));
            Assert.AreEqual(2, DiagramRenderer.LayerInfoLineCount(3 * lh + 3, lh, lhs));
            Assert.AreEqual(2, DiagramRenderer.LayerInfoLineCount(2 * lh + 4, lh, lhs));
            Assert.AreEqual(1, DiagramRenderer.LayerInfoLineCount(2 * lh + 3, lh, lhs));
            Assert.AreEqual(1, DiagramRenderer.LayerInfoLineCount(lh + 2, lh, lhs));
            Assert.AreEqual(1, DiagramRenderer.LayerInfoLineCount(lhs, lh, lhs), "小さい書体で 1 行入るはず");
            Assert.AreEqual(0, DiagramRenderer.LayerInfoLineCount(lhs - 1, lh, lhs), "引き出し線に逃がすはず");
        }

        /// <summary>
        /// 粘着力は粘性土以外では 0。0 のときに「Cu=0」と書くと
        /// 測ってこの値だったように読めるので、項ごと省く。
        /// </summary>
        [TestMethod]
        public void TheLayerInfoOmitsCohesionWhenItIsZero()
        {
            var withCu = DiagramRenderer.LayerInfoLines(4, "シルト質細砂", 3.2, 8.2, 18.0, 12, 45, 3);
            Assert.AreEqual(3, withCu.Length);
            StringAssert.StartsWith(withCu[0], "④ シルト質細砂");
            StringAssert.Contains(withCu[1], "t=3.20");
            StringAssert.Contains(withCu[1], "GL-8.20");
            StringAssert.Contains(withCu[2], "Cu=45");

            var sand = DiagramRenderer.LayerInfoLines(5, "細砂", 5.0, 13.2, 19.0, 25, 0, 3);
            Assert.IsFalse(sand[2].Contains("Cu", StringComparison.Ordinal), "Cu=0 が書かれています");

            var two = DiagramRenderer.LayerInfoLines(4, "シルト質細砂", 3.2, 8.2, 18.0, 12, 45, 2);
            Assert.AreEqual(2, two.Length);
            Assert.IsFalse(two[1].Contains("GL-", StringComparison.Ordinal), "2 行では下端深度を落とすはず");

            var one = DiagramRenderer.LayerInfoLines(4, "シルト質細砂", 3.2, 8.2, 18.0, 12, 45, 1);
            Assert.AreEqual(1, one.Length);
            StringAssert.Contains(one[0], "N=12");

            Assert.AreEqual(0, DiagramRenderer.LayerInfoLines(4, "シルト質細砂", 3.2, 8.2, 18.0, 12, 45, 0).Length);
        }

        // ------------------------------------------------------------------
        // 実描画スモーク (STA 必須)
        // ------------------------------------------------------------------

        /// <summary>
        /// 実際に描いて、指定どおりの大きさの PNG になり、中身が空でないこと。
        /// <c>ExecuteOnUIThread</c> は例外を握り潰して空配列を返すため、
        /// 長さ 0 を「失敗」として扱わないと描画エラーを見逃す。
        /// </summary>
        [TestMethod]
        [Timeout(180000)]
        public void RenderPileForceElevation_ProducesAReadablePng()
        {
            const double widthMm = 150;
            const double heightMm = 190;
            const double dpi = 96.0;
            const double scale = 2.0;
            int expectedW = (int)Math.Round(widthMm * dpi * scale / 25.4);
            int expectedH = (int)Math.Round(heightMm * dpi * scale / 25.4);

            var style = new PileDiagramStyle("游ゴシック", 8.0, 7.0, 1.2, 0.7, 24, 8.0);
            var soilPile = BuildSoilPile();

            var ex = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                foreach (string springType in new[] { "vertical", "horizontal" })
                {
                    byte[] png = DiagramRenderer.RenderPileForceElevationPngBytes(
                        soilPile, springType, style, widthMm, heightMm, dpi, scale);

                    Assert.IsTrue(png.Length > 0, $"{springType}: 描画に失敗しています (空配列)");
                    CollectionAssert.AreEqual(
                        new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray(),
                        $"{springType}: PNG になっていません");

                    var frame = BitmapFrame.Create(
                        new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    Assert.AreEqual(expectedW, frame.PixelWidth, $"{springType}: 幅が違います");
                    Assert.AreEqual(expectedH, frame.PixelHeight, $"{springType}: 高さが違います");

                    Assert.IsTrue(NonWhiteRatio(frame) > 0.01,
                        $"{springType}: ほぼ白紙です (「杭のデータがありません」に落ちている可能性)");
                }
            }, out bool timedOut, 120);

            if (timedOut) Assert.Inconclusive("STA スレッドがタイムアウトしました");
            if (ex != null) throw ex;
        }

        /// <summary>白でない画素の割合。図が空白に落ちていないことの確認用。</summary>
        private static double NonWhiteRatio(BitmapSource source)
        {
            var gray = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Gray8, null, 0);
            int stride = gray.PixelWidth;
            var buffer = new byte[stride * gray.PixelHeight];
            gray.CopyPixels(buffer, stride, 0);

            int dark = buffer.Count(b => b < 250);
            return (double)dark / buffer.Length;
        }

        /// <summary>
        /// 図の描画に必要な最小限のモデル。解析結果は要らない。
        /// 層厚をわざと不揃いにして、薄い層の縮退 (引き出し線) も通す。
        /// </summary>
        internal static SoilPile BuildSoilPile()
        {
            var ground = new GroundInput
            {
                GroundTopAltitude = 0,
                GroundWaterTableAltitude = -2.0,
            };
            // GroundInput は既定の土層・土質点を持って生まれるので、まず空にする
            ground.GroundLayers.Clear();
            ground.GroundMassesData.Clear();

            (string name, string cls, double bottom, double n, double cu)[] layers =
            [
                ("埋土",       "砂質土", -2.0,  5, 0),
                ("シルト",     "粘性土", -8.0,  3, 40),
                ("薄い砂層",   "砂質土", -8.2, 12, 0),   // 引き出し線に逃げるはずの薄い層
                ("細砂",       "砂質土", -18.0, 20, 0),
                ("砂礫",       "礫質土", -24.0, 45, 0),
                ("支持層",     "礫質土", -30.0, 75, 0),  // N > 60。軸が伸びるかの確認も兼ねる
            ];

            double top = 0;
            foreach (var (name, cls, bottom, n, cu) in layers)
            {
                ground.GroundLayers.Add(new GroundLayerInput
                {
                    No = ground.GroundLayers.Count + 1,
                    Name = name,
                    GranularityClass = cls,
                    BottomAltitude = bottom,
                    BottomGLDepth = -bottom,
                    LayerThickness = top - bottom,
                    Density = 18.0,
                    NValue = n,
                    Cohesive = cu,
                });
                top = bottom;
            }

            for (int i = 0; i <= 30; i++)
            {
                ground.GroundMassesData.Add(new GroundMassDataInput
                {
                    No = i + 1,
                    AltitudeDepth = -i,
                    NValue = Math.Min(75, 2 + i * 2),
                });
            }

            var soilPile = new SoilPile
            {
                No = 1,
                Z = 0,
                GroundInput = ground,
                PileBodySegments = [],
                ZDataItems = [],
            };

            for (int i = 1; i <= 3; i++)
            {
                soilPile.PileBodySegments.Add(new PileBodySegment
                {
                    SegmentLength = 9.0,
                    SegmentDepth = 9.0 * i,
                });
            }

            for (int i = 0; i <= 27; i++)
            {
                soilPile.ZDataItems.Add(new PileZDataItem
                {
                    Z = -i,
                    GroundDisp1 = 30.0 * (27 - i) / 27.0,
                    GroundDisp2 = 20.0 * (27 - i) / 27.0,
                    GroundDisp1L = 45.0 * (27 - i) / 27.0,
                    GroundDisp2L = 35.0 * (27 - i) / 27.0,
                });
            }

            return soilPile;
        }
    }

    /// <summary>入力データ章の土層表。</summary>
    [TestClass]
    public class GroundLayerTableTests
    {
        /// <summary>
        /// 土層表に N値 と 粘着力 Cu が載っていること。
        /// 列を足したときにヘッダだけ直してデータ行を忘れると、
        /// 表の列が 1 つずつずれて別の値が別の見出しの下に並ぶ。
        /// </summary>
        [TestMethod]
        public void TheGroundLayerTableCarriesNAndCohesion()
        {
            var layers = new ObservableCollection<GroundLayerInput>
            {
                new() { No = 1, Name = "シルト", GranularityClass = "粘性土", LayerThickness = 3.0,
                        BottomGLDepth = 3.0, BottomAltitude = -3.0, Density = 17.0, NValue = 3, Cohesive = 45 },
                new() { No = 2, Name = "細砂", GranularityClass = "砂質土", LayerThickness = 5.0,
                        BottomGLDepth = 8.0, BottomAltitude = -8.0, Density = 19.0, NValue = 25, Cohesive = 0 },
            };

            var body = new DocumentFormat.OpenXml.Wordprocessing.Body();
            WordDocument.AddGroundLayerTable(body, layers, new FundamentalInput());

            var table = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Table>().Single();
            var rows = table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().ToList();
            Assert.AreEqual(3, rows.Count, "見出し 1 行 + データ 2 行のはず");

            int headerCells = rows[0].Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>().Count();
            foreach (var row in rows.Skip(1))
            {
                Assert.AreEqual(headerCells, row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>().Count(),
                    "見出しとデータ行で列数が合っていません");
            }

            string text = body.InnerText;
            StringAssert.Contains(text, "N値", "N値の列がありません");
            StringAssert.Contains(text, "Cu", "粘着力の列がありません");
            StringAssert.Contains(text, "45.0", "粘着力の値が出ていません");
            StringAssert.Contains(text, "-", "粘着力 0 の層が \"-\" になっていません");
        }

        /// <summary>
        /// 表の行がページ境界で上下に割れないこと。
        /// Word の既定は行を分割できるため、ページ末尾の行が割れて
        /// 前のページに空のセルだけが残る（実際に計算書で発生した）。
        /// </summary>
        [TestMethod]
        public void TableRowsDoNotSplitAcrossPages()
        {
            var layers = new ObservableCollection<GroundLayerInput>
            {
                new() { No = 1, Name = "シルト", GranularityClass = "粘性土", LayerThickness = 3.0,
                        BottomGLDepth = 3.0, BottomAltitude = -3.0, Density = 17.0, NValue = 3, Cohesive = 45 },
            };

            var body = new DocumentFormat.OpenXml.Wordprocessing.Body();
            WordDocument.AddGroundLayerTable(body, layers, new FundamentalInput());

            // 2 回通しても重複しないこと（生成経路が増えても壊れないように）
            WordDocument.PreventTableRowsFromSplittingAcrossPages(body);
            WordDocument.PreventTableRowsFromSplittingAcrossPages(body);

            var rows = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.TableRow>().ToList();
            Assert.IsTrue(rows.Count >= 2, "行が見つかりません");

            foreach (var row in rows)
            {
                var trPr = row.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableRowProperties>();
                Assert.IsNotNull(trPr, "trPr がありません");
                Assert.AreEqual(1,
                    trPr.Elements<DocumentFormat.OpenXml.Wordprocessing.CantSplit>().Count(),
                    "cantSplit が無い、または重複しています");

                // trPr は行の先頭要素でなければならない (OpenXML スキーマ)
                Assert.AreSame(trPr, row.FirstChild, "trPr が行の先頭にありません");
            }

            // 見出し行の繰返し設定 (tblHeader) を壊していないこと
            var headerTrPr = rows[0].GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableRowProperties>();
            Assert.IsTrue(headerTrPr!.Elements<DocumentFormat.OpenXml.Wordprocessing.TableHeader>().Any(),
                "見出し行の繰返し設定が失われています");
        }
    }
}

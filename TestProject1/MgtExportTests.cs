using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Output;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media.Media3D;

namespace TestProject1
{
    /// <summary>
    /// MGT 出力 (midas Gen) の 2 つの取りこぼし。
    ///
    /// - 荷重ケースを「名前 + レベル」でまとめていたので、同じレベルに同じ名前のケースが 2 つあると
    ///   先頭しか出力されなかった。名前は自由に付けられ、重複も拒まない。今は「レベル + 番号」でまとめる
    ///   (出力するケース名 UF_L{Level}_{No} もこれで作る)。
    /// - 数値を実行中の地域設定の小数点で書いていたので、小数点がカンマの地域では「1,25」になり、
    ///   MGT の項目の区切りとぶつかった。今は書き出しのあいだだけ InvariantCulture にする。
    /// </summary>
    [TestClass]
    public class MgtExportTests
    {
        private static AnaModel BuildModel(InputModel input)
        {
            var nI = new Node { Name = "I", Coord = new Point3D(0, 0, 0), Boundary = new Boundary(true, true, true, true, true, true) };
            var nJ = new Node { Name = "J", Coord = new Point3D(1.25, 0, -2.5), Boundary = new Boundary(false, false, false, false, false, false) };
            var mat = new PileDesign.FEM.Material(205e6, 0.3);
            var sec = new Section(mat, 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4);
            var beam = new Beam("B1", sec, nI, nJ, 1.0, 1.0);
            return new AnaModel(input, [nI, nJ], [beam], [], [], [], []);
        }

        private static string Export(AnaModel model)
        {
            string file = Path.Combine(Path.GetTempPath(), $"MgtExport_{Guid.NewGuid():N}.mgt");
            try
            {
                new MgtExporter(model).Export(file);
                return File.ReadAllText(file);
            }
            finally
            {
                File.Delete(file);
            }
        }

        [TestMethod]
        public void NumbersUseAPointEvenWhenTheRegionUsesACommaDecimal()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");   // 小数点がカンマの地域
            try
            {
                Assert.AreEqual("1,25", 1.25.ToString(CultureInfo.CurrentCulture), "前提: この地域では小数点がカンマ");

                string mgt = Export(BuildModel(new InputModel()));

                StringAssert.Contains(mgt, "1.25", "節点の座標が小数点のピリオドで書かれていません");
                var broken = Regex.Matches(mgt, @"\d,\d").Select(m => m.Value).ToList();
                Assert.AreEqual(0, broken.Count,
                    "数値の小数点がカンマで書かれています (MGT の区切りとぶつかります): " + string.Join(" ", broken.Take(5)));
                Assert.AreEqual("de-DE", CultureInfo.CurrentCulture.Name, "書き出しのあと、地域設定が元に戻っていません");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [TestMethod]
        public void LoadCasesWithTheSameNameAreBothExported()
        {
            var input = new InputModel { LoadCasesInput = new LoadCasesInput() };
            var model = BuildModel(input);

            // 同じレベルに同じ名前の別ケース (番号 1 と 2)
            var case1 = new LoadCase { Level = 1, No = 1, LoadName = "E", UpperMassForce = 100 };
            var case2 = new LoadCase { Level = 1, No = 2, LoadName = "E", UpperMassForce = 200, LoadAngle = 90 };
            var comb = new LoadCombination(1, 1.0, 1.0, 1.0);
            model.AnalysisStepResults =
            [
                new AnalysisStepResult(case1, comb, false, 1, 1, 0.0),
                new AnalysisStepResult(case2, comb, false, 1, 1, 0.0),
            ];

            string mgt = Export(model);

            foreach (var name in new[] { "UF_L1_1", "UF_L1_2", "FF_L1_1", "FF_L1_2", "GD_L1_1", "GD_L1_2", "C_L1_1_1", "C_L1_2_1" })
                StringAssert.Contains(mgt, name, $"同じ名前のもう一方のケース ({name}) が出力から欠けています");
        }

        /// <summary>
        /// 荷重ケース名に改行・タブ・カンマがあっても、1 件の説明が 1 行に収まること。
        /// 以前はカンマだけを空白にしていたので、改行があると説明が複数行に割れ、続く行が別のレコードとして読まれた。
        /// </summary>
        [TestMethod]
        public void ALoadCaseNameWithLineBreaksStaysOnOneLine()
        {
            var input = new InputModel { LoadCasesInput = new LoadCasesInput() };
            var model = BuildModel(input);
            var loadCase = new LoadCase { Level = 1, No = 1, LoadName = "地震\r\nX方向,\t北\u2028南", UpperMassForce = 100 };
            model.AnalysisStepResults = [new AnalysisStepResult(loadCase, new LoadCombination(1, 1.0, 1.0, 1.0), false, 1, 1, 0.0)];

            var lines = Export(model).Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            foreach (var (key, tail) in new[]
                     {
                         ("UF_L1_1 ", "Upper Mass Force"),
                         ("FF_L1_1 ", "Foundation Mass Force"),
                         ("GD_L1_1 ", "Ground Disp"),
                         ("NAME=C_L1_1_1", "b1="),
                     })
            {
                var line = lines.FirstOrDefault(l => l.TrimStart().StartsWith(key, StringComparison.Ordinal));
                Assert.IsNotNull(line, $"{key.Trim()} の行がありません");
                StringAssert.Contains(line, "地震 X方向 北 南", $"{key.Trim()} の説明に荷重ケース名がそのまま 1 行で入っていません: {line}");
                StringAssert.Contains(line, tail, $"{key.Trim()} の説明が途中で改行されています: {line}");
            }
            Assert.IsFalse(lines.Any(l => l.StartsWith("X方向", StringComparison.Ordinal)),
                "荷重ケース名の改行の後ろが、別の行 (別のレコード) として書かれています");
        }

        /// <summary>
        /// 同じレベル・番号の荷重ケースが 2 つあると、どのケースかを示して出力を止めること (片方を黙って欠かない)。
        /// 出力先のファイルも作らないこと。
        /// </summary>
        [TestMethod]
        public void DuplicateLoadCaseNumbersStopTheExport()
        {
            var input = new InputModel { LoadCasesInput = new LoadCasesInput() };
            var model = BuildModel(input);
            var comb = new LoadCombination(1, 1.0, 1.0, 1.0);
            var caseA = new LoadCase { Level = 1, No = 2, LoadName = "X方向", UpperMassForce = 100 };
            var caseB = new LoadCase { Level = 1, No = 2, LoadName = "Y方向", UpperMassForce = 200, LoadAngle = 90 };
            var other = new LoadCase { Level = 2, No = 2, LoadName = "L2", UpperMassForce = 300 };   // レベルが違えば重複ではない
            model.AnalysisStepResults =
            [
                new AnalysisStepResult(caseA, comb, false, 1, 1, 0.0),
                new AnalysisStepResult(caseA, comb, true, 1, 1, 0.0),    // 同じケースの別の結果は重複ではない
                new AnalysisStepResult(caseB, comb, false, 1, 1, 0.0),
                new AnalysisStepResult(other, comb, false, 1, 1, 0.0),
            ];

            string file = Path.Combine(Path.GetTempPath(), $"MgtExport_{Guid.NewGuid():N}.mgt");
            try
            {
                var ex = Assert.ThrowsException<InvalidOperationException>(() => new MgtExporter(model).Export(file),
                    "番号が重複しているのに出力しました (片方のケースが黙って欠けます)");
                StringAssert.Contains(ex.Message, "レベル1 の番号 2");
                StringAssert.Contains(ex.Message, "「X方向」");
                StringAssert.Contains(ex.Message, "「Y方向」");
                Assert.IsFalse(ex.Message.Contains("レベル2", StringComparison.Ordinal), "重複していないレベル2 のケースまで挙げています");
                Assert.IsFalse(File.Exists(file), "出力を止めたのに、出力先のファイルが作られています");
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        /// <summary>
        /// 同じ荷重ケース・液状化の有無で、係数の違う組合せが同じ番号を持つと、どれかを示して出力を止めること。
        /// 係数が同じなら書く内容も同じなので止めないこと。液状化の有無が違えば別の組合せとして扱うこと。
        /// </summary>
        [TestMethod]
        public void DuplicateLoadCombinationNumbersStopTheExport()
        {
            var input = new InputModel { LoadCasesInput = new LoadCasesInput() };
            var model = BuildModel(input);
            var loadCase = new LoadCase { Level = 1, No = 1, LoadName = "X方向", UpperMassForce = 100 };
            model.AnalysisStepResults =
            [
                new AnalysisStepResult(loadCase, new LoadCombination(1, 1.0, 1.0, 1.0), false, 1, 1, 0.0),
                new AnalysisStepResult(loadCase, new LoadCombination(1, 0.5, -1.0, 0.5), false, 1, 1, 0.0),   // 同じ番号・違う係数
                new AnalysisStepResult(loadCase, new LoadCombination(2, 1.0, 1.0, 1.0), false, 1, 1, 0.0),
                new AnalysisStepResult(loadCase, new LoadCombination(2, 1.0, 1.0, 1.0), false, 2, 1, 0.0),    // 同じ番号・同じ係数 (別の実体)
                new AnalysisStepResult(loadCase, new LoadCombination(1, 0.5, -1.0, 0.5), true, 1, 1, 0.0),    // 液状化ありは別
            ];

            string file = Path.Combine(Path.GetTempPath(), $"MgtExport_{Guid.NewGuid():N}.mgt");
            try
            {
                var ex = Assert.ThrowsException<InvalidOperationException>(() => new MgtExporter(model).Export(file),
                    "組合せの番号が重複しているのに出力しました (片方の組合せが黙って欠けます)");
                StringAssert.Contains(ex.Message, "「X方向」 の組合せ番号 1");
                StringAssert.Contains(ex.Message, "β1=1 β2=1 α1=1");
                StringAssert.Contains(ex.Message, "β1=-1 β2=0.5 α1=0.5");
                Assert.IsFalse(ex.Message.Contains("組合せ番号 2", StringComparison.Ordinal), "係数の同じ組合せまで重複として挙げています");
                Assert.IsFalse(ex.Message.Contains("液状化あり", StringComparison.Ordinal), "液状化の有無が違う組合せまで重複として挙げています");
                Assert.IsFalse(File.Exists(file), "出力を止めたのに、出力先のファイルが作られています");
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        /// <summary>
        /// 出力の途中で失敗しても、前に出力した MGT ファイルが残ること (途中までの内容で上書きしない)。
        /// 一時ファイルも残さないこと。
        /// </summary>
        [TestMethod]
        public void AFailedExportLeavesThePreviousFileIntact()
        {
            string dir = Path.Combine(Path.GetTempPath(), "MgtExportTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "model.mgt");
            try
            {
                File.WriteAllText(file, Export(BuildModel(new InputModel())));
                string previous = File.ReadAllText(file);

                // <b>書き出しの途中</b>で失敗させる。梁の端の節点を消すと、準備 (BuildContext) は通り、
                // 見出しと節点を書いたあと、要素を書く所 (節点番号を引く所) で例外になる
                var broken = BuildModel(new InputModel());
                var beam = broken.Beams[0];
                var nodeI = typeof(Beam).GetProperty("NodeI")!;
                if (nodeI.CanWrite) nodeI.SetValue(beam, null);
                else typeof(Beam).GetField("<NodeI>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .SetValue(beam, null);

                var failure = Assert.ThrowsException<InvalidOperationException>(() => new MgtExporter(broken).Export(file),
                    "(前提) 出力が失敗していません");
                StringAssert.Contains(failure.StackTrace, "WriteElements",
                    "(前提) 書き出しの途中ではなく、書き始める前に失敗しています (この検査が成立していない)");

                Assert.AreEqual(previous, File.ReadAllText(file), "出力に失敗したのに、前の MGT ファイルが書き換わっています");
                CollectionAssert.AreEqual(new[] { "model.mgt" }, Array.ConvertAll(Directory.GetFiles(dir), Path.GetFileName),
                    "失敗した出力の一時ファイルが残っています");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            }
        }

        /// <summary>MGT の *SECTION の各断面の 2 行目 (AREA, ASy, ASz, Ixx, Iyy, Izz) を読む。</summary>
        private static System.Collections.Generic.Dictionary<string, (string Shape, double[] Dims, double[] Props)> ReadValueSections(string mgt)
        {
            var lines = mgt.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            int start = lines.FindIndex(l => l.StartsWith("*SECTION", StringComparison.Ordinal));
            var result = new System.Collections.Generic.Dictionary<string, (string, double[], double[])>();
            for (int i = start + 1; i < lines.Count && !lines[i].StartsWith("*", StringComparison.Ordinal); i++)
            {
                var head = lines[i].Split(',').Select(t => t.Trim()).ToArray();
                if (head.Length < 2 || head[1] != "VALUE") continue;
                double[] Parse(string line) => line.Split(',').Select(t => double.Parse(t.Trim(), CultureInfo.InvariantCulture)).ToArray();
                result[head[2]] = (head[12], [double.Parse(head[14], CultureInfo.InvariantCulture), double.Parse(head[15], CultureInfo.InvariantCulture)],
                                   Parse(lines[i + 1]));
            }
            return result;
        }

        /// <summary>
        /// 断面性能を値 (VALUE 型) で、解析モデルの値のまま書くこと。
        /// 以前は断面積から円の直径を逆算した寸法入力の円形断面で書いていたので、midas が計算し直す
        /// 曲げ・ねじり剛性・せん断断面積が解析モデルと合わなかった。
        /// </summary>
        [TestMethod]
        public void SectionsCarryTheAnalysisModelsValues()
        {
            var model = BuildModel(new InputModel());   // AX=AY=AZ=0.01, IX=2e-4, IY=IZ=1e-4 (面積 0.01 の円なら I=7.96e-6)
            var sections = ReadValueSections(Export(model));

            Assert.AreEqual(1, sections.Count, "VALUE 型の断面が書かれていません");
            var (shape, _, props) = sections.Values.Single();
            Assert.AreEqual("SR", shape, "矩形でない断面の代表形状は円");
            CollectionAssert.AreEqual(new[] { 0.01, 0.01, 0.01, 2e-4, 1e-4, 1e-4 }, props,
                "解析モデルの断面性能 (AX, AY, AZ, IX, IY, IZ) がそのまま書かれていません");
        }

        /// <summary>
        /// せん断断面積が断面積の 5/6 の断面 (基礎梁) は、代表形状を矩形とし、幅と高さを逆算すること
        /// (応力を出す点の位置に使う。剛性は値のまま)。
        /// </summary>
        [TestMethod]
        public void RectangularSectionsGetARectangleShape()
        {
            const double b = 0.5, h = 1.0;
            var section = new Section(new PileDesign.FEM.Material(2.5e7, 0.2), b * h, 5.0 / 6.0 * b * h, 5.0 / 6.0 * b * h,
                0.0286, b * h * h * h / 12.0, h * b * b * b / 12.0);
            var shape = MgtExporter.RepresentativeShape.Of(section);

            Assert.AreEqual("SB", shape.Code);
            Assert.AreEqual(h, shape.D1, 1e-12, "高さ");
            Assert.AreEqual(b, shape.D2, 1e-12, "幅");
        }

        /// <summary>
        /// 実際の計算例の解析モデルで、すべての断面が、その断面の値のまま書かれること
        /// (杭の等価断面・基礎梁・剛な連結のどれも、円に置き換えない)。
        /// </summary>
        [TestMethod]
        public void EverySectionOfARealModelIsWrittenWithItsOwnValues()
        {
            var options = new TestProject1.ConvergenceRegression.HeadlessHorizontalRunner.RunOptions
            {
                Level1Steps = 4, Level2Steps = 8, UseLineSearch = true, Parallelism = 1,
                LiquefactionMode = PileDesign.ViewModels.HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
            };
            int checkedSections = 0;
            foreach (var (ground, pile) in new[] { ("Example9", "PileExample9"), ("Example10", "PileExample10") })
            {
                PileDesign.ViewModels.MainWindowViewModel vm;
                try
                {
                    vm = TestProject1.ConvergenceRegression.HeadlessHorizontalRunner.RunExampleForViewModel(ground, pile, options);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
                {
                    continue;
                }

                var anaModel = vm.CurrentModel!;
                var expected = anaModel.Beams.Where(bm => bm.Section?.Material != null).Select(bm => bm.Section)
                    .Distinct(System.Collections.Generic.ReferenceEqualityComparer.Instance).Cast<Section>().ToList();
                var written = ReadValueSections(Export(anaModel)).Values.Select(v => v.Props).ToList();

                Assert.AreEqual(expected.Count, written.Count, $"{ground}: 断面の数が解析モデルと合いません");
                foreach (var s in expected)
                {
                    double[] props = [s.AX, s.AY, s.AZ, s.IX, s.IY, s.IZ];
                    Assert.IsTrue(written.Any(w => w.Zip(props).All(p => Math.Abs(p.First - p.Second) <= 1e-9 * Math.Max(1, Math.Abs(p.Second)))),
                        $"{ground}: 断面 (AX={s.AX}, IY={s.IY}) が、その値のまま書かれていません");
                    checkedSections++;
                }
            }
            if (checkedSections == 0) Assert.Inconclusive("例題ファイルなし");
        }

        /// <summary>
        /// 断面・材料・節点の参照が見つからない梁があると、梁と理由を示して出力を止めること。
        /// 以前はその梁を黙って飛ばし、梁の欠けた MGT を正常に出力していた。
        /// </summary>
        [TestMethod]
        public void BeamsThatCannotBeWrittenStopTheExport()
        {
            var model = BuildModel(new InputModel());
            var nodes = model.Nodes.ToList();
            var section = model.Beams[0].Section;
            var stray = new Node { Name = "X", Coord = new Point3D(9, 9, 9), Boundary = new Boundary(false, false, false, false, false, false) };
            model.Beams.Add(new Beam("B2", section, nodes[0], stray, 1.0, 1.0));      // 節点の一覧に無い節点
            var noSection = new Beam("B3", section, nodes[0], nodes[1], 1.0, 1.0);
            typeof(Beam).GetProperty("Section")!.SetValue(noSection, null);            // 断面が無い
            model.Beams.Add(noSection);

            string file = Path.Combine(Path.GetTempPath(), $"MgtExport_{Guid.NewGuid():N}.mgt");
            try
            {
                var ex = Assert.ThrowsException<InvalidOperationException>(() => new MgtExporter(model).Export(file),
                    "書けない梁があるのに出力しました (梁の欠けた MGT になります)");
                StringAssert.Contains(ex.Message, "梁 2 本");
                StringAssert.Contains(ex.Message, "梁「B2」: 終端の節点が解析モデルの節点にありません");
                StringAssert.Contains(ex.Message, "梁「B3」: 断面がありません");
                Assert.IsFalse(ex.Message.Contains("「B1」", StringComparison.Ordinal), "書ける梁まで挙げています");
                Assert.IsFalse(File.Exists(file), "出力を止めたのに、出力先のファイルが作られています");
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        [TestMethod]
        public void SanitizeDescRemovesControlCharactersAndCommas()
        {
            Assert.AreEqual("A B C D", MgtExporter.SanitizeDesc(" A,\r\nB\t\tC\u2029D "));
            Assert.AreEqual("", MgtExporter.SanitizeDesc(null));
            Assert.AreEqual("地震 X", MgtExporter.SanitizeDesc("地震\u0000X"));
        }
    }
}

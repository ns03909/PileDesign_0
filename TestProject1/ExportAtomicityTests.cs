using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows.Data;

namespace TestProject1
{
    /// <summary>
    /// 書き出し (CSV・DXF・3dm・計算書 docx・MGT) が、一時ファイルに書き切ってから保存先と差し替えること。
    ///
    /// 以前は保存先を直接開いて書いていたので、途中で失敗すると (容量不足・例外)、前に出力した
    /// 正常なファイルが途中までの内容で上書きされて失われた。
    /// </summary>
    [TestClass]
    public class ExportAtomicityTests
    {
        private string _dir = "";

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PileDesignExportTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }

        private string[] FilesInDir() => Array.ConvertAll(Directory.GetFiles(_dir), Path.GetFileName)!;

        /// <summary>パスで書かせる部品が途中で失敗しても、保存先は前の内容のままで、一時ファイルも残らないこと。</summary>
        [TestMethod]
        public void AFailureWhileWritingByPathLeavesThePreviousFile()
        {
            string path = Path.Combine(_dir, "out.dxf");
            File.WriteAllText(path, "前回の出力");

            Assert.ThrowsException<IOException>(() => FileOperationService.ReplaceAtomically(path, tempPath =>
            {
                File.WriteAllText(tempPath, "書きかけ");        // 途中まで書いてから
                throw new IOException("容量不足 (試験)");       // 失敗する
            }));

            Assert.AreEqual("前回の出力", File.ReadAllText(path), "書き出しに失敗したのに、前のファイルが書き換わっています");
            CollectionAssert.AreEqual(new[] { "out.dxf" }, FilesInDir(), "一時ファイルが残っています");
        }

        [TestMethod]
        public void CsvIsWrittenWithTheSameBytesAndReplacesThePreviousFile()
        {
            string path = Path.Combine(_dir, "table.csv");
            File.WriteAllText(path, "前回の出力");

            var error = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var grid = new DataGrid { AutoGenerateColumns = false };
                grid.Columns.Add(new DataGridTextColumn { Header = "名前", Binding = new Binding("Name") });
                var rows = new List<object> { new Row("杭1"), new Row("杭2") };
                grid.ItemsSource = rows;
                DataGridCsv.CreateCsv(rows, grid, path);
            }, out bool timedOut);
            Assert.IsFalse(timedOut);
            if (error != null) throw error;

            byte[] bytes = File.ReadAllBytes(path);
            CollectionAssert.AreEqual(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray(),
                "CSV の先頭の BOM が無くなっています (Excel が文字コードを判別できない)");
            string text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            StringAssert.Contains(text, "名前", "見出しの行がありません");
            StringAssert.Contains(text, "杭2");
            CollectionAssert.AreEqual(new[] { "table.csv" }, FilesInDir(), "一時ファイルが残っています");
        }

        public sealed record Row(string Name);

        [TestMethod]
        public void DxfExportsReplaceThePreviousFile()
        {
            foreach (var (name, export) in new (string, Action<string>)[]
                     {
                         ("model.dxf", p => new DxfExporter(new InputModel()).Export(p)),
                         ("plan.dxf", p => new DxfPlanExporter(new InputModel()).Export(p)),
                     })
            {
                string path = Path.Combine(_dir, name);
                File.WriteAllText(path, "前回の出力");
                export(path);
                string text = File.ReadAllText(path);
                Assert.AreNotEqual("前回の出力", text, $"{name}: 出力で差し替わっていません");
                StringAssert.Contains(text, "SECTION", $"{name}: DXF として書かれていません");
            }
            CollectionAssert.AreEquivalent(new[] { "model.dxf", "plan.dxf" }, FilesInDir(), "一時ファイルが残っています");
        }

        [TestMethod]
        public void Rhino3dmExportReplacesThePreviousFile()
        {
            string path = Path.Combine(_dir, "model.3dm");
            File.WriteAllText(path, "前回の出力");
            try
            {
                new Rhino3dmExporter(new InputModel()).Export(path);
            }
            catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or BadImageFormatException)
            {
                Assert.Inconclusive("Rhino3dm のネイティブ部品を読み込めない環境です: " + ex.Message);
                return;
            }
            Assert.AreNotEqual("前回の出力", File.ReadAllText(path), "出力で差し替わっていません");
            CollectionAssert.AreEqual(new[] { "model.3dm" }, FilesInDir(), "一時ファイルが残っています");
        }

        /// <summary>
        /// 書き出しのどれも、保存先を直接開いて書いていないこと (一時ファイルを経由する)。
        /// 新しい書き出しを足したときの取りこぼしを見張る。
        /// </summary>
        [TestMethod]
        public void NoExporterWritesStraightToTheDestination()
        {
            string[] direct =
            [
                "new DxfWriter(filePath",
                "file.Write(filePath",
                "File.WriteAllText(filePath",
                "File.WriteAllBytes(filePath",
                "new StreamWriter(filePath",
                "WordprocessingDocument.Create(fileName",
            ];
            var offenders = new List<string>();
            int scanned = 0;
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1", "Output"), "*.cs"))
            {
                scanned++;
                string text = File.ReadAllText(file);
                foreach (var pattern in direct)
                    if (text.Contains(pattern, StringComparison.Ordinal))
                        offenders.Add($"{Path.GetFileName(file)}: {pattern}");
            }
            TestSource.AssertScanned(scanned, 20, "書き出しのソース");
            Assert.AreEqual(0, offenders.Count,
                "保存先を直接開いて書いている書き出しがあります (失敗すると前のファイルが壊れます)。"
                + "FileOperationService.WriteAtomically / ReplaceAtomically を通してください:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }
    }
}

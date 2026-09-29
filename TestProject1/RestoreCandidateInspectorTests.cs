using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1;

/// <summary>
/// 自動保存の復元候補を、開く前に調べ終えること (<see cref="RestoreCandidateInspector"/>)。
///
/// 以前は「復元しますか？」と尋ねてから読み込み、壊れていればそこで初めて失敗を知らせていた。
/// 何が戻るのか (入力だけか・結果もあるか・元のファイルへ保存できるか) も尋ねる時点では分からなかった。
/// </summary>
[TestClass]
public class RestoreCandidateInspectorTests
{
    private static FileOperationService Files() => new(new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve });

    private static string TempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"pd_restore_{Guid.NewGuid():N}.pdj");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary><b>本題。</b> 壊れた候補は、尋ねる前に理由付きで「復元できない」と分かる。</summary>
    [TestMethod]
    public void ABrokenFile_IsFoundBeforeAsking()
    {
        string path = TempFile("{ \"FormatVersion\": 2, \"InputModel\": ");
        try
        {
            var result = RestoreCandidateInspector.Inspect(Files(), path);
            Assert.IsFalse(result.CanRestore);
            StringAssert.Contains(result.Problem, "JSON");
        }
        finally { File.Delete(path); }
    }

    /// <summary>新しい版で保存されたファイルも、尋ねる前に理由が分かる。</summary>
    [TestMethod]
    public void ANewerFormat_IsFoundBeforeAsking()
    {
        string path = TempFile("{ \"FormatVersion\": 99 }");
        try
        {
            var result = RestoreCandidateInspector.Inspect(Files(), path);
            Assert.IsFalse(result.CanRestore);
            StringAssert.Contains(result.Problem, "新しいバージョン");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void NoInput_CannotBeRestored()
    {
        var result = RestoreCandidateInspector.Describe(new ProjectData { FormatVersion = 2 });
        Assert.IsFalse(result.CanRestore);
        StringAssert.Contains(result.Problem, "入力データ");
    }

    /// <summary>読める候補は、戻る中身と注意 (壊れた参照・元のファイルの有無・古い形式) を添える。</summary>
    [TestMethod]
    public void AReadableFile_DescribesContentsAndCautions()
    {
        var input = new InputModel
        {
            GroundsInput = [new GroundInput()],
            PileBodies = [],
            PileLayoutItems = [new PileLayoutDataItem { PileNo = 1, No = 1, GroundNo = 1, PileBodyNo = 1 },
                               new PileLayoutDataItem { PileNo = 2, No = 2, GroundNo = 3, PileBodyNo = 1 }],
        };
        var result = RestoreCandidateInspector.Describe(new ProjectData
        {
            FormatVersion = 1, InputModel = input, SourceFilePath = @"C:\どこにも無い\作業.pdj",
        });

        Assert.IsTrue(result.CanRestore);
        StringAssert.Contains(result.Contents[0], "杭 2 本");
        StringAssert.Contains(string.Join("\n", result.Contents), "解析結果は含まれていません");
        string cautions = string.Join("\n", result.Cautions);
        StringAssert.Contains(cautions, "杭No.2 が存在しない地盤");
        StringAssert.Contains(cautions, "杭No.1, 2 が存在しない杭体");
        StringAssert.Contains(cautions, "古い形式");
        StringAssert.Contains(cautions, "元のファイルが見つからない");

        string question = MainWindowViewModel.DescribeRestoreQuestion(
            new AutoSaveService.RestoreCandidate(@"C:\auto\作業_autosave_1.pdj", DateTime.Now, null), result);
        StringAssert.Contains(question, "復元できる内容");
        StringAssert.Contains(question, "杭No.2 が存在しない地盤");
    }

    [TestMethod]
    public void SkippedCandidates_AreListedWithTheirReasons()
    {
        string text = RestoreCandidateInspector.DescribeSkipped([(@"C:\auto\a_autosave_1.pdj", "JSON 形式が不正です")]);
        StringAssert.Contains(text, "a_autosave_1.pdj: JSON 形式が不正です");
    }
}

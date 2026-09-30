using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1;

/// <summary>
/// 杭体・地盤の画面で並びを変えたあとの番号の付け直し (<see cref="EditRenumbering"/>)。
/// 付け直すのは OK で閉じたときの 1 回だけで、先に全部の新しい番号を求めてから書き込む。
/// </summary>
[TestClass]
public class EditRenumberingTests
{
    [TestMethod]
    public void NewNumbers_FollowTheOriginalNumbers()
    {
        // 開いたときの 1・2・3 のうち 2 を消し、末尾に足した (0)
        var map = EditRenumbering.NewNumberByOrigin([1, 3, 0]);
        Assert.AreEqual(1, map[1]);
        Assert.AreEqual(2, map[3]);
        Assert.IsFalse(map.ContainsKey(2));
        Assert.IsFalse(map.ContainsKey(0));
    }

    /// <summary>付け直せない参照は書き換えずに説明を返し、付け直せる参照だけを書き換える。</summary>
    [TestMethod]
    public void Apply_ChangesOnlyResolvableReferences()
    {
        int a = 3, b = 2;
        var unresolved = EditRenumbering.Apply([
            new EditRenumbering.Reference("杭 No.1", () => a, n => a = n),
            new EditRenumbering.Reference("杭 No.2", () => b, n => b = n),
        ], EditRenumbering.NewNumberByOrigin([1, 3]), "杭体");
        Assert.AreEqual(2, a);
        Assert.AreEqual(2, b, "付け直せない参照を書き換えています");
        StringAssert.Contains(unresolved.Single(), "杭 No.2 (杭体番号 2 の杭体がありません)");
    }

    /// <summary>
    /// <b>本題。</b> 地盤を消した画面を OK で閉じると、杭配置と根入部の地盤番号が付け直される (3 → 2)。
    /// 以前は地盤を消した時点で杭配置だけを下げ、根入部は下げていなかった。
    /// </summary>
    [TestMethod]
    public void GroundReferences_AreRenumberedForPilesAndEmbedment()
    {
        var input = new InputModel
        {
            PileLayoutItems = [
                new PileLayoutDataItem { No = 1, GroundNo = 1 },
                new PileLayoutDataItem { No = 2, GroundNo = 3 },
            ],
            EmbedmentInput = new EmbedmentInput(),
        };
        input.EmbedmentInput.GroundNo = 3;
        input.EmbedmentInput.EmbedmentLayers.Add(new EmbedmentDataItem());
        input.EmbedmentInput.EmbedmentLayersCount = 1;

        var unresolved = GroundLayerViewModel.RenumberGroundReferences(input,
            [new GroundInput { NoAtEditStart = 1 }, new GroundInput { NoAtEditStart = 3 }]);
        Assert.AreEqual(0, unresolved.Count);
        CollectionAssert.AreEqual(new[] { 1, 2 }, input.PileLayoutItems.Select(p => p.GroundNo).ToArray());
        Assert.AreEqual(2, input.EmbedmentInput.GroundNo, "根入部の地盤番号を付け直していません");
    }

    /// <summary>地盤を消した時点では杭配置を書き換えない (キャンセル・× で閉じる・元に戻すで杭が別の地盤を指した)。開いたときの番号は複製でも残る。</summary>
    [TestMethod]
    public void DeletingAGround_DoesNotTouchReferencesUntilOk()
    {
        string source = TestSource.Read("Graphics_r1", "ViewModels", "GroundLayerViewModel.cs");
        string delete = TestSource.MethodBody(source, "public void GroundDelete()");
        Assert.IsFalse(Regex.IsMatch(delete, @"\w+\.GroundNo\s*(-=|\+=|=(?!=))"), "地盤を消した時点で杭配置・根入部の番号を書き換えています");
        StringAssert.Contains(delete, "embedment.GroundNo == originNo", "根入部が使う地盤を消せてしまいます");
        StringAssert.Contains(TestSource.MethodBody(source, "private void OnOk()"), "RenumberGroundReferences(");
        Assert.AreEqual(5, new GroundInput { NoAtEditStart = 5 }.DeepCopy().NoAtEditStart);
    }
}

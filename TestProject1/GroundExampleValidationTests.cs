using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.IO;

namespace TestProject1;

/// <summary>
/// 地盤の例題は、地盤に当てる前に中身を確かめる。当てる処理は地層・地盤質量を丸ごと差し替えるので、
/// 途中で止まったり、おかしな値のまま当てたりすると、画面の地盤が半端に書き換わる。
/// </summary>
[TestClass]
public class GroundExampleValidationTests
{
    private static GroundExampleData Valid() => new()
    {
        GroundTopAltitude = 0,
        GroundWaterGLDepth = -2,
        GroundAcceleration1 = 2,
        GroundLayers =
        [
            new GroundLayerDto { BottomGLDepth = -2, LayerThickness = 2, Density = 17, Vs = 170, NValue = 8 },
            new GroundLayerDto { BottomGLDepth = -10, LayerThickness = 8, Density = 18, Vs = 200, NValue = 20 },
        ],
        GroundMassesData = [new GroundMassDataDto { GLDepth = -1, NValue = 8 }],
    };

    [TestMethod]
    public void AValidExample_HasNoProblems()
        => CollectionAssert.AreEqual(System.Array.Empty<string>(), GroundExampleLoader.Validate(Valid()).ToArray());

    /// <summary>同梱の地盤の例題はすべて点検を通る (点検が厳しすぎて、例題が読めなくなっていない)。</summary>
    [TestMethod]
    public void EveryBundledGroundExample_Passes()
    {
        var dir = TestSource.ExamplesDir();
        int scanned = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "Example*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            scanned++;
            // 点検に通らなければ LoadFromFile が理由を添えて止める
            var data = GroundExampleLoader.LoadFromFile(name);
            Assert.IsTrue(data.GroundLayers.Count > 0, name);
        }
        TestSource.AssertScanned(scanned, 20, "地盤の例題");
    }

    [TestMethod]
    public void NoLayers_IsRejected()
    {
        var data = Valid();
        data.GroundLayers.Clear();
        StringAssert.Contains(string.Join("\n", GroundExampleLoader.Validate(data)), "地層が 1 層もありません");
        data.GroundLayers = null!;
        StringAssert.Contains(string.Join("\n", GroundExampleLoader.Validate(data)), "地層が 1 層もありません");
    }

    [TestMethod]
    public void NonFiniteValues_AreRejected()
    {
        var data = Valid();
        data.GroundMassesData[0].VS0 = double.NaN;
        data.GroundLayers[1].NValue = double.PositiveInfinity;
        data.GroundTopAltitude = double.NaN;
        var problems = string.Join("\n", GroundExampleLoader.Validate(data));
        StringAssert.Contains(problems, "地盤質量の点 1 のせん断波速度");
        StringAssert.Contains(problems, "地層 2 の N 値");
        StringAssert.Contains(problems, "地盤天端の標高");
    }

    /// <summary>地層の下端は浅い方から深い方へ並ぶ。崩れていると、地盤質量の点が別の地層の値で上書きされる。</summary>
    [TestMethod]
    public void LayersOutOfDepthOrder_AreRejected()
    {
        var data = Valid();
        (data.GroundLayers[0], data.GroundLayers[1]) = (data.GroundLayers[1], data.GroundLayers[0]);
        StringAssert.Contains(string.Join("\n", GroundExampleLoader.Validate(data)), "地層 2 の下端深さ");
    }

    [TestMethod]
    public void NonPositivePhysicalValues_AreRejected()
    {
        var data = Valid();
        data.GroundLayers[0].Density = 0;
        data.GroundLayers[0].Vs = -1;
        data.GroundLayers[1].LayerThickness = 0;
        var problems = GroundExampleLoader.Validate(data);
        Assert.AreEqual(3, problems.Count, string.Join("\n", problems));
    }

    /// <summary>点検は地盤に当てる前 (読み込みの中) で行う。当ててから確かめても、地盤はもう書き換わっている。</summary>
    [TestMethod]
    public void TheCheckRunsBeforeApplying()
    {
        string src = TestSource.Read("Graphics_r1", "Services", "GroundExampleLoader.cs");
        string load = TestSource.MethodBody(src, "public static GroundExampleData LoadFromFile(");
        StringAssert.Contains(load, "var problems = Validate(data);");
        StringAssert.Contains(load, "地盤は変更していません");
        Assert.IsFalse(TestSource.MethodBody(src, "public static void ApplyToGroundInput(").Contains("Validate("),
            "(前提) 当てる処理は点検済みの中身を受け取る");
    }
}

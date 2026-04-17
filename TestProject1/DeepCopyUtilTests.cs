using PileDesign.Common;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TestProject1
{
    /// <summary>
    /// DeepCopyUtil.CloneJson&lt;T&gt;（JSON による汎用 deep copy）のテスト。
    /// </summary>
    [TestClass]
    public class DeepCopyUtilTests
    {
        private sealed class PlainDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public List<int> Values { get; set; } = new();
            public NestedDto? Nested { get; set; }
        }

        private sealed class NestedDto
        {
            public double X { get; set; }
            public double Y { get; set; }
        }

        [TestMethod]
        public void CloneJson_Null_ReturnsNull()
        {
            PlainDto? source = null;
            var copy = DeepCopyUtil.CloneJson(source);
            Assert.IsNull(copy);
        }

        [TestMethod]
        public void CloneJson_SimpleObject_ValuesPreserved()
        {
            var src = new PlainDto { Id = 42, Name = "hello" };
            var copy = DeepCopyUtil.CloneJson(src);
            Assert.IsNotNull(copy);
            Assert.AreEqual(42, copy!.Id);
            Assert.AreEqual("hello", copy.Name);
        }

        [TestMethod]
        public void CloneJson_ProducesIndependentInstance()
        {
            var src = new PlainDto { Id = 1, Name = "a", Nested = new NestedDto { X = 1, Y = 2 } };
            var copy = DeepCopyUtil.CloneJson(src)!;
            Assert.AreNotSame(src, copy);
            Assert.AreNotSame(src.Nested, copy.Nested);
        }

        [TestMethod]
        public void CloneJson_MutatingCopy_DoesNotAffectSource()
        {
            var src = new PlainDto { Id = 1, Values = new List<int> { 10, 20, 30 } };
            var copy = DeepCopyUtil.CloneJson(src)!;
            copy.Values.Add(999);
            copy.Id = 9999;
            Assert.AreEqual(1, src.Id);
            CollectionAssert.AreEqual(new List<int> { 10, 20, 30 }, src.Values);
        }

        [TestMethod]
        public void CloneJson_NaNAndInfinity_AreAllowed()
        {
            var src = new NestedDto { X = double.NaN, Y = double.PositiveInfinity };
            var copy = DeepCopyUtil.CloneJson(src)!;
            Assert.IsTrue(double.IsNaN(copy.X));
            Assert.IsTrue(double.IsPositiveInfinity(copy.Y));
        }

        [TestMethod]
        public void CloneJsonOrThrow_NullInput_Throws()
        {
            PlainDto? source = null;
            Assert.ThrowsException<System.ArgumentNullException>(() =>
                DeepCopyUtil.CloneJsonOrThrow(source!));
        }

        [TestMethod]
        public void CloneJsonOrThrow_ValidInput_ReturnsIndependentInstance()
        {
            var src = new PlainDto { Id = 7 };
            var copy = DeepCopyUtil.CloneJsonOrThrow(src);
            Assert.AreNotSame(src, copy);
            Assert.AreEqual(7, copy.Id);
        }
    }

    /// <summary>
    /// DeepCopy の独立性テスト（実データ型）。MemberwiseClone の浅い共有バグが
    /// JSON 移行で解消されていることを検証する。
    /// </summary>
    [TestClass]
    public class DeepCopyIndependenceTests
    {
        [TestMethod]
        public void DoatsuGoryokuBane_DeepCopy_ItemsAreNotShared()
        {
            // 以前の MemberwiseClone 実装では Items が共有参照になっていた。
            // JSON 移行後は独立コレクションを持つこと。
            var source = new DoatsuGoryokuBane
            {
                DeltaP = 1.5,
                Ysp = 0.15,
                Items = new ObservableCollection<DoatsuGoryokuBaneItem>
                {
                    new() { ZTop = 0.0, ZBtm = -1.0 },
                    new() { ZTop = -1.0, ZBtm = -2.0 }
                }
            };

            var copy = source.DeepCopy();

            Assert.AreNotSame(source, copy);
            Assert.AreNotSame(source.Items, copy.Items);
            Assert.AreEqual(source.Items.Count, copy.Items.Count);

            // コピー側を変更しても原本は変わらない
            copy.Items.Add(new DoatsuGoryokuBaneItem { ZTop = -2.0, ZBtm = -3.0 });
            Assert.AreEqual(2, source.Items.Count);
            Assert.AreEqual(3, copy.Items.Count);

            // 個々の Item も別インスタンスである
            Assert.AreNotSame(source.Items[0], copy.Items[0]);
        }

        [TestMethod]
        public void DoatsuGoryokuBane_DeepCopy_ScalarFieldsCopied()
        {
            var source = new DoatsuGoryokuBane { DeltaP = 2.5, Ysp = 0.25 };
            var copy = source.DeepCopy();
            Assert.AreEqual(2.5, copy.DeltaP, 1e-12);
            Assert.AreEqual(0.25, copy.Ysp, 1e-12);
        }

        [TestMethod]
        public void DoatsuGoryokuBane_DeepCopy_ItemsMutationIndependent()
        {
            var source = new DoatsuGoryokuBane
            {
                Items = new ObservableCollection<DoatsuGoryokuBaneItem>
                {
                    new() { ZTop = 0.0, ZBtm = -1.0 }
                }
            };

            var copy = source.DeepCopy();
            copy.Items[0].ZTop = 999.0;
            Assert.AreEqual(0.0, source.Items[0].ZTop, 1e-12);
        }

        [TestMethod]
        public void InputModel_DeepCopy_ReturnsIndependentInstance()
        {
            // InputModel.DeepCopy も DeepCopyUtil.CloneJson 経由に移行済み。
            // 最低限の smoke test として、空の InputModel が deep copy できることを確認する。
            var source = new InputModel();
            var copy = source.DeepCopy();
            Assert.IsNotNull(copy);
            Assert.AreNotSame(source, copy);
        }
    }
}

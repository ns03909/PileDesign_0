using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.InputData;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 入力データの複製が、変更通知の購読者 (元を見ている画面) を写さないこと (2026-09-26)。
    ///
    /// MemberwiseClone は field-like event のデリゲートも写す。解析結果・Undo・キャンセル用の控えとして複製した
    /// ものを変えると、元を見ている画面へ通知が飛んだ。荷重組合せで指摘され、同じ形の複製が 23 か所あった。
    /// </summary>
    [TestClass]
    public class CloneWithoutSubscribersTests
    {
        [TestMethod]
        public void ABaseModelCopyDoesNotNotifyTheOriginalsSubscribers()
        {
            var original = new LoadCase { Level = 1, No = 1, IsApplicable = true };
            int raised = 0;
            original.PropertyChanged += (_, _) => raised++;

            var copy = original.DeepCopy();
            copy.IsApplicable = false;

            Assert.AreEqual(0, raised, "荷重ケースの複製を変えると、元の購読者へ通知が飛びます");
            Assert.IsTrue(original.IsApplicable);
        }

        [TestMethod]
        public void ABaseDataItemCopyDoesNotNotifyTheOriginalsSubscribers()
        {
            var original = new GroundMassDataInput { GLDepth = 1.0 };
            int raised = 0;
            original.PropertyChanged += (_, _) => raised++;

            var copy = original.DeepCopy();
            copy.GLDepth = 2.0;

            Assert.AreEqual(0, raised, "土質データの複製を変えると、元の購読者へ通知が飛びます");
            Assert.AreEqual(1.0, original.GLDepth);
        }

        /// <summary>基本設定・プロジェクト情報のキャンセルは複製を書き戻すので、ここが残ると元の画面へ通知が重なる。</summary>
        [TestMethod]
        public void AnObservableModelCopyDoesNotNotifyTheOriginalsSubscribers()
        {
            Assert.IsNotNull(ObservableModel.PropertyChangedField,
                "ツールキットの変更通知のフィールド名が変わりました。ObservableModel.CloneWithoutSubscribers を直してください");
            Assert.IsNotNull(ObservableModel.PropertyChangingField);

            var original = new FundamentalInput { SeismicGrade = "A" };
            int raised = 0;
            original.PropertyChanged += (_, _) => raised++;

            var copy = original.ShallowCopy();
            copy.SeismicGrade = "S";

            Assert.AreEqual(0, raised, "基本設定の複製を変えると、元の購読者へ通知が飛びます");
            Assert.AreEqual("A", original.SeismicGrade);
        }

        /// <summary>入力データで MemberwiseClone を直に使わない (複製は CloneWithoutSubscribers を通す)。</summary>
        [TestMethod]
        public void NoModelClonesWithMemberwiseCloneDirectly()
        {
            string modelsDir = TestSource.Dir("Graphics_r1", "Models");
            var files = Directory.GetFiles(modelsDir, "*.cs", SearchOption.AllDirectories);
            TestSource.AssertScanned(files.Length, 50, "Models のソース");
            var offenders = files
                .Where(f => Path.GetFileName(f) != "BaseModel.cs")
                .Where(f => File.ReadAllLines(f).Any(l => !l.TrimStart().StartsWith("//") && Regex.IsMatch(l, @"\bMemberwiseClone\(\)")))
                .Select(Path.GetFileName)
                .ToList();
            Assert.AreEqual(0, offenders.Count,
                "MemberwiseClone を直に使っています (購読者まで写す。CloneWithoutSubscribers を使う): " + string.Join(", ", offenders));
        }
    }
}

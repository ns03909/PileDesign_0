using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.InputData;
using System;
using System.Security;

namespace TestProject1
{
    /// <summary>
    /// 入力ファイルから復元してよい型を、このアプリの型に限ること。
    ///
    /// ファイル読込のフォールバックは <c>TypeNameHandling.Auto</c> で Newtonsoft を使う。
    /// この設定は JSON の <c>$type</c> に書かれた任意の型を復元しようとするため、
    /// 細工したファイルで意図しない型を作らせることができてしまう
    /// (デシリアライズのガジェット攻撃)。
    ///
    /// 保存は System.Text.Json が行い <c>$type</c> を書かないので、
    /// 正規のファイルにこの指定は現れない。許すのはこのアセンブリの型だけでよい。
    /// </summary>
    [TestClass]
    public class TrustedTypeBinderTests
    {
        [TestMethod]
        public void OwnTypes_AreAllowed()
        {
            var t = TrustedTypeBinder.Instance.BindToType(
                assemblyName: null, typeName: typeof(PileLayoutDataItem).FullName!);

            Assert.AreEqual(typeof(PileLayoutDataItem), t);
        }

        /// <summary>
        /// 他のアセンブリの型は拒む。
        /// ここで通してしまうと、ファイルを開いただけで任意の型が作られる。
        /// </summary>
        [TestMethod]
        public void ForeignTypes_AreRejected()
        {
            foreach (string typeName in new[]
            {
                "System.Diagnostics.Process",
                "System.IO.FileInfo",
                "System.Windows.Data.ObjectDataProvider",
            })
            {
                Assert.ThrowsException<SecurityException>(
                    () => TrustedTypeBinder.Instance.BindToType(null, typeName),
                    $"{typeName} が通ってしまっている");
            }
        }

        /// <summary>
        /// 拒むときの文面に、内部の型名以外の余計な情報を混ぜないこと。
        /// (利用者が見るのはファイルが開けないという事実と、その型名まで)
        /// </summary>
        [TestMethod]
        public void RejectionMessage_NamesTheOffendingType()
        {
            var ex = Assert.ThrowsException<SecurityException>(
                () => TrustedTypeBinder.Instance.BindToType(null, "System.Diagnostics.Process"));

            StringAssert.Contains(ex.Message, "System.Diagnostics.Process");
        }

        /// <summary>
        /// 読込のフォールバック 2 経路がどちらもこの制限を通していること。
        /// 片方だけ塞いでも意味がない。
        /// </summary>
        [TestMethod]
        public void BothFallbackPathsUseTheBinder()
        {
            string source = System.IO.File.ReadAllText(FindInputModelSource());

            int autoCount = System.Text.RegularExpressions.Regex.Matches(
                source, @"TypeNameHandling\s*=\s*TypeNameHandling\.Auto").Count;
            int binderCount = System.Text.RegularExpressions.Regex.Matches(
                source, @"SerializationBinder\s*=\s*Common\.TrustedTypeBinder\.Instance").Count;

            Assert.AreNotEqual(0, autoCount, "前提が崩れている (TypeNameHandling.Auto が無い)");
            Assert.AreEqual(autoCount, binderCount,
                $"TypeNameHandling.Auto が {autoCount} 箇所あるのに、型の制限は {binderCount} 箇所しかない");
        }

        private static string FindInputModelSource()
        {
            var dir = new System.IO.DirectoryInfo(
                System.IO.Path.GetDirectoryName(typeof(TrustedTypeBinderTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                string candidate = System.IO.Path.Combine(
                    dir.FullName, "Graphics_r1", "Models", "InputData", "InputModel.cs");
                if (System.IO.File.Exists(candidate)) return candidate;
            }
            throw new System.IO.FileNotFoundException("InputModel.cs が見つかりません");
        }
    }
}

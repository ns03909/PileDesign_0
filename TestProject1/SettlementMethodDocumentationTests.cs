using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプが、単杭沈下の<b>実際に動いている</b>解き方を説明していること。
    ///
    /// <para>ヘルプの「荷重伝達法」節は長いあいだ「杭頭変位を段階的に増分させる
    /// 変位制御解析で算定します」と説明していたが、実際に走っていたのは荷重増分
    /// (荷重制御) だった。変位制御法の実装は画面から一度も選べないまま残っており、
    /// 2026-09-11 に削除した。</para>
    ///
    /// <para>解き方を変えたら、ヘルプも同じ変更で直すこと。ここは「実装に変位制御が
    /// あるか」と「ヘルプが変位制御を説明しているか」が<b>一致していること</b>を見る。</para>
    /// </summary>
    [TestClass]
    public class SettlementMethodDocumentationTests
    {
        [TestMethod]
        public void TheHelp_DescribesTheMethodThatActuallyRuns()
        {
            string code = TestSource.Read("Graphics_r1", "FEM", "VerticalLoadTransferMethod.cs");
            Assert.IsTrue(code.Contains("RunLoadIncrementAnalysis(", StringComparison.Ordinal),
                "荷重増分の解析が見つかりません (名前が変わった?)");

            bool codeHasDisplacementControl = Regex.IsMatch(
                Regex.Replace(code, "//.*", ""),
                @"\bDisplacementControl\b|RunDisplacementIncrementAnalysis");

            string help = TestSource.Read("Graphics_r1", "Help", "help.html");
            int at = help.IndexOf("id=\"h-荷重伝達法\"", StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, "ヘルプに「荷重伝達法」の節がありません");
            int end = help.IndexOf("<h3", at + 10, StringComparison.Ordinal);
            string section = help[at..(end < 0 ? help.Length : end)];

            TestSource.AssertScanned(section.Length, 1000, "ヘルプの「荷重伝達法」節");

            bool helpSaysDisplacementControl = section.Contains("変位制御", StringComparison.Ordinal);

            Assert.AreEqual(codeHasDisplacementControl, helpSaysDisplacementControl,
                codeHasDisplacementControl
                    ? "実装に変位制御法があるのに、ヘルプの「荷重伝達法」節が触れていません"
                    : "ヘルプの「荷重伝達法」節が、実装に無い変位制御解析を説明しています");

            if (!codeHasDisplacementControl)
            {
                StringAssert.Contains(section, "荷重増分",
                    "ヘルプの「荷重伝達法」節が、実際の解き方 (荷重増分) を説明していません");
            }
        }
    }
}

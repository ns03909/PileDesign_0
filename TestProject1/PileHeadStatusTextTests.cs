using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;

namespace TestProject1
{
    /// <summary>
    /// 解析結果テーブルの「状態」列に、内部のセットアップ経路がそのまま出ないことの検証。
    ///
    /// 以前は CombinedXY(12pts, Mcr=715, axialN=2465kN) や
    /// Rigid(def.Mode=Rigid, PileTop='...') のような実装都合の文字列が表に出ていた。
    /// 読んでも次の操作が決まらないので、利用者向けの短い表記に直す。
    /// </summary>
    [TestClass]
    public class PileHeadStatusTextTests
    {
        private static string Convert(string reason, bool isFallback)
        {
            var m = typeof(AnalysisResultTableService).GetMethod(
                "ToPileHeadStatusText", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "ToPileHeadStatusText が見つからない");
            return (string)m!.Invoke(null, [reason, isFallback])!;
        }

        [TestMethod]
        public void InternalSetupReason_IsNotShownRaw()
        {
            foreach (var reason in new[]
            {
                "CombinedXY(12pts, Mcr=715, axialN=2465kN)",
                "Rigid(def.Mode=Rigid, PileTop='鉄筋定着工法', PileBody='場所打ち鉄筋コンクリート杭', axialN=2465kN)",
                "Rigid(IsPileNonLinear=false, axialN=2465kN)",
            })
            {
                string shown = Convert(reason, false);
                foreach (var leak in new[] { "Mcr=", "axialN=", "def.Mode", "PileTop=", "pts", "IsPileNonLinear", "(" })
                    Assert.IsFalse(shown.Contains(leak),
                        $"内部表記が表に出ている: \"{shown}\" に \"{leak}\" が含まれる");
            }
        }

        [TestMethod]
        public void KnownStates_AreMappedToReadableLabels()
        {
            Assert.AreEqual("M-θ曲線", Convert("CombinedXY(12pts, Mcr=715, axialN=2465kN)", false));
            Assert.AreEqual("剛", Convert("Rigid(def.Mode=Rigid, PileTop='x', PileBody='y', axialN=1kN)", false));
            Assert.AreEqual("線形ばね", Convert("CombinedXY(...)", true));
        }

        [TestMethod]
        public void UnknownOrEmpty_ShowsNothing()
        {
            Assert.AreEqual("", Convert("", false));
            Assert.AreEqual("", Convert("?", false));
            Assert.AreEqual("", Convert("SomeNewInternalMode(x=1)", false),
                "未知の内部表記をそのまま出している");
        }
    }
}

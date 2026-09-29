using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.Results;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TestProject1;

/// <summary>
/// 結果の表の数値の列は、見出しに単位を書くこと (「Mxi(kNm)」「θ(rad)」のように括弧で)。
///
/// 単位の無い数値は、m と mm・kN と N の取り違えに気付く手掛かりが無い (計算書の各杭位置の沈下量一覧では、
/// 単杭沈下量を m のまま [mm] の列に出していた)。単位の無い量は「(-)」と書くか、名前を「〜比」「〜係数」「〜率」「〜割増」にする。
/// </summary>
[TestClass]
public class ResultColumnUnitTests
{
    private static bool IsNumeric(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t == typeof(double) || t == typeof(float);
    }

    /// <summary>
    /// 見出しの括弧書き (半角・全角) に単位がある。または名前そのものが単位の無い量を表す (検定比・割増・係数・率)。
    /// </summary>
    private static bool HasUnit(string header)
        => System.Text.RegularExpressions.Regex.IsMatch(header, @"[\(（\[][^\)）\]]+[\)）\]]")
           || System.Text.RegularExpressions.Regex.IsMatch(header, "比$|割増$|係数$|率$");

    [TestMethod]
    public void EveryNumericResultColumn_StatesItsUnit()
    {
        var missing = new List<string>();
        int total = 0;
        foreach (var type in typeof(ResultColumnAttribute).Assembly.GetTypes()
                     .Where(t => t.Namespace == "PileDesign.Models.Results"))
        {
            foreach (var prop in type.GetProperties())
            {
                var attr = prop.GetCustomAttribute<ResultColumnAttribute>();
                if (attr == null || !IsNumeric(prop.PropertyType)) continue;
                total++;
                if (!HasUnit(attr.Header))
                    missing.Add($"{type.Name}.{prop.Name} (見出し \"{attr.Header}\")");
            }
        }

        TestSource.AssertScanned(total, 50, "結果の表の数値の列");
        Assert.AreEqual(0, missing.Count,
            "見出しに単位の無い数値の列があります。単位を括弧で書いてください (単位の無い量は「(-)」):\n  "
            + string.Join("\n  ", missing));
    }
}

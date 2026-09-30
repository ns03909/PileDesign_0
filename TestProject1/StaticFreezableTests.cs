using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace TestProject1
{
    /// <summary>
    /// 静的なフィールドに置いたブラシ・ペンなど (Freezable) は凍結しておく。
    ///
    /// <para>凍結していないブラシは<b>最初に作ったスレッドのもの</b>になり、別のスレッドで描くと例外になる。
    /// 計算書の杭姿図は、別のスレッドで先にブラシが作られていると作れずに省かれた (全体テストの並び順しだいで、
    /// 代表の計算書の杭姿図 4 枚が「作成できませんでした」になった)。凍結すればどのスレッドからも使える。</para>
    /// </summary>
    [TestClass]
    public class StaticFreezableTests
    {
        [TestMethod]
        public void StaticFreezables_AreFrozen()
        {
            var unfrozen = new List<string>();
            int scanned = 0;
            var error = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var assembly = typeof(PileDesign.Common.NikkenBrush).Assembly;
                foreach (var type in assembly.GetTypes())
                {
                    if (type.ContainsGenericParameters) continue;
                    foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (field.IsLiteral || !typeof(Freezable).IsAssignableFrom(field.FieldType)) continue;
                        object? value;
                        try { value = field.GetValue(null); }
                        catch (Exception) { continue; }   // 型の初期化に画面が要るものは見ない (数に入れない)
                        if (value is not Freezable freezable) continue;
                        scanned++;
                        if (!freezable.IsFrozen) unfrozen.Add($"{type.FullName}.{field.Name}");
                    }
                }
            }, out bool timedOut);

            Assert.IsFalse(timedOut);
            Assert.IsNull(error, error?.ToString());
            TestSource.AssertScanned(scanned, 30, "静的なブラシ・ペン");
            Assert.AreEqual(0, unfrozen.Count,
                "凍結していない静的なブラシ・ペンがあります。別のスレッドで描くと例外になります "
                + "(NikkenBrush.Frozen で作るか Freeze してください):\n  " + string.Join("\n  ", unfrozen));
        }
    }
}

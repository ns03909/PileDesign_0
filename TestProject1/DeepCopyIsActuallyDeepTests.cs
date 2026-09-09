using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 入力モデルの <c>DeepCopy()</c> が、本当に深いコピーになっていること。
    ///
    /// <c>MemberwiseClone()</c> を返すだけの <c>DeepCopy()</c> は、値型は写しますが
    /// <b>子オブジェクトは元と同じ実体を指したまま</b>です。Undo のスナップショットが
    /// 生きているモデルと中身を共有するので、<b>戻したつもりで戻っていない</b>という
    /// 壊れ方をします。例外にはなりません。
    ///
    /// <c>PileTop.DeepCopy()</c> がこれで、<c>ShallowCopy()</c> と 1 文字も違いません
    /// でした。杭頭接合部 (キャプテン / FT-Pile / キャプリング) の中身は Ctrl+Z で
    /// 戻りませんでした。
    ///
    /// <c>DeepCopyInvariantsTests</c> は FEM の <c>AnaModel</c> 専用で、入力モデル側は
    /// 1 つも見ていませんでした。
    /// </summary>
    [TestClass]
    public class DeepCopyIsActuallyDeepTests
    {
        /// <summary>
        /// 元と同じ実体を指していてよいもの。増やすときは<b>なぜ共有してよいか</b>を書くこと。
        /// </summary>
        private static readonly HashSet<string> SharedOnPurpose = new(StringComparer.Ordinal)
        {
            // 親への逆参照。複製しても意味がなく、複製すると木が二重になる。
            "LoadCase.InputModel",
            "PileCircumVertical.PileBodySegment",
            "PileCircumVertical.SoilPile",
            "EmbedmentZDataItem.GroundInput",
            "PileZDataItem.GroundInput",
            "PileZDataItem.PileBodySegment",
            "GroundMassDataInput.GroundLayer",

            // 元とする土層そのもの。根入部は地盤側の実体を参照する。
            "SoilEmbedment.GroundLayers",

            // 「どの地盤・どの杭体から作ったか」を指す元への参照。
            "SoilPile.GroundInput",
            "SoilPile.PileBodyInput",
        };

        [TestMethod]
        public void EveryDeepCopy_ActuallyCopiesItsChildren()
        {
            var shallow = new List<string>();
            int checkedTypes = 0;
            var skipped = new List<string>();

            var asm = typeof(PileDesign.Models.InputData.InputModel).Assembly;
            foreach (var type in asm.GetTypes().OrderBy(t => t.Name))
            {
                if (!type.IsClass || type.IsAbstract) continue;
                if (type.Namespace == null
                    || !type.Namespace.StartsWith("PileDesign.Models", StringComparison.Ordinal))
                    continue;

                var deepCopy = type.GetMethod("DeepCopy", BindingFlags.Public | BindingFlags.Instance,
                                              binder: null, types: Type.EmptyTypes, modifiers: null);
                if (deepCopy == null || !type.IsAssignableFrom(deepCopy.ReturnType)) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    skipped.Add($"{type.Name} (引数なしで作れない)");
                    continue;
                }

                object original;
                try { original = Activator.CreateInstance(type)!; }
                catch (Exception ex) { skipped.Add($"{type.Name} ({ex.GetType().Name})"); continue; }

                // 子オブジェクトを実際に持たせる。持っていないと共有かどうか分からない。
                var filled = new List<PropertyInfo>();
                foreach (var p in Children(type))
                {
                    if (p.SetMethod == null || !p.SetMethod.IsPublic) continue;
                    try
                    {
                        if (p.GetValue(original) != null) { filled.Add(p); continue; }
                        var child = Activator.CreateInstance(p.PropertyType);
                        if (child == null) continue;
                        p.SetValue(original, child);
                        filled.Add(p);
                    }
                    catch { /* 作れない型は飛ばす */ }
                }
                if (filled.Count == 0) { skipped.Add($"{type.Name} (子を持たせられない)"); continue; }

                object? copy;
                try { copy = deepCopy.Invoke(original, null); }
                catch (Exception ex) { skipped.Add($"{type.Name} ({ex.InnerException?.GetType().Name ?? ex.GetType().Name})"); continue; }
                if (copy == null) { skipped.Add($"{type.Name} (null が返る)"); continue; }

                checkedTypes++;
                foreach (var p in filled)
                {
                    string key = $"{type.Name}.{p.Name}";
                    if (SharedOnPurpose.Contains(key)) continue;
                    object? a = null, b = null;
                    try { a = p.GetValue(original); b = p.GetValue(copy); } catch { continue; }
                    if (a != null && ReferenceEquals(a, b))
                        shallow.Add($"{key} ({p.PropertyType.Name})");
                }
            }

            TestSource.AssertScanned(checkedTypes, 8, "DeepCopy() を持つ入力モデルの型");

            Assert.AreEqual(0, shallow.Count,
                "DeepCopy() が子オブジェクトを元と共有しています。Undo のスナップショットが"
                + "生きているモデルと中身を共有するので、戻したつもりで戻りません。"
                + "共有してよい理由があるなら SharedOnPurpose に理由つきで足してください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", shallow)
                + Environment.NewLine + $"(検査 {checkedTypes} 型 / 飛ばした {skipped.Count} 型)");
        }

        /// <summary>
        /// 複製されるべき「子」。
        ///
        /// <b>その型自身が <c>DeepCopy()</c> を持っているものだけ</b>を対象にする。
        /// 誰かが複製の仕方を書いた型は、複製する意図がある型だからである。
        ///
        /// これで次が自動的に外れる。個別に書き並べる必要がない。
        /// <list type="bullet">
        /// <item>CSV から読んだ製品カタログの行 (PC リング、FT キャップ、定着筋)</item>
        /// <item>ComboBox の選択肢 (文字列の並び)</item>
        /// <item>計算し直した曲線 (数値の並び。差し替えでしか変わらない)</item>
        /// </list>
        /// </summary>
        private static IEnumerable<PropertyInfo> Children(Type type)
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (p.GetMethod == null || !p.GetMethod.IsPublic) continue;
                var t = p.PropertyType;
                if (t.IsValueType || t == typeof(string)) continue;
                if (HasDeepCopy(t) || (ElementOf(t) is Type e && HasDeepCopy(e)))
                    yield return p;
            }
        }

        private static bool HasDeepCopy(Type t)
            => t.GetMethod("DeepCopy", BindingFlags.Public | BindingFlags.Instance,
                           binder: null, types: Type.EmptyTypes, modifiers: null) != null;

        private static Type? ElementOf(Type t)
        {
            if (t == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(t)) return null;
            if (t.IsArray) return t.GetElementType();
            return t.IsGenericType ? t.GetGenericArguments().LastOrDefault() : null;
        }
    }
}

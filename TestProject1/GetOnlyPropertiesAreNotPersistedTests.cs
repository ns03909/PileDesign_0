using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// 保存グラフの <b>get だけのプロパティ</b>には <c>[JsonIgnore]</c> が付いていること。
    ///
    /// セッターが無いプロパティは、書き出せても<b>読み戻せない</b>。だから書き出すのは
    /// 常に無駄で、しかも次の 3 つの害がある。
    ///
    /// <list type="number">
    /// <item><b>不変条件の網を鳴らし続ける。</b> 「保存 → 開く → 保存」で 1 バイトも
    ///   違わないことを見る網は、計算値が入力の違いで変わるだけで落ちる。本当の不具合が
    ///   その騒音に埋もれる (実際に EA・EI・GJ・A0・Ac・W などで埋もれていた)。</item>
    /// <item><b>保存が遅くなる。</b> 直列化は computed の getter を<b>全部</b>呼ぶ。
    ///   断面の耐力曲線のような重い getter が保存のたびに走る
    ///   (保存 16,130ms → 116ms の主因の 1 つがこれだった)。</item>
    /// <item><b>参照の畳まれ方を壊しうる。</b> <c>ReferenceHandler.Preserve</c> では、
    ///   書き出されるのに復元されない<b>参照型</b>のプロパティが <c>$id</c>/<c>$ref</c> の
    ///   対応をずらす。</item>
    /// </list>
    ///
    /// <see cref="PersistedPropertyRestorabilityTests"/> は「セッターがあるのに非 public」を
    /// 見る。こちらは「セッターが無い」を見る。<b>2 つで表裏を覆う。</b>
    /// </summary>
    [TestClass]
    public class GetOnlyPropertiesAreNotPersistedTests
    {
        /// <summary>
        /// 書き出してよい get だけのプロパティ。増やすときは理由を書くこと。
        /// </summary>
        private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
        {
            // いまのところ無し。
        };

        [TestMethod]
        public void EveryGetOnlyProperty_IsExcludedFromSaving()
        {
            var offenders = new List<string>();
            var visited = new HashSet<Type>();

            Walk(typeof(InputModel));

            Assert.IsTrue(visited.Count >= 40,
                $"保存グラフをたどれていません (型 {visited.Count} 個)。"
                + "InputModel から先が見えているか確かめてください");

            Assert.AreEqual(0, offenders.Count,
                $"get だけなのに書き出されるプロパティが {offenders.Count} 件あります。"
                + "読み戻せないので書き出す意味がなく、往復の網を鳴らし続け、保存を遅くします。"
                + "[JsonIgnore] を付けてください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders.Take(200)));

            void Walk(Type? rawType)
            {
                Type? type = Unwrap(rawType);
                if (type == null || !visited.Add(type)) return;

                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance
                                                     | BindingFlags.DeclaredOnly))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (p.GetMethod == null || !p.GetMethod.IsPublic) continue;

                    bool ignored = p.GetCustomAttribute<JsonIgnoreAttribute>() != null
                        || p.GetCustomAttribute<Newtonsoft.Json.JsonIgnoreAttribute>() != null;

                    if (p.SetMethod == null && !ignored
                        && !Allowed.Contains($"{type.Name}.{p.Name}"))
                    {
                        offenders.Add($"{type.Name}.{p.Name} ({Pretty(p.PropertyType)})");
                    }

                    if (!ignored) Walk(p.PropertyType);
                }
            }
        }

        /// <summary>コレクションや Nullable の中身の型を返す。自社型以外は辿らない。</summary>
        private static Type? Unwrap(Type? t)
        {
            if (t == null) return null;
            if (t == typeof(string)) return null;

            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(Nullable<>)) return Unwrap(t.GetGenericArguments()[0]);
                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(t))
                    return Unwrap(t.GetGenericArguments()[0]);
            }
            if (t.IsArray) return Unwrap(t.GetElementType());
            if (t.IsPrimitive || t.IsEnum || t == typeof(decimal)
                || t == typeof(DateTime) || t == typeof(Guid)) return null;

            return (t.Namespace ?? "").StartsWith("PileDesign", StringComparison.Ordinal) ? t : null;
        }

        private static string Pretty(Type t) =>
            t.IsGenericType
                ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(a => a.Name))}>"
                : t.Name;
    }
}

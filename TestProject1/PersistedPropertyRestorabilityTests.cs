using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// 保存ファイルに書き出される型に、<b>書き出されるのに読み戻されない</b>
    /// プロパティが無いこと。
    ///
    /// <c>public double X { get; private set; }</c> は System.Text.Json が
    /// セッターを呼ばないので、書き出しても読込で宣言の既定値に戻ります。
    /// ファイルには正しい値が書いてあるので、ファイルを見ても気づけません。
    /// <c>ReferenceHandler.Preserve</c> と組み合わさると、<c>$ref</c> で
    /// 共有されていた参照が読込で別物になり、参照の共有まで壊れます。
    ///
    /// 実際に 3 件ありました。
    /// <list type="bullet">
    /// <item>FT-Pile の杭径 (φ1000 で保存 → 開くと既定の φ600)</item>
    /// <item>FT-Pile のパイルキャップ Fc (42 で保存 → 開くと既定の 24)</item>
    /// <item>杭断面の軸力制限値 (<c>$ref</c> が空リストに化けていた)</item>
    /// </list>
    ///
    /// どうするかは 2 つに 1 つです。
    /// <list type="bullet">
    /// <item>保存して復元したい値 … <c>[JsonInclude]</c> (非 public なセッターでも呼ばれる)</item>
    /// <item>計算し直せる派生値 … <c>[JsonIgnore]</c> (そもそも書き出さない)</item>
    /// </list>
    ///
    /// <c>SaveLoadFirstCycleTests</c> は例題を実際に往復させて同じことを見ますが、
    /// 例題に出てこない工法 (FT-Pile) は通りません。こちらは型から見るので漏れません。
    /// </summary>
    [TestClass]
    public class PersistedPropertyRestorabilityTests
    {
        /// <summary>
        /// 戻らなくてよいと分かっているもの。増やすときは理由を書くこと。
        /// </summary>
        private static readonly HashSet<string> Exempt = new(StringComparer.Ordinal)
        {
            // 読込時に static カウンタで振り直す runtime-only な番号。
            // 永続識別子は UniqueId (Guid) のほう。戻ってはいけない。
            "InputNode.Id",
        };

        [TestMethod]
        public void EveryPersistedProperty_CanBeReadBack()
        {
            var offenders = new List<string>();
            var visited = new HashSet<Type>();

            Walk(typeof(InputModel));

            Assert.IsTrue(visited.Count >= 40,
                $"保存グラフをたどれていません (型 {visited.Count} 個)。"
                + "InputModel から先が見えているか確かめてください");

            Assert.AreEqual(0, offenders.Count,
                "保存はされるのに読み戻されないプロパティがあります。"
                + "復元したいなら [JsonInclude]、計算し直せる派生値なら [JsonIgnore] を付けてください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));

            void Walk(Type? rawType)
            {
                Type? type = Unwrap(rawType);
                if (type == null || !visited.Add(type)) return;

                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance
                                                     | BindingFlags.DeclaredOnly))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                    if (p.GetMethod == null || !p.GetMethod.IsPublic) continue;

                    // セッターがあるのに非 public、が危ない形。
                    // 「持っている状態」に見えて、書き出されるのに読み戻されない。
                    //
                    // get だけの計算プロパティ (EI, SumV, ICommand など) はここでは見ない。
                    // 復元する状態ではないので、戻らなくても正しく動く
                    // (書き出しの無駄と $ref の扱いは別の話)。
                    if (p.SetMethod != null
                        && !p.SetMethod.IsPublic
                        && p.GetCustomAttribute<JsonIncludeAttribute>() == null
                        && !Exempt.Contains($"{type.Name}.{p.Name}"))
                    {
                        offenders.Add($"{type.Name}.{p.Name} ({p.PropertyType.Name})");
                    }

                    Walk(p.PropertyType);
                }

                if (type.BaseType != null) Walk(type.BaseType);
            }

            static Type? Unwrap(Type? t)
            {
                if (t == null) return null;
                if (t.IsArray) return Unwrap(t.GetElementType());
                if (t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t))
                    return Unwrap(t.GetGenericArguments().LastOrDefault());
                if (Nullable.GetUnderlyingType(t) is Type u) return Unwrap(u);
                // PileDesign のモデルだけを追う。BCL / WPF / 外部ライブラリには入らない
                if (t.Namespace == null || !t.Namespace.StartsWith("PileDesign", StringComparison.Ordinal))
                    return null;
                return t;
            }
        }
    }
}

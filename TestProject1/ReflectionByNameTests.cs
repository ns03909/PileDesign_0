using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 反射で名前を指定して探しているプロパティ・メソッドが、実際に存在すること。
    ///
    /// <c>obj.GetType().GetProperty("Xxx")</c> は、名前を打ち間違えても
    /// <b>ビルドが通り、実行時も例外にならず、ただ null が返る</b>。
    /// 多くの場合その先に「見つからなければ既定値」というフォールバックがあるので、
    /// <b>間違った値で静かに計算が進む</b>。画面にも計算書にも何も出ない。
    ///
    /// 実際にあった例 (2026-09-05 に修正):
    /// <list type="bullet">
    ///   <item>杭頭ウィンドウが鋼管厚を <c>"SteelPipeThickness"</c> / <c>"PipeThickness"</c> /
    ///     <c>"Ts"</c> で探していたが、実名は <c>PileSection.PipeTs</c>。3 つとも存在せず
    ///     常にコンクリート肉厚へフォールバックし、キャプリング杭頭の合成 EI が狂っていた
    ///     (例題読込と自動初期化は正しく <c>PipeTs</c> を読んでいたので、
    ///     <b>杭頭ウィンドウを開いたときだけ値が変わる</b>という形だった)。</item>
    ///   <item><c>PileBodyInput.GetMThetaRelationship</c> の末尾が <c>"IsRigidHead"</c> /
    ///     <c>"MthetaXY_ByN"</c> / <c>"KthetaXY"</c> を探していたが <c>PileTop</c> に
    ///     どれも無く、必ず剛結フォールバックへ落ちていた。</item>
    ///   <item>グラフが軸力を <c>"NonlinearAxialForceN"</c> で探していたが
    ///     <c>LoadCase</c> に無く、必ず別経路へ落ちていた。</item>
    /// </list>
    ///
    /// 反射そのものは禁止しない (2 種類のコマンド型に同じ名前で呼びかける、といった
    /// 正当な用途がある)。禁じるのは<b>どこにも存在しない名前</b>を探すことだけ。
    /// </summary>
    [TestClass]
    public class ReflectionByNameTests
    {
        /// <summary>
        /// アプリの型に無くてよい名前。フレームワーク側の型に対して呼ぶもの。
        /// 足すときは「どの型のメンバーか」を必ず書くこと。
        /// </summary>
        private static readonly HashSet<string> FrameworkMemberNames = new(StringComparer.Ordinal)
        {
            // ICollection<T>.Add — ChangWindow が ObservableCollection へ反射で追加する
            "Add",

            // CommunityToolkit.Mvvm.Input.IRelayCommand.NotifyCanExecuteChanged —
            // このアプリには自前の RelayCommand (RaiseCanExecuteChanged) と
            // [RelayCommand] が生成するツールキット側のコマンドが混在しており、
            // 「どちらの名前でもよいから再評価を通知する」ために両方を反射で呼んでいる。
            // 対になる RaiseCanExecuteChanged は自前の型にあるので検査を通る。
            "NotifyCanExecuteChanged",
        };

        /// <summary><c>GetProperty("Xxx")</c>。<c>TryGetProperty</c> (JsonElement) は対象外。</summary>
        private static readonly Regex PropertyByName =
            new(@"(?<!Try)\.GetProperty\(\s*""(?<name>[^""]+)""", RegexOptions.Compiled);

        /// <summary><c>GetMethod("Xxx")</c>。</summary>
        private static readonly Regex MethodByName =
            new(@"(?<!Try)\.GetMethod\(\s*""(?<name>[^""]+)""", RegexOptions.Compiled);

        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(ReflectionByNameTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        /// <summary>PileDesign アセンブリの全型が持つメンバー名を集める。</summary>
        private static HashSet<string> CollectAppMemberNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var assembly = typeof(PileDesign.Models.InputData.InputModel).Assembly;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Instance | BindingFlags.Static
                                     | BindingFlags.FlattenHierarchy;

            foreach (var type in types)
            {
                MemberInfo[] members;
                try { members = type.GetMembers(Flags); }
                catch (TypeLoadException) { continue; }
                foreach (var m in members) names.Add(m.Name);
            }

            return names;
        }

        [TestMethod]
        public void ReflectionByName_OnlyLooksForMembersThatExist()
        {
            string root = Path.Combine(FindSolutionRoot(), "Graphics_r1");
            var known = CollectAppMemberNames();
            var violations = new List<string>();

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("///", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal))
                        continue;

                    foreach (Regex pattern in new[] { PropertyByName, MethodByName })
                    {
                        foreach (Match m in pattern.Matches(line))
                        {
                            string name = m.Groups["name"].Value;
                            if (known.Contains(name) || FrameworkMemberNames.Contains(name)) continue;

                            violations.Add(
                                $"{Path.GetRelativePath(root, file)}:{i + 1}  \"{name}\" "
                                + $"はどの型にも存在しません → {trimmed}");
                        }
                    }
                }
            }

            Assert.AreEqual(0, violations.Count,
                "反射で探している名前がどこにも存在しません。名前を打ち間違えているか、"
                + "対象のプロパティが消えています。見つからないと既定値へ静かに落ちるので、"
                + "正しい名前に直すか、その分岐ごと消してください。\n"
                + string.Join("\n", violations));
        }
    }
}

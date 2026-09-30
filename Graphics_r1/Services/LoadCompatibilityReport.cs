using PileDesign.Common;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PileDesign.Services
{
    /// <summary>読み込みで古い形式を今の形に合わせた内容の種類。</summary>
    public enum CompatibilityKind
    {
        /// <summary>ファイルに無かった項目を既定値で補った。</summary>
        Filled,
        /// <summary>古い書き方・食い違いを今の形に置き換えた (番号の振り直し・意味の読み替えなど)。</summary>
        Converted,
        /// <summary>ファイルにあるが、今の版では読み込みに使わなかった項目 (廃止した・計算で求めるようになった)。</summary>
        Unused,
    }

    public sealed record CompatibilityEntry(CompatibilityKind Kind, string Text);

    /// <summary>
    /// ファイルを開いたときの互換の記録。古い形式からの読み込みで、既定値で補った・置き換えた・使わなかった項目の一覧。
    ///
    /// <para>以前は置き換えのいくつかを個別のダイアログで知らせるだけで、既定値で補った項目や、今の版で使わない項目
    /// (保存し直すと消える) は知らせていなかった。保存し直すと元のファイルがこの内容で書き換わるので、その前に確かめられるようにする。</para>
    /// </summary>
    public sealed class LoadCompatibilityReport
    {
        public const int CurrentFormatVersion = 2;

        public LoadCompatibilityReport(int formatVersion) => FormatVersion = formatVersion;

        /// <summary>読み込んだファイルの形式の版 (0 は版の欄が無い旧ファイル)。</summary>
        public int FormatVersion { get; }

        public List<CompatibilityEntry> Entries { get; } = [];

        public void Add(CompatibilityKind kind, string text) => Entries.Add(new(kind, text));

        public bool IsEmpty => Entries.Count == 0 && FormatVersion >= CurrentFormatVersion;

        private static string Label(CompatibilityKind kind) => kind switch
        {
            CompatibilityKind.Filled => "既定値で補った",
            CompatibilityKind.Converted => "置き換えた",
            CompatibilityKind.Unused => "読み込みで使わなかった",
            _ => "",
        };

        /// <summary>知らせる文 (何も無ければ null)。種類ごとに分け、保存し直す前の注意を添える。</summary>
        public string? Describe()
        {
            if (IsEmpty) return null;
            var lines = new List<string>();
            lines.Add(FormatVersion < CurrentFormatVersion
                ? $"このファイルは古い形式 (v{FormatVersion}) で保存されています。今の形式 (v{CurrentFormatVersion}) に合わせて読み込みました。"
                : "読み込むときに、次の項目を今の形に合わせました。");
            foreach (var group in Entries.GroupBy(e => e.Kind).OrderBy(g => g.Key))
            {
                lines.Add("");
                lines.Add($"■ {Label(group.Key)}もの:");
                lines.AddRange(group.Take(15).Select(e => "・" + e.Text));
                if (group.Count() > 15) lines.Add($"…ほか {group.Count() - 15} 件");
            }
            lines.Add("");
            lines.Add("保存し直すと、元のファイルはこの内容で書き換わります (読み込みで使わなかった項目は消えます)。"
                      + "元のファイルを残したいときは「名前を付けて保存」で別の名前にしてください。"
                      + "この一覧は「解析条件/解析」タブの「入力の診断」でも確かめられます。");
            return string.Join("\n", lines);
        }

        /// <summary>知らせるべきもの (個別のダイアログで知らせていないもの) があるか。</summary>
        public bool NeedsNotice(ISet<string> alreadyNotified)
            => FormatVersion < CurrentFormatVersion || Entries.Any(e => !alreadyNotified.Contains(e.Text));

        /// <summary>「入力の診断」の一覧に出す形 (重さは「情報」)。</summary>
        public IEnumerable<Diagnostic> AsDiagnostics()
            => Entries.Select(e => Diagnostic.Notice(DiagnosticSeverity.Info, DiagnosticTarget.Nowhere,
                   $"読込 ({Label(e.Kind)}): {e.Text}", "保存し直す前に内容を確かめる"));

        /// <summary>
        /// JSON の中の項目を<b>階層込みの経路</b>ですべて集める (「GroundsInput[].GroundLayers[].Thickness」の形。「$id」など参照の印は除く)。
        /// <paramref name="section"/> があればその節だけ (経路はその節から数える)。
        ///
        /// <para>以前は項目名だけを集めて比べていた。同じ名前の項目がほかの階層にあると (No・Name・X など)、ある階層で
        /// 使わなくなった項目も、別の階層で補った項目も見逃した。</para>
        ///
        /// <para>参照の保存 (ReferenceHandler.Preserve) の形は読み解く。一覧は「$values」の中身を一覧として、
        /// 「$ref」は参照先の中身をその場所にあるものとして数える (同じ物がどこに先に書かれるかは保存したときの並びで変わるので、
        /// 参照の印のままだと、書かれた場所の違いを項目の違いと取り違える)。</para>
        /// </summary>
        internal static HashSet<string> CollectPropertyNames(string json, string? section = null)
        {
            var paths = new HashSet<string>(System.StringComparer.Ordinal);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            // 参照先は節の外に先に書かれていることがあるので、文書全体から引けるようにする
            var ids = new Dictionary<string, JsonElement>(System.StringComparer.Ordinal);
            IndexIds(root, ids);
            if (section != null)
            {
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(section, out root)) return paths;
            }
            Walk(root, "", paths, ids, new HashSet<string>(System.StringComparer.Ordinal), new HashSet<string>(System.StringComparer.Ordinal));
            return paths;
        }

        private static void IndexIds(JsonElement element, Dictionary<string, JsonElement> ids)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (element.TryGetProperty("$id", out var id) && id.ValueKind == JsonValueKind.String)
                        ids.TryAdd(id.GetString()!, element);
                    foreach (var p in element.EnumerateObject()) IndexIds(p.Value, ids);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) IndexIds(item, ids);
                    break;
            }
        }

        /// <param name="walked">「参照の番号|経路」。同じ物を同じ経路でもう一度たどらない (杭ごとに同じ杭体を指すなど)。</param>
        /// <param name="onPath">いまたどっている途中の参照の番号。循環する参照をたどり続けない。</param>
        private static void Walk(JsonElement element, string path, HashSet<string> paths,
            Dictionary<string, JsonElement> ids, HashSet<string> walked, HashSet<string> onPath)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (element.TryGetProperty("$ref", out var reference) && reference.ValueKind == JsonValueKind.String)
                    {
                        string refId = reference.GetString()!;
                        if (ids.TryGetValue(refId, out var target) && !onPath.Contains(refId))
                            Walk(target, path, paths, ids, walked, onPath);
                        return;
                    }
                    string? id = element.TryGetProperty("$id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                        ? idElement.GetString() : null;
                    if (id != null && !walked.Add(id + "|" + path)) return;
                    if (id != null) onPath.Add(id);
                    try
                    {
                        if (element.TryGetProperty("$values", out var values))
                        {
                            Walk(values, path, paths, ids, walked, onPath);
                            return;
                        }
                        foreach (var p in element.EnumerateObject())
                        {
                            if (p.Name.StartsWith('$')) continue;
                            string child = path.Length == 0 ? p.Name : path + "." + p.Name;
                            paths.Add(child);
                            Walk(p.Value, child, paths, ids, walked, onPath);
                        }
                    }
                    finally
                    {
                        if (id != null) onPath.Remove(id);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Walk(item, path + "[]", paths, ids, walked, onPath);
                    break;
            }
        }
    }
}

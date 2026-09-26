using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PileDesign.Services
{
    /// <summary>
    /// help.html を section 単位にパースし、簡易キーワード検索 (日本語 bigram + ASCII 単語) を提供する。
    /// オフライン専用、追加依存なし。シングルトン、初回検索時に lazy load。
    /// </summary>
    public sealed class HelpSearchService
    {
        public sealed class HelpSection
        {
            public string Id { get; init; } = "";
            public int Level { get; init; }
            public string Title { get; init; } = "";
            public string TitlePath { get; init; } = "";
            public string PlainText { get; init; } = "";
        }

        public sealed class SearchResult
        {
            public HelpSection Section { get; init; } = null!;
            public int Score { get; init; }
            public string Snippet { get; init; } = "";
        }

        private static readonly Lazy<HelpSearchService> _instance = new(() => new HelpSearchService());
        public static HelpSearchService Instance => _instance.Value;

        private List<HelpSection>? _sections;
        private readonly object _initLock = new();

        public IReadOnlyList<HelpSection> EnsureLoaded()
        {
            if (_sections != null) return _sections;
            lock (_initLock)
            {
                if (_sections != null) return _sections;
                _sections = LoadSections();
            }
            return _sections;
        }

        private static readonly Regex _headingRegex = new(
            @"<h([2-4])([^>]*)>(.*?)</h\1>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _idAttrRegex = new(
            @"id\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _tagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

        // 本文でないもの (スクリプト・スタイル・コメント)。タグだけ除くと中身が本文として残る
        private static readonly Regex _nonContentRegex = new(
            @"<(script|style)\b[^>]*>.*?</\1\s*>|<!--.*?-->",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 本文の 1 語あたりの点の上限。長い節ほど同じ語が多く出るので、出現回数をそのまま足すと、
        /// 見出しが一致する短い節より、語を何度も含むだけの長い節が上に来た。
        /// 本文は「含むか・何度か出るか」までを見て、それ以上は数えない。
        /// </summary>
        internal const int BodyHitCap = 3;

        /// <summary>入力した語が、分けずにそのまま見出しに出るときの加点。</summary>
        internal const int PhraseInTitleBonus = 80;

        /// <summary>入力した語が、分けずにそのまま本文に出るときの加点 (見出しより小さく)。</summary>
        internal const int PhraseInBodyBonus = 50;
        private static readonly Regex _wsRegex = new(@"\s+", RegexOptions.Compiled);

        private static List<HelpSection> LoadSections()
        {
            // ファイル名は実ディスクに合わせて lowercase で完全一致させる
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help", "help.html");
            if (!File.Exists(path))
            {
                Log.Warning("[HelpSearch] help.html が見つかりません: {Path}", path);
                return new List<HelpSection>();
            }

            string html;
            try
            {
                html = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[HelpSearch] help.html 読込失敗: {Path}", path);
                return new List<HelpSection>();
            }
            return ParseSections(html);
        }

        /// <summary>
        /// HTML を見出し (h2〜h4) ごとの節に分ける。
        ///
        /// <b>スクリプト・スタイル・コメントは先に除く。</b>以前はタグだけを除いていたので、help.html の末尾の
        /// 長い &lt;script&gt; の中身 (JavaScript の文字列) が最後の見出しの本文として索引に入り、
        /// 無関係な語で最後の節が検索に出た。
        /// </summary>
        internal static List<HelpSection> ParseSections(string html)
        {
            html = _nonContentRegex.Replace(html, " ");
            var matches = _headingRegex.Matches(html);
            if (matches.Count == 0) return new List<HelpSection>();

            var sections = new List<HelpSection>(matches.Count);
            string?[] crumb = new string?[5];

            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                int level = int.Parse(m.Groups[1].Value);
                string attrs = m.Groups[2].Value;
                var idMatch = _idAttrRegex.Match(attrs);
                string id = idMatch.Success ? idMatch.Groups[1].Value : "";
                string title = StripHtml(m.Groups[3].Value).Trim();
                if (string.IsNullOrEmpty(title)) continue;

                int bodyStart = m.Index + m.Length;
                int bodyEnd = (i + 1 < matches.Count) ? matches[i + 1].Index : html.Length;
                string body = html.Substring(bodyStart, bodyEnd - bodyStart);
                string plain = StripHtml(body);

                crumb[level] = title;
                for (int k = level + 1; k < crumb.Length; k++) crumb[k] = null;
                var path2 = string.Join(" › ", new[] { crumb[2], crumb[3], crumb[4] }
                    .Where(s => !string.IsNullOrEmpty(s) && s != title));

                sections.Add(new HelpSection
                {
                    Id = id,
                    Level = level,
                    Title = title,
                    TitlePath = path2,
                    PlainText = plain,
                });
            }
            return sections;
        }

        private static string StripHtml(string s)
        {
            var noTag = _tagRegex.Replace(s, " ");
            var decoded = System.Net.WebUtility.HtmlDecode(noTag);
            return _wsRegex.Replace(decoded, " ").Trim();
        }

        public IReadOnlyList<SearchResult> Search(string query, int maxResults = 6)
            => Search(query, maxResults, out _);

        /// <summary>検索する。<paramref name="totalHits"/> は絞る前の該当件数 (表示件数とは別)。</summary>
        public IReadOnlyList<SearchResult> Search(string query, int maxResults, out int totalHits)
            => Search(EnsureLoaded(), query, maxResults, out totalHits);

        /// <summary>与えた節から検索する (テストで検索例を比べるため)。</summary>
        internal static IReadOnlyList<SearchResult> Search(IReadOnlyList<HelpSection> sections, string query, int maxResults, out int totalHits)
        {
            totalHits = 0;
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchResult>();

            query = query.Replace('　', ' ').Trim();
            if (query.Length == 0) return Array.Empty<SearchResult>();

            var tokens = ExtractTokens(query);
            if (tokens.Count == 0) return Array.Empty<SearchResult>();

            var results = new List<SearchResult>();
            foreach (var sec in sections)
            {
                int score = 0;
                int titleHits = 0;
                foreach (var t in tokens)
                {
                    int th = CountOccurrences(sec.Title, t);
                    if (th > 0) titleHits++;
                    score += th * 12;
                    score += CountOccurrences(sec.TitlePath, t) * 4;
                    // 本文は上限まで (長い節が語の繰り返しだけで上位を占めないように。BodyHitCap 参照)
                    score += Math.Min(CountOccurrences(sec.PlainText, t), BodyHitCap);
                }
                if (titleHits >= 2) score += 30;
                // 入力した語がそのまま (2 文字ずつに分けずに) 出る節を上に。分けた語だけで比べると、
                // 「ダッシュボード」が「キーボード」「クリップボード」の見出しと「ボー」「ード」で一致して上位を占めた
                if (score > 0 && query.Length >= 2)
                {
                    if (sec.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) score += PhraseInTitleBonus;
                    else if (sec.PlainText.Contains(query, StringComparison.OrdinalIgnoreCase)) score += PhraseInBodyBonus;
                }
                if (score > 0)
                {
                    results.Add(new SearchResult
                    {
                        Section = sec,
                        Score = score,
                        Snippet = ExtractSnippet(sec.PlainText, tokens),
                    });
                }
            }
            totalHits = results.Count;
            return results.OrderByDescending(r => r.Score)
                          .ThenBy(r => r.Section.Level)
                          .Take(maxResults).ToList();
        }

        private static List<string> ExtractTokens(string query)
        {
            var tokens = new List<string>();
            var segments = query.Split(new[] { ' ', '\t', '　' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var seg in segments)
            {
                if (seg.Length == 0) continue;
                if (seg.Length >= 2 && IsAsciiWord(seg))
                {
                    tokens.Add(seg);
                }
                else if (seg.Length == 1)
                {
                    if (!IsAsciiWord(seg)) tokens.Add(seg);
                }
                else
                {
                    for (int i = 0; i + 2 <= seg.Length; i++)
                    {
                        var bg = seg.Substring(i, 2);
                        if (!IsCommonBigram(bg)) tokens.Add(bg);
                    }
                }
            }
            return tokens.Distinct().ToList();
        }

        private static bool IsAsciiWord(string s)
        {
            foreach (var c in s) if (c > 127) return false;
            return true;
        }

        private static readonly HashSet<string> _commonBigrams = new()
        {
            "して", "した", "する", "され", "せる", "ます", "です", "ない", "なる",
            "ある", "いる", "から", "まで", "など", "また", "とき", "ため", "もの",
            "こと", "には", "では", "とは", "への", "での", "って", "てい", "いう",
            "とい", "という", "場合", "の場", "場面", "及び", "または",
        };

        private static bool IsCommonBigram(string bg) => _commonBigrams.Contains(bg);

        private static int CountOccurrences(string text, string token)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token)) return 0;
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(token, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                idx += token.Length;
            }
            return count;
        }

        private static string ExtractSnippet(string text, List<string> tokens, int radius = 60)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int firstIdx = -1;
            string firstToken = "";
            foreach (var t in tokens)
            {
                int idx = text.IndexOf(t, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && (firstIdx < 0 || idx < firstIdx))
                {
                    firstIdx = idx;
                    firstToken = t;
                }
            }
            if (firstIdx < 0) return Truncate(text, radius * 2);

            int start = Math.Max(0, firstIdx - radius);
            int end = Math.Min(text.Length, firstIdx + firstToken.Length + radius);
            string snippet = text.Substring(start, end - start);
            if (start > 0) snippet = "…" + snippet;
            if (end < text.Length) snippet = snippet + "…";
            return snippet;
        }

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}

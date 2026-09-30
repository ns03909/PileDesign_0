using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 杭体・地盤の画面で並びを変えた (消した・足した・元に戻した) あと、ほかの入力が持つ番号の参照を付け直す。
    ///
    /// <para>杭配置・根入部は杭体・地盤を番号 (並び順) で指す。以前は画面で消した<b>その場で</b>参照を 1 つずつ下げていたので、
    /// そのあとキャンセル・× で閉じる、または画面の「元に戻す」で戻すと、杭体・地盤の一覧は元のままなのに参照だけ下がり、
    /// 例外にならずに<b>黙って別の杭体・地盤を指した</b>。いまは画面を開いたときの番号を各要素に控え
    /// (<c>NoAtEditStart</c>)、OK で閉じたときにここで 1 回だけ付け直す。</para>
    ///
    /// <para>新しい番号はすべて先に求めてから書き込む (途中で半分だけ付け直した状態を作らない)。
    /// 指す先が無くなった参照は書き換えずに説明を返す (削除は参照中なら断るので、通常は空)。</para>
    /// </summary>
    public static class EditRenumbering
    {
        /// <summary>番号を持つ参照 1 つ (誰の・読む・書く)。</summary>
        public sealed record Reference(string Owner, Func<int> Get, Action<int> Set);

        /// <summary>
        /// 編集後の並び (各要素の、開いたときの番号。画面で足した要素は 0) から、開いたときの番号 → いまの番号の対応を作る。
        /// </summary>
        public static Dictionary<int, int> NewNumberByOrigin(IEnumerable<int> originNosInNewOrder)
        {
            var map = new Dictionary<int, int>();
            int position = 0;
            foreach (int origin in originNosInNewOrder)
            {
                position++;
                if (origin > 0 && !map.ContainsKey(origin)) map[origin] = position;
            }
            return map;
        }

        /// <summary>参照を付け直す。付け直せなかった参照の説明を返す (それらは書き換えない)。</summary>
        public static List<string> Apply(IEnumerable<Reference> references, IReadOnlyDictionary<int, int> newNumberByOrigin, string kind)
        {
            var planned = new List<(Reference Ref, int NewNo)>();
            var unresolved = new List<string>();
            foreach (var r in references)
            {
                int current = r.Get();
                if (newNumberByOrigin.TryGetValue(current, out int newNo)) planned.Add((r, newNo));
                else unresolved.Add($"{r.Owner} ({kind}番号 {current} の{kind}がありません)");
            }
            // 求め終えてから書き込む
            foreach (var (r, newNo) in planned)
                if (r.Get() != newNo) r.Set(newNo);
            return unresolved;
        }
    }
}

using System;
using System.Reflection;

namespace PileDesign.Common
{
    /// <summary>
    /// 控えから<b>中身だけ</b>を写す道具。
    ///
    /// <para>入力ウィンドウの多くは <c>InputModel</c> の子オブジェクトの<b>実体</b>を
    /// そのまま編集する。キャンセルや Undo で
    /// <c>X = 控え.DeepCopy()</c> と<b>オブジェクトごと差し替えると</b>、
    /// 画面の指す先が実体から外れ、</para>
    ///
    /// <list type="number">
    /// <item>画面には戻った値が出るのに実体は編集後のままで、解析も計算書もそれを使う</item>
    /// <item>そのあとに打った値がどこにも届かない</item>
    /// <item>キャンセルを押しても、戻されるのは外れた複製なので効かない</item>
    /// </list>
    ///
    /// <para>という三つが<b>すべて黙って</b>起きる（杭断面ウィンドウと荷重条件ウィンドウで
    /// 実際に起きた）。だから差し替えず、フィールドの値だけを写す。</para>
    ///
    /// <para><b>参照型は写さない。</b>写すとコレクションや子オブジェクトの実体が
    /// 控え側と入れ替わり、他所が持っている参照と保存グラフの <c>$ref</c> が外れる。
    /// 子まで戻したいときは、子に対して改めてこれを呼ぶこと。</para>
    /// </summary>
    internal static class ModelRestore
    {
        /// <summary>
        /// <paramref name="source"/> の値型・文字列フィールドを <paramref name="target"/> へ写す。
        ///
        /// <para>セッターを通さずフィールドを直に写す。セッター経由だと派生値の再計算や
        /// 子要素の同期が走るが、写すのは<b>もともと過去の姿</b>なので計算し直す必要はない
        /// （むしろ途中の食い違った状態から派生値を計算し直してしまう）。</para>
        ///
        /// <para>基底クラスの private フィールドまで辿る。<c>readonly</c>（init-only）は
        /// 写さない（ロックや購読の入れ物）。</para>
        ///
        /// <para>写したあとの画面への通知は呼び出し側の仕事
        /// （<c>BaseModel.OnPropertyChanged(string.Empty)</c> が「全部」の意味）。</para>
        /// </summary>
        internal static void CopyScalarFields(object target, object source)
        {
            if (target == null || source == null) return;

            Type type = target.GetType();

            // 型が違うものを写すと、同名フィールドだけが混ざった状態になる。何もしない。
            if (!type.IsInstanceOfType(source)) return;

            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly))
                {
                    if (f.IsInitOnly) continue;
                    if (!f.FieldType.IsValueType && f.FieldType != typeof(string)) continue;
                    f.SetValue(target, f.GetValue(source));
                }
            }
        }
    }
}

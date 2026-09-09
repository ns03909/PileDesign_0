using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace PileDesign.Models.InputData
{
    // InputModel partial: 保存のあいだだけ使う「編集から守った器」。
    public partial class InputModel
    {
        /// <summary>
        /// 保存の直前に、<b>画面から編集できるコレクションだけ</b>を写した器を返す。
        ///
        /// 保存は別スレッドで直列化する一方、利用者は表を編集し続けられる。生きたモデルを
        /// そのまま辿ると、編集で列挙が壊れて <see cref="InvalidOperationException"/> になるか、
        /// 前半と後半で状態の違うファイルが書かれる。
        ///
        /// <b>要素は同じインスタンスのまま</b>写す。入れ物だけを新しくして、中身は共有する。
        /// これで
        /// <list type="bullet">
        /// <item>直列化が辿るのは編集されない入れ物になる (列挙が壊れない)</item>
        /// <item>保存ファイルの中身は 1 バイトも変わらない
        ///   (参照の畳まれ方 <c>$id</c>/<c>$ref</c> は実体の同一性で決まるため)</item>
        /// </list>
        /// が両立する。
        ///
        /// <see cref="DeepCopy"/> は使えない。あちらは元に戻す操作のためのもので、
        /// 地盤の一覧を型ごとの複製で、要素分割を JSON の往復で写すため、
        /// 土層-杭セットが指す地盤と地盤の一覧の地盤が<b>別インスタンスに分かれる</b>。
        /// 元では 1 つに畳まれていた実体が 2 つになり、保存ファイルの形が変わる。
        ///
        /// 守るのは<b>入れ物の差し替えと要素の出し入れ</b>だけで、要素そのものの
        /// 値の書き換え (セルの編集) は防げない。そこまで守るには要素も複製することになり、
        /// 実体が変わって保存ファイルの形が変わる。値の取り違えは 1 つの数値に留まるのに対し、
        /// 列挙の破壊は保存そのものを失敗させるので、ここでは前者を許容する。
        /// </summary>
        internal InputModel SnapshotForSaving()
        {
            // 器だけ新しくする。参照はすべて元と同じものを指したまま。
            var snapshot = (InputModel)MemberwiseClone();

            // 複製した器に購読者を引き継がない (MemberwiseClone はデリゲートも写す)。
            snapshot.DetachChangeNotification();

            // ── メイン画面の表 ──
            snapshot._pileLayoutItems = Shallow(_pileLayoutItems);
            snapshot._gridXItems = Shallow(_gridXItems);
            snapshot._gridYItems = Shallow(_gridYItems);
            snapshot._inputNodes = Shallow(_inputNodes);

            // ── 各ウィンドウの表 ──
            // 入れ物を差し替えるのは snapshot 側だけ。元のモデルには触らない。
            snapshot.GroundsInput = Shallow(GroundsInput);
            snapshot.PileBodies = Shallow(PileBodies);

            return snapshot;
        }

        /// <summary>
        /// 同じ要素を指す新しい入れ物を返す。null は null のまま
        /// (空の入れ物に置き換えると保存ファイルの中身が変わる)。
        /// </summary>
        private static ObservableCollection<T>? Shallow<T>(ObservableCollection<T>? source)
            => source == null ? null : new ObservableCollection<T>(source);

        /// <summary>
        /// 写した器から変更通知の購読者を外す。
        ///
        /// <see cref="object.MemberwiseClone"/> は field-like event のデリゲートも写すため、
        /// 何もしないと保存用の器が画面の購読者を抱えたまま生き残る。
        /// </summary>
        private void DetachChangeNotification()
        {
            ClearPropertyChangedSubscribers();
        }
    }
}

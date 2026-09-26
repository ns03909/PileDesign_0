using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 荷重組合せ 1 通り分の係数。
    ///
    /// プロパティ名と画面・計算書の記号の対応 (名前からは読み取れないので注意):
    ///   Alpha1 = αL … 杭曲げモーメント最大時の地盤変位 / 地盤変位の最大値
    ///   Beta1  = βU … 杭曲げモーメント最大時の上部構造慣性力 / その最大値
    ///   Beta2  = βL … 杭曲げモーメント最大時の基礎部慣性力 / その最大値
    ///
    /// 杭体応力の低減係数 β₁ / β₂ (基礎指針) とは別の量。
    /// 表示で β₁ / β₂ を使うとそちらと見分けがつかないので、
    /// 画面・グラフ・計算書のいずれも αL / βU / βL で統一する。
    /// </summary>
    public class LoadCombination(int no, double alpha1, double beta1, double beta2) : INotifyPropertyChanged
    {
        private bool _isApplicable = true;
        public bool IsApplicable
        {
            get => _isApplicable;
            set
            {
                if (_isApplicable != value)
                {
                    _isApplicable = value;
                    OnPropertyChanged(nameof(IsApplicable));
                    OnPropertyChanged(nameof(IsApplicableForDocxDisplay));
                }
            }
        }

        /// <summary>
        /// 2 つの荷重組合せが同じ組合せか (番号で決める)。
        ///
        /// 表示名 (<see cref="Name"/>) は係数を小数 2 桁に丸めた文字列なので、係数の近い別の組合せが同じ名前になりうる
        /// (係数 0.999 の組合せ 1 と 2 は、どちらも「1.00/-1.00/1.00」)。名前で照合すると、別の組合せの結果を拾ったり
        /// 2 つを 1 つにまとめたりする。番号は読込で一覧の並び順に揃える
        /// (<see cref="LoadCasesInput.NormalizeLoadCaseNumbers"/>) ので一意。
        /// </summary>
        public static bool IsSameCombination(LoadCombination? a, LoadCombination? b)
            => a != null && b != null && a.No == b.No;

        private int _no = no;
        public int No
        {
            get => _no;
            set
            {
                if (_no != value)
                {
                    _no = value;
                    OnPropertyChanged(nameof(No));
                }
            }
        }

        private double _alpha1 = alpha1;
        public double Alpha1
        {
            get => _alpha1;
            set
            {
                if (_alpha1 != value)
                {
                    _alpha1 = value;
                    OnPropertyChanged(nameof(Alpha1));
                }
            }
        }
        private double _beta1 = beta1;
        public double Beta1
        {
            get => _beta1;
            set
            {
                if (_beta1 != value)
                {
                    _beta1 = value;
                    OnPropertyChanged(nameof(Beta1));
                }
            }
        }


        private double _beta2 = beta2;
        public double Beta2
        {
            get => _beta2;
            set
            {
                if (_beta2 != value)
                {
                    _beta2 = value;
                    OnPropertyChanged(nameof(Beta2));
                }
            }
        }



        private bool _isAnalyzed = true;
        /// <summary>水平解析が実施済みかどうか（DocxOutputWindow表示時にセット）</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsAnalyzed
        {
            get => _isAnalyzed;
            set
            {
                if (_isAnalyzed != value)
                {
                    _isAnalyzed = value;
                    OnPropertyChanged(nameof(IsAnalyzed));
                    OnPropertyChanged(nameof(IsApplicableForDocxDisplay));
                }
            }
        }

        /// <summary>
        /// DocxOutputWindow の CheckBox 表示専用プロパティ。
        /// 未解析時は IsApplicable=true でも未チェック表示にする (UX)。
        /// 実体 (_isApplicable) は変更しないので CanExecuteAnalysis 等の解析ロジックに影響しない。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsApplicableForDocxDisplay
        {
            get => _isApplicable && _isAnalyzed;
            set
            {
                if (_isAnalyzed && _isApplicable != value)
                {
                    _isApplicable = value;
                    OnPropertyChanged(nameof(IsApplicable));
                    OnPropertyChanged(nameof(IsApplicableForDocxDisplay));
                }
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string Name => "α\u2097:" + Alpha1.ToString("F2") + "/β\u1d64:" + Beta1.ToString("F2") + "/β\u2097:" + Beta2.ToString("F2");

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));


        public string GetName()
        {
            return Alpha1.ToString("F2") + "/" + Beta1.ToString("F2") + "/" + Beta2.ToString("F2");
        }

        /// <summary>
        /// 値を新しいインスタンスへ写す。
        ///
        /// 以前は MemberwiseClone で、PropertyChanged の購読者 (元の組合せを見ている画面) まで写していた。
        /// 解析結果や Undo の控えとして複製した組合せを変えると、元の組合せの画面へ通知が飛んだ。
        /// 購読者は写さない。
        /// </summary>
        public LoadCombination DeepCopy() => new(No, Alpha1, Beta1, Beta2)
        {
            _isApplicable = _isApplicable,
            _isAnalyzed = _isAnalyzed,
        };
    }

    /// <summary>
    /// グラフなどで荷重組合せを選ぶ項目。表示は係数、選んだものは<b>番号</b>で見分ける (<see cref="No"/> が null は「すべて」)。
    ///
    /// 以前は選択肢を係数を小数 2 桁に丸めた文字列で持ち、一致した最初の組合せを返していた。係数の近い 2 つの
    /// 組合せは同じ文字列になり、選び分けられなかった。表示が重なるときは番号を添える。
    /// </summary>
    public sealed record LoadCombinationChoice(int? No, string Label)
    {
        public override string ToString() => Label;

        public bool IsAll => No == null;

        /// <summary>「すべて」と各組合せの選択肢。表示名が重なる組合せには番号を添える。</summary>
        public static List<LoadCombinationChoice> Build(IEnumerable<LoadCombination>? combinations)
            => Build(combinations?.Where(c => c != null).Select(c => (c.No, c.GetName())));

        /// <summary>(番号, 表示名) の組から選択肢を作る (表の絞り込みなど、組合せの実体を持たない所で使う)。</summary>
        public static List<LoadCombinationChoice> Build(IEnumerable<(int No, string Name)>? combinations)
        {
            var list = new List<LoadCombinationChoice> { new(null, PileDesign.Common.UiText.All) };
            var items = combinations?.ToList() ?? [];
            var duplicatedNames = items.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            foreach (var (no, name) in items)
                list.Add(new(no, duplicatedNames.Contains(name) ? $"{name} (組合せ{no})" : name));
            return list;
        }
    }

    /// <summary>
    /// 画面の選択肢の文字列と荷重組合せの対応。
    ///
    /// 選択肢の文字列は <see cref="LoadCombinationChoice.Build(IEnumerable{LoadCombination})"/> の表示名
    /// (係数を丸めた文字列。重なるときは番号を添える)。以前は係数の文字列だけで照合し、一致した最初の組合せを返したので、
    /// 表示名の重なる 2 つ目の組合せは選べなかった。
    /// </summary>
    public static class LoadCombinations
    {
        /// <summary>選択肢の文字列に当たる組合せ。番号を添えた表示名も解く。どれにも当たらなければ係数の文字列で探す。</summary>
        public static LoadCombination? GetLoadCombination(IEnumerable<LoadCombination>? loadCombinations, string? label)
        {
            if (loadCombinations == null || string.IsNullOrEmpty(label)) return null;
            var list = loadCombinations.Where(c => c != null).ToList();
            var choice = LoadCombinationChoice.Build(list).FirstOrDefault(c => !c.IsAll && c.Label == label);
            if (choice != null) return list.FirstOrDefault(c => c.No == choice.No);
            return list.FirstOrDefault(c => c.GetName() == label || c.Name == label);
        }

        /// <summary>組合せの選択肢の文字列 (<see cref="GetLoadCombination"/> の逆)。</summary>
        public static string? LabelOf(IEnumerable<LoadCombination>? loadCombinations, LoadCombination? combination)
        {
            if (combination == null) return null;
            return LoadCombinationChoice.Build(loadCombinations).FirstOrDefault(c => c.No == combination.No)?.Label
                ?? combination.GetName();
        }
    }
}



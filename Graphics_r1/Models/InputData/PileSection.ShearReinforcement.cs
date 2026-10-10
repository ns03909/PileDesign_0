using PileDesign.Constants;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using PileDesign.Services;

namespace PileDesign.Models.InputData
{
    // PileSection のうち、せん断補強筋の工法・規格・算定式の選択肢とその整合。
    public partial class PileSection
    {
        /// <summary>
        /// 有効せい d [mm]（MonQd計算用）: d = 0.9D（基礎指針'19）。
        ///
        /// せん断補強筋の工法を選んだときは、工法の指針の定義
        /// d = (B/2)·√π − dt（dt は主筋重心かぶり厚）に替える。
        /// せん断耐力の式が d を使うので、M/(Q·d) を別の d で作ると
        /// 耐力を引く位置がずれる（B=1000 で 1.2 倍ほど違う）。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double EffectiveDepth
        {
            get
            {
                if (!ShearReinforcementMethods.IsProprietary(HoopMethod)) return PileDiameter * 0.9;
                // 断面側 (InsituReinforcedConcreteSection) と同じ出所から作る。
                // かぶり厚と配筋径のどちらから引くかを変えると、両者が静かにずれる。
                double dt = (ConcreteOutDia - MainBarDr) / 2.0;
                double d = ConcreteOutDia / 2.0 * Math.Sqrt(Math.PI) - dt;
                return d > 0 ? d : PileDiameter * 0.9;
            }
        }

        // せん断補強筋の工法
        //
        // 「標準」以外を選ぶと、材料だけでなく<b>せん断耐力の算定式そのもの</b>が
        // 工法の指針の式に替わる (ShearReinforcementMethods 参照)。
        private string _hoopMethod = ShearReinforcementMethods.Standard;
        public string HoopMethod
        {
            get => _hoopMethod;
            set
            {
                // ComboBox は選択肢を差し替えた瞬間に null を書き戻してくる。
                // 受けると工法が消えて式が静かに標準へ戻るので、無視する。
                if (string.IsNullOrEmpty(value)) return;
                RequireHoopOption(value, ShearReinforcementMethods.Options, "せん断補強筋の工法");
                if (SetProperty(ref _hoopMethod, value))
                {
                    // 呼び名・規格・算定式の選択肢はどれも工法で入れ替わる。
                    // 入れ物は作り替えず、選択値が候補の外に出ないように書き換える。
                    SyncHoopOptionCollections();

                    OnPropertyChanged(nameof(IsStandardHoopMethod));
                    OnPropertyChanged(nameof(IsHoopDamageFormulaSelectable));
                    OnPropertyChanged(nameof(IsHoopUltimateFormulaSelectable));
                    OnPropertyChanged(nameof(ShearDesignMagnification));
                    InvalidateAllCaches();
                }
            }
        }

        /// <summary>せん断補強筋の工法の選択肢。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string[] HoopMethodOption => ShearReinforcementMethods.Options;

        // 工法で入れ替わる選択肢は、<b>入れ物ごと差し替えない</b>。
        //
        // ItemsSource を別の配列に差し替えると、WPF は候補から外れた選択値を捨てて
        // null を元へ書き戻す。モデル側で null を無視しても<b>画面の ComboBox は空のまま残り</b>、
        // 利用者には「入力が消えた」ように見える (値は正しいので保存すると直る、という分かりにくさ)。
        //
        // 同じコレクションを ①新しい候補を足す → ②選択値を移す → ③古い候補を外す の順に
        // 書き換えれば、選択値が候補の外に出る瞬間が無くなる。
        // 「選択肢を絞るときは現在値を必ず含める」を、入れ替えの途中にも守らせる形。
        private ObservableCollection<string> _hoopSizeOption = new(ShearReinforcementMethods.StandardBarSizes);
        private ObservableCollection<string> _hoopSpecOption = new(ShearReinforcementMethods.StandardGrades);

        /// <summary>せん断補強筋の呼び名の選択肢。工法で入れ替わる。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ObservableCollection<string> HoopSizeOption => _hoopSizeOption;

        /// <summary>せん断補強筋の規格の選択肢。工法では 1 つに決まる。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ObservableCollection<string> HoopSpecOption => _hoopSpecOption;

        /// <summary>
        /// 工法の断面で、杭径・Fc が工法の適用範囲の外ならその理由。範囲内・工法なし (標準) なら null。
        /// せん断の検定はこの理由を項目に付け、判定を「適用範囲外」にする (EvaluationService)。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string? HoopMethodOutOfScopeReason
            => UsesHoopMethodFormulas
                ? ShearReinforcementMethods.Get(HoopMethod)?.OutOfScopeReason(ConcreteOutDia, ConcreteFc)
                : null;

        /// <summary>
        /// 工法の断面で、せん断補強筋比・間隔が工法の構造規定を満たさなければその理由。満たす・工法なしなら null。
        /// 間隔の上限は深さで変わるので、要素の上端の杭頭からの深さ (m) を受け取る。
        /// せん断の検定はこの理由を項目に付け、判定を「適用範囲外」にする (EvaluationService)。
        /// </summary>
        /// <param name="ultimate">終局 (安全限界) の検定か</param>
        /// <param name="elementTopDepthM">要素の上端の、杭頭からの深さ (m)</param>
        internal string? HoopMethodDetailingReason(bool ultimate, double elementTopDepthM)
        {
            if (!UsesHoopMethodFormulas) return null;
            var spec = ShearReinforcementMethods.Get(HoopMethod);
            if (spec == null) return null;
            bool nearPileHead = elementTopDepthM * 1000.0 < spec.PileHeadRangeInDiameters * ConcreteOutDia;
            return spec.DetailingViolation(HoopPw, HoopSpacing, nearPileHead, ultimate);
        }

        /// <summary>
        /// せん断耐力を工法の指針の式で算定する断面か。工法を選んでいて、かつ工法の式を使う断面の種類
        /// (場所打ちRC杭 / 場所打ち鋼管コンクリート杭の RC 部) のとき。<see cref="CreateSectionCalculator"/> と同じ条件。
        ///
        /// 工法の設定は断面の種類を変えても残るので、工法名だけを見ると、工法の式を使っていない
        /// 既製杭などの検定まで「適用範囲外」にしてしまう。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        internal bool UsesHoopMethodFormulas
            => ShearReinforcementMethods.IsProprietary(HoopMethod)
               && (PileBodyType == PileTypeNames.InsituRc
                   || (PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSectionType == PileTypeNames.RcSection));

        /// <summary>工法を使っていない (＝規格を選べる) か。画面の有効・無効に使う。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool IsStandardHoopMethod => !ShearReinforcementMethods.IsProprietary(HoopMethod);

        // 損傷限界 (一次設計) の算定式。
        //
        // エムケーパイルリング785 は 3.2式 (損傷限界せん断力) と
        // 3.3式 (安全性確保のための短期許容せん断力) のどちらかによる、と指針が定めている。
        // 3.3式 を選んだときは設計用せん断力を 1.5 倍に割り増すのが前提なので、
        // 式だけ差し替えて応答をそのままにしてはいけない (ShearDesignMagnification 参照)。
        private string _hoopDamageFormula = ShearReinforcementMethods.DamageFormulaDamageLimit;
        public string HoopDamageFormula
        {
            get => _hoopDamageFormula;
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                RequireHoopOption(value, _hoopDamageFormulaOption, "損傷限界の算定式");
                if (SetProperty(ref _hoopDamageFormula, value))
                {
                    OnPropertyChanged(nameof(ShearDesignMagnification));
                    InvalidateAllCaches();
                }
            }
        }

        // 終局限界の算定式。
        //
        // ウルボンは「③式（大野・荒川 min 式）または④式（トラス・アーチ式）による」。
        // どちらを使うかは設計者の選択なので、プログラムが大きい方を勝手に採ることはしない。
        private string _hoopUltimateFormula = ShearReinforcementMethods.UltimateFormulaArakawa;
        public string HoopUltimateFormula
        {
            get => _hoopUltimateFormula;
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                RequireHoopOption(value, _hoopUltimateFormulaOption, "終局の算定式");
                if (SetProperty(ref _hoopUltimateFormula, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        private ObservableCollection<string> _hoopDamageFormulaOption =
            new(ShearReinforcementMethods.DamageFormulas(null));
        private ObservableCollection<string> _hoopUltimateFormulaOption =
            new(ShearReinforcementMethods.UltimateFormulas(null));

        /// <summary>損傷限界の算定式の選択肢。工法で入れ替わる。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ObservableCollection<string> HoopDamageFormulaOption => _hoopDamageFormulaOption;

        /// <summary>終局限界の算定式の選択肢。工法で入れ替わる。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ObservableCollection<string> HoopUltimateFormulaOption => _hoopUltimateFormulaOption;

        /// <summary>損傷限界の算定式を選べるか (選択肢が 2 つ以上あるか)。画面の有効・無効に使う。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool IsHoopDamageFormulaSelectable => HoopDamageFormulaOption.Count > 1;

        /// <summary>終局限界の算定式を選べるか (選択肢が 2 つ以上あるか)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool IsHoopUltimateFormulaSelectable => HoopUltimateFormulaOption.Count > 1;

        /// <summary>
        /// 工法に応じて選択肢のコレクションを書き換え、外れた選択値を新しい候補へ移す。
        ///
        /// 順番が要点。新しい候補を先に足してから選択値を移し、最後に古い候補を外す。
        /// 逆順にすると、選択値が候補に無い瞬間ができて ComboBox が空になる。
        /// </summary>
        /// <summary>
        /// 工法・呼び名・規格・算定式に、いまの工法で選べない値を入れさせない。
        ///
        /// 以前は読み込みのあとにだけ整合を取っていて、setter は選択肢の外の文字列もそのまま受け入れた。
        /// 画面の外 (プログラム・計算例の読み込みなど) から入れると、表示は入れた値なのに、
        /// 耐力は別の式 (標準の式や工法の既定の式) で計算される、という食い違いが起きうる。
        ///
        /// <b>読み込みの最中は受け入れる。</b>保存ファイルは工法より前に呼び名が来ることがあり、
        /// その時点では正しい呼び名 (工法の「MD13」など) も「いまの工法 (標準) の外」に見える。
        /// 読み終わりの <see cref="SyncHoopMethodConsistency"/> が工法に合わせて寄せ直す。
        /// </summary>
        private void RequireHoopOption(string value, IEnumerable<string> options, string what)
        {
            if (_isDeserializing) return;
            var list = options as IList<string> ?? [.. options];
            if (list.Contains(value)) return;
            throw new ArgumentException(
                $"{what}に「{value}」は選べません (工法「{HoopMethod}」)。選べるのは {string.Join("・", list)} です。",
                nameof(value));
        }

        private void SyncHoopOptionCollections()
        {
            Apply(_hoopSizeOption, ShearReinforcementMethods.BarSizes(_hoopMethod),
                ref _hoopSize, nameof(HoopSize));
            Apply(_hoopSpecOption, ShearReinforcementMethods.Grades(_hoopMethod),
                ref _hoopSpec, nameof(HoopSpec));
            Apply(_hoopDamageFormulaOption, ShearReinforcementMethods.DamageFormulas(_hoopMethod),
                ref _hoopDamageFormula, nameof(HoopDamageFormula));
            Apply(_hoopUltimateFormulaOption, ShearReinforcementMethods.UltimateFormulas(_hoopMethod),
                ref _hoopUltimateFormula, nameof(HoopUltimateFormula));

            void Apply(ObservableCollection<string> options, string[] wanted, ref string current, string valueName)
            {
                if (wanted.Length == 0) return;
                foreach (string w in wanted)                                  // ① 足す
                    if (!options.Contains(w)) options.Add(w);
                if (!wanted.Contains(current)) current = wanted[0];           // ② 移す
                OnPropertyChanged(valueName);
                for (int i = options.Count - 1; i >= 0; i--)                  // ③ 外す
                    if (!wanted.Contains(options[i])) options.RemoveAt(i);
            }
        }

        /// <summary>
        /// 損傷限界せん断の検定で、設計用せん断力に掛ける割増係数。
        ///
        /// 「安全性確保のための短期許容せん断力」(エムケーパイルリング785 3.3式) は、
        /// <b>水平荷重時せん断力を 1.5 倍以上に割り増した設計用せん断力</b>と比べる式。
        /// 耐力式だけ差し替えて応答を解析値のままにすると、指針より緩い検定になる。
        /// 検定側 (EvaluationService) が損傷限界せん断の応答にこれを掛ける。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double ShearDesignMagnification
        {
            get
            {
                var spec = ShearReinforcementMethods.Get(HoopMethod);
                if (spec == null) return 1.0;
                return HoopDamageFormula == ShearReinforcementMethods.DamageFormulaSafetyShortTerm
                    ? spec.SafetyShearMagnification
                    : 1.0;
            }
        }

        // せん断補強筋径
        private string _hoopSize = "D13";
        public string HoopSize
        {
            get => _hoopSize;
            set
            {
                // 選択肢の差し替えに伴う null の書き戻しを無視する (HoopMethod 参照)。
                if (string.IsNullOrEmpty(value)) return;
                RequireHoopOption(value, _hoopSizeOption, "せん断補強筋の呼び名");
                if (SetProperty(ref _hoopSize, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // せん断補強筋間隔
        private double _hoopSpacing = 150.0;
        public double HoopSpacing
        {
            get => _hoopSpacing;
            set
            {
                double safeValue = value < 10 ? 10 : value;
                if (SetProperty(ref _hoopSpacing, safeValue))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // せん断補強筋規格
        private string _hoopSpec = "SD295";
        public string HoopSpec
        {
            get => _hoopSpec;
            set
            {
                // 選択肢の差し替えに伴う null の書き戻しを無視する (HoopMethod 参照)。
                if (string.IsNullOrEmpty(value)) return;
                RequireHoopOption(value, _hoopSpecOption, "せん断補強筋の規格");
                if (SetProperty(ref _hoopSpec, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // せん断補強筋重心かぶり
        private double _hoopCenterCover = 150.0;
        public double HoopCenterCover
        {
            get => _hoopCenterCover;
            set
            {
                if (SetProperty(ref _hoopCenterCover, value))
                {
                    InvalidateAllCaches();
                }
            }
        }
        /// <summary>せん断補強筋 1 本の公称断面積 (mm²)。工法では工法の呼び名の表を引く。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double HoopBarArea
        {
            get
            {
                var spec = ShearReinforcementMethods.Get(HoopMethod);
                if (spec == null) return GetBarArea(HoopSize);

                double area = spec.BarArea(HoopSize);
                if (area <= 0)
                {
                    // 工法の表に無い呼び名。0 のままだと pw が 0 になり、
                    // 安全限界せん断から補強筋の項が黙って消える。
                    PileDesign.Common.CalcFallbackTracker.Report(
                        "せん断補強筋の公称断面積 (→工法の既定径)", null,
                        $"HoopMethod={HoopMethod}, HoopSize={HoopSize}");
                    area = spec.BarArea(spec.DefaultBarSize);
                }
                return area;
            }
        }

        /// <summary>
        /// せん断補強筋比 pw = aw/(b·x)。aw は 1 組 (両側 2 本) の断面積。
        ///
        /// <b>等価な幅 b の取り方が工法で違う</b>。標準は b = πD/4、工法は b = (B/2)·√π で、
        /// 同じ配筋でも pw が 13% ずれる。工法の適用範囲 (0.1%〜) の判定も
        /// 工法の式が使う pw で見ないと意味がないので、ここで揃えておく。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double HoopPw
        {
            get
            {
                if (!(ConcreteOutDia > 0) || !(HoopSpacing > 0)) return 0.0;
                double b = ShearReinforcementMethods.IsProprietary(HoopMethod)
                    ? ConcreteOutDia / 2.0 * Math.Sqrt(Math.PI)
                    : Math.PI / 4.0 * ConcreteOutDia;
                return 2 * HoopBarArea / b / HoopSpacing;
            }
        }

        /// <summary>
        /// せん断補強筋の降伏点 σwy (N/mm²)。工法では終局限界せん断力の算定に使う材料強度。
        ///
        /// 以前は if の連鎖で、知らない規格名では<b>黙って 0</b> を返していた。
        /// 0 のまま安全限界せん断の √(pw·σwy) に入ると、補強筋の項が消えて
        /// 耐力だけが静かに下がる。表に無い規格は記録して既定へ落とす。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double HoopSigmay
        {
            get
            {
                var spec = ShearReinforcementMethods.Get(HoopMethod);
                if (spec != null) return spec.UltimateSigmaWy;
                if (ShearReinforcementMethods.StandardGrades.Contains(HoopSpec))
                    return MainBars.GradeYieldStrength(HoopSpec);

                PileDesign.Common.CalcFallbackTracker.Report(
                    "せん断補強筋の降伏点 (→SD295)", null, $"HoopSpec={HoopSpec}, HoopMethod={HoopMethod}");
                return 295.0;
            }
        }

        /// <summary>
        /// せん断補強筋の短期許容引張応力度 wft (N/mm²)。工法の短期許容せん断力の第2項に使う。
        /// 工法を使わないときは短期許容せん断力の式自体を使わないので 0。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double HoopWft => ShearReinforcementMethods.Get(HoopMethod)?.ShortTermTensileStress ?? 0.0;

        // せん断補強筋降伏点
        private double _hoopPsSigmay;
        public double HoopPsSigmay
        {
            get => _hoopPsSigmay;
            set => SetProperty(ref _hoopPsSigmay, value);
        }

    }
}

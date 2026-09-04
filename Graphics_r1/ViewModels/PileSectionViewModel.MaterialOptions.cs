using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// PileSectionViewModel.MaterialOptions.cs
    ///
    /// 杭断面ウィンドウから、基本設定と同じ<b>材料のモデル化オプション</b>を変えられるようにする。
    ///
    /// N-M・Q-N・M-φ の曲線はこれらのオプションで形が変わるが、変えるには基本設定を開き直す
    /// 必要があり、曲線を見ながら効きを確かめられなかった。ここで変えると曲線が即座に描き直される。
    ///
    /// 値の置き場所は基本設定と同じ <see cref="FundamentalInput"/> で、この画面はその窓口にすぎない。
    /// <b>いま見ている断面の設定ではなく、すべての杭 (全杭体・全区間) に効く</b>。
    /// 断面ごとの入力に見えてしまうと取り違えるので、画面にもその旨を出している。
    ///
    /// <b>杭断面ウィンドウのキャンセルでは戻らない</b> (キャンセルが戻すのは断面の入力だけ)。
    /// 元に戻すときは同じ選択肢を選び直すか、基本設定側の Undo を使う。
    ///
    /// 出すのは<b>その断面に効く項目だけ</b>。効かない項目を出すと、選び直しても曲線が変わらず
    /// 「壊れている」と読める。どの断面に効くかは実装をたどって決めてある
    /// (例: 引張・圧縮・Ec の ξ は InsituConcrete のモデル化なので場所打ち系と充填鋼管部、
    /// 鋼材ヤング係数 n=5 は既製杭、KCTB の項目は場所打ち鋼管コンクリート杭のみ)。
    /// 鋼管杭の鋼管部は SteelPipeSection で別系統なので、出す項目が無い。
    /// </summary>
    public partial class PileSectionViewModel
    {
        private FundamentalInput? Fundamental => InputModel?.FundamentalInput;

        /// <summary>いま表示している断面が場所打ち鉄筋コンクリート部か。</summary>
        public bool IsInsituRcSectionShown =>
            PileSection != null
            && (PileSection.PileBodyType == PileTypeNames.InsituRc
                || (PileSection.PileBodyType == PileTypeNames.InsituSteelPipeConcrete
                    && PileSection.PileSectionType == PileTypeNames.RcSection));

        /// <summary>いま表示している断面が場所打ち鋼管コンクリート部か。</summary>
        public bool IsInsituSteelPipeConcreteSectionShown =>
            PileSection != null
            && PileSection.PileBodyType == PileTypeNames.InsituSteelPipeConcrete
            && PileSection.PileSectionType == PileTypeNames.SteelPipeConcreteSection;

        /// <summary>いま表示している断面が既製コンクリート杭 (PHC/PRC/SC・節杭・BF.S) か。</summary>
        public bool IsPrecastConcreteSectionShown =>
            PileSection != null
            && PileSection.PileBodyType == PileTypeNames.PrecastConcrete;

        /// <summary>いま表示している断面が鋼管杭のコンクリート充填鋼管部 (杭頭部) か。</summary>
        public bool IsCftSectionShown =>
            PileSection != null
            && PileSection.PileBodyType == PileTypeNames.SteelPipe
            && PileSection.PileSectionType == PileTypeNames.CftSection;

        // ── 項目ごとの表示可否 ──
        //
        // 「効く断面にだけ出す」。効かない断面に出すと、選び直しても曲線が変わらず
        // 「壊れている」と読める。どの断面に効くかは実装をたどって決めてある。

        /// <summary>
        /// 場所打ちコンクリート (InsituConcrete) を使う断面か。
        /// Ec の ξ・引張・圧縮の折れ点はこのコンクリートのモデル化なので、ここが対象。
        /// コンクリート充填鋼管部も充填部に InsituConcrete を使う。
        /// </summary>
        public bool UsesInsituConcrete =>
            IsInsituRcSectionShown || IsInsituSteelPipeConcreteSectionShown || IsCftSectionShown;

        /// <summary>鉄筋 1.1F の対象 (場所打ち系。充填鋼管部は主筋 0 本なので対象外)。</summary>
        public bool ShowRebarYieldOption =>
            IsInsituRcSectionShown || IsInsituSteelPipeConcreteSectionShown;

        /// <summary>鋼管 1.1F の対象 (場所打ち鋼管コンクリート杭のみ。鋼管杭は対象外)。</summary>
        public bool ShowSteelPipeYieldOption => IsInsituSteelPipeConcreteSectionShown;

        /// <summary>告示1113(第8) の許容圧縮の対象 (場所打ちコンクリートを使う断面)。</summary>
        public bool ShowNotification1113CompressionOption => UsesInsituConcrete;

        /// <summary>告示1113(第8) の許容せん断の対象 (場所打ちRC杭のみ)。</summary>
        public bool ShowNotification1113ShearOption => IsInsituRcSectionShown;

        /// <summary>既製杭の PC鋼材・鉄筋・鋼管のヤング係数 (n=5) の対象。</summary>
        public bool ShowGuideYoungsModulusOption => IsPrecastConcreteSectionShown;

        /// <summary>鋼材のモデル化グループを出すか (中身が 1 つでもあるとき)。</summary>
        public bool ShowSteelOptionsGroup =>
            ShowGuideYoungsModulusOption || ShowRebarYieldOption || ShowSteelPipeYieldOption;

        /// <summary>KCTB (TB工法) の項目。場所打ち鋼管コンクリート杭のみ。</summary>
        public bool ShowKctbOptions => IsInsituSteelPipeConcreteSectionShown;

        /// <summary>
        /// ファイバー M-φ の対象。AbstractPileSection 系すべて。
        /// 鋼管杭の鋼管部だけ別系統 (SteelPipeSection) なので対象外。
        /// </summary>
        public bool ShowFiberMPhiOption =>
            IsInsituRcSectionShown || IsInsituSteelPipeConcreteSectionShown
            || IsPrecastConcreteSectionShown || IsCftSectionShown;

        /// <summary>材料のモデル化パネルに出すものが 1 つでもあるか。</summary>
        public bool AreMaterialOptionsAvailable =>
            UsesInsituConcrete || ShowGuideYoungsModulusOption || ShowFiberMPhiOption;

        /// <summary>断面の切り替え時に、パネルの表示と各オプションの選択状態を出し直す。</summary>
        public void NotifyMaterialOptionsChanged()
        {
            OnPropertyChanged(nameof(IsInsituRcSectionShown));
            OnPropertyChanged(nameof(IsInsituSteelPipeConcreteSectionShown));
            OnPropertyChanged(nameof(IsPrecastConcreteSectionShown));
            OnPropertyChanged(nameof(IsCftSectionShown));
            OnPropertyChanged(nameof(UsesInsituConcrete));
            OnPropertyChanged(nameof(ShowRebarYieldOption));
            OnPropertyChanged(nameof(ShowSteelPipeYieldOption));
            OnPropertyChanged(nameof(ShowNotification1113CompressionOption));
            OnPropertyChanged(nameof(ShowNotification1113ShearOption));
            OnPropertyChanged(nameof(ShowGuideYoungsModulusOption));
            OnPropertyChanged(nameof(ShowSteelOptionsGroup));
            OnPropertyChanged(nameof(ShowKctbOptions));
            OnPropertyChanged(nameof(ShowFiberMPhiOption));
            OnPropertyChanged(nameof(AreMaterialOptionsAvailable));

            OnPropertyChanged(nameof(UseUnitGsiForConcreteE));
            OnPropertyChanged(nameof(IgnoreConcreteTensileStrength));
            OnPropertyChanged(nameof(UseReducedConcreteCompressiveStrength));
            OnPropertyChanged(nameof(RebarYieldAt11F));
            OnPropertyChanged(nameof(SteelPipeYieldAt11F));
            OnPropertyChanged(nameof(UseNotification1113Compression));
            OnPropertyChanged(nameof(UseNotification1113Shear));
            OnPropertyChanged(nameof(UseInsituUltimateEFunction));
            OnPropertyChanged(nameof(UseFiberMPhi));
            OnPropertyChanged(nameof(UseFiberNMForSteelPipeConcrete));
            OnPropertyChanged(nameof(UseUltimateStrain5000ForSteelPipeConcrete));
            OnPropertyChanged(nameof(ExcludeRebarFromAllowableLimitForSteelPipeConcrete));
            OnPropertyChanged(nameof(UseGuideYoungsModulus));

            // M-φ の主曲線は「解析用 M-φ 関係」に追随するので、比較の文言も出し直す
            OnPropertyChanged(nameof(FiberMPhiOverlayLabel));
            OnPropertyChanged(nameof(FiberMPhiOverlayCheckText));
        }

        // ── 場所打ち系 共通 ──

        public bool UseUnitGsiForConcreteE
        {
            get => Fundamental?.UseUnitGsiForConcreteE ?? false;
            set => ChangeOption(value,
                () => Fundamental!.UseUnitGsiForConcreteE,
                v => Fundamental!.UseUnitGsiForConcreteE = v,
                nameof(UseUnitGsiForConcreteE),
                "ヤング係数 Ec の算定の変更", affectsAnalysis: true);
        }

        public bool IgnoreConcreteTensileStrength
        {
            get => Fundamental?.IgnoreConcreteTensileStrength ?? false;
            set => ChangeOption(value,
                () => Fundamental!.IgnoreConcreteTensileStrength,
                v => Fundamental!.IgnoreConcreteTensileStrength = v,
                nameof(IgnoreConcreteTensileStrength),
                "コンクリート引張無視の変更", affectsAnalysis: true);
        }

        public bool UseReducedConcreteCompressiveStrength
        {
            get => Fundamental?.UseReducedConcreteCompressiveStrength ?? false;
            set => ChangeOption(value,
                () => Fundamental!.UseReducedConcreteCompressiveStrength,
                v => Fundamental!.UseReducedConcreteCompressiveStrength = v,
                nameof(UseReducedConcreteCompressiveStrength),
                "コンクリート圧縮低減の変更", affectsAnalysis: true);
        }

        public bool RebarYieldAt11F
        {
            get => Fundamental?.RebarYieldAt11F ?? false;
            set => ChangeOption(value,
                () => Fundamental!.RebarYieldAt11F,
                v => Fundamental!.RebarYieldAt11F = v,
                nameof(RebarYieldAt11F),
                "鉄筋の降伏応力度の変更", affectsAnalysis: true);
        }

        public bool UseInsituUltimateEFunction
        {
            get => Fundamental?.UseInsituUltimateEFunction ?? false;
            set => ChangeOption(value,
                () => Fundamental!.UseInsituUltimateEFunction,
                v => Fundamental!.UseInsituUltimateEFunction = v,
                nameof(UseInsituUltimateEFunction),
                "安全限界の応力度〜ひずみ度関係の変更", affectsAnalysis: true);
        }

        public bool UseFiberMPhi
        {
            get => Fundamental?.UseFiberMPhi ?? false;
            set => ChangeOption(value,
                () => Fundamental!.UseFiberMPhi,
                v => Fundamental!.UseFiberMPhi = v,
                nameof(UseFiberMPhi),
                "解析用 M-φ 関係の変更", affectsAnalysis: true,
                // 主曲線がこの選択に追随するので、比較チェックの文言も出し直す
                alsoNotify: [nameof(FiberMPhiOverlayLabel), nameof(FiberMPhiOverlayCheckText)]);
        }

        /// <summary>
        /// 許容圧縮応力度を告示1113(第8) で求めるか。
        /// 検定の耐力側 (使用限界・損傷限界 NM) にしか効かないので、解析結果は消さない。
        /// </summary>
        public bool UseNotification1113Compression
        {
            get => Fundamental?.UseNotification1113Compression ?? false;
            set => ChangeOption(value,
                () => Fundamental!.UseNotification1113Compression,
                v => Fundamental!.UseNotification1113Compression = v,
                nameof(UseNotification1113Compression),
                "許容圧縮応力度の変更", affectsAnalysis: false);
        }

        /// <summary>許容せん断を告示1113(第8) で求めるか (場所打ちRC杭)。耐力側のみ。</summary>
        public bool UseNotification1113Shear
        {
            get => Fundamental?.UseNotification1113Shear ?? false;
            set => ChangeOption(value,
                () => Fundamental!.UseNotification1113Shear,
                v => Fundamental!.UseNotification1113Shear = v,
                nameof(UseNotification1113Shear),
                "許容せん断の変更", affectsAnalysis: false);
        }

        /// <summary>
        /// 既製杭の PC鋼材・鉄筋・鋼管のヤング係数を指針の n=5 で与えるか。
        /// N-M 曲線・M-φ に効くので、変更時は解析結果の確認を出す。
        /// </summary>
        public bool UseGuideYoungsModulus
        {
            get => Fundamental?.UseGuideYoungsModulus ?? false;
            set => ChangeOption(value,
                () => Fundamental!.UseGuideYoungsModulus,
                v => Fundamental!.UseGuideYoungsModulus = v,
                nameof(UseGuideYoungsModulus),
                "既製杭の鋼材ヤング係数の変更", affectsAnalysis: true);
        }

        // ── 場所打ち鋼管コンクリート杭だけの項目 ──

        public bool SteelPipeYieldAt11F
        {
            get => Fundamental?.SteelPipeYieldAt11F ?? false;
            set => ChangeOption(value,
                () => Fundamental!.SteelPipeYieldAt11F,
                v => Fundamental!.SteelPipeYieldAt11F = v,
                nameof(SteelPipeYieldAt11F),
                "鋼管の降伏応力度の変更", affectsAnalysis: true);
        }

        public bool UseFiberNMForSteelPipeConcrete
        {
            get => Fundamental?.UseFiberNMForSteelPipeConcrete ?? false;
            set => ChangeOption(value,
                () => Fundamental!.UseFiberNMForSteelPipeConcrete,
                v => Fundamental!.UseFiberNMForSteelPipeConcrete = v,
                nameof(UseFiberNMForSteelPipeConcrete),
                "本体部の設計法（許容時 N-M）の変更", affectsAnalysis: false);
        }

        public bool UseUltimateStrain5000ForSteelPipeConcrete
        {
            get => Fundamental?.UseUltimateStrain5000ForSteelPipeConcrete ?? false;
            set => ChangeOption(value,
                () => Fundamental!.UseUltimateStrain5000ForSteelPipeConcrete,
                v => Fundamental!.UseUltimateStrain5000ForSteelPipeConcrete = v,
                nameof(UseUltimateStrain5000ForSteelPipeConcrete),
                "終局の圧縮縁ひずみ εcu の変更", affectsAnalysis: true);
        }

        public bool ExcludeRebarFromAllowableLimitForSteelPipeConcrete
        {
            get => Fundamental?.ExcludeRebarFromAllowableLimitForSteelPipeConcrete ?? false;
            set => ChangeOption(value,
                () => Fundamental!.ExcludeRebarFromAllowableLimitForSteelPipeConcrete,
                v => Fundamental!.ExcludeRebarFromAllowableLimitForSteelPipeConcrete = v,
                nameof(ExcludeRebarFromAllowableLimitForSteelPipeConcrete),
                "許容時の判定材料の変更", affectsAnalysis: false);
        }

        // ── 変更の適用 ──

        /// <summary>
        /// オプションを 1 つ変える。基本設定側 (FundamentalViewModel) と同じ扱いにする。
        ///
        /// <paramref name="affectsAnalysis"/> が true のものは解析 (M-φ・変形・応力) に効くので、
        /// 解析結果を消してよいか確認する。断られたら値を戻す。
        /// false のものは検定の耐力側にしか効かないので確認しない。
        ///
        /// 変更後は曲線を描き直す。曲線を見ながら効きを確かめられるようにするのが、
        /// この画面にオプションを置いた理由なので、描き直さないと意味がない。
        /// </summary>
        private void ChangeOption(
            bool value, Func<bool> getter, Action<bool> setter,
            string propertyName, string reason, bool affectsAnalysis,
            string[]? alsoNotify = null)
        {
            if (Fundamental == null) return;
            if (_suppressMaterialOptionChange) return;

            bool oldValue = getter();
            if (oldValue == value) return;

            if (affectsAnalysis
                && !_mainWindowViewModel.CheckAndResetAnalysisResultsKeepingSplit(reason))
            {
                // 断られたので選択を戻す。戻す代入で再入しないよう抑制する。
                _suppressMaterialOptionChange = true;
                try { OnPropertyChanged(propertyName); }
                finally { _suppressMaterialOptionChange = false; }
                return;
            }

            setter(value);
            _mainWindowViewModel.ApplyConcreteModelOptions();

            OnPropertyChanged(propertyName);
            foreach (string extra in alsoNotify ?? []) OnPropertyChanged(extra);
            RedrawAfterMaterialOptionChange();
        }

        private bool _suppressMaterialOptionChange;

        /// <summary>
        /// オプション変更後に曲線を描き直す。
        ///
        /// <b>この画面の断面は現在の入力モデルに居ない。</b>杭体ウィンドウが
        /// <c>InputModel.PileBodies</c> の複製を編集しており、断面ウィンドウはその複製の断面を
        /// 受け取るため、<c>ApplyConcreteModelOptions</c> のキャッシュ破棄
        /// (CurrentInputModel の断面をたどる) が届かない。
        /// 破棄しないと N-M・N-Q が<b>前の設定のまま描かれ続ける</b> (実際にそうなっていた)。
        /// この断面のキャッシュはここで自分で捨てる。
        /// </summary>
        private void RedrawAfterMaterialOptionChange()
        {
            try
            {
                if (PileSection != null)
                {
                    PileSection.InvalidateComputedCaches();

                    // ξ→Ec のオプションは PileSection.ConcreteE (諸元・EA/EI) にも効く。
                    // 既製杭は式ベースではないので対象外 (ApplyConcreteModelOptions と同じ扱い)。
                    if (PileSection.PileBodyType != PileTypeNames.PrecastConcrete)
                        PileSection.RecalculateConcreteE();
                }

                // N-M / M-φ / M-θ は ChartUpdate、N-Q は専用の描画ヘルパー
                ChartUpdate();
                DrawNQForCurrentPile();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[杭断面] 材料オプション変更後の再描画に失敗");
            }
        }
    }
}

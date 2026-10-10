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
    // PileSection のうち、断面諸量 (A・EA・EI・GJ・W、腐食考慮・公称) と諸元表。
    public partial class PileSection
    {
        // 杭全断面積(mm2)
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double A0 => Ac + RebarAgInSection + TendonAp + PipeAs;

        /// <summary>
        /// コンクリート (と鉄筋) を持つ断面か。鋼管杭の「鋼管部」(コンクリートを充填しない区間) は持たない。
        ///
        /// <para>鋼管杭の区間は「コンクリート充填鋼管部」として作られ、充填コンクリートの径 (ConcreteOutDia) と厚さが入る。
        /// 鋼管部へ切り替えてもそれらは残るので、以前は存在しないコンクリートが EI・EA・自重 W・GJ に入っていた
        /// (M-φ は鋼管だけから作るので、要素の初期剛性と M-φ の勾配も食い違った)。入力の値は残し、断面の量で除く。</para>
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool HasConcreteCore => !(PileBodyType == PileTypeNames.SteelPipe && PileSectionType != PileTypeNames.CftSection);

        /// <summary>断面の量に入れる主筋の断面積 (鋼管部では 0)。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        private double RebarAgInSection => HasConcreteCore ? MainBarAg : 0.0;

        // 杭コンクリート断面積(mm2)
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double Ac => HasConcreteCore ? (ConcreteOutDia - ConcreteThickness) * Math.PI * ConcreteThickness - MainBarAg - TendonAp : 0.0;

        // 杭単位長さ重量 (kN/m)
        //
        // PHC節杭 はカタログ標準質量をそのまま用いる。節部の体積を推定形状から積分しても
        // カタログ質量は再現できない（既製杭の ConcreteGamma = 26 kN/m³ で RMS 3.4%・最大 7.0% 相違。
        // 密度と節長さを自由に振った最良フィットでも RMS 1.5% 止まりで、しかもその γ は 27.4 kN/m³ と
        // コンクリートとして非現実的。継手金物等を含むためと推定）。メーカー公称値が唯一正確な出所。
        // この結果、基本設定のコンクリート単位体積重量 ConcreteGamma は節杭の自重に効かない。
        //
        // ※ コンクリートの断面積は Ac をそのまま使う。Ac は定義の時点で
        //   主筋・テンドンを控除済みなので、ここで再度引くと鋼材ぶんを二重に控除して
        //   自重が過小になる (PHC で 0.5〜1.1%、PRC で 1.4〜5.5%)。
        //   押込み側は軸力の過小評価 = 危険側、引抜き側は抵抗の過小評価 = 安全側だった。
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double W => IsNodularPile && CatalogMassPerM > 0
            ? CatalogMassPerM * UnitConversion.TON_TO_KN   // t/m -> kN/m
            : ((RebarAgInSection + TendonAp + PipeAs) * UnitConversion.STEEL_UNIT_WEIGHT + Ac * ConcreteGamma) * Math.Pow(10, -6);

        // 軸剛性 (kN)
        // 合成断面: コンクリート + 主筋 + PC鋼材 + 鋼管（Es·As）。鋼管を持たない断面では PipeAs=0 のため影響なし。
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        // ※ 鋼管は腐食後の寸法。EI と揃える (2026-09-12 に利用者の判断で公称から腐食後へ。重量 W は公称のまま)
        public double EA => EACorroded;

        // 公称寸法 (腐食代を見込まない) の軸剛性 (kN)。諸元表の「腐食非考慮」欄だけが使う
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double EANominal => (ConcreteE * Ac + MainBarEr * RebarAgInSection + TendonEp * TendonAp + PipeEs * PipeAs) * 0.001;

        // ───── 換算断面二次モーメント（曲げ剛性 EI の中身）─────
        //
        // Ie = I + (1/2)(np - 1)·Ap·(Dp/2)^2 + (1/2)(nr - 1)·Ag·(Dr/2)^2   [+ 鋼管項]
        //   np = Ep/Ec (PC鋼棒)   nr = Er/Ec (異形棒鋼)
        //
        // 鋼材ごとに配置円 (PCD) も換算比も分けている。JP-NPRC の PRC節杭は 232 行すべてで
        // PC鋼棒の PCD と異形棒鋼の PCD が異なり、分離するとカタログ Ie と最大 0.046% 一致するのに対し、
        // PC鋼棒の PCD で一括すると 1.41% ずれる。
        // 「基礎部材の強度と変形性能」の (Ap+Ag)·rp^2 は PCD が近いことを前提にした簡略式。

        /// <summary>コンクリート中空断面の断面二次モーメント (mm4)。</summary>
        private double ConcreteI =>
            Math.PI * (Math.Pow(ConcreteOutDia, 4) - Math.Pow(ConcreteOutDia - 2 * ConcreteThickness, 4)) / 64.0;

        /// <summary>
        /// PC鋼棒の換算断面二次モーメント項 (mm4)。<c>(1/2)(np - 1)·Ap·(Dp/2)^2</c>。
        /// EA には <c>Ep·Ap</c> が入っているのに EI にはこの項が無く、
        /// カタログの換算断面二次モーメント Ie より PHC系で最大 5.9%、
        /// PRC節杭で最大 29.5% 小さくなっていた (2026-08-20 修正)。
        /// </summary>
        private double TendonIEquivalent =>
            ConcreteE > 0 && TendonAp > 0 && TendonDp > 0 && TendonEp > 0
                ? 0.5 * (TendonEp / ConcreteE - 1) * TendonAp * Math.Pow(TendonDp, 2) / 4.0
                : 0.0;

        /// <summary>
        /// 異形棒鋼の換算断面二次モーメント項 (mm4)。<c>(1/2)(nr - 1)·Ag·(Dr/2)^2</c>。
        /// <c>ConcreteOutDia - 2·MainBarCenterCover</c> は鉄筋配置直径 <see cref="MainBarDr"/> と一致する
        /// (製品ライブラリ適用時に <c>MainBarCenterCover = (ConcreteOutDia - MainBarDr)/2</c> と設定するため)。
        /// </summary>
        private double MainBarIEquivalent =>
            ConcreteE > 0 && MainBarAg > 0 && MainBarEr > 0
                ? 0.5 * (MainBarEr / ConcreteE - 1) * MainBarAg
                  * Math.Pow(ConcreteOutDia - 2 * MainBarCenterCover, 2) / 4.0
                : 0.0;

        // 曲げ剛性 (kNm2)
        // ※ 換算項の断面積は MainBarAg / TendonAp（鋼材断面積）を使用。
        //    A0（全断面積）を使用すると過大評価になるので注意
        // ※ コンクリート断面は中空断面（ConcreteOutDia - 2*ConcreteThickness = 内径）として計算
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        // ※ 鋼管は腐食後の寸法 (外径 − 2×腐食代。内径 = コンクリート外径は不変) で計算する。
        //    M-φ・耐力は元から腐食後の寸法なので、弾性剛性だけ公称寸法だと 1 本の杭の中で初期剛性と
        //    M-φ が食い違っていた。2026-09-12 に利用者の判断で腐食後に揃えた (PileSectionCorrosionTests)。
        public double EI => EICorroded;

        // 公称寸法 (腐食代を見込まない) の曲げ剛性 (kNm2)。諸元表の「腐食非考慮」欄だけが使う
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double EINominal => ((HasConcreteCore ? ConcreteE * (ConcreteI + TendonIEquivalent + MainBarIEquivalent) : 0.0)
            + PipeEs * Math.PI * (Math.Pow(PipeDia, 4) - Math.Pow(PipeDia - 2 * PipeTs, 4)) / 64.0) * Math.Pow(10, -9);

        // ===== 腐食代考慮の断面諸量 =====
        // 腐食モデル: 鋼管外径を 2×腐食代 だけ縮小、管厚を腐食代だけ減じ、内径（コンクリート外径）は不変。
        // （解析用の EI は上の EI = 腐食後。PipeAs/A0/W/EA は公称寸法のままで、ここの値は諸元表の比較用）
        private double CorrodedPipeOuterDiaDisp => PipeDia - 2.0 * CorrosionDepth;
        private double PipeInnerDiaDisp => PipeDia - 2.0 * PipeTs; // = コンクリート外径（腐食で不変）

        // 腐食考慮 鋼管断面積 (mm2)
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        // 鋼管の無い断面 (PipeDia = 0) では、腐食後外径が −2×腐食代になって偽の鋼管項が出るので 0
        public double PipeAsCorroded => PipeDia > 0
            ? Math.PI / 4.0 * (Math.Pow(CorrodedPipeOuterDiaDisp, 2) - Math.Pow(PipeInnerDiaDisp, 2))
            : 0.0;

        // 腐食考慮 杭全断面積 (mm2)
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double A0Corroded => Ac + RebarAgInSection + TendonAp + PipeAsCorroded;

        // 腐食考慮 杭単位長さ重量 (kN/m)
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        // 公称の W と同じ鋼材量で数え、鋼管だけを腐食後の断面積にする (諸元表の「腐食考慮」欄だけが使う)。
        // 以前は主筋に MainBarAg を使っており、鋼管部のようにコンクリートの無い区間でも残った主筋の重量を足し、
        // 節杭のカタログ重量も見ていなかったので、公称の欄と腐食考慮の欄で鋼管以外の項が食い違った
        public double WCorroded => IsNodularPile && CatalogMassPerM > 0
            ? W
            : ((RebarAgInSection + TendonAp + PipeAsCorroded) * UnitConversion.STEEL_UNIT_WEIGHT + Ac * ConcreteGamma) * Math.Pow(10, -6);

        // 腐食考慮 軸剛性 (kN)
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double EACorroded =>
            (ConcreteE * Ac + MainBarEr * RebarAgInSection + TendonEp * TendonAp + PipeEs * PipeAsCorroded) * 0.001;

        // 腐食考慮 曲げ剛性 (kNm2) — 鋼管項のみ腐食後外径で置換
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        // 鋼管の無い断面 (PipeDia = 0) では、腐食後外径が −2×腐食代になって偽の鋼管項が出るので足さない
        public double EICorroded => ((HasConcreteCore ? ConcreteE * (ConcreteI + TendonIEquivalent + MainBarIEquivalent) : 0.0)
            + (PipeDia > 0
                ? PipeEs * Math.PI * (Math.Pow(CorrodedPipeOuterDiaDisp, 4) - Math.Pow(PipeInnerDiaDisp, 4)) / 64.0
                : 0.0)) * Math.Pow(10, -9);

        // ねじり剛性 (kNm2)
        // ※ コンクリート断面は中空断面として計算
        // ねじり剛性 (kNm2)
        // 円形断面のねじり定数は断面二次極モーメント J = Ip = π(D^4 - d^4)/32。
        // 曲げの断面二次モーメント I = π(D^4 - d^4)/64 を使っていたため 2 倍過小だった。
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double GJ => ((HasConcreteCore ? GetG(ConcreteE, 0.2) * Math.PI * (Math.Pow(ConcreteOutDia, 4) - Math.Pow(ConcreteOutDia - 2 * ConcreteThickness, 4)) / 32.0 : 0.0) +
            GetG(PipeEs, 0.3) * Math.PI * (Math.Pow(PipeDia, 4) - Math.Pow(PipeDia - 2 * PipeTs, 4)) / 32.0) * Math.Pow(10, -9);

        // せん断剛性
        private static double GetG(double e, double nu)
        {
            return e / (2 * (1 + nu));
        }

        private ObservableCollection<Spec>? _selectedPileSectionSpecification;
        public ObservableCollection<Spec> SelectedPileSectionSpecification
        {
            //get => _selectedPileSectionSpecification;
            get => _selectedPileSectionSpecification ??= [];
            set => SetProperty(ref _selectedPileSectionSpecification, value);
        }

        /// <summary>
        /// 断面タイプの補足（メーカー・製品系列と、断面耐力の算定がどの杭に準じるか）。
        /// 「BF.S先端軸部」のような製品名だけの断面タイプは、それが PHC節杭 であることが
        /// 名前から読み取れないため、諸元表の備考で補う。
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public string SectionTypeNote => PileSectionType switch
        {
            PileTypeNames.PhcNodular => "ジャパンパイル JP-NPH パイル (PHC節杭)。断面耐力は PHC杭 と同一",
            PileTypeNames.PrcNodular => "ジャパンパイル JP-NPRC パイル (PRC節杭)。断面耐力は PRC杭 と同一",
            PileTypeNames.PrcNodularPhcPart => "ジャパンパイル JP-NPRC パイルの PHC部 (PRC節杭)。断面耐力は PHC杭 と同一",
            PileTypeNames.BfsHead => "三谷セキサン BF.S パイル (PHC節杭) の頭部軸部。断面耐力は PHC杭 と同一",
            PileTypeNames.BfsTip => "三谷セキサン BF.S パイル (PHC節杭) の先端軸部。節はこちらに付く。断面耐力は PHC杭 と同一",
            _ => "",
        };

        public void SetSpecs()
        {
            SelectedPileSectionSpecification = [
                new Spec("杭断面タイプ", "", PileSectionType, "", SectionTypeNote),
                new Spec("杭径", "D", $"{PileDiameter:N0}", "mm")];

            if (PileSectionType == PileTypeNames.SteelPipeConcreteSection || PileSectionType == PileTypeNames.Sc || PileSectionType == PileTypeNames.SteelPipe)
            {
                string notePipeDia =
                    (PileSectionType == PileTypeNames.SteelPipeConcreteSection && PipeDia > 2700) ? "([強度と変形性能]4.1,3)2700より大" :
                    (PileSectionType == PileTypeNames.SteelPipeConcreteSection && PipeDia < 600) ? "([強度と変形性能]4.1,3)600未満" :
                    (PileSectionType == PileTypeNames.SteelPipe && PipeDia > 2000) ? "([強度と変形性能]8.1.4(1))2000より大" :
                    (PileSectionType == PileTypeNames.SteelPipe && PipeDia < 318.5) ? "([強度と変形性能]8.1.4(1))318.5未満" : "";

                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管外径", "PipeDia", $"{PipeDia:N0}", "mm", notePipeDia));

                string notePipeTs =
                    (PileSectionType == PileTypeNames.SteelPipeConcreteSection && PipeTs < 6) ? "([強度と変形性能]4.1,3)6未満" :
                    (PileSectionType == PileTypeNames.SteelPipe && PipeTs > 6) ? "([強度と変形性能]8.1.4(2))6未満" : "";

                // 1 mm 未満の肉厚が「0」に見えないよう、端数は 2 桁まで出す
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管厚", "Ts", $"{PipeTs:#,0.##}", "mm", notePipeTs));
                // 肉厚が腐食代以下だと、腐食を考えた鋼管の断面積が負になる (入力の診断でも止める)
                string noteCorrosion = CorrosionDepth >= PipeTs ? "肉厚 Ts 以上 (腐食後の肉厚が 0 以下)" : "";
                SelectedPileSectionSpecification.Add(
                    new Spec("腐食代", "", $"{CorrosionDepth:N1}", "mm", noteCorrosion));
                string noteDonTs =
                    (PileSectionType == PileTypeNames.SteelPipeConcreteSection && PipeDia / PipeTs > 125) ? "([強度と変形性能]4.1,3)125より大" :
                    (PileSectionType == PileTypeNames.SteelPipe && PipeDia / PipeTs > 100) ? "([強度と変形性能]8.1.4(2))100より大" : "";
                string pipeDiaTsValue = (PipeTs != 0) ? $"{PipeDia / PipeTs:N1}" : "N/A";
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管径厚比", "Tc/Ts", pipeDiaTsValue, "", noteDonTs));
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管断面積(腐食非考慮)", "As", $"{PipeAs:N0}", "mm2"));
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管断面積(腐食考慮)", "As'", $"{PipeAsCorroded:N0}", "mm2"));
                // 製品ライブラリの選択では鋼管規格は切り替わらない (杭径・鋼管厚だけ転記される)。
                // メーカー製品は規格が決まっているので、選択中の規格と食い違っていたら諸元表で知らせる。
                // 製品ライブラリの降伏点 (メーカー公称) と、鋼管規格の基準強度 F は別の量なので
                // (SKK490 なら JIS の規格降伏点 315 / 基準強度 F=325) わずかな差では知らせない。
                // 規格の選び間違い (SKK490 の製品に SKK400 を選ぶ等) だけを拾う。
                string noteGrade = "";
                double productF = SelectedPrecastPile?.Fts ?? 0.0;
                if (productF > 0)
                {
                    double gradeF = SteelPipeGrades.GetProperties(PipeGrade ?? "SKK400").F;
                    if (gradeF < productF * 0.95)
                        noteGrade = $"製品ライブラリの鋼管降伏点 {productF:N0} N/mm² に対し、" +
                                    $"選択中の鋼管規格 {PipeGrade} の基準強度は {gradeF:N0} N/mm² です。" +
                                    "製品選択では鋼管規格は切り替わりません。規格の選択をご確認ください";
                }
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管規格", "", PipeGrade, "", noteGrade));
                SelectedPileSectionSpecification.Add(
                    new Spec("鋼管ヤング係数", "Es", $"{PipeEs:N0}", "N/mm2"));
            }

            if (PileSectionType != PileTypeNames.SteelPipe)
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート外径", "Dc", $"{ConcreteOutDia:N0}", "mm"));

            if (IsHollowPrecastSection)
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート肉厚", "Dt", $"{ConcreteThickness:N0}", "mm"));

            // 節杭 固有: 節部径とカタログ標準質量、および周面抵抗の扱いの明示
            if (IsNodularPile)
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("節部径", "Do", $"{NodeDiameter:N0}", "mm",
                        "支持力の周面抵抗は軸部径で算定 (節の効果は未考慮)"));
                if (CatalogMassPerM > 0)
                    SelectedPileSectionSpecification.Add(
                        new Spec("カタログ標準質量", "m", $"{CatalogMassPerM:N3}", "t/m",
                            "自重はこの値による (コンクリート単位体積重量 γc は不適用)"));
                else
                    SelectedPileSectionSpecification.Add(
                        new Spec("自重の算定", "", "軸部断面 × γc", "",
                            "カタログに標準質量表が無いため軸部体積による (節の分は未計上)"));
                SelectedPileSectionSpecification.Add(
                    new Spec("杭頭タイプ", "", NodularHeadType, "", NodularHeadNote));
                if (NodeHeadDiameter > 0)
                {
                    SelectedPileSectionSpecification.Add(
                        new Spec("拡頭部径", "Dt", $"{NodeHeadDiameter:N0}", "mm"));
                    SelectedPileSectionSpecification.Add(
                        new Spec("拡頭部長さ", "Lt", $"{NodeHeadLength:N0}", "mm"));
                }
            }

            if (PileSectionType != PileTypeNames.SteelPipe)
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート断面積", "Ac", $"{Ac:N0}", "mm2"));
                string noteFc =
                    PileSectionType == PileTypeNames.RcSection && ConcreteFc < 21 ? "([強度と変形性能]3.1,2)21N/mm2未満" :
                    PileSectionType == PileTypeNames.RcSection && ConcreteFc > 40 ? "([強度と変形性能]3.1,2)40N/mm2より大" :
                    PileSectionType == PileTypeNames.SteelPipeConcreteSection && ConcreteFc < 21 ? "([強度と変形性能]4.1,2)21N/mm2未満" :
      PileSectionType == PileTypeNames.SteelPipeConcreteSection && ConcreteFc > 40 ? "([強度と変形性能]4.1,2)40N/mm2より大" : "";

                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート基準強度", "Fc", $"{ConcreteFc:N0}", "N/mm2", noteFc));
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート単位体積重量", "γc", $"{ConcreteGamma:N1}", "kN/m3"));
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート縦弾性係数", "Ec", PileDesign.Common.NumberDisplay.Modulus(ConcreteE), "N/mm2"));
            }

            if (PileBodyType == PileTypeNames.InsituRc || PileBodyType == PileTypeNames.InsituSteelPipeConcrete)
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリート施工品質管理係数", "ξ", $"{ConcreteGsi:N2}", ""));
            }

            if (IsPrestressedPrecastSection)
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("コンクリートプレストレス", "σe", $"{Prestress:N1}", "N/mm2"));
            }

            if (IsPrestressedPrecastSection)
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("PC鋼材断面積", "Ap", $"{TendonAp:N0}", "mm2"));

                SelectedPileSectionSpecification.Add(
                    new Spec("PC鋼材降伏点", "σpy", $"{TendonSigmaPy:N0}", "N/mm2"));
                SelectedPileSectionSpecification.Add(
                    new Spec("PC鋼材最大耐力", "σpu", $"{TendonSigmaPu:N0}", "N/mm2"));
                SelectedPileSectionSpecification.Add(
                    new Spec("PC鋼材ヤング係数", "Ep", $"{TendonEp:N0}", "N/mm2"));
            }

            if (PileSectionType == PileTypeNames.RcSection || PileSectionType == PileTypeNames.Prc ||
                (PileSectionType == PileTypeNames.SteelPipeConcreteSection && MainBarAg > 0))
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋数-呼び径", "", MainBarNum.ToString() + "-" + MainBarSize, ""));
                // 鋼管規格と同じく、製品ライブラリの選択では鉄筋規格は切り替わらない
                // (本数・径・配筋径だけが転記される)。メーカー製品は規格が決まっているので、
                // 選択中の規格の降伏点と食い違っていたら諸元表で知らせる。
                string noteBarSpec = "";
                double productFtr = SelectedPrecastPile?.Ftr ?? 0.0;
                if (productFtr > 0)
                {
                    double specFy = MainBars.GradeYieldStrength(MainBarSpec);
                    if (Math.Abs(specFy - productFtr) > 1.0)
                        noteBarSpec = $"製品ライブラリの主筋降伏点は {productFtr:N0} N/mm² です" +
                                      $"(選択中の {MainBarSpec} は {specFy:N0} N/mm²)。" +
                                      "製品選択では鉄筋規格は切り替わりません。規格の選択をご確認ください";
                }
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋規格", "", $"{MainBarSpec}", "", noteBarSpec));
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋断面積", "Ag", $"{MainBarAg:N0}", "mm2"));
                string notePg =
                    PileSectionType == PileTypeNames.RcSection && MainBarAg / A0 * 100 < 0.4 ? "([強度と変形性能]3.1,5(5))0.4%未満" : "";
                string mainBarRatio = (A0 != 0) ? $"{MainBarAg / A0 * 100:N2}" : "N/A";
                SelectedPileSectionSpecification.Add(
                    new Spec("主筋比", "pg", mainBarRatio, "%", notePg));
                //SelectedPileSectionSpecification.Add(
                //    new Spec("鉄筋規格降伏点", "Ftr", $"{MainBarFtr:N0}", "mm2"));
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋重心かぶり厚", "", $"{MainBarCenterCover:N0}", "mm"));
                SelectedPileSectionSpecification.Add(
                    new Spec("鉄筋ヤング係数", "Er", $"{MainBarEr:N0}", "N/mm2"));
            }

            if (PileSectionType == PileTypeNames.RcSection || PileSectionType == PileTypeNames.Prc)
            {
                SelectedPileSectionSpecification.Add(
                    new Spec("せん断補強筋規格", "", $"{HoopSpec}", ""));

                if (PileSectionType == PileTypeNames.Prc)
                {
                    SelectedPileSectionSpecification.Add(
                        new Spec("せん断補強筋比×降伏点", "ps・σy", $"{HoopPsSigmay:N2}", "N/mm2"));
                }
                else
                {
                    var hoopMethod = ShearReinforcementMethods.Get(HoopMethod);
                    if (hoopMethod != null)
                    {
                        SelectedPileSectionSpecification.Add(
                            new Spec("せん断補強筋の工法", "", hoopMethod.Name, "",
                                $"{hoopMethod.Certification}。せん断耐力は本工法の設計施工指針の式による"));

                        bool isSafetyShortTerm =
                            HoopDamageFormula == ShearReinforcementMethods.DamageFormulaSafetyShortTerm;
                        SelectedPileSectionSpecification.Add(
                            new Spec("損傷限界の算定式", "", HoopDamageFormula, "",
                                isSafetyShortTerm
                                    ? $"設計用せん断力を水平荷重時せん断力の {ShearDesignMagnification:N1} 倍に割り増して検定する"
                                    : hoopMethod.DamageIncludesHoop
                                        ? "b·j·{ sfs + 0.5·wft·(pw − 0.001) }"
                                        : "sfs·Ac/κ (せん断補強筋を考慮しない式)"));

                        bool isTrussArch =
                            HoopUltimateFormula == ShearReinforcementMethods.UltimateFormulaTrussArch;
                        SelectedPileSectionSpecification.Add(
                            new Spec("終局の算定式", "", HoopUltimateFormula, "",
                                isTrussArch
                                    ? "トラス項のみ (b·jt·pw·σwy、上限 (ν·Fc/3)·b·jt)。"
                                      + "アーチ項は部材長 L が入力に無いため安全側に 0 とする"
                                    : "大野・荒川 min 式"));
                    }

                    // 間隔の上限は工法で違う (エムケーパイルリング785 は杭頭から杭径の 5 倍まで
                    // 150mm 以下・それ以外 300mm 以下、ウルボンは 150mm 以下)。
                    // どの深さの区間かはこの断面だけでは決まらないので、緩い方を超えたときに知らせる。
                    string noteHoopSpacing =
                        hoopMethod != null && HoopSpacing > hoopMethod.SpacingMax
                            ? $"({hoopMethod.Name})構造規定の{hoopMethod.SpacingMax:N0}mmより大"
                            : hoopMethod != null && HoopSpacing > hoopMethod.SpacingMaxNearPileHead
                            ? $"({hoopMethod.Name})杭頭から杭径の{hoopMethod.PileHeadRangeInDiameters:N0}倍の範囲は{hoopMethod.SpacingMaxNearPileHead:N0}mm以下"
                            : hoopMethod == null && HoopSpacing > 300 ? "([強度と変形性能]3.1,5(6))300より大" : "";
                    SelectedPileSectionSpecification.Add(
                        new Spec("せん断補強筋呼び径@間隔", "", $"{HoopSize}@{HoopSpacing}", "", noteHoopSpacing));
                    SelectedPileSectionSpecification.Add(
                        new Spec("せん断補強筋降伏点", "σy", $"{HoopSigmay:N0}", "N/mm2",
                            hoopMethod != null ? "終局限界せん断力の算定に使う材料強度" : ""));
                    if (hoopMethod != null)
                    {
                        SelectedPileSectionSpecification.Add(
                            new Spec("せん断補強筋短期許容応力度", "wft", $"{HoopWft:N0}", "N/mm2",
                                "短期許容せん断力の補強筋項に使う"));
                    }

                    double pwPercent = HoopPw * 100.0;
                    string noteHoopPw =
                        hoopMethod != null && pwPercent < hoopMethod.MinPw * 100.0
                            ? $"({hoopMethod.Name})構造規定の{hoopMethod.MinPw * 100:N1}%未満"
                            : hoopMethod != null && pwPercent > hoopMethod.MaxPw * 100.0
                            ? $"({hoopMethod.Name})構造規定の{hoopMethod.MaxPw * 100:N1}%より大"
                            : hoopMethod != null && pwPercent < hoopMethod.UltimateMinPw * 100.0
                            ? $"({hoopMethod.Name})終局を検討する場合は{hoopMethod.UltimateMinPw * 100:N1}%以上"
                            : hoopMethod != null && pwPercent > hoopMethod.UltimatePwCap * 100.0
                            ? $"({hoopMethod.Name})終局は{hoopMethod.UltimatePwCap * 100:N1}%を上限として算定"
                            : hoopMethod == null && PileSectionType == PileTypeNames.RcSection && pwPercent < 0.1
                            ? "([強度と変形性能]3.1,5(6))0.1%未満" : "";
                    string hoopPwValue = (HoopSpacing != 0 && ConcreteOutDia != 0) ? $"{pwPercent:N2}" : "N/A";
                    SelectedPileSectionSpecification.Add(
                        new Spec("せん断補強筋比", "pw", hoopPwValue, "%", noteHoopPw));
                    SelectedPileSectionSpecification.Add(
                        new Spec("せん断補強筋重心かぶり厚", "", $"{HoopCenterCover:N0}", "mm"));

                    // 工法の適用範囲 (杭径・Fc・引張軸力) は入力を見れば押せる前に分かる。
                    if (hoopMethod != null)
                    {
                        bool hasDiaLimit = !double.IsPositiveInfinity(hoopMethod.PileDiaMax);
                        string diaText = hasDiaLimit
                            ? $"杭径 {hoopMethod.PileDiaMin:N0}〜{hoopMethod.PileDiaMax:N0}mm"
                            : "杭径 規定なし";
                        // 範囲の判定は検定と同じもの (HoopMethodOutOfScopeReason) を使う
                        string noteRange = HoopMethodOutOfScopeReason
                            ?? (hoopMethod.DamageExcludesTension
                                ? "損傷限界せん断力時に引張軸力になる杭体は本工法の適用外です (その検定は「適用範囲外」になります)"
                                : "");
                        SelectedPileSectionSpecification.Add(
                            new Spec("せん断補強筋工法の適用範囲", "",
                                $"{diaText} / Fc {hoopMethod.FcMin:N0}〜{hoopMethod.FcMax:N0}N/mm2",
                                "", noteRange));
                    }
                }
            }

            //new Spec("PCリング定着筋呼び径", "", BarSize, ""),

            // 鋼管を持つ断面（鋼管コンクリート部/SC杭/鋼管杭）では腐食考慮/非考慮の両方を記載する。
            bool hasPipe = PipeAs > 0;
            void AddCorrodible(string name, string sym, string unit, string fmt, double nominal, double corroded)
            {
                if (hasPipe)
                {
                    SelectedPileSectionSpecification.Add(new Spec($"{name}(腐食非考慮)", sym, nominal.ToString(fmt), unit));
                    SelectedPileSectionSpecification.Add(new Spec($"{name}(腐食考慮)", sym + "'", corroded.ToString(fmt), unit));
                }
                else
                {
                    SelectedPileSectionSpecification.Add(new Spec(name, sym, nominal.ToString(fmt), unit));
                }
            }

            AddCorrodible("杭の全断面積", "A0", "mm2", "N0", A0, A0Corroded);
            AddCorrodible("杭の単位長さ重量", "W", "kN/m", "N2", W, WCorroded);
            AddCorrodible("杭の弾性軸剛性", "EA", "kN", "N0", EANominal, EACorroded);
            AddCorrodible("杭の弾性曲げ剛性", "EI", "kNm2", "N0", EINominal, EICorroded);
            //new Spec("PCリングスパイラル巻数", "", SpiralNum.ToString(), "")
            //return specs;
        }

    }
}

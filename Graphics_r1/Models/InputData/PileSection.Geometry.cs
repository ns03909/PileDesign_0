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
    // PileSection のうち、杭種・断面タイプと寸法 (杭径・節杭の諸元・腐食代・座屈長など)。
    public partial class PileSection
    {
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string PileDescription
        {
            get
            {
                if (PileBodyType == PileTypeNames.InsituRc ||
                    (PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSectionType == PileTypeNames.RcSection))
                {
                    return $"D={PileDiameter}({MainBarNum}-{MainBarSize})";
                }
                else if (PileBodyType == PileTypeNames.InsituSteelPipeConcrete && PileSectionType == PileTypeNames.SteelPipeConcreteSection)
                {
                    if (MainBarNum != 0)
                    {
                        return $"{PipeDia}x{PipeTs}({MainBarNum}-{MainBarSize}) ";
                    }
                    else
                    {
                        return $"{PipeDia}x{PipeTs}";
                    }
                }
                else if (PileBodyType == PileTypeNames.PrecastConcrete)
                {
                    return SelectedPrecastPile.Name;
                }
                else if (PileBodyType == PileTypeNames.SteelPipe)
                {
                    return SelectedSteelPipePileName;
                }

                return string.Empty;
            }
        }

        private string _pileBodyType = PileTypeNames.InsituRc;
        public string PileBodyType
        {
            get => _pileBodyType;
            set
            {
                // 値のバリデーション
                var validTypes = new[]
                {
                    PileTypeNames.InsituRc,
                    PileTypeNames.InsituSteelPipeConcrete,
                    PileTypeNames.PrecastConcrete,
                    PileTypeNames.SteelPipe
                };
                bool isUnknown = !string.IsNullOrWhiteSpace(value) && !validTypes.Contains(value);
                var safeValue = string.IsNullOrWhiteSpace(value) || isUnknown
                    ? validTypes[0]
                    : value;

                // PileSectionType 側と同じ理由 (無言のデータ破損を避ける) でログに残す
                if (isUnknown)
                {
                    Serilog.Log.Warning(
                        "[PileSection] 未知の杭体タイプ '{Unknown}' を '{Fallback}' に差し替えました。" +
                        "杭種を追加した場合は PileSection.PileBodyType の validTypes への登録漏れを確認してください。",
                        value, safeValue);
                }

                if (SetProperty(ref _pileBodyType, safeValue))
                {
                    // 場所打ち鉄筋コンクリート杭は断面タイプが 1 つしかないので、
                    // 杭体タイプが変わったこの時点で揃える。
                    // (以前は RecalculatePileDia の中でやっていたが、そこは
                    //  PileSectionType のセッターからも呼ばれるため、読み込みの
                    //  順序しだいで断面タイプが化けていた)
                    if (safeValue == PileTypeNames.InsituRc)
                        PileSectionType = PileTypeNames.RcSection;

                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        /// <summary>
        /// 杭体タイプに対する断面タイプの既定値。
        /// 未知の断面タイプを差し替えるときの行き先で、常に <c>鉄筋コンクリート部</c> に落とすと
        /// 既製コンクリート杭や鋼管杭が場所打ち RC 断面に化けてしまう。
        /// </summary>
        private string DefaultSectionTypeForBodyType => PileBodyType switch
        {
            PileTypeNames.PrecastConcrete => PileTypeNames.Phc,
            PileTypeNames.SteelPipe => PileTypeNames.SteelPipeSection,
            PileTypeNames.InsituSteelPipeConcrete => PileTypeNames.SteelPipeConcreteSection,
            _ => PileTypeNames.RcSection,
        };

        private string _pileSectionType = PileTypeNames.RcSection;
        public string PileSectionType
        {
            get => _pileSectionType;
            set
            {
                var validTypes = new[]
                {
                    PileTypeNames.RcSection,
                    PileTypeNames.SteelPipeConcreteSection,
                    PileTypeNames.Phc,
                    PileTypeNames.PhcNodular,
                    PileTypeNames.Prc,
                    PileTypeNames.PrcNodular,
                    PileTypeNames.PrcNodularPhcPart,
                    PileTypeNames.BfsHead,
                    PileTypeNames.BfsTip,
                    PileTypeNames.Sc,
                    PileTypeNames.SteelPipe,      // 旧互換 (鋼管杭 を細分化する前のサブタイプ名)
                    PileTypeNames.SteelPipeSection,
                    PileTypeNames.CftSection,
                };
                // null / 空の書き戻しは無視して現在値を保つ。
                // ComboBox の ItemsSource から現在値が外れると SelectedItem が null になり、
                // TwoWay バインドがその null を書き戻してくる。これを既定値に落とすと
                // ユーザーが何も操作していないのに断面タイプが差し替わってしまう。
                if (string.IsNullOrWhiteSpace(value)) return;

                bool isUnknown = !validTypes.Contains(value);
                var safeValue = isUnknown ? DefaultSectionTypeForBodyType : value;

                // 未知の断面タイプを黙って差し替えると、断面耐力が別物になったまま
                // 解析が通ってしまう (新しい断面タイプの登録漏れが「無言のデータ破損」になる)。
                // 互換のためフォールバック自体は残すが、必ず記録に残す。
                if (isUnknown)
                {
                    Serilog.Log.Warning(
                        "[PileSection] 未知の断面タイプ '{Unknown}' を '{Fallback}' に差し替えました。" +
                        "断面タイプを追加した場合は PileSection.PileSectionType の validTypes への登録漏れを確認してください。",
                        value, safeValue);
                }

                if (SetProperty(ref _pileSectionType, safeValue))
                {
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        private string _selectedSteelPipe = string.Empty;
        public string SelectedSteelPipe
        {
            get => _selectedSteelPipe;
            set => SetProperty(ref _selectedSteelPipe, value);
        }

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string[] InsituPileSectionTypesOption { get; } =
        [
            PileTypeNames.InsituRc,
            PileTypeNames.InsituSteelPipeConcrete,
        ];

        // 場所打ち鋼管コンクリート杭の部位 
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string[] InsituSteelPileSectionTypeOption { get; } =
        [
            PileTypeNames.SteelPipeConcreteSection,
            PileTypeNames.RcSection,
        ];

        private bool _isTopSegment;
        /// <summary>
        /// この断面が杭体の最上段区間か。<see cref="PileBodyInput.PileBodySegmentsUpdate"/> が設定する。
        /// PHC節杭 は杭頭で継手を介して上杭に接合される下杭として使うため、
        /// 最上段区間では選択できないようにするのに使う。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsTopSegment
        {
            get => _isTopSegment;
            set
            {
                if (SetProperty(ref _isTopSegment, value))
                    OnPropertyChanged(nameof(PreCastConcretePileSectionTypeOption));
            }
        }

        /// <summary>
        /// 既製コンクリート杭の断面タイプ選択肢。区間の位置によらず全種類を出す。
        ///
        /// 以前は「節杭は上杭に継ぐ下杭として使う」という運用から最上段区間で節杭を除外していたが、
        /// 次の 2 点からやめた。
        /// <list type="bullet">
        /// <item>除外の判定に使う <see cref="IsTopSegment"/> は区間更新が走るまで false のままで、
        ///       同じ状態でも「初回は選べるが開き直すと消える」という不安定な挙動になっていた</item>
        /// <item>Smart-MAGNUM / Hybrid ニーディングは先端が節杭である前提の工法で、
        ///       1 区間だけでモデル化したい場合に節杭が選べなくなってしまう</item>
        /// </list>
        /// 「最上段に節杭が来ている」こと自体は施工上の注意なので、入力チェックの警告で知らせる
        /// （<c>CheckInputData.CheckPileBodyGeometry</c>）。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string[] PreCastConcretePileSectionTypeOption =>
        [
            PileTypeNames.Phc,
            PileTypeNames.PhcNodular,
            PileTypeNames.Prc,
            PileTypeNames.PrcNodular,
            PileTypeNames.PrcNodularPhcPart,
            PileTypeNames.BfsHead,
            PileTypeNames.BfsTip,
            PileTypeNames.Sc
        ];

        // 鋼管杭の部位
        // 場所打ち鋼管コンクリート杭の PileTypeNames.SteelPipeConcreteSection/PileTypeNames.RcSection と同じ思想で、
        // 鋼管杭でも杭頭部 (鉄筋定着工法用、コンクリート充填+鉄筋配置) と杭中下部 (鋼管のみ) を
        // 別サブタイプとして杭区間で選べるようにする。
        // - PileTypeNames.SteelPipeSection          : 純粋な鋼管 (M-φ 計算: SteelPipeSection)
        // - PileTypeNames.CftSection: 杭頭部、鋼管内コンクリート充填 + 鉄筋配置可
        //                       (但し M-φ の耐力には鉄筋を参入しない、長さ ≒ 鋼管外径)
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string[] SteelPipePileSectionTypeOption { get; } =
        [
            PileTypeNames.CftSection,
            PileTypeNames.SteelPipeSection,
        ];

        // 杭径
        private double _pileDiameter = 1200.0;
        public double PileDiameter
        {
            get => _pileDiameter;
            set
            {
                if (!double.IsFinite(value)) return;
                if (SetProperty(ref _pileDiameter, value))
                {
                    RecalculatePileDia();
                    // 既製杭では杭径そのものが断面を決める。派生値の再計算で
                    // ConcreteOutDia 等が変わらない場合にも曲線を捨てる
                    // (以前は「カタログ経路が最後に捨てる」前提で捨てていなかったが、
                    // その経路は実際には捨てていなかった)。
                    InvalidateAllCaches();
                }
            }
        }

        /// <summary>
        /// 表示用の杭径（腐食代を見込まない公称外径）[mm]。
        /// 解析で使う <see cref="PileDiameter"/> は鋼管系では腐食代を控除した有効径
        /// (PipeDia − 2×CorrosionDepth) だが、計算書の「杭径」表示では腐食前の
        /// 公称外径（鋼管外径 = PipeDia）を用いる。
        /// 鋼管を外面に持たない断面（場所打ち RC / 鉄筋コンクリート部 / PHC・PRC・SC）は
        /// PileDiameter が既に公称外径なのでそのまま返す。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double NominalPileDiameter => (PileBodyType, PileSectionType) switch
        {
            (PileTypeNames.InsituSteelPipeConcrete, PileTypeNames.SteelPipeConcreteSection) => PipeDia,
            (PileTypeNames.SteelPipe, _) => PipeDia,
            _ => PileDiameter
        };

        // ───── PHC節杭 固有の諸元 ─────
        // 断面耐力は軸部基準でストレート PHC 杭と同一なので、これらは断面計算には一切使わない。
        // 断面タイプが PHC節杭 でないときは 0。

        private double _nodeDiameter;
        /// <summary>
        /// 節部径 Do [mm]（PHC節杭のみ。それ以外は 0）。
        /// 現状は表示・計算書用で、支持力の周面抵抗は軸部径 <see cref="PileDiameter"/> で算定している
        /// （節の効果は未考慮＝安全側）。工法別の支持力式を実装する際にここを使う。
        /// </summary>
        public double NodeDiameter
        {
            get => _nodeDiameter;
            set => SetProperty(ref _nodeDiameter, value);
        }

        private double _catalogMassPerM;
        /// <summary>
        /// カタログ標準質量 [t/m]（PHC節杭のみ。それ以外は 0）。
        /// 節杭の自重 <see cref="W"/> はこの値から求める。節部体積を幾何的に積分しても
        /// カタログ質量は再現できない（最良フィットでも RMS 3.1%・最大 6.1% 相違）ため、
        /// メーカー公称値を唯一の出所とする。
        /// </summary>
        public double CatalogMassPerM
        {
            get => _catalogMassPerM;
            set => SetProperty(ref _catalogMassPerM, value);
        }

        private double _nodePitch;
        /// <summary>節ピッチ（節中心間距離）[mm]（PHC節杭のみ。それ以外は 0）。カタログ姿図の寸法記入値。</summary>
        public double NodePitch
        {
            get => _nodePitch;
            set => SetProperty(ref _nodePitch, value);
        }

        private double _nodeHeadOffset;
        /// <summary>杭頭から第 1 節中心までの距離 [mm]（PHC節杭のみ）。カタログ姿図の寸法記入値。</summary>
        public double NodeHeadOffset
        {
            get => _nodeHeadOffset;
            set => SetProperty(ref _nodeHeadOffset, value);
        }

        private double _nodeToeOffset;
        /// <summary>杭先端から最終節中心までの距離 [mm]（PHC節杭のみ）。カタログ姿図の寸法記入値。</summary>
        public double NodeToeOffset
        {
            get => _nodeToeOffset;
            set => SetProperty(ref _nodeToeOffset, value);
        }

        /// <summary>
        /// この断面が節杭か (PHC節杭 / PRC節杭 / PRC節杭(PHC部))。
        /// 節杭固有の諸元・姿図・杭頭タイプ判定はすべてこの判定で分岐する。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsNodularPile => PileTypeNames.IsNodularSection(PileSectionType);

        private double _nodeHeadDiameter;
        /// <summary>
        /// 拡頭部径 Dt [mm]（PHC節杭で拡頭タイプのときのみ。標準タイプ・節杭以外は 0）。
        /// 直上の杭区間の径から自動で決まる（<see cref="ResolveNodularHead"/>）。
        /// </summary>
        public double NodeHeadDiameter
        {
            get => _nodeHeadDiameter;
            set => SetProperty(ref _nodeHeadDiameter, value);
        }

        private double _nodeHeadLength;
        /// <summary>拡頭部長さ Lt [mm]（カタログ全行で 600mm）。標準タイプ・節杭以外は 0。</summary>
        public double NodeHeadLength
        {
            get => _nodeHeadLength;
            set => SetProperty(ref _nodeHeadLength, value);
        }

        private string _nodularHeadType = NodularHeadTypes.Standard;
        /// <summary>PHC節杭 の杭頭タイプ（標準タイプ / 拡頭中間径タイプ / 拡頭タイプ）。</summary>
        public string NodularHeadType
        {
            get => _nodularHeadType;
            set => SetProperty(ref _nodularHeadType, value);
        }

        private string _nodularHeadNote = string.Empty;
        /// <summary>杭頭タイプの自動判定の根拠・注意（諸元表に出す）。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string NodularHeadNote
        {
            get => _nodularHeadNote;
            set => SetProperty(ref _nodularHeadNote, value);
        }

        /// <summary>PHC節杭 の杭頭タイプ名。</summary>
        public static class NodularHeadTypes
        {
            public const string Standard = "標準タイプ";
            public const string IntermediateHead = "拡頭中間径タイプ";
            public const string EnlargedHead = "拡頭タイプ";
        }

        /// <summary>
        /// 直上の杭区間の径に合わせて PHC節杭 の杭頭タイプ（標準／拡頭中間径／拡頭）を決める。
        ///
        /// 継手で接合する以上、杭頭径は直上の区間の径と一致していなければならないので、
        /// カタログの拡頭形状一覧から Dt が一致する製品を選ぶ。一致するものが無ければ
        /// 標準タイプのままとし、その旨を <see cref="NodularHeadNote"/> に残す。
        /// </summary>
        /// <param name="diameterAbove">直上の杭区間の杭径 [mm]。最上段の区間なら null。</param>
        public void ResolveNodularHead(double? diameterAbove)
        {
            if (!IsNodularPile)
            {
                NodeHeadDiameter = 0.0;
                NodeHeadLength = 0.0;
                NodularHeadType = NodularHeadTypes.Standard;
                NodularHeadNote = string.Empty;
                return;
            }

            // BF.S は頭部軸部そのものが太いので「拡頭」という設定を持たない。
            if (PileSectionType is PileTypeNames.BfsHead or PileTypeNames.BfsTip)
            {
                NodeHeadDiameter = 0.0;
                NodeHeadLength = 0.0;
                NodularHeadType = NodularHeadTypes.Standard;
                NodularHeadNote = "頭部軸部と先端軸部で外径が変わる製品のため、拡頭の設定はありません";
                return;
            }

            const double tol = 0.5; // mm

            // 最上段、または直上が軸部径と同じなら拡頭不要
            if (diameterAbove is not double above || above <= PileDiameter + tol)
            {
                NodeHeadDiameter = 0.0;
                NodeHeadLength = 0.0;
                NodularHeadType = NodularHeadTypes.Standard;
                NodularHeadNote = diameterAbove == null
                    ? "最上段の区間のため標準タイプ"
                    : "直上区間が軸部径と同径のため標準タイプ";
                return;
            }

            var heads = CurrentNodularHeads();
            int idx = heads.FindIndex(h => Math.Abs(h.Dt - above) <= tol);

            if (idx < 0)
            {
                var available = heads.Select(h => $"{h.Dt:N0}").ToList();
                NodeHeadDiameter = 0.0;
                NodeHeadLength = 0.0;
                NodularHeadType = NodularHeadTypes.Standard;
                NodularHeadNote = available.Count > 0
                    ? $"直上区間の径 {above:N0}mm に一致する拡頭径がありません (選択可: {string.Join(" / ", available)}mm)。標準タイプとして扱います"
                    : $"この呼び名には拡頭タイプの設定がありません。標準タイプとして扱います";
                return;
            }

            var match = heads[idx];
            NodeHeadDiameter = match.Dt;
            NodeHeadLength = match.Lt;
            NodularHeadType = match.IsIntermediate
                ? NodularHeadTypes.IntermediateHead
                : NodularHeadTypes.EnlargedHead;
            NodularHeadNote = $"直上区間の径 {above:N0}mm に合わせて自動選択";
        }

        /// <summary>
        /// 選択中の節杭製品に対応する拡頭形状の一覧。
        /// NPH と NPRC で DTO が別なので、ここで共通の形に畳んでから判定に使う。
        /// </summary>
        private List<(double Dt, double Lt, bool IsIntermediate)> CurrentNodularHeads()
        {
            string name = NodularProductName();
            if (string.IsNullOrEmpty(name)) return [];

            return PileSectionType == PileTypeNames.PhcNodular
                ? [.. NodularPileHeads.Where(h => h.Name == name)
                                      .Select(h => (h.Dt, h.Lt, h.IsIntermediateHead))]
                : [.. NodularPrcPileHeads.Where(h => h.Name == name)
                                         .Select(h => (h.Dt, h.Lt, h.IsIntermediateHead))];
        }

        /// <summary>選択中の節杭製品の呼び名 (例: 440-300)。節杭でなければ空。</summary>
        private string NodularProductName()
        {
            if (!IsNodularPile) return string.Empty;
            string? name = SelectedPrecastPile?.Name;
            return PileSectionType == PileTypeNames.PhcNodular
                ? NodularPiles.FirstOrDefault(p => p.DisplayName == name)?.Name ?? string.Empty
                : FindNodularPrcProduct(name)?.Name ?? string.Empty;
        }

        /// <summary>
        /// 節の半径方向の高さ [mm]（＝ (Do − D)/2）。PHC節杭 以外は 0。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double NodeRadialRise => IsNodularPile && NodeDiameter > PileDiameter
            ? (NodeDiameter - PileDiameter) * 0.5
            : 0.0;

        /// <summary>
        /// 節テーパーの軸方向長さ [mm]。カタログ姿図でテーパーは厳密に 45°（軸方向長 = 半径方向高さ）
        /// なので、Do と D から一意に決まる。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double NodeTaperLength => NodeRadialRise;

        private double _catalogNodeFlatLength;
        /// <summary>
        /// 製品カタログに寸法記入がある場合の節部（平坦部）長さ [mm]。0 なら記入が無い。
        /// BF.S は寸法記入があり、節杭 (JP-NPH / JP-NPRC) は無い。
        /// </summary>
        public double CatalogNodeFlatLength
        {
            get => _catalogNodeFlatLength;
            set => SetProperty(ref _catalogNodeFlatLength, value);
        }

        /// <summary>
        /// 節部（最大径が一定の区間）の軸方向長さ [mm]。
        /// カタログに寸法記入がある製品 (BF.S) はその値、
        /// <b>記入が無い製品 (JP-NPH / JP-NPRC) は姿図の実測値（テーパー軸長と等長）に基づく推定値。</b>
        /// 図示専用で、断面耐力・自重・支持力には一切使わない。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double NodeFlatLength =>
            CatalogNodeFlatLength > 0 ? CatalogNodeFlatLength : NodeRadialRise;

        /// <summary>節 1 個の軸方向全長 [mm]（テーパー + 節部 + テーパー）。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double NodeTotalLength => 2.0 * NodeTaperLength + NodeFlatLength;

        /// <summary>
        /// 節中心からの相対位置で、節の外形を「(軸方向オフセット [mm], 半径 [mm])」の列として返す
        /// （上から下へ）。姿図・3D 表示が同じ形状を描くための唯一の定義。
        /// PHC節杭 以外や寸法が揃わない場合は空。
        /// </summary>
        public IReadOnlyList<(double OffsetFromCenter, double Radius)> NodeProfile()
        {
            if (!IsNodularPile || NodeRadialRise <= 0) return [];

            double rShaft = PileDiameter * 0.5;
            double rNode = NodeDiameter * 0.5;
            double halfFlat = NodeFlatLength * 0.5;
            double halfTotal = halfFlat + NodeTaperLength;

            // 上のテーパー始点 → 節部上端 → 節部下端 → 下のテーパー終点
            return
            [
                (+halfTotal, rShaft),
                (+halfFlat, rNode),
                (-halfFlat, rNode),
                (-halfTotal, rShaft),
            ];
        }

        /// <summary>
        /// PHC節杭 の区間外形を「(区間上端からの深さ [m], 半径 [mm])」の折れ線として上から順に返す。
        /// 姿図・3D 表示が同じ形状を描くための唯一の定義。
        ///
        /// 拡頭タイプでは上端から <see cref="NodeHeadLength"/> までが拡頭部径 Dt になる。
        /// 拡頭部と第 1 節は重なる（Lt = 杭頭〜第 1 節中心 = 600mm）ので、重なる範囲は
        /// 大きい方の径を採る。
        /// </summary>
        public IReadOnlyList<(double Depth, double Radius)> NodularOutline(double segmentLengthM)
        {
            if (!IsNodularPile || segmentLengthM <= 0) return [];

            double rShaft = PileDiameter * 0.5;
            var pts = new List<(double Depth, double Radius)>();

            // 拡頭部 (上端から Lt)
            bool hasHead = NodeHeadDiameter > PileDiameter && NodeHeadLength > 0;
            double headBottom = hasHead ? NodeHeadLength / 1000.0 : 0.0;
            if (hasHead)
            {
                double rHead = NodeHeadDiameter * 0.5;
                pts.Add((0.0, rHead));
                pts.Add((headBottom, rHead));
            }
            else
            {
                pts.Add((0.0, rShaft));
            }

            // 節
            foreach (double centerDepth in NodeCenterDepthsFromSegmentTop(segmentLengthM))
            {
                foreach (var (offset, radius) in NodeProfile())
                {
                    // offset は節中心から上が正 → 深さは中心深さ − offset
                    double depth = centerDepth - offset / 1000.0;
                    if (depth < 0 || depth > segmentLengthM) continue;
                    // 拡頭部の範囲内は拡頭部径と比べて大きい方を採る
                    double r = hasHead && depth <= headBottom
                        ? Math.Max(radius, NodeHeadDiameter * 0.5)
                        : radius;
                    pts.Add((depth, r));
                }
            }

            pts.Add((segmentLengthM, rShaft));

            // 深さ順に整列し、拡頭部内で潰れた重複点を除く
            var ordered = pts.OrderBy(p => p.Depth).ToList();
            var result = new List<(double Depth, double Radius)>(ordered.Count);
            foreach (var p in ordered)
            {
                if (result.Count > 0
                    && Math.Abs(result[^1].Depth - p.Depth) < 1e-9
                    && Math.Abs(result[^1].Radius - p.Radius) < 1e-9)
                    continue;
                result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// 杭区間の上端から測った節中心位置 [m] を上から順に返す。
        /// PHC節杭 以外、または寸法が未設定なら空。
        ///
        /// 節杭は 1 本の製品が 1 区間に対応する前提で、区間上端から <see cref="NodeHeadOffset"/>、
        /// 以降 <see cref="NodePitch"/> ごとに節が並び、最終節は区間下端から
        /// <see cref="NodeToeOffset"/> 上方に来る（カタログの杭長 4〜15m・1m ピッチと整合する）。
        ///
        /// <b>節の形状（節部長さ・テーパー長）はカタログに寸法記入が無く再現できないため、
        /// ここで返すのは位置だけである。</b>
        /// </summary>
        public IEnumerable<double> NodeCenterDepthsFromSegmentTop(double segmentLengthM)
        {
            if (!IsNodularPile || NodePitch <= 0 || segmentLengthM <= 0) yield break;

            const double tol = 1.0e-9;
            double pitch = NodePitch / 1000.0;
            double last = segmentLengthM - NodeToeOffset / 1000.0;

            for (double z = NodeHeadOffset / 1000.0; z <= last + tol; z += pitch)
            {
                if (z < -tol || z > segmentLengthM + tol) continue;
                yield return z;
            }
        }

        /// <summary>
        /// PC鋼材とプレストレスを持つ既製杭断面 (PHC杭 / PHC節杭 / PRC杭) か。
        /// 断面タイプの直積判定が各所に散らばると追加漏れが起きるため、意味のある単位で名前を付ける。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsPrestressedPrecastSection =>
            PileSectionType is PileTypeNames.Phc or PileTypeNames.PhcNodular or PileTypeNames.Prc;

        /// <summary>中空円形の既製コンクリート杭断面 (PHC杭 / PHC節杭 / PRC杭 / SC杭) か。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsHollowPrecastSection =>
            PileSectionType is PileTypeNames.Phc or PileTypeNames.PhcNodular
                            or PileTypeNames.Prc or PileTypeNames.PrcNodular
                            or PileTypeNames.PrcNodularPhcPart or PileTypeNames.Sc
                            or PileTypeNames.BfsHead or PileTypeNames.BfsTip;

        // コンクリート外径
        private double _concreteOutDia = 1200.0;
        public double ConcreteOutDia
        {
            get => _concreteOutDia;
            set
            {
                if (SetProperty(ref _concreteOutDia, value))
                {
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        // 腐食代
        private double _corrosionDepth = 1.0;
        public double CorrosionDepth
        {
            get => _corrosionDepth;
            set
            {
                if (SetProperty(ref _corrosionDepth, value))
                {
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        private double _corrodedPipeTs;
        public double CorrodedPipeTs
        {
            get => _corrodedPipeTs;
            set
            {
                if (SetProperty(ref _corrodedPipeTs, value))
                {
                }
            }
        }

        // 杭径変更時のメソッド
        public void RecalculatePileDia()
        {
            // 読み込み中は走らせない。すべての値が揃ってから OnDeserialized で一度だけ。
            if (_isDeserializing) return;

            try
            {
                // 断面タイプも見てから動く。ほかの 2 つの分岐は元からそうしている。
                //
                // 場所打ちRC の分岐だけが杭体タイプしか見ておらず、読み込みで
                // プロパティの順序が入れ替わって「杭体タイプは既定 (場所打ちRC) の
                // まま、断面タイプだけ先に PHC杭 が入る」状態になると、
                // <b>既製杭の肉厚・杭径・鋼管径を場所打ちRC の式で塗り潰していた。</b>
                // 塗り潰された値は、あとで杭体タイプが正しくなっても戻らない。
                if (PileBodyType == PileTypeNames.InsituRc
                    && PileSectionType == PileTypeNames.RcSection)
                {
                    PileDiameter = ConcreteOutDia;
                    ConcreteThickness = ConcreteOutDia * 0.5;
                    MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    // ここで PileSectionType を書き換えないこと。
                    //
                    // PileSectionType のセッターがこのメソッドを呼ぶので、
                    // 「今セットした断面タイプを自分で上書きする」形になる。
                    // 読み込みでプロパティの順序が入れ替わり、杭体タイプがまだ
                    // 既定 (場所打ちRC) のまま断面タイプが先に来ると、
                    // <b>PHC杭 が鉄筋コンクリート部に化ける</b>。杭種が変われば
                    // 断面計算オブジェクトごと別物になるので、耐力が丸ごと変わる。
                    // 場所打ちRC への揃え直しは PileBodyType のセッターが行う。
                    PipeDia = 0.0;
                    PipeTs = 0.0;
                }

                else if (PileBodyType == PileTypeNames.InsituSteelPipeConcrete)
                {
                    if (PileSectionType == PileTypeNames.RcSection)
                    {
                        PileDiameter = ConcreteOutDia;
                        ConcreteThickness = ConcreteOutDia * 0.5;
                        MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    }
                    else if (PileSectionType == PileTypeNames.SteelPipeConcreteSection)
                    {
                        PileDiameter = PipeDia - CorrosionDepth * 2.0;
                        CorrodedPipeTs = PipeTs - CorrosionDepth;
                        ConcreteOutDia = PipeDia - PipeTs * 2.0;
                        ConcreteThickness = ConcreteOutDia * 0.5;
                        MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    }
                }
                else if (PileBodyType == PileTypeNames.SteelPipe)
                {
                    // 場所打ち鋼管コンクリート杭の鋼管コンクリート部と同じ計算で、
                    // 腐食代を考慮した有効径を PileDiameter とする。
                    // コンクリート充填鋼管部では充填コンクリート関連 (Dc, Tc, MainBarDr) も派生。
                    if (PileSectionType == PileTypeNames.CftSection)
                    {
                        PileDiameter = PipeDia - CorrosionDepth * 2.0;
                        CorrodedPipeTs = PipeTs - CorrosionDepth;
                        ConcreteOutDia = PipeDia - PipeTs * 2.0;
                        ConcreteThickness = ConcreteOutDia * 0.5;
                        MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    }
                    else // PileTypeNames.SteelPipeSection or 旧 PileTypeNames.SteelPipe
                    {
                        PileDiameter = PipeDia - CorrosionDepth * 2.0;
                        CorrodedPipeTs = PipeTs - CorrosionDepth;
                    }
                }
            }
            catch (Exception ex)
            {
                // 画面へ直接ダイアログを出さない。ここは解析からも呼ばれる。
                // ケース並列では 8 本のワーカーが同時に画面スレッドへ割り込むことになり、
                // 相手が終了していると永久に待つ。記録に寄せて、解析の完了時にまとめて出す。
                PileDesign.Common.CalcFallbackTracker.Report(
                    "杭径の再計算（既定値で継続）", ex, $"杭体={PileBodyType}, 断面={PileSectionType}");
            }
        }

        //杭断面変更時（デフォルト）のメソッド
        public void ResetSectionProperties()
        {
            try
            {


                // 表示の即時更新用にクリア（後で GetNMRaw が再計算して再設定）
                UltimateLimitAxialForceThresholds = [];

                if (PileBodyType == PileTypeNames.InsituRc)
                {
                    PileSectionType = PileTypeNames.RcSection;
                    ConcreteOutDia = 1200.0;
                    MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    PipeDia = 0.0;
                    PipeTs = 0.0;
                    RecalculatePileDia();
                    ConcreteGamma = 23.0;
                    RecalculateConcreteE();
                }
                else if (PileBodyType == PileTypeNames.InsituSteelPipeConcrete)
                {
                    PileSectionType = PileTypeNames.SteelPipeConcreteSection;
                    ConcreteOutDia = 0.0;
                    PipeDia = 1200.0;
                    PipeTs = 16.0;
                    RecalculatePileDia();
                    ConcreteGamma = 23.0;
                    RecalculateConcreteE();

                }
                else if (PileBodyType == PileTypeNames.PrecastConcrete)
                {
                    PileSectionType = PileTypeNames.Phc;
                    ConcreteOutDia = 0.0;
                    MainBarNum = 0;
                    PipeDia = 0.0;
                    PipeTs = 0.0;
                    // PHCsの要素数チェック
                    if (PHCs.Count >= 10)
                        SelectedPrecastPile = PHCs[^10];
                    else if (PHCs.Count > 0)
                        SelectedPrecastPile = PHCs[0];
                    else
                        SelectedPrecastPile = new PrecastPile();
                    RecalculateSelectedPrecastPile();
                    RecalculatePileDia();
                    ConcreteGamma = 26.0;
                }
                else if (PileBodyType == PileTypeNames.SteelPipe)
                {
                    // 杭頭側 (区間追加時の最初の区間) は PileTypeNames.CftSection を既定とする
                    // (鉄筋定着工法では杭頭が常にこの部位)。下方区間は手動で PileTypeNames.SteelPipeSection に切替える。
                    PileSectionType = PileTypeNames.CftSection;
                    ConcreteOutDia = 0.0;
                    MainBarNum = 0;
                    PipeDia = 0.0;
                    PipeTs = 0.0;
                    // SteelPipePilesの要素数チェック
                    if (SteelPipePiles.Count >= 5)
                        SelectedSteelPipePileName = $"{SteelPipePiles[^5].Diameter}x{SteelPipePiles[^5].Thickness}";
                    else if (SteelPipePiles.Count > 0)
                        SelectedSteelPipePileName = $"{SteelPipePiles[0].Diameter}x{SteelPipePiles[0].Thickness}";
                    else
                        SelectedSteelPipePileName = string.Empty;
                    RecalculateSelectedSteelPipePipe();
                    RecalculatePileDia();
                }
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report(
                    "断面諸元の初期化（既定値で継続）", ex, $"杭体={PileBodyType}, 断面={PileSectionType}");
            }
        }

        // 座屈長 (m)。鋼管杭の柱座屈の検討範囲で、液状化区間の長さ。
        //
        // 断面そのものの性質ではなく<b>その杭が置かれた地盤</b>で決まるので、
        // 杭要素分割 (GenerateSoilPiles) が地盤と杭の対応から求めて書き込む。
        // 0 なら柱座屈による低減を行わない (液状化を検討していないモデルはこれになる)。
        //
        // 同じ杭体を複数の杭が使い、それぞれ別の地盤に居ることがある。その場合は
        // <b>最も長い座屈長</b>を採る (どの杭でも成り立つ耐力にするため)。
        private double _bucklingLength;

        /// <summary>
        /// 耐力の計算に使う座屈長。基本設定で鋼管杭の柱座屈を考慮しないなら 0 (低減しない)。
        /// 設定は使う時点で見る (<see cref="BucklingLength"/> には設定に関わらず液状化区間の長さを持つ)。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double EffectiveBucklingLength
            => ConcreteModelOptions.ConsiderSteelPipeColumnBuckling ? BucklingLength : 0.0;

        public double BucklingLength
        {
            get => _bucklingLength;
            set
            {
                double safeValue = value < 0 || !double.IsFinite(value) ? 0.0 : value;
                if (SetProperty(ref _bucklingLength, safeValue))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // コンクリート肉厚
        private double _concreteThickness;
        public double ConcreteThickness
        {
            get => _concreteThickness;
            set
            {
                double safeValue = value < 0 ? 0 : value;
                if (SetProperty(ref _concreteThickness, safeValue))
                {
                    InvalidateAllCaches();
                }
            }
        }

    }
}

using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using PileDesign.Models.InputData;

namespace PileDesign.Models.Results
{
    /// <summary>
    /// 群杭沈下解析の結果。
    ///
    /// <b>入力ではない。</b>以前はケース記録が <c>InputModel.PileGroupSettlement</c> の中に
    /// 格納されていたため、
    /// <list type="bullet">
    /// <item>保存ファイルに<b>二重</b>に入った (現在の入力と、解析時のスナップショットの両方)</item>
    /// <item>Undo が入力ごと巻き戻すので、入力を 1 つ直して戻すと沈下の結果まで消えた</item>
    /// <item>スナップショットへ写す処理を別途書く必要があり、写し忘れると結果表示に出なかった</item>
    /// </list>
    /// という不整合が出ていた。結果は 1 つの型に閉じ込め、<b>実体を 1 つだけ</b>持つ。
    ///
    /// <see cref="PileGroupSettlement.Result"/> は現在の入力・解析時のスナップショットの
    /// どちらからも<b>同じインスタンス</b>を指す ([JsonIgnore] なので JSON 往復では複製されない)。
    /// 保存は <c>ProjectData.GroupSettlementResult</c> の節が受け持つ。
    /// </summary>
    public sealed class GroupSettlementResult : BaseModel
    {
        /// <summary>
        /// ケース別の結果。通常 (Steinbrenner 単発) は 1 件、
        /// 基礎梁考慮反復は VL/L1/L2 各 1 件。
        /// </summary>
        public ObservableCollection<GroupSettlementCaseRecord> CaseRecords { get; set; } = [];

        /// <summary>表示中のケース index。-1 = 未選択。</summary>
        public int ActiveCaseIndex { get; set; } = -1;

        /// <summary>
        /// 表示中の LoadingType (<see cref="CaseRecords"/> を絞り込んで表示するため)。
        /// 入力設定の LoadingType (次回解析で使う) とは独立。空文字 = 未指定 (旧データ)。
        /// </summary>
        public string ActiveLoadingType { get; set; } = "";

        /// <summary>結果を持っているか。</summary>
        [JsonIgnore]
        public bool HasResults => CaseRecords != null && CaseRecords.Count > 0;

        /// <summary>表示中のケースの結果。未解析・該当なしは null。</summary>
        [JsonIgnore]
        public GroupSettlementCaseRecord? ActiveRecord =>
            CaseRecords != null && ActiveCaseIndex >= 0 && ActiveCaseIndex < CaseRecords.Count
                ? CaseRecords[ActiveCaseIndex]
                : null;

        /// <summary>表示中のケースの沈下グリッド。無ければ空 (null を返さない)。</summary>
        [JsonIgnore]
        public ObservableCollection<SettlementGridDataItem> ActiveSettlementGridData =>
            ActiveRecord?.SettlementGridData ?? _emptyGrid;

        private readonly ObservableCollection<SettlementGridDataItem> _emptyGrid = [];

        /// <summary>表示中のケースのコンタ格子 X 座標。ケースが無ければ空。</summary>
        [JsonIgnore]
        public List<double> ActiveGridX => ActiveRecord?.GridX ?? _emptyAxis;

        /// <summary>表示中のケースのコンタ格子 Y 座標。ケースが無ければ空。</summary>
        [JsonIgnore]
        public List<double> ActiveGridY => ActiveRecord?.GridY ?? _emptyAxis;

        private static readonly List<double> _emptyAxis = [];

        /// <summary>表示中のケースにおける杭 <paramref name="pileNo"/> の沈下量 [mm]。未解析・該当なしは 0。</summary>
        public double SettlementOf(int pileNo) =>
            ActiveRecord != null && ActiveRecord.PileSettlements_mm.TryGetValue(pileNo, out double s)
                ? s
                : 0.0;

        /// <summary>結果を空にする。<b>インスタンスは差し替えない</b> (共有している参照が切れるため)。</summary>
        public void Clear()
        {
            CaseRecords?.Clear();
            ActiveCaseIndex = -1;
        }
    }

    /// <summary>
    /// 群杭沈下解析結果のケース別レコード。
    /// 通常 (Steinbrenner 単発) は 1 レコード、基礎梁考慮反復は VL/L1/L2 各 1 レコード。
    ///
    /// 杭は <see cref="PileSettlements_mm"/> のように <c>PileNo</c> で参照し、
    /// <see cref="RectLoads"/> / <see cref="SettlementGridData"/> も生成時に値複製する。
    /// <b>入力モデルのオブジェクトを参照で抱えないこと</b> — 抱えると
    /// <c>ReferenceHandler.Preserve</c> のもとで入力と結果が <c>$ref</c> で絡み合い、
    /// 片方だけ保存・復元できなくなる。
    /// </summary>
    public class GroupSettlementCaseRecord : BaseModel
    {
        public string LoadCaseName { get; set; } = "";

        /// <summary>このレコードを生成した解析タイプ ("任意矩形" / "個別矩形" / "個別十字" / "個別十字（基礎梁反力）" / "個別矩形（基礎梁考慮）")。空文字 = 旧データ。</summary>
        public string LoadingType { get; set; } = "";

        /// <summary>true: 個別矩形（基礎梁考慮）反復解析の結果。false: 通常 Steinbrenner 単発。</summary>
        public bool IsBeamAware { get; set; }

        /// <summary>このケースの (反復後の) 矩形荷重。</summary>
        public ObservableCollection<RectLoad> RectLoads { get; set; } = [];

        /// <summary>このケースの沈下グリッドデータ (コンタ図描画用)。</summary>
        public ObservableCollection<SettlementGridDataItem> SettlementGridData
        {
            get => _settlementGridData;
            set
            {
                _settlementGridData = value ?? [];
                _gridX = null;
                _gridY = null;
            }
        }
        private ObservableCollection<SettlementGridDataItem> _settlementGridData = [];

        /// <summary>
        /// コンタ格子の X 座標 (昇順・重複なし)。<b>この記録の沈下値から作る。</b>
        ///
        /// 以前は入力モデルの <c>PileGroupSettlement.SettlementGridX</c> を読んでいた。
        /// あれは解析が<b>現在の入力</b>に書くもので、解析時のスナップショットには移らない。
        /// そのため沈下だけ再実行したときに軸が古いまま (または空のまま) になり、
        /// コンタが描かれない・点が落ちる、という食い違いが出ていた。
        /// 軸は沈下値そのものから決まるので、持たずに引き出す。
        /// </summary>
        [JsonIgnore]
        public List<double> GridX =>
            _gridX ??= [.. SettlementGridData.Select(d => d.X).Distinct().OrderBy(v => v)];
        private List<double>? _gridX;

        /// <summary>コンタ格子の Y 座標 (昇順・重複なし)。<see cref="GridX"/> と同じ理由でここから引く。</summary>
        [JsonIgnore]
        public List<double> GridY =>
            _gridY ??= [.. SettlementGridData.Select(d => d.Y).Distinct().OrderBy(v => v)];
        private List<double>? _gridY;

        /// <summary>各杭の沈下量 [mm]。Key = PileLayoutDataItem.PileNo</summary>
        public Dictionary<int, double> PileSettlements_mm { get; set; } = [];

        // ── 基礎梁考慮の場合のみ ──
        public bool IsConverged { get; set; }
        public int IterationCount { get; set; }
        public double FinalResidual { get; set; }

        /// <summary>各杭の杭反力 Pi [kN]。</summary>
        public Dictionary<int, double> PileReactions_kN { get; set; } = [];

        /// <summary>各杭の杭頭ばね剛性 ki [kN/m]。</summary>
        public Dictionary<int, double> SpringStiffness { get; set; } = [];

        /// <summary>節点変位 (基礎梁考慮のみ)。</summary>
        public List<FEM.VerticalBeamNodeResult> NodeResults { get; set; } = [];

        /// <summary>梁断面力 (基礎梁考慮のみ)。</summary>
        public List<FEM.VerticalBeamBeamResult> BeamResults { get; set; } = [];

        /// <summary>反復ログ (基礎梁考慮反復のときの履歴。表示は ObservableCollection 化される)。</summary>
        public List<string> IterationLog { get; set; } = [];
    }
}

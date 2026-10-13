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
    public partial class PileSection : BaseModel,
        System.Text.Json.Serialization.IJsonOnDeserializing,
        System.Text.Json.Serialization.IJsonOnDeserialized
    {
        // 責務ごとに partial ファイルへ分けてある (PileSection.<責務>.cs。2026-10-10 に 4,826 行の 1 ファイルから分けた)。
        // ここには読込の後始末・杭体番号・コンストラクタ・複製と戻し (ShallowCopy/RestoreFrom/DeepCopy) を置く。

        // 読み込み中は、寸法の連鎖 (RecalculatePileDia) を止める。
        //
        // System.Text.Json は<b>ファイルに書かれた順</b>にプロパティを設定する。
        // ConcreteOutDia / CorrosionDepth / PileDiameter のセッターはどれも
        // RecalculatePileDia() を呼ぶので、杭体タイプがまだ既定 (場所打ちRC) の
        // うちにそれが走ると、既製杭の肉厚・杭径・鋼管径を場所打ちRC の式で
        // 塗り潰す (φ1100 で 140mm → 550mm)。<b>塗り潰された値は、あとで
        // 杭体タイプが正しくなっても戻らない。</b>
        //
        // 読み終わってから一度だけ走らせれば、すべての値が揃った状態で
        // 正しい分岐に入る。RecalculatePileDia は他の値から計算するだけなので、
        // 何度呼んでも結果は同じ (冪等)。
        //
        // PileBodyInput / SoilPile / ZDataItem が同じ仕組みを使っている。
        private bool _isDeserializing;

        // System.Text.Json 用コールバック (本プロジェクトの主デシリアライザ)
        void System.Text.Json.Serialization.IJsonOnDeserializing.OnDeserializing()
            => _isDeserializing = true;

        void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
        {
            _isDeserializing = false;
            RecalculateConcreteE();
            // 再計算より先にかぶり厚を配置直径へ揃える。RecalculatePileDia は
            // 場所打ちRC で配置直径をかぶり厚から導くので、揃えずに呼ぶと
            // ファイルに書かれた配置直径が既定のかぶり厚から導いた値に塗り潰される。
            SyncCoverFromPlacementDiameter();
            RecalculatePileDia();
            SyncHoopMethodConsistency();
            InvalidateAllCaches();
        }

        // Newtonsoft.Json 用コールバック (副デシリアライザ経由で読む場合に備えて)
        [System.Runtime.Serialization.OnDeserializing]
        internal void OnDeserializingHandler(System.Runtime.Serialization.StreamingContext _)
            => _isDeserializing = true;

        [System.Runtime.Serialization.OnDeserialized]
        internal void OnDeserializedHandler(System.Runtime.Serialization.StreamingContext _)
        {
            _isDeserializing = false;
            RecalculateConcreteE();
            SyncCoverFromPlacementDiameter();
            RecalculatePileDia();
            SyncHoopMethodConsistency();
            InvalidateAllCaches();
        }

        /// <summary>
        /// せん断補強筋の工法・呼び名・規格の食い違いを、読み込み後に揃える。
        ///
        /// System.Text.Json はファイルに書かれた順にプロパティを設定するので、
        /// 工法より後に呼び名が来ると、工法の表に無い呼び名 (「標準」の D13 など) が
        /// 残ることがある。そのままだと公称断面積が引けず <b>pw が 0</b> になり、
        /// 安全限界せん断の √(pw·σwy) から補強筋の項が黙って消える。
        /// 読み終わってから一度だけ、工法側の既定へ寄せる。
        /// </summary>
        private void SyncHoopMethodConsistency()
        {
            var spec = ShearReinforcementMethods.Get(HoopMethod);
            if (spec == null)
            {
                // 「標準」または知らない工法名。呼び名・規格が標準の一覧の外なら既定へ戻す。
                if (!string.IsNullOrEmpty(HoopMethod) && HoopMethod != ShearReinforcementMethods.Standard)
                {
                    PileDesign.Common.CalcFallbackTracker.Report(
                        "せん断補強筋の工法 (→標準)", null, $"HoopMethod={HoopMethod}");
                    _hoopMethod = ShearReinforcementMethods.Standard;
                }
                // 「標準」には算定式の選択肢が無い。工法から戻したファイルで
                // 選べない式が残ると、標準の断面が工法の式で計算される。
                SyncHoopOptionCollections();
                return;
            }

            if (!spec.BarAreas.ContainsKey(HoopSize))
            {
                PileDesign.Common.CalcFallbackTracker.Report(
                    $"せん断補強筋の呼び名 (→{spec.DefaultBarSize})", null,
                    $"HoopMethod={HoopMethod}, HoopSize={HoopSize}");
            }
            // 選択肢の入れ替えと、外れた値の寄せ直しは 1 か所に集約してある。
            SyncHoopOptionCollections();
        }

        // フィールド
        private int _pileBodyNo;
        public int PileBodyNo
        {
            get => _pileBodyNo;
            set => SetProperty(ref _pileBodyNo, value);
        }

        // コンストラクタ
        public PileSection()
        {
            // 初期化処理
            SelectedPrecastPile = new PrecastPile();

            // PHCs, PRCs, SCs, SteelPipeOption は静的キャッシュのプロパティから取得
            // （コンストラクタでの初期化は不要）

            RecalculatePileDia();
            RecalculateConcreteE();
            SetSpecs();
            MainBarAg = MainBarNum * GetBarArea(MainBarSize);
        }

        // 浅いコピーを作成するメソッド
        public PileSection ShallowCopy()
        {
            return CloneWithoutSubscribers<PileSection>();
        }

        /// <summary>
        /// 他の断面の状態を、この断面に<b>そのまま</b>書き戻す。
        ///
        /// 杭断面ウィンドウの「キャンセル」で使う。あちらは開いたときに
        /// <see cref="DeepCopy"/> で控えを取っておき、キャンセル時にこれで戻す。
        ///
        /// <para>以前は呼び出し側が<b>戻す項目を手で並べて</b>いた (20 項目)。
        /// PC 鋼材の諸元 (TendonAp・TendonDp・TendonSigmaPy・TendonSigmaPu・Prestress) が
        /// 抜けており、<b>既製杭の製品を変えてキャンセルすると PC 鋼材が戻らなかった</b>。
        /// 製品名と杭径だけ戻るので断面が食い違った状態になり、N-M 曲線が変わる。
        /// 一覧を持つ形は諸元が増えるたびに取り残されるので、まるごと写す形にした。</para>
        ///
        /// <para>フィールドを直に写す。セッターを順に呼ぶと、途中の食い違った状態で
        /// 派生値が計算され直してしまう。写すのは<b>整合した過去の状態</b>なので
        /// 計算し直す必要はない。</para>
        ///
        /// <para>イベントの購読者 (デリゲート) は写さない。写すと画面の購読が
        /// 控えのものに入れ替わる (MemberwiseClone がデリゲートも写す問題と同じ)。
        /// readonly なフィールド (ロック等) も写さない。</para>
        ///
        /// (PileSectionRestoreTests が、揺らしてから戻して全項目が一致することを見張る)
        /// </summary>
        internal void RestoreFrom(PileSection other)
        {
            if (other == null) return;

            foreach (var f in typeof(PileSection).GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic))
            {
                if (f.IsInitOnly) continue;
                if (typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                f.SetValue(this, f.GetValue(other));
            }

            // 曲線は作り直させる
            InvalidateAllCaches();

            // 画面へ全プロパティの変更を知らせる (空文字は「全部」の意味)
            OnPropertyChanged(string.Empty);
        }

        // 深いコピーを作成するメソッド
        public PileSection DeepCopy()
        {
            // 浅いコピーを作成してから、参照型フィールドを個別に複製する
            var copy = CloneWithoutSubscribers<PileSection>();

            // せん断補強筋の選択肢は入れ物を共有させない。
            // 共有したまま控えを取ると、控え側で工法を変えたときに元の画面の
            // ComboBox の候補まで書き換わる (同じコレクションを見ているため)。
            copy._hoopSizeOption = new ObservableCollection<string>(this._hoopSizeOption);
            copy._hoopSpecOption = new ObservableCollection<string>(this._hoopSpecOption);
            copy._hoopDamageFormulaOption = new ObservableCollection<string>(this._hoopDamageFormulaOption);
            copy._hoopUltimateFormulaOption = new ObservableCollection<string>(this._hoopUltimateFormulaOption);

            // SelectedPrecastPile をコピー (null 安全)
            if (this.SelectedPrecastPile != null)
            {
                copy.SelectedPrecastPile = new PrecastPile
                {
                    No = this.SelectedPrecastPile.No,
                    ThicknessType = this.SelectedPrecastPile.ThicknessType,
                    PrestressType = this.SelectedPrecastPile.PrestressType,
                    Name = this.SelectedPrecastPile.Name,
                    PileType = this.SelectedPrecastPile.PileType,
                    PileDiameter = this.SelectedPrecastPile.PileDiameter,
                    PileThickness = this.SelectedPrecastPile.PileThickness,
                    Fc = this.SelectedPrecastPile.Fc,
                    SFc = this.SelectedPrecastPile.SFc,
                    Fbc = this.SelectedPrecastPile.Fbc,
                    SigmaE = this.SelectedPrecastPile.SigmaE,
                    Ec = this.SelectedPrecastPile.Ec,
                    Ap = this.SelectedPrecastPile.Ap,
                    Dp = this.SelectedPrecastPile.Dp,
                    Ftp = this.SelectedPrecastPile.Ftp,
                    SigmaPu = this.SelectedPrecastPile.SigmaPu,
                    Ep = this.SelectedPrecastPile.Ep,
                    HasReinf = this.SelectedPrecastPile.HasReinf,
                    Nr = this.SelectedPrecastPile.Nr,
                    RDesignation = this.SelectedPrecastPile.RDesignation,
                    Ag = this.SelectedPrecastPile.Ag,
                    Dr = this.SelectedPrecastPile.Dr,
                    Ftr = this.SelectedPrecastPile.Ftr,
                    Er = this.SelectedPrecastPile.Er,
                    Ts = this.SelectedPrecastPile.Ts,
                    Fts = this.SelectedPrecastPile.Fts,
                    Es = this.SelectedPrecastPile.Es,
                    PsSigmaY = this.SelectedPrecastPile.PsSigmaY
                };
            }
            else
            {
                copy.SelectedPrecastPile = new PrecastPile();
            }

            // SelectedSteelPipePile をコピー
            copy.SelectedSteelPipePile = new SteelPipePile
            {
                Diameter = this.SelectedSteelPipePile?.Diameter ?? 0.0,
                Thickness = this.SelectedSteelPipePile?.Thickness ?? 0.0
            };

            // SelectedPileSectionSpecification の複製（仕様表示のコレクション）
            if (this.SelectedPileSectionSpecification != null)
            {
                copy.SelectedPileSectionSpecification = new ObservableCollection<Spec>(
                    this.SelectedPileSectionSpecification.Select(s => new Spec(s.Item, s.Mark, s.Value, s.Unit, s.Note))
                );
            }
            else
            {
                copy.SelectedPileSectionSpecification = [];
            }

            // キャッシュ系はコピーせず再計算させる。
            // 以前はここでキャッシュを 1 つずつ null にしており、後から足した
            // レベル1 の損傷 N-M と N-Q 6 本が漏れて、元の断面の曲線を共有したままだった。
            // 捨てる一覧は InvalidateAllCaches の 1 か所だけに持つ。
            copy.InvalidateAllCaches();

            return copy;
        }

    }
}

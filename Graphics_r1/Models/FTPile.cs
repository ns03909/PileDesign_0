using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PileDesign.Models
{
    // FTパイル引張鉄筋クラス
    public class FTPileTensionBars : BaseModel
    {

        /// <summary>値だけを持つので MemberwiseClone で十分。参照型のメンバは持たない。</summary>
        public FTPileTensionBars DeepCopy() => CloneWithoutSubscribers<FTPileTensionBars>();
        // 引張鉄筋
        [System.Text.Json.Serialization.JsonIgnore]
        public string[] TensionAnchorGradeOption { get; private set; } =
        [
            "φ11, SBPR785/1030",
            "φ11, SBPR1080/1230",
        ];

        [System.Text.Json.Serialization.JsonIgnore]
        public double Ft { get; private set; } // 鋼棒の短期許容応力度(N/mm2)
        [System.Text.Json.Serialization.JsonIgnore]
        public double Fu { get; private set; } // 鋼棒の引張耐力（N/mm2）
        [System.Text.Json.Serialization.JsonIgnore]
        public double Ag { get; private set; } // 引抜き抵抗用鋼棒の全断面積（mm2）
        [System.Text.Json.Serialization.JsonIgnore]
        public double Ag1 { get; private set; } // 引抜き抵抗用鋼棒の全断面積（mm2）
        [System.Text.Json.Serialization.JsonIgnore]
        public double Es { get; } = 205_000.0; // 鋼棒の弾性係数（N/mm2）
        [System.Text.Json.Serialization.JsonIgnore]
        public double Ls { get; } = 600.0; // 鋼棒の有効長さ（m）600 - 2000
        [System.Text.Json.Serialization.JsonIgnore]
        public double Ds { get; } = 500.0;// 引抜き抵抗用鋼棒の配置距離（m）
        [System.Text.Json.Serialization.JsonIgnore]
        public double EpsilonA { get; private set; } // 鋼棒の許容ひずみ
        [System.Text.Json.Serialization.JsonIgnore]
        public double DeltaA { get; private set; } // 鋼棒の許容伸び(m)
        [System.Text.Json.Serialization.JsonIgnore]
        public double ThetaAS { get; private set; }
        public int TensionAnchorNum { get; set; } = 8; // 引抜き抵抗用鋼棒の配置距離（m）
        public string SelectedTensionAnchorGrade { get; set; } = "φ11, SBPR1080/1230";// 引抜き抵抗用鋼棒の配置距離（m）
        public bool IsTensionType { get; set; } = false;


        // FTパイル引張鉄筋クラスコンストラクタ
        public FTPileTensionBars() //bool isTensionType, int tensionAnchorNum, double ds, double ls)
        {
            Update();
        }

        // アップデートメソッド
        public void Update()
        {
            if (SelectedTensionAnchorGrade == "φ11, SBPR785/1030")
            {
                Ft = 785.0;
                Fu = 1030.0;
                Ag1 = 95.03;
            }
            else if (SelectedTensionAnchorGrade == "φ11, SBPR1080/1230")
            {
                Ft = 1080.0;
                Fu = 1230.0;
                Ag1 = 95.03;
            }
            Ag = Ag1 * TensionAnchorNum;
            EpsilonA = Ft / Es;
            DeltaA = EpsilonA * Ls;
            ThetaAS = DeltaA / Ds;
        }
    }

    // FTパイルキャップクラス
    public class FTPileCap : BaseModel
    {

        /// <summary>値だけを持つので MemberwiseClone で十分。参照型のメンバは持たない。</summary>
        public FTPileCap DeepCopy() => CloneWithoutSubscribers<FTPileCap>();
        // [JsonInclude] が要る理由は FTPilePile.D1 のコメントを参照。
        [System.Text.Json.Serialization.JsonInclude]
        public double Fc { get; private set; } = 24.0;// パイルキャップのコンクリートの設計基準強度(N/mm2)
        [System.Text.Json.Serialization.JsonInclude]
        public double E { get; private set; } = 33_500.0 * Math.Pow(23.0 / 24.0, 2.0) * Math.Pow(24.0 / 60, 1.0 / 3.0); // パイルキャップコンクリートの弾性係数（N/mm2）
        [System.Text.Json.Serialization.JsonIgnore]
        public double Density { get; private set; } = 23.0;
        [System.Text.Json.Serialization.JsonIgnore]
        public double Nu { get; private set; } = 0.2; // パイルキャップコンクリートのポアソン比
        [System.Text.Json.Serialization.JsonIgnore]
        public double Ac { get; private set; } // 支承面積（パイルキャップ面積）(m2)

        // パラメータなしコンストラクタ（System.Text.Json デシリアライズ用）。
        // Fc / E はセッターが private なので逆直列化されない。読込後は
        // PileTop.ApplyPileCapConcrete() が入力値を入れ直す。
        public FTPileCap() { }

        // FTパイルキャップクラスコンストラクタ
        public FTPileCap(double pileCapFc, double pileCapEc)
        {
            SetPileCapConcrete(pileCapFc, pileCapEc);
        }

        /// <summary>
        /// 杭頭部ウィンドウの「パイルキャップ」で入力された Fc・Ec を反映する。
        /// 反映したら true。
        ///
        /// 0 以下は無視する。逆直列化の途中や未入力のまま Fc=0 が入ると、
        /// 曲げ耐力 (3/5·φc·Ap·Fc) が 0 になり、θ 上限と η が 0 除算になる。
        /// </summary>
        internal bool SetPileCapConcrete(double pileCapFc, double pileCapEc)
        {
            if (pileCapFc <= 0.0 || pileCapEc <= 0.0) return false;
            if (Fc == pileCapFc && E == pileCapEc) return false;
            Fc = pileCapFc;
            E = pileCapEc;
            return true;
        }

        public void UpdateAc(double d)
        {
            Ac = Math.Pow(2.0 * d, 2); // 仮
        }
    }

    // FTパイル杭クラス
    public class FTPilePile : BaseModel
    {

        /// <summary>値だけを持つので MemberwiseClone で十分。参照型のメンバは持たない。</summary>
        public FTPilePile DeepCopy() => CloneWithoutSubscribers<FTPilePile>();
        // [JsonInclude] が要る。private セッターは既定では逆直列化されないので、
        // 付けないと保存ファイルから読んだときに既定の 600 / 400 に戻る。
        // ファイルには正しい値が書いてあるので、ファイルを見ても気づけない。
        [System.Text.Json.Serialization.JsonInclude]
        public double D1 { get; private set; } = 600.0; // 杭の外径
        [System.Text.Json.Serialization.JsonInclude]
        public double D2 { get; private set; } = 400.0; // 杭の内径
        [System.Text.Json.Serialization.JsonInclude]
        public double Ap { get; private set; } // 杭頭面積（m2）

        // FTパイル杭クラスコンストラクタ
        public FTPilePile()//double d1, double d2)
        {
            UpdateAp();
        }

        // 杭の外径と内径を設定
        public void SetDimensions(double outerDia, double innerDia)
        {
            D1 = outerDia;
            D2 = innerDia;
            UpdateAp();
        }

        public void UpdateAp()
        {
            Ap = Math.PI * (Math.Pow(D1, 2) - Math.Pow(D2, 2)) / 4.0;
        }
    }

    // FTパイルクラス
    public class FTPile : BaseModel, System.Text.Json.Serialization.IJsonOnDeserialized
    {
        /// <summary>
        /// 読込の直後に、カタログ (FT キャップ) を読み直し、保存しない派生値 ([JsonIgnore] を付けたもの。どれも入力から求め直せる) (支圧係数 PhiC・K0・N-M-θ 表。private set なので復元されない) を組み直す。
        /// 組み直さないと PhiC=0 のまま、パイルキャップの Fc・杭径が変わらない限り Update が走らず、
        /// θ の上限 (0.03 − 0.05·σN/(PhiC·Fc)) が 0 除算で数値でなくなる。
        /// </summary>
        void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
        {
            if (FTCaps == null || FTCaps.Count == 0) LoadFTCaps();
            if (FTPileCap == null || FTPilePile == null || FTPileTensionBars == null) return;
            // 引張鉄筋の派生値 (Ft・Ag・θas など) も保存しないので、規格・本数から求め直す
            try { FTPileTensionBars.Update(); Update(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[FTPile] 読込後の組み直しに失敗しました"); }
        }

        public FTPilePile FTPilePile { get; set; } = new(); // FTパイル杭体
        public FTPileCap FTPileCap { get; set; } // FTパイルキャップ
        public FTPileTensionBars FTPileTensionBars { get; set; } = new(); // FTパイル引張鉄筋
        /// <summary>選んだ FT キャップ。以前は公開フィールドで保存されず、開き直すと既定 (寸法 0) に戻っていた。</summary>
        public FTCap FTCap { get; set; } = new();
        // カタログ (CSV から読む選択肢) なので保存しない。読込後に読み直す (OnDeserialized)。
        // 保存すると杭体ごとにカタログの写しが入り、ファイルの半分ほどを占めていた
        [System.Text.Json.Serialization.JsonIgnore]
        public List<FTCap> FTCaps { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public ObservableCollection<string> FTCapOption { get; set; } = [];

        /// <summary>
        /// FTキャップ + (引張鉄筋ありの場合) 引張鉄筋諸元を合成した諸元リストを返す。
        /// PileTop.SelectedPileTopSpecification および計算書の杭頭諸元表で使用する。
        /// </summary>
        public ObservableCollection<Spec> GetCombinedSpecs()
        {
            var combined = new ObservableCollection<Spec>();
            if (FTCap != null)
            {
                foreach (var s in FTCap.GetSpecs()) combined.Add(s);
            }
            if (FTPileTensionBars != null && FTPileTensionBars.IsTensionType && FTPileTensionBars.TensionAnchorNum > 0)
            {
                combined.Add(new Spec("引張鉄筋本数", "", $"{FTPileTensionBars.TensionAnchorNum:N0}", "本"));
                combined.Add(new Spec("引張鉄筋規格", "", FTPileTensionBars.SelectedTensionAnchorGrade ?? "", ""));
                combined.Add(new Spec("引張鉄筋全断面積", "Ag", $"{FTPileTensionBars.Ag:N1}", "mm²"));
                combined.Add(new Spec("引張鉄筋短期許容応力度", "Ft", $"{FTPileTensionBars.Ft:N0}", "N/mm²"));
                combined.Add(new Spec("引張鉄筋有効長さ", "Ls", $"{FTPileTensionBars.Ls:N0}", "mm"));
                combined.Add(new Spec("引張鉄筋配置距離", "Ds", $"{FTPileTensionBars.Ds:N0}", "mm"));
            }
            return combined;
        }

        private string _selectedFTCapName;
        public string SelectedFTCapName
        {
            get => _selectedFTCapName;
            set => SetProperty(ref _selectedFTCapName, value);
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public double PhiC { get; private set; } // 支圧係数
        [System.Text.Json.Serialization.JsonIgnore]
        public double K0 { get; private set; } // 杭頭接合部の初期回転剛性
        [System.Text.Json.Serialization.JsonIgnore]
        public ObservableCollection<double> Ns { get; private set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public ObservableCollection<(ObservableCollection<double>, ObservableCollection<double>)> ThetasMs { get; private set; }

        // パラメータなしコンストラクタ（System.Text.Json デシリアライズ用）。
        // CSV テーブル読込・再計算は LoadFTCaps() と Update() を明示的に呼ぶこと。
        public FTPile() { }

        // FTパイルクラスコンストラクタ
        public FTPile(double pileCapFc, double pileCapEc)
        {
            FTPileCap = new(pileCapFc, pileCapEc);

            LoadFTCaps();
            Update();
        }

        /// <summary>
        /// パイルキャップの Fc・Ec を反映し、変わったときだけ M-θ を組み直す。
        /// <see cref="Models.InputData.PileTop.ApplyPileCapConcrete"/> から呼ばれる。
        /// </summary>
        /// <summary>
        /// Undo のスナップショット用。子のうち<b>その場で書き換えられるもの</b>だけ写す。
        /// <c>FTPileCap</c> は <c>SetPileCapConcrete</c> / <c>UpdateAc</c> で、
        /// <c>FTPilePile</c> は <c>SetDimensions</c> で、<c>FTPileTensionBars</c> は
        /// <c>Update</c> で中身が書き換わるので、共有すると元と一緒に動く。
        ///
        /// <c>FTCaps</c> (CSV の表) と <c>FTCapOption</c>、<c>Ns</c> / <c>ThetasMs</c> は
        /// 差し替えでしか変わらないので共有してよい。
        /// </summary>
        public FTPile DeepCopy()
        {
            var copy = CloneWithoutSubscribers<FTPile>();
            copy.FTPileCap = this.FTPileCap?.DeepCopy()!;
            copy.FTPilePile = this.FTPilePile?.DeepCopy()!;
            copy.FTPileTensionBars = this.FTPileTensionBars?.DeepCopy()!;
            return copy;
        }

        internal void SetPileCapConcrete(double pileCapFc, double pileCapEc)
        {
            if (FTPileCap == null) return;
            if (!FTPileCap.SetPileCapConcrete(pileCapFc, pileCapEc)) return;
            Update();
        }

        /// <summary>
        /// 杭断面から FT パイル杭体の外径・内径を決める。
        /// コンクリート厚が未設定なら中空比 0.6 で仮に置く (元から 3 か所で
        /// 同じ式を書いていたのをここにまとめた)。
        /// </summary>
        internal static (double Outer, double Inner) DimensionsFromSection(
            double pileDiameter, double concreteThickness)
            => (pileDiameter,
                concreteThickness > 0 ? pileDiameter - 2.0 * concreteThickness : pileDiameter * 0.6);

        /// <summary>
        /// 杭径を杭断面に合わせ、変わったときだけ M-θ を組み直す。
        ///
        /// 杭径は 3 か所 (工法の選択・FT キャップの選択・杭頭部ウィンドウの初期化) から
        /// しか入っておらず、どれも画面の操作でしか走らない。杭断面ウィンドウで杭径を
        /// 変えても届かず、既定の φ600 / φ400 のまま耐力 (Ap 経由) と初期回転剛性
        /// K0 (D1³ − D2³) を計算していた。解析の入口からも呼ぶ。
        /// </summary>
        /// <summary>
        /// FT キャップを選んでいないか (キャップの杭径 Phi が 0)。未選択だと支圧係数 φc が 0 になり、
        /// 杭頭の M-θ と許容回転角が数値にならない。
        /// </summary>
        public bool IsCapUnselected() => FTCap == null || !(FTCap.Phi > 0);

        /// <summary>杭径に合う FT キャップ (キャップの杭径 Phi = 杭径)。カタログに無ければ null。</summary>
        public FTCap? CapForPileDiameter(double pileDiameter)
            => FTCaps?.FirstOrDefault(c => c != null && Math.Abs(c.Phi - pileDiameter) < 0.5);

        /// <summary>
        /// FT キャップを選ぶ (キャップの杭径 Phi で指定)。カタログに無ければ何もせず false。
        /// 杭頭部の画面と、解析の前に未選択を知らせて選ぶ画面の両方から使う。
        /// </summary>
        public bool SelectCap(double phi)
        {
            var cap = FTCaps?.FirstOrDefault(c => c != null && Math.Abs(c.Phi - phi) < 0.5);
            if (cap == null) return false;
            FTCap = cap;
            SelectedFTCapName = $"{cap.Phi}";
            Update();
            return true;
        }

        /// <summary>FT キャップを未選択にする (杭径に合うキャップがカタログに無いとき。別の径のキャップで黙って計算しない)。</summary>
        public void ClearCap()
        {
            FTCap = new FTCap();
            SelectedFTCapName = null!;
        }

        internal void SetDimensionsFromSection(double pileDiameter, double concreteThickness)
        {
            if (FTPilePile == null || pileDiameter <= 0.0) return;
            var (outer, inner) = DimensionsFromSection(pileDiameter, concreteThickness);
            if (FTPilePile.D1 == outer && FTPilePile.D2 == inner) return;
            FTPilePile.SetDimensions(outer, inner);
            Update();
        }

        private void LoadFTCaps()
        {
            // 実行ファイルのディレクトリを基準にパスを組み立てる
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string filePath = System.IO.Path.Combine(baseDir, "Models", "PileLibrary", "FTCap.csv");

            if (System.IO.File.Exists(filePath))
            {
                FTCaps = FTCapLoader.LoadFromCsv(filePath);
                foreach (var ftCap in FTCaps)
                {
                    FTCapOption.Add($"{ftCap.Phi}");
                }
            }
            else
            {
            }
        }

        public void Update()
        {
            FTPileCap?.UpdateAc(FTCap.Phi); // パイルキャップAc

            if (FTPilePile?.Ap != null && FTPileCap?.Ac != null)
            {
                PhiC = Math.Min(Math.Sqrt(FTPileCap.Ac / FTPilePile.Ap), 4.0); // 支圧係数
                (Ns, ThetasMs) = GetNMThetaRelationship(); // N、M、θ関係
            }

        }

        //N、θ、M関係を返すメソッド
        (ObservableCollection<double>, ObservableCollection<(ObservableCollection<double>, ObservableCollection<double>)>) GetNMThetaRelationship()
        {
            ObservableCollection<double> Ns = [];
            ObservableCollection<(ObservableCollection<double>, ObservableCollection<double>)> thetasMs = [];
            int nNum = 10;
            double NMin = GetNMin();
            double NMax = GetNMax();
            double N;

            for (int i = 0; i < nNum; i++)
            {
                N = NMin + (NMax - NMin) * i / nNum;
                thetasMs.Add(GetMThetaRelationship(N));
                Ns.Add(N);
            }
            return (Ns, thetasMs);
        }

        // ある軸力に対するθ, M関係を返すメソッド
        internal (ObservableCollection<double>, ObservableCollection<double>) GetMThetaRelationship(double N)
        {
            ObservableCollection<double> thetas = [0];
            ObservableCollection<double> Ms = [0];
            int DivNum = 100;

            K0 = GetK0(N);
            double thetaC = GetThetaC(N);
            double thetaMax = GetThetaAc(N);
            if (FTPileTensionBars.IsTensionType)
            {
                thetaMax = Math.Min(GetThetaAc(N), FTPileTensionBars.ThetaAS);
            }

            for (int i = 0; i <= DivNum; i++)
            {
                thetas.Add(thetaC + (thetaMax - thetaC) * i / DivNum);
                Ms.Add(GetMoment(thetas[^1], N));
            }
            return (thetas, Ms);
        }

        // 最大圧縮軸力を返すメソッド
        private double GetNMax()
        {
            return 3.0 / 5.0 * PhiC * FTPilePile.Ap * FTPileCap.Fc;
        }

        // 最大引張軸力を返すメソッド
        private double GetNMin()
        {
            if (FTPileTensionBars.IsTensionType)
            {
                return -FTPileTensionBars.Ag * FTPileTensionBars.Fu;
            }
            else
            {
                return 0.0;
            }
        }

        /// <summary>
        /// 軸力 N (kN, 圧縮正) に対する許容回転角 θa = min(θac, θas) を返す。検定から使う。
        ///
        /// 出典: F.T.Pile 構法 既製コンクリート杭 設計・施工指針 4.3.1 設計クライテリア / 表4-3-1。
        /// 標準タイプは θac (パイルキャップのひび割れ発生限界)、
        /// 引抜き対応タイプは min(θac, θas) (θas は引抜き抵抗用鋼棒の短期許容応力度で決まる)。
        /// <b>短期の照査</b>として規定されている (終局時の限界回転角は本指針に無い)。
        /// </summary>
        public double GetAllowableRotationAngle(double N) => GetThetaA(N);

        // 許容回転角を返すメソッド
        private double GetThetaA(double N)
        {
            // 標準タイプは θac、引抜き対応タイプは min(θac, θas) (表4-3-1。M-θ を作る GetMThetaRelationship と同じ)。
            // 以前は標準タイプでも、使わない鋼棒の既定値から求めた θas (≈0.0063) との最小をとり、照査が数倍厳しくなっていた
            double thetaA = GetThetaAc(N);
            if (FTPileTensionBars.IsTensionType)
                thetaA = Math.Min(thetaA, FTPileTensionBars.ThetaAS);
            return thetaA;
        }

        // コンクリートの圧縮合力を返すメソッド
        private double GetNc(double N, double M)
        {
            double Nc;
            if (FTPileTensionBars.IsTensionType)
            {
                Nc = N;
            }
            else
            {
                if (N <= 0 && Math.Abs(N * 0.5) > Math.Abs(M / FTPileTensionBars.Ds))
                {
                    Nc = 0;
                }
                else if (Math.Abs(N * 0.5) < Math.Abs(M / FTPileTensionBars.Ds))
                {
                    Nc = Math.Abs(M / FTPileTensionBars.Ds) + N * 0.5;
                }
                else
                {
                    Nc = N;
                }
            }
            return Nc;
        }

        // 浮き上がり回転角を返すメソッド
        private double GetThetaC(double N)
        {
            return GetMc(N) / K0;
        }

        // パイルキャップのひび割れで決まる許容回転角を返すメソッド
        private double GetThetaAc(double N)
        {
            return 0.03 - 0.05 * GetSigmaN(N) / (PhiC * FTPileCap.Fc);
        }

        // 曲げモーメントを返すメソッド
        private double GetMoment(double theta, double N)
        {
            double M;
            double thetaC = GetThetaC(N);
            if (0 <= theta && theta <= thetaC)
            {
                M = K0 * theta;
            }
            else
            {
                M = theta / (theta + GetThetaF(N)) * GetMmax(N);
            }
            return M;
        }

        // 基準回転角θfを返すメソッド
        private double GetThetaF(double N)
        {
            double Mmax = GetMmax(N);
            double Mc = GetMc(N);
            return (Mmax - Mc) / K0;
        }

        // 初期回転剛性K0を返すメソッド
        private double GetK0(double N)
        {
            double K0;
            if (N > 0)
            {
                K0 = Math.PI * FTPileCap.E / 32.0 / (1 - Math.Pow(FTPileCap.Nu, 2.0)) * (Math.Pow(FTPilePile.D1, 3.0) - Math.Pow(FTPilePile.D2, 3.0));
            }
            else
            {
                K0 = GetMa(N) / GetThetaA(N);
            }
            return K0;
        }

        // 浮き上がりモーメントMcを返すメソッド
        private double GetMc(double N)
        {
            return Math.Max((Math.Pow(FTPilePile.D1, 2.0) + Math.Pow(FTPilePile.D2, 2.0)) / (8.0 * FTPilePile.D1) * N, 0);
        }

        // 最大抵抗モーメントMmaxを返すメソッド
        private double GetMmax(double N)
        {
            double sigmaN = GetSigmaN(N);
            double eta = Math.Min(Math.Max(-0.16 * sigmaN / FTPileCap.Fc + 1.0, 0.85), 1.0);
            double Me = GetMe(N);
            return eta * Me;
        }

        // パイルキャップの軸応力度を返すメソッド
        private double GetSigmaN(double N)
        {
            return N / FTPilePile.Ap;
        }

        // 基準抵抗モーメントMeを返すメソッド
        private double GetMe(double N)
        {
            double Me;
            if (!FTPileTensionBars.IsTensionType)
            {
                Me = Math.Max(0.5 * N * FTPilePile.D1, 0.0);
            }
            else
            {
                // 指針の 5×10⁻⁴·ag·fu·Ds は kN·m 系の係数。この実装は N・mm 系なので 0.5 (GetMa の 3.75×10⁻⁴ → 0.375 と同じ換算)。
                // 以前は 5×10⁻⁴ のままで鋼棒の項が 1/1000 になり、引張になると Me ≒ 0 で杭頭がピンと同じになっていた
                Me = Math.Max(0.5 * FTPileTensionBars.Ag * FTPileTensionBars.Fu * FTPileTensionBars.Ds + 0.5 * N * FTPileTensionBars.Ds, 0.0);
            }
            return Me;
        }

        // 引き抜き対応タイプの許容モーメントを返すメソッド
        private double GetMa(double N)
        {
            return 0.375 * FTPileTensionBars.Ag * FTPileTensionBars.Ft * FTPileTensionBars.Ds + 0.5 * N * FTPileTensionBars.Ds;
        }

    }
}

using System;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 鋼管杭断面クラス。
    /// 損傷限界・安全限界曲げモーメントを 2 形態で提供する:
    /// - 中間部・下部 (鋼管のみ): 線形相互作用 (損傷) / 0.2 軸力比分岐 (安全)
    /// - 杭頭部 (鋼管 + 充填コンクリート コンクリート充填鋼管部): sM + cM 合算
    /// 出典: 基礎部材の強度と変形性能 (日本建築学会、2022年) 鋼管杭。
    /// </summary>
    internal class SteelPipeSection
    {
        // 入力値
        public double D { get; }       // 鋼管外径 (腐食代考慮後) [mm]
        public double T { get; }       // 鋼管厚 (腐食代考慮後) [mm]
        public double F { get; }       // 鋼材基準強度 [N/mm²] (例: SS400=235)
        public double SigmaB { get; }  // 鋼の引張強さ [N/mm²] (例: SS400=400)。安全限界引張で使用
        public double Beta1 { get; }   // 低減係数 β1
        public double Fc { get; }      // 充填コンクリート設計基準強度 [N/mm²]
        public double E { get; }       // 鋼材ヤング係数 [N/mm²] (既定 205000)

        // 派生幾何量 (sro = 鋼管外半径、sri = 鋼管内半径 = コンクリート外半径 cro)
        public double sro => D / 2.0;
        public double sri => sro - T;
        public double srm => sro - T / 2.0;       // 鋼管平均半径
        public double cro => sri;
        public double zh { get; }                 // ずれ止め板厚 [mm]
        public double cri => cro - zh;
        public int Zn { get; } = 2;               // ずれ止め段数

        // 断面性能
        public double sAp { get; }
        public double sZe { get; }
        public double sZp => 4.0 / 3.0 * Math.Pow(sro, 3) * (1.0 - Math.Pow(1.0 - T / sro, 3));  // 塑性断面係数
        public double sApf => Math.PI * cro * cro;       // 鋼管部閉塞面積
        public double Atr => Math.PI * (cro * cro - cri * cri); // ずれ止め支圧面積 = Air

        // 鋼管の断面 2 次モーメント
        public double Iisteel => Math.PI / 64.0 * (Math.Pow(D, 4) - Math.Pow(D - 2 * T, 4));
        // 充填コンクリートの断面 2 次モーメント (鋼管内全断面、半径 cro の円板)
        public double Iiconcrete => Math.PI / 4.0 * Math.Pow(cro, 4);

        // コンクリートヤング係数 (強度と変形性能 3.1 式の簡略形 Ec = 33500 × (Fc/60)^(1/3))
        public double Ec => Fc > 0 ? 33500.0 * Math.Pow(Fc / 60.0, 1.0 / 3.0) : 0.0;

        // 許容応力度 (基準 sfc1 = F/1.5 with 局部座屈低減)
        public double sft => F / 1.5;
        public double Sfc1 { get; }
        public double Sfc2 { get; }

        // 安全限界用降伏応力度 (= 1.1 × 1.5 × 許容応力度)
        public double sSigmaCy1 => 1.1 * 1.5 * Sfc1;
        public double sSigmaCy2 => 1.1 * 1.5 * Sfc2;
        public double sSigmaTy => 1.1 * 1.5 * sft;

        // 損傷限界軸力
        public double sNdt => -1.5 * sft * sAp;          // 引張 (負値)
        public double sNdc1 => 1.5 * Sfc1 * sAp;         // 圧縮 (局部座屈系)
        public double sNdc2 => 1.5 * Sfc2 * sAp;         // 圧縮 (柱座屈系)
        public double Ndc => Beta1 * Math.Min(sNdc1, sNdc2);

        // 安全限界軸力 (β2 は文献 8 章で 1.0 固定)
        public double sNuc1 => sSigmaCy1 * sAp;          // 鋼管圧縮 (局部座屈系)
        public double sNuc2 => sSigmaCy2 * sAp;          // 鋼管圧縮 (柱座屈系)
        public double sNut => sSigmaTy * sAp;            // 鋼管引張 (中間部・下部、降伏ベース、正値マグニチュード)
        public double sNut1 => SigmaB * sAp;             // 鋼管引張 (杭頭部、引張強さベース)
        public double cNuc => cSigmaIr * Atr;            // 充填コン圧縮
        public double sNuc => Math.Min(sNuc1, sNuc2);    // 中間部・下部 鋼管圧縮 (min)
        public double NucMiddle => Beta1 * 1.0 * sNuc;
        public double NutMiddle => Beta1 * 1.0 * sNut;
        public double NucHead => Beta1 * 1.0 * (sNuc1 + cNuc);
        public double NutHead => Beta1 * 1.0 * sNut1;

        // コンクリート系
        public double Alpha => Math.Max(5.05 - 0.053 * (D / T), 1.0);
        public double cSigmaCk { get; }
        public double cSigmaIr { get; }
        public double cNdc => cSigmaCk * Atr;

        // Xn=t 近傍の特異性線形補間しろ (相対値、cMd 第 2 ケース分母発散対策)
        private const double XnSingularityWidth = 0.05;  // 5% of T

        /// <summary>
        /// 簡易コンストラクタ (杭中間部・下部のみ使う場合)。
        /// 杭頭部 コンクリート充填鋼管部 用機能 (sMd, cMd, Ndc) には Fc が必要、安全限界引張 (sNut1) には sigmaB が必要。
        /// </summary>
        internal SteelPipeSection(double _D, double _T, double _F, double _beta1)
            : this(_D, _T, _F, _beta1, fc: 0.0, sigmaB: 0.0, e: 205000.0)
        { }

        /// <summary>
        /// 損傷限界 コンクリート充填鋼管部 杭頭部対応コンストラクタ (Fc 指定、sigmaB なし)。
        /// </summary>
        internal SteelPipeSection(double _D, double _T, double _F, double _beta1, double fc, double e = 205000.0)
            : this(_D, _T, _F, _beta1, fc, sigmaB: 0.0, e)
        { }

        /// <summary>
        /// 完全コンストラクタ (損傷限界 + 安全限界 両対応)。Fc, sigmaB を指定する。
        /// </summary>
        internal SteelPipeSection(double _D, double _T, double _F, double _beta1, double fc, double sigmaB, double e = 205000.0)
        {
            D = _D;
            T = _T;
            F = _F;
            SigmaB = sigmaB;
            Beta1 = _beta1;
            Fc = fc;
            E = e;

            // ずれ止め板厚 zh (D に応じて段階値、文献 8 章規定)
            zh = D switch
            {
                < 800 => 9.0,
                < 1200 => 12.0,
                _ => 16.0,
            };

            // 断面性能
            sAp = Math.PI * (Math.Pow(D, 2) - Math.Pow(D - 2 * T, 2)) / 4.0;
            sZe = Math.PI * (Math.Pow(D, 4) - Math.Pow(D - 2 * T, 4)) / (32.0 * D);

            // 許容圧縮応力度 sfc1 (局部座屈低減、D/t > 25 で連続式)
            Sfc1 = D / T > 25
                ? F / 1.5 * (0.8 + 5.0 / (D / T))
                : F / 1.5;

            // 許容圧縮応力度 sfc2 (柱座屈低減、Johnson 放物線 + Euler、Nc=Ny プレースホルダ)
            Sfc2 = ComputeSfc2();

            // ずれ止めコンクリートの短期許容支圧応力度
            // cσCk = (2/3) × α × √(sApf / (zn × Atr)) × Fc
            cSigmaCk = Fc > 0
                ? (2.0 / 3.0) * Alpha * Math.Sqrt(sApf / (Zn * Atr)) * Fc
                : 0.0;

            // 安全限界用支圧強度 (cσCk と同形だが 2/3 係数なし、cσIr = 1.5 × cσCk)
            // cσIr = α × √(sApf / (zn × Atr)) × Fc
            cSigmaIr = Fc > 0
                ? Alpha * Math.Sqrt(sApf / (Zn * Atr)) * Fc
                : 0.0;
        }

        /// <summary>
        /// 柱座屈低減を考慮した許容圧縮応力度 sfc2 を計算する。
        /// Nc プレースホルダ = Ny のため λC = 1 で固定。
        /// (Nc 本格実装後は引数化する想定)
        /// </summary>
        private double ComputeSfc2()
        {
            // 暫定実装: Nc (弾性曲げ座屈荷重) の本格算定が未実装のため、柱座屈低減を行わず
            // sfc2 = sfc1 (= F/1.5) を返す。地盤に支持される杭では Nc が十分大きく λc → 0 と
            // なり sfc2 は sfc1 にほぼ等しくなる前提。Nc プレースホルダ = Ny のままだと
            // λc = 1 で過度な低減 (sfc2 ≈ 0.4·F) となり、安全限界軸力が損傷限界を下回る
            // 異常を生じるため、ここでは保守的に sfc1 を返す。Nc 本格実装後にこの暫定処理を
            // 解除する想定。
            return Sfc1;

            // 元実装 (Nc プレースホルダ = Ny で λc=1 固定の場合):
            //   double Ny = 1.5 * sft * sAp;
            //   double Nc = Ny;                      // placeholder
            //   double lambdaC = Math.Sqrt(Ny / Nc); // = 1
            //   double eLambdaC = 1.0 / Math.Sqrt(0.6);
            //   double lambda = lambdaC * Math.PI * Math.Sqrt(E / F);
            //   double Lambda = Math.Sqrt(Math.PI * Math.PI * E / 0.6 / F);
            //   double ratio = lambda / Lambda;
            //   double nu = 1.5 + (2.0 / 3.0) * ratio * ratio;
            //   if (lambda <= Lambda) {
            //       double r = lambdaC / eLambdaC;
            //       return (1.0 - 0.4 * r * r) * F / nu;
            //   } else {
            //       return F / (nu * lambdaC * lambdaC);
            //   }
        }

        // -------------- 使用限界 --------------

        /// <summary>
        /// 使用限界モーメント (線形相互作用、圧縮側のみチェック)。
        /// </summary>
        internal double GetServiceLimitMoment(double Nsd)
        {
            double sf = (0 <= Nsd) ? Sfc1 : sft;
            return Beta1 * (sf - Math.Abs(Nsd) / sAp) * sZe;
        }

        // -------------- 損傷限界モーメント --------------

        /// <summary>
        /// 損傷限界モーメント (杭中間部・杭下部、コンクリート充填なし)。
        /// Md = β1 × (1.5 sfc1 − |Ndd|/sAp) × sZe
        /// </summary>
        internal double GetDamageLimitMomentMiddle(double Ndd)
        {
            return Beta1 * (1.5 * Sfc1 - Math.Abs(Ndd) / sAp) * sZe;
        }

        /// <summary>
        /// 損傷限界モーメント (杭頭部、コンクリート充填鋼管部 領域 = 鋼管 + 充填コンクリート)。
        /// Md = β1 × sMd            (Ndd < 0)
        /// Md = β1 × (sMd + cMd)    (0 ≤ Ndd ≤ cNdc)
        /// Md = β1 × sMd            (cNdc < Ndd)
        /// 内部で Xn を Nc(Xn) = Ndd から二分法で求める (case B のみ)。
        /// </summary>
        internal double GetDamageLimitMomentHead(double Ndd)
        {
            double sMd = ComputeSMd(Ndd);

            if (Ndd < 0 || Ndd > cNdc)
            {
                // Case A or C: コンクリート寄与なし
                return Beta1 * sMd;
            }

            // Case B: 0 ≤ Ndd ≤ cNdc → Xn を Nc(Xn) = Ndd から求めて cMd を加算
            double Xn = SolveXnForConcreteAxial(Ndd);
            double cMd = ComputeCMd(Xn);
            return Beta1 * (sMd + cMd);
        }

        // -------------- 鋼管部 sMd (5 ケース) --------------

        private double ComputeSMd(double Ndd)
        {
            // Case 1: Ndd ≤ sNdt (純引張容量超過)
            if (Ndd <= sNdt) return 0.0;

            // Case 2: sNdt < Ndd < 0 (引張領域、線形相互作用)
            if (Ndd < 0)
            {
                double sigD = ComputeSSigmaD_FromAxial(Ndd);
                return Math.Max(0.0, (sigD - Math.Abs(Ndd) / sAp) * sZe);
            }

            // Case 3: 0 ≤ Ndd ≤ cNdc (コンクリートが軸力、鋼管は均衡曲げ)
            if (Ndd <= cNdc)
            {
                double sigD0 = 1.5 * (sft + Sfc1) / 2.0;
                return sigD0 * sZe;
            }

            // Case 4: cNdc < Ndd ≤ sNdc1 + cNdc (コン飽和、鋼管が超過軸力)
            if (Ndd <= sNdc1 + cNdc)
            {
                double Nresid = Ndd - cNdc;
                double sigD = ComputeSSigmaD_FromAxial(Nresid);
                return Math.Max(0.0, (sigD - Math.Abs(Nresid) / sAp) * sZe);
            }

            // Case 5: sNdc1 + cNdc < Ndd (圧縮容量超過)
            return 0.0;
        }

        /// <summary>
        /// sσD を「鋼管が軸力 Naxial を負担」状態から求める。
        /// sσD = 1.5 × ((π − sθO) × sft + sθO × sfc1) / π
        /// sθO は鋼管に作用する Naxial に対応する塑性中立軸位置から求める。
        /// 簡略実装: Naxial = 0 → sθO = π/2 (バランス)、|Naxial| 増加で sθO が 0 または π に近づく。
        /// 解析式: 全圧縮容量 sNdc1 と引張容量 |sNdt| の比率から sθO を比例近似。
        /// </summary>
        private double ComputeSSigmaD_FromAxial(double Naxial)
        {
            // 鋼管断面で N = (sθO/π × 1.5×sfc1 − (π−sθO)/π × 1.5×sft) × sAp と仮定
            // → sθO = π × (N/sAp + 1.5×sft) / (1.5×(sfc1 + sft))
            double sigCp = 1.5 * Sfc1;
            double sigTp = 1.5 * sft;
            double denom = sigCp + sigTp;
            if (denom < 1e-12) return 0.0;

            double sthetaO = Math.PI * (Naxial / sAp + sigTp) / denom;
            sthetaO = Math.Clamp(sthetaO, 0.0, Math.PI);

            return 1.5 * ((Math.PI - sthetaO) * sft + sthetaO * Sfc1) / Math.PI;
        }

        // -------------- コンクリート部 cMd (3 ケース) --------------

        private double ComputeCMd(double Xn)
        {
            if (Fc <= 0 || cSigmaCk <= 0) return 0.0;

            // Case 1: Xn ≤ t — コンクリートに圧縮なし
            if (Xn <= T) return 0.0;

            // 特異性対策: t < Xn ≤ t × (1+δ) 領域は線形補間で 0 → 通常式の値へ
            double XnLinSwitch = T * (1.0 + XnSingularityWidth);
            if (Xn < XnLinSwitch && Xn > T)
            {
                double cMdAtSwitch = ComputeCMdRaw(XnLinSwitch);
                double t = (Xn - T) / (XnLinSwitch - T);
                return t * cMdAtSwitch;
            }

            return ComputeCMdRaw(Xn);
        }

        private double ComputeCMdRaw(double Xn)
        {
            double cthO = ComputeCThetaO(Xn);
            double cthI = ComputeCThetaI(Xn);

            // Case 3: sro + cri ≤ Xn — 全圧縮 (一般式が cthO=cthI=π を代入した形と一致)
            // ここでは一般式で計算 (両 θ が π にクリップされ自動で簡略化)
            // Case 2 一般式
            double g0 = cthO + (Math.Pow(Math.Cos(cthO), 2) - 5.0 / 2.0) * Math.Sin(2 * cthO) / 3.0;
            double gI = cthI + (Math.Pow(Math.Cos(cthI), 2) - 5.0 / 2.0) * Math.Sin(2 * cthI) / 3.0;

            double cro4 = cro * cro * cro * cro;
            double cri4 = cri * cri * cri * cri;

            double denom = 4.0 * (Xn - T);
            if (Math.Abs(denom) < 1e-12) return 0.0;

            return cSigmaCk / denom * (cro4 * g0 - cri4 * gI);
        }

        // コンクリート外円 (半径 cro) での圧縮セグメントの半角。
        // NA 位置 y_NA = sro - Xn (鋼管中心基準)。半径 cro の円での角度は
        //   θ = acos(y_NA / cro) = acos((sro - Xn) / cro)
        // で求まる。サチュレーションは Xn ≥ sro + cro (NA がコン外円の引張縁に到達)。
        private double ComputeCThetaO(double Xn)
        {
            if (Xn <= T) return 0.0;
            if (Xn >= sro + cro) return Math.PI;
            double arg = (sro - Xn) / cro;
            arg = Math.Clamp(arg, -1.0, 1.0);
            return Math.Acos(arg);
        }

        // コンクリート内円 (半径 cri) での圧縮セグメントの半角。
        //   θ = acos((sro - Xn) / cri)
        // サチュレーション: Xn ≥ sro + cri (NA がコン内円の引張縁に到達)。
        private double ComputeCThetaI(double Xn)
        {
            if (Xn <= T + zh) return 0.0;
            if (Xn >= sro + cri) return Math.PI;
            double arg = (sro - Xn) / cri;
            arg = Math.Clamp(arg, -1.0, 1.0);
            return Math.Acos(arg);
        }

        // -------------- Nc(Xn) と Xn ソルバ (case B 用) --------------

        /// <summary>
        /// 与えられた Xn に対するコンクリート部の軸圧縮力 Nc を返す (cσCk × 圧縮環セクター面積)。
        /// </summary>
        private double ComputeNc(double Xn)
        {
            if (Fc <= 0 || cSigmaCk <= 0) return 0.0;
            if (Xn <= T) return 0.0;

            double cthO = ComputeCThetaO(Xn);
            double cthI = ComputeCThetaI(Xn);

            // 環状セグメント面積 = 外円圧縮セグメント − 内円圧縮セグメント
            // セグメント面積: R² × (θ − sin θ × cos θ)
            double Aouter = cro * cro * (cthO - Math.Sin(cthO) * Math.Cos(cthO));
            double Ainner = cri * cri * (cthI - Math.Sin(cthI) * Math.Cos(cthI));
            double area = Aouter - Ainner;

            return cSigmaCk * Math.Max(0.0, area);
        }

        /// <summary>
        /// Nc(Xn) = Ndd を満たす Xn を二分法で求める。case B (0 ≤ Ndd ≤ cNdc) 専用。
        /// </summary>
        private double SolveXnForConcreteAxial(double targetNc)
        {
            if (targetNc <= 0) return T;
            if (targetNc >= cNdc) return sro + cri;

            double lo = T;
            double hi = sro + cri;
            const int maxIter = 60;
            const double tolN = 1.0;  // N (1 N 精度)

            for (int i = 0; i < maxIter; i++)
            {
                double mid = 0.5 * (lo + hi);
                double Nc_mid = ComputeNc(mid);
                if (Math.Abs(Nc_mid - targetNc) < tolN) return mid;
                if (Nc_mid < targetNc) lo = mid;
                else hi = mid;
            }
            return 0.5 * (lo + hi);
        }

        // -------------- 安全限界モーメント --------------

        /// <summary>
        /// 安全限界曲げモーメント (杭中間部・下部、鋼管のみ)。
        /// |Nud|/sNuc > 0.2 のとき: Mu = β1 β2 × 1.25 × sσCy1 × (1 − |Nud|/sNuc) × sZp
        /// |Nud|/sNuc ≤ 0.2 のとき: Mu = β1 β2 × sσCy1 × sZp (塑性モーメント、軸力依存なし)
        /// 0.2 境界で連続 (どちらも sZp を使用する)。
        /// </summary>
        internal double GetUltimateLimitMomentMiddle(double Nud)
        {
            const double beta2 = 1.0;
            double sNucLocal = sNuc;
            if (sNucLocal <= 0) return 0.0;

            double ratio = Math.Abs(Nud) / sNucLocal;
            if (ratio > 0.2)
            {
                double m = 1.25 * sSigmaCy1 * (1.0 - ratio) * sZp;
                return Beta1 * beta2 * Math.Max(0.0, m);
            }
            else
            {
                return Beta1 * beta2 * sSigmaCy1 * sZp;
            }
        }

        /// <summary>
        /// 安全限界曲げモーメント (杭頭部 コンクリート充填鋼管部、鋼管 + 充填コンクリート)。
        /// Mu = β1 β2 × (sMu + cMu)
        /// sMu = 4 × srm² × t × sin(sθO) × sσU         (鋼管部、塑性近似)
        /// sσU = ((π−sθO) × sσTy + sθO × sσCy1) / π    (角度加重平均応力)
        /// cMu = (2/3) × cσIr × (cro³ sin³(cθO) − cri³ sin³(cθI))   (コンクリート充填鋼管部 環状部)
        /// 中立軸位置 Xn は鋼管+コンクリート全体の軸力釣合 Ns(Xn) + Nc(Xn) = Nud から二分法で求める (approach b)。
        /// </summary>
        internal double GetUltimateLimitMomentHead(double Nud)
        {
            const double beta2 = 1.0;
            if (Fc <= 0 || cSigmaIr <= 0) return 0.0;

            var (sMu, cMu) = ComputeUltimateMomentComponents(Nud);
            return Beta1 * beta2 * (sMu + cMu);
        }

        /// <summary>
        /// Ns(Xn) + Nc(Xn) = Nud を満たす Xn を二分法で求める (安全限界用、塑性応力分布)。
        /// </summary>
        private double SolveXnForUltimate(double Nud)
        {
            // 容量上下限
            double maxCompr = sNuc1 + cNuc;
            double maxTens = sNut;  // 引張は降伏ベース (杭頭部だが Ns は鋼管全体で評価)
            double clamped = Math.Clamp(Nud, -maxTens, maxCompr);

            double lo = 0.0;
            double hi = 2.0 * sro;
            const int maxIter = 60;
            const double tolN = 1.0;

            for (int i = 0; i < maxIter; i++)
            {
                double mid = 0.5 * (lo + hi);
                double f = ComputeNsUltimate(mid) + ComputeNcUltimate(mid) - clamped;
                if (Math.Abs(f) < tolN) return mid;
                if (f < 0) lo = mid;
                else hi = mid;
            }
            return 0.5 * (lo + hi);
        }

        /// <summary>
        /// Xn から鋼管外面角度 sθO を求める (= cθO の式と同形、sro 分母)。
        /// </summary>
        private double ComputeSThetaO(double Xn)
        {
            if (Xn <= 0) return 0.0;
            if (Xn >= 2.0 * sro) return Math.PI;
            double arg = (sro - Xn) / sro;
            return Math.Acos(Math.Clamp(arg, -1.0, 1.0));
        }

        /// <summary>
        /// 鋼管内円 (半径 sri) での圧縮セグメントの半角を Xn から求める。
        /// NA 位置 y_NA = sro - Xn (鋼管中心基準) に対し、半径 sri の円での角度は
        ///   θ = acos(y_NA / sri) = acos((sro - Xn) / sri)
        /// で求まる。サチュレーション: Xn ≥ sro + sri (NA が内円の引張縁に到達)。
        /// </summary>
        private double ComputeSThetaI(double Xn)
        {
            if (Xn <= T) return 0.0;
            if (Xn >= sro + sri) return Math.PI;
            double arg = (sro - Xn) / sri;
            return Math.Acos(Math.Clamp(arg, -1.0, 1.0));
        }

        /// <summary>
        /// 安全限界での鋼管軸力 (塑性応力分布: 圧縮 +sσCy1, 引張 −sσTy)。
        /// </summary>
        private double ComputeNsUltimate(double Xn)
        {
            double sthO = ComputeSThetaO(Xn);
            double sthI = ComputeSThetaI(Xn);

            // 環状塑性圧縮面積 = 外円セグメント − 内円セグメント
            double Aouter = sro * sro * (sthO - Math.Sin(sthO) * Math.Cos(sthO));
            double Ainner = sri * sri * (sthI - Math.Sin(sthI) * Math.Cos(sthI));
            double Acomp = Math.Max(0.0, Aouter - Ainner);

            return sSigmaCy1 * Acomp - sSigmaTy * (sAp - Acomp);
        }

        /// <summary>
        /// 安全限界での充填コンクリート軸力 (cσIr × 圧縮環セクター面積、cNuc でクリップ)。
        /// </summary>
        private double ComputeNcUltimate(double Xn)
        {
            if (Fc <= 0 || cSigmaIr <= 0) return 0.0;
            if (Xn <= T) return 0.0;

            double cthO = ComputeCThetaO(Xn);
            double cthI = ComputeCThetaI(Xn);

            double Aouter = cro * cro * (cthO - Math.Sin(cthO) * Math.Cos(cthO));
            double Ainner = cri * cri * (cthI - Math.Sin(cthI) * Math.Cos(cthI));
            double area = Math.Max(0.0, Aouter - Ainner);

            return Math.Min(cNuc, cSigmaIr * area);
        }

        // -------------- せん断 --------------

        /// <summary>
        /// 使用限界せん断力。Qs = β1 × sfs × sAp / κ
        /// sfs = F / (1.5 √3) (Mises 流降伏剪応力 / 安全率 1.5)
        /// κ = 2.0 (薄肉円管のせん断シェイプファクタ)
        /// 軸力依存なし。
        /// </summary>
        internal double GetServiceLimitShear()
        {
            const double kappa = 2.0;
            double sfs = F / 1.5 / Math.Sqrt(3.0);
            return Beta1 * sfs * sAp / kappa;
        }

        /// <summary>
        /// 損傷限界せん断力。Qd = β1 × 1.5 × sfs × sAp / κ
        /// = 1.5 × Qs (使用限界に対し安全率 1.5 を撤去)
        /// </summary>
        internal double GetDamageLimitShear()
        {
            const double kappa = 2.0;
            double sfs = F / 1.5 / Math.Sqrt(3.0);
            return Beta1 * 1.5 * sfs * sAp / kappa;
        }

        /// <summary>
        /// 安全限界せん断力 (杭中間部・杭下部)。塑性せん断耐力 × 軸力相互作用。
        /// Qu = β1 β2 × sQ0 × √(1 − η²)
        /// sQ0 = 2 t (D − t) × sσTy / √3      (sσTy = 1.1 × 1.5 × sft = 1.1 F)
        /// η = Nud / sNy,  sNy = sσTy × sAp
        /// |η| ≥ 1 では 0 を返す (軸力で降伏到達)。
        /// </summary>
        internal double GetUltimateLimitShearMiddle(double Nud)
        {
            const double beta2 = 1.0;
            double sNy = sSigmaTy * sAp;
            if (Math.Abs(sNy) < 1e-10) return 0.0;

            double eta = Nud / sNy;
            if (Math.Abs(eta) >= 1.0) return 0.0;

            double sQ0 = 2.0 * T * (D - T) * sSigmaTy / Math.Sqrt(3.0);
            return Beta1 * beta2 * sQ0 * Math.Sqrt(1.0 - eta * eta);
        }

        /// <summary>
        /// 安全限界せん断力 (杭頭部 コンクリート充填鋼管部)。中間部式に Mu/sMu 比を乗じる。
        /// Qu = β1 β2 × sQ0 × √(1 − η²) × (Mu / sMu)
        /// 軸力比 η は圧縮では Nuc、引張では Nut で正規化:
        ///   η = Nud / Nuc  (Nud ≥ 0)
        ///   η = Nud / Nut  (Nud &lt; 0)
        /// |η| ≥ 1 または sMu ≤ 0 では 0 を返す。
        /// </summary>
        internal double GetUltimateLimitShearHead(double Nud)
        {
            const double beta2 = 1.0;
            if (Fc <= 0 || cSigmaIr <= 0) return 0.0;

            // 軸力比
            double NucH = NucHead;
            double NutH = NutHead;
            if (NucH <= 0 || NutH <= 0) return 0.0;

            double eta = Nud >= 0 ? Nud / NucH : Nud / NutH;
            if (Math.Abs(eta) >= 1.0) return 0.0;

            // sMu, cMu (Mu/sMu 比のため)
            var (sMu, cMu) = ComputeUltimateMomentComponents(Nud);
            if (sMu <= 0) return 0.0;

            double sQ0 = 2.0 * T * (D - T) * sSigmaTy / Math.Sqrt(3.0);
            double moment_ratio = (sMu + cMu) / sMu;

            return Beta1 * beta2 * sQ0 * Math.Sqrt(1.0 - eta * eta) * moment_ratio;
        }

        /// <summary>
        /// 杭頭部の安全限界モーメント成分 (sMu, cMu) を求める。
        /// 内部で Xn を全断面軸力釣合から二分法で求める。
        /// </summary>
        private (double sMu, double cMu) ComputeUltimateMomentComponents(double Nud)
        {
            double Xn = SolveXnForUltimate(Nud);

            double sthetaO = ComputeSThetaO(Xn);
            double sigmaU = ((Math.PI - sthetaO) * sSigmaTy + sthetaO * sSigmaCy1) / Math.PI;
            double sMu = 4.0 * srm * srm * T * Math.Sin(sthetaO) * sigmaU;

            double cthetaO = ComputeCThetaO(Xn);
            double cthetaI = ComputeCThetaI(Xn);
            double cMu = 2.0 / 3.0 * cSigmaIr * (
                Math.Pow(cro, 3) * Math.Pow(Math.Sin(cthetaO), 3) -
                Math.Pow(cri, 3) * Math.Pow(Math.Sin(cthetaI), 3));

            return (sMu, Math.Max(0.0, cMu));
        }

        // -------------- M-φ 関係 (4 点トリリニア) --------------

        /// <summary>
        /// 鋼管杭中間部・下部の M-φ 関係を返す (鋼管のみ、コンクリート充填なし)。
        /// φ = [0, φ_Md, φ_Mu', φ_u]、M = [0, Md, Mu, Mu]
        /// 軸力第 2 効果 (Nud > 0) と弾性 (Nud ≤ 0) で θMu の式が異なる。
        /// L = 4D を等価長として使用。
        /// </summary>
        internal (System.Collections.Generic.List<double> phis, System.Collections.Generic.List<double> moments)
            GetMPhiRelationshipMiddle(double Nud)
        {
            double Md = GetDamageLimitMomentMiddle(Nud);
            double Mu = GetUltimateLimitMomentMiddle(Nud);

            if (Md <= 0 || Mu <= 0)
                return (new() { 0.0 }, new() { 0.0 });

            double EI = E * Iisteel;
            double phiMd = Md / EI;

            double L = 4.0 * D;
            double thetaMu = ComputeThetaMu(Nud, Mu, L, EI);

            double beta = D / T * sSigmaTy / E;
            double pmNy = sSigmaTy * sAp;
            double nud = Math.Max(Nud, 0.0);
            double term = pmNy * beta > 0 ? 1.0 - nud / (pmNy * beta) : 0.0;
            term = Math.Max(0.0, term);

            double pmRMuPrime = 0.01 * (term * term + 0.246 * term);
            double pmR95 = 0.021 * (term * term + 0.289 * term);

            const double beta3 = 1.0;
            const double beta3p = 0.45;

            double thetaMuPrime = beta3 * (beta3p * pmRMuPrime + 1.0) * thetaMu;
            double theta95 = (beta3p * pmR95 + 1.0) * thetaMu;
            double thetaU = beta3 * theta95;

            double phiMuPrime = thetaMuPrime * 2.0 / D;
            double phiU = thetaU * 2.0 / D;

            return BuildMPhiList(phiMd, phiMuPrime, phiU, Md, Mu);
        }

        /// <summary>
        /// 鋼管杭頭部 (コンクリート充填鋼管部) の M-φ 関係を返す。
        /// φ = [0, φ_Md, φ_Mu', φ_u]、M = [0, Md, Mu, Mu]
        /// 杭頭部 EI は L³/EIeq = (L³−(l2+l3)³)/EI1 + ((l2+l3)³−l3³)/EI2 + l3³/EI3 で合成。
        /// 既定: l1=D (コンクリート充填鋼管部), l2=0 (厚部なし), l3=3D (鋼管のみ)。
        /// </summary>
        internal (System.Collections.Generic.List<double> phis, System.Collections.Generic.List<double> moments)
            GetMPhiRelationshipHead(double Nud)
        {
            if (Fc <= 0 || cSigmaIr <= 0)
                return (new() { 0.0 }, new() { 0.0 });

            double Md = GetDamageLimitMomentHead(Nud);
            double Mu = GetUltimateLimitMomentHead(Nud);

            if (Md <= 0 || Mu <= 0)
                return (new() { 0.0 }, new() { 0.0 });

            // EIeq 計算 (3 セグメント直列合成、デフォルト構成)
            double EIeq = ComputeEIeqHead();

            double phiMd = Md / EIeq;

            double L = 4.0 * D;
            double thetaMu = ComputeThetaMu(Nud, Mu, L, EIeq);

            // Xn 依存量
            double Xn = SolveXnForUltimate(Nud);
            double ptAc = ComputePtAc(Xn);
            double ptA = sAp + Atr;
            double zeta = ptA > 0 ? 2.0 / 3.0 * (1.0 - ptAc / ptA) : 0.0;
            zeta = Math.Max(0.0, zeta);

            double Di = D - 2.0 * T;     // 鋼管の内径
            const double cTau = 0.5;
            double cLc = Di > 0
                ? Zn * (cro * cro - cri * cri) * cSigmaIr / (cTau * Di)
                : 0.0;
            double cLPrime = zeta * cLc;

            double pmNy = sSigmaTy * sAp;
            double ptNu = 2.0 * srm * T * Math.PI * sSigmaCy1 + (cro * cro - cri * cri) * Math.PI * cSigmaIr;
            double beta = D / T * sSigmaTy / E;
            double nud = Math.Max(Nud, 0.0);

            double termMu = pmNy * beta > 0 ? 1.0 - nud / (pmNy * beta) : 0.0;
            termMu = Math.Max(0.0, termMu);
            double thetaMuPrime = D > 0 ? 0.172 * termMu * cLPrime / D : 0.0;

            double term95 = ptNu * beta > 0 && D > 0
                ? 1.0 - nud * cLPrime / (ptNu * beta * D)
                : 0.0;
            term95 = Math.Max(0.0, term95);
            double pmR95 = 0.003 * (term95 * term95 + 0.289 * term95);

            const double beta3 = 1.0;
            const double beta3p = 0.75;

            double theta95 = (beta3p * pmR95 + 1.0) * thetaMu;
            double thetaU = beta3 * theta95;

            double phiMuPrime = thetaMuPrime * 2.0 / D;
            double phiU = thetaU * 2.0 / D;

            return BuildMPhiList(phiMd, phiMuPrime, phiU, Md, Mu);
        }

        /// <summary>
        /// 軸力 Nud と等価長 L、剛性 EI から θMu を計算する。
        /// Nud > 0: 軸力 2 次効果込み θ = Mu × (1 − αL/tan(αL)) / (Nud × L)
        /// Nud ≤ 0: 弾性 θ = Mu × L / (3 EI)
        /// αL ≥ π/2 (座屈到達) では弾性式にフォールバック (発散回避)。
        /// </summary>
        private static double ComputeThetaMu(double Nud, double Mu, double L, double EI)
        {
            if (EI <= 0 || L <= 0) return 0.0;

            if (Nud <= 0)
                return Mu * L / (3.0 * EI);

            double alphaPt = Math.Sqrt(Math.Abs(Nud) / EI);
            double aL = alphaPt * L;

            // 座屈到達 (αL ≥ π/2 for tip-moment cantilever) で弾性近似にフォールバック
            if (aL >= Math.PI * 0.5 - 1e-3 || double.IsNaN(aL))
                return Mu * L / (3.0 * EI);

            double tanAL = Math.Tan(aL);
            if (Math.Abs(tanAL) < 1e-12)
                return Mu * L / (3.0 * EI);

            return Mu * (1.0 - aL / tanAL) / (Nud * L);
        }

        /// <summary>
        /// 杭頭部の 3 セグメント直列合成 EIeq を計算する。
        /// L³/EIeq = (L³ − (l2+l3)³)/EI1 + ((l2+l3)³ − l3³)/EI2 + l3³/EI3
        /// 既定: l1 = D (コンクリート充填鋼管部), l2 = 0 (厚部分なし、EI2 = EI3 と仮定), l3 = 3D
        /// </summary>
        private double ComputeEIeqHead()
        {
            double l1 = D;
            double l2 = 0.0;
            double l3 = 3.0 * D;
            double L = l1 + l2 + l3;

            double EI1 = E * Iisteel + Ec * Iiconcrete;   // コンクリート充填鋼管部 合成 (鋼管 + 充填コン)
            double EI2 = E * Iisteel;                      // 厚部 (本実装ではデフォルト同一)
            double EI3 = E * Iisteel;                      // 鋼管のみ薄部

            double L3 = L * L * L;
            double l2l3 = l2 + l3;
            double l2l3_3 = l2l3 * l2l3 * l2l3;
            double l3_3 = l3 * l3 * l3;

            double inv = (L3 - l2l3_3) / EI1 + (l2l3_3 - l3_3) / EI2 + l3_3 / EI3;
            return inv > 0 ? L3 / inv : E * Iisteel;
        }

        /// <summary>
        /// コンクリート充填鋼管部 領域における圧縮側鋼管断面積とずれ止め支圧面積の和 ptAc を、Xn から計算する。
        /// ζ = 2/3 × (1 − ptAc/ptA) の構成要素。
        /// </summary>
        private double ComputePtAc(double Xn)
        {
            double sthO = ComputeSThetaO(Xn);
            double sthI = ComputeSThetaI(Xn);
            double Aouter_s = sro * sro * (sthO - Math.Sin(sthO) * Math.Cos(sthO));
            double Ainner_s = sri * sri * (sthI - Math.Sin(sthI) * Math.Cos(sthI));
            double A_steel_comp = Math.Max(0.0, Aouter_s - Ainner_s);

            double cthO = ComputeCThetaO(Xn);
            double cthI = ComputeCThetaI(Xn);
            double Aouter_c = cro * cro * (cthO - Math.Sin(cthO) * Math.Cos(cthO));
            double Ainner_c = cri * cri * (cthI - Math.Sin(cthI) * Math.Cos(cthI));
            double A_conc_comp = Math.Max(0.0, Aouter_c - Ainner_c);

            return A_steel_comp + A_conc_comp;
        }

        /// <summary>
        /// 4 点 M-φ リストを構築する。φ の単調性を確保 (φMd ≤ φMu' ≤ φu)、退化時は短縮。
        /// </summary>
        private static (System.Collections.Generic.List<double>, System.Collections.Generic.List<double>)
            BuildMPhiList(double phiMd, double phiMuPrime, double phiU, double Md, double Mu)
        {
            // 単調性ガード: 値が小さくなる場合は前の値で更新
            double pMd = Math.Max(0.0, phiMd);
            double pMuP = Math.Max(pMd, phiMuPrime);
            double pU = Math.Max(pMuP, phiU);

            return (
                new System.Collections.Generic.List<double> { 0.0, pMd, pMuP, pU },
                new System.Collections.Generic.List<double> { 0.0, Md, Mu, Mu }
            );
        }
    }
}

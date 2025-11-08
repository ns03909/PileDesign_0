using System;

namespace PileDesign.Models.PileLibrary
{
    public class EmbedmentPileTop : BaseModel
    {
        // 杭外径
        private double _d;
        public double D
        {
            get => _d;
            set => SetProperty(ref _d, value);
        }

        // パイルキャップ内への埋込み深さ
        private double _h;
        public double H
        {
            get => _h;
            set => SetProperty(ref _h, value);
        }

        // 杭頭の曲げモーメントMとせん断力Qの比
        private double _monQ;
        public double MonQ
        {
            get => _monQ;
            set => SetProperty(ref _monQ, value);
        }

        // 水平力作用方向の杭前面のパイルキャップのコーン状破壊面の有効投影面積
        public double Aqc2 => 0.5 * Math.PI * Math.Pow(C + D * 0.5, 2);

        // 杭表面からパイルキャップ側面までの距離
        private double _c;
        public double C
        {
            get => _c;
            set => SetProperty(ref _c, value);
        }


        // パイルキャップコンクリートの設計基準強度
        private double _fc;
        public double Fc
        {
            get => _fc;
            set => SetProperty(ref _fc, value);
        }

        // パイルキャップコンクリートの長期許容圧縮応力度
        public double Lfc => Fc / 3.0;

        // パイルキャップコンクリートの短期許容圧縮応力度
        public double Sfc => Fc * 2.0 / 3.0;

        // パイルキャップのコンクリートのせん断強度
        public double CSigmaS => 0.31 * Math.Sqrt(Fc);

        // 埋込部の使用限界曲げモーメント
        public double SMph => 1.0 * Lfc * D * H * H * MonQ / (6 * MonQ + 4 + H);

        // 埋込部の使用限界曲げモーメント
        public double DMph => 0.75 * Sfc * D * H * H * MonQ / (6 * MonQ + 4 + H);

        // 埋込部の安全限界曲げモーメント
        public double UMph => 0.75 * Fc * D * H * H * MonQ / (6 * MonQ + 4 + H);

        // 埋込部の使用限界せん断力
        public double SQph => 1.0 * (1.0 / 3.0) * CSigmaS * Aqc2;

        // 埋込部の損傷限界せん断力
        public double DQph => 0.75 * (2.0 / 3.0) * CSigmaS * Aqc2;

        // 埋込部の安全限界せん断力
        public double UQph => 0.75 * CSigmaS * Aqc2;

    }
}

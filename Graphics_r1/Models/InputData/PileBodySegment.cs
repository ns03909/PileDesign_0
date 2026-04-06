namespace PileDesign.Models.InputData
{
    public class PileBodySegment : BaseModel
    {
        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        // 要素長さ
        private double _segmentLength;
        public double SegmentLength
        {
            get => _segmentLength;
            set
            {
                if (value <= 0 || !double.IsFinite(value)) return;
                SetProperty(ref _segmentLength, value);
            }
        }

        // 要素深さ（PileBodySegmentsUpdateで累積計算される値のためバリデーション不要）
        private double _segmentDepth;
        public double SegmentDepth
        {
            get => _segmentDepth;
            set => SetProperty(ref _segmentDepth, value);
        }

        // 要素断面
        private PileSection _pileSection;
        public PileSection PileSection
        {
            get => _pileSection;
            set => SetProperty(ref _pileSection, value);
        }

        // コンストラクタ
        public PileBodySegment()
        {
            No = 1;
            SegmentLength = 15.0;
            SegmentDepth = 15.0;
            PileSection = new();
        }

        // 深いコピーを作成するメソッド
        public PileBodySegment DeepCopy()
        {
            var copy = (PileBodySegment)this.MemberwiseClone();
            copy.PileSection = this.PileSection.DeepCopy();
            return copy;
        }
    }
}
namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 鋼管杭の杭頭部で、杭体ウィンドウの l1・l2 から作った区間か (2026-11-21)。
    /// 作った区間の長さは l1・l2 で決まり、表では編集しない。
    /// </summary>
    public enum HeadZoneRole
    {
        /// <summary>l1・l2 と関係のない区間 (表で編集する)。</summary>
        None = 0,
        /// <summary>コンクリート充填鋼管部 (杭頭〜l1)。</summary>
        Fill = 1,
        /// <summary>充填部下端〜鋼管の断面変化部 (l2)。充填部と同じ鋼管の鋼管部。</summary>
        ThickPart = 2,
    }

    public class PileBodySegment : BaseModel
    {
        /// <summary>
        /// 鋼管杭の杭頭部で l1・l2 から作った区間か (<see cref="PileBodyInput.SetHeadZone"/>)。保存する。
        /// None 以外の区間は、区間長を表で編集せず、削除もしない (l1・l2 を 0 にして消す)。
        /// </summary>
        private HeadZoneRole _headZoneRole;
        public HeadZoneRole HeadZoneRole
        {
            get => _headZoneRole;
            set => SetProperty(ref _headZoneRole, value);
        }

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
                if (!double.IsFinite(value)) return;
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
        private PileSection _pileSection = null!;   // コンストラクタで入る
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
            var copy = CloneWithoutSubscribers<PileBodySegment>();
            copy.PileSection = this.PileSection.DeepCopy();
            return copy;
        }
    }
}
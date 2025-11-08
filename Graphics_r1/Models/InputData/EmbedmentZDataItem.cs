namespace PileDesign.Models.InputData
{
    public class EmbedmentZDataItem : ZDataItem
    {
        public EmbedmentZDataItem DeepCopy()
        {
            return new EmbedmentZDataItem
            {
                Z = this.Z,
                IsChangeable = this.IsChangeable,
                SegmentNo = this.SegmentNo,
                GroundInput = this.GroundInput?.DeepCopy(), // 必要ならDeepCopy
                GroundDisp1 = this.GroundDisp1,
                GroundDisp2 = this.GroundDisp2,
                GroundDisp1L = this.GroundDisp1L,
                GroundDisp2L = this.GroundDisp2L
            };
        }
    }
}
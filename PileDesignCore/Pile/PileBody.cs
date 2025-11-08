using System.Collections.Generic;

namespace PileDesignCore.Pile
{
    internal class PileBody
    {
        public string name { get; set; }
        public int no { get; set; }
        public string pile_category { get; set; }
        public float pile_toe_alpha { get; set; }
        public float pile_toe_dp { get; set; }
        public int pile_toe_n { get; set; }
        public string pile_top_type { get; set; }
        public float pile_seg_len { get; set; }
        public List<float> pile_seg { get; set; }
        public int pile_seg_num { get; set; }
        public float pile_toe_d { get; set; }
        public float LRonDtoe { get; set; }
        public float URonDtoe { get; set; }
        public float r_OC { get; set; }
        public int seg_input_state { get; set; }
        public List<float> PRs { get; set; }

        public PileBody(
            string name,
            int no,
            string pile_category,
            float pile_toe_alpha,
            float pile_toe_dp,
            int pile_toe_n,
            string pile_top_type,
            float pile_seg_len,
            List<float> pile_seg,
            int pile_seg_num,
            float pile_toe_d,
            float LRonDtoe,
            float URonDtoe,
            float r_OC,
            int seg_input_state
        )
        {
            this.name = name;
            this.no = no;
            this.pile_category = pile_category;
            this.pile_toe_alpha = pile_toe_alpha;
            this.pile_toe_dp = pile_toe_dp;
            this.pile_toe_n = pile_toe_n;
            this.pile_top_type = pile_top_type;
            this.pile_seg_len = pile_seg_len;
            this.pile_seg = pile_seg;
            this.pile_seg_num = pile_seg_num;
            this.pile_toe_d = pile_toe_d;
            this.LRonDtoe = LRonDtoe;
            this.URonDtoe = URonDtoe;
            this.r_OC = r_OC;
            this.seg_input_state = seg_input_state;
        }

        //public void set_PRs(List<float> cl_PRs)
        //{
        //    PRs = cl_PRs;
        //}
    }
}

using PileDesign.Models.InputData;

namespace PileDesign.FEM
{
    /// <summary>
    /// ダミー梁の 1 ステップぶんの結果。
    ///
    /// <b>プロパティは get だけにしないこと。</b>この型は <see cref="DummyBeam.DummyBeamResults"/>
    /// を通じて保存グラフ (<c>AnaModel</c>) に入る。<c>ReferenceHandler.Preserve</c> では
    /// 「書き出されるが復元されない」プロパティがあると、そこに付いた <c>$id</c> が
    /// 読込時に登録されず、他所からの <c>$ref</c> が解決できなくなる。
    /// そうなると<b>保存ファイルが一切開けなくなる</b> (README の暗黙の前提 1)。
    ///
    /// 現状はダミー梁の結果が空のまま運用されているため表面化していないだけで、
    /// 1 件でも入った瞬間に壊れる。引数付きコンストラクタは使い勝手のために残し、
    /// 復元用に既定コンストラクタと set を持たせる。
    /// </summary>
    public class DummyBeamResult
    {
        public DummyBeamResult() { }

        public DummyBeamResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step)
        {
            LoadCase = loadCase;
            LoadCombination = loadCombination;
            IsLiquefaction = isLiquefaction;
            Step = step;
        }

        public LoadCase LoadCase { get; set; }
        public LoadCombination LoadCombination { get; set; }
        public bool IsLiquefaction { get; set; }
        public int Step { get; set; }

        public DummyBeamResult DeepCopy()
        {
            return new DummyBeamResult(
                this.LoadCase?.DeepCopy(),
                this.LoadCombination?.DeepCopy(),
                this.IsLiquefaction,
                this.Step
            );
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1;

/// <summary>
/// 診断から入力画面へ移るまでに並びが変わっても、最初の診断と同じ杭体・区間・地盤を示す (番号ではなく実体で追う)。
/// </summary>
[TestClass]
public class NavigationFollowTests
{
    /// <summary><b>本題。</b> 杭体を先頭に足して番号がずれたら、控えた杭体のいまの番号を返す。消されていれば null。</summary>
    [TestMethod]
    public void AMovedBodyOrSegment_IsFoundAtItsNewNumber()
    {
        var vm = new MainWindowViewModel();
        var bodies = vm.CurrentInputModel!.PileBodies;
        bodies.Add(bodies[0].DeepCopy());
        var bodyTarget = DiagnosticTarget.PileBody(2);
        var segTarget = DiagnosticTarget.PileBodySegment(2, 1);
        object body = vm.SubjectOf(bodyTarget)!;
        object segment = vm.SubjectOf(segTarget)!;

        bodies.Insert(0, bodies[0].DeepCopy());
        Assert.AreEqual(DiagnosticTarget.PileBody(3), vm.RelocateSubject(bodyTarget, body));
        Assert.AreEqual(DiagnosticTarget.PileBodySegment(3, 1), vm.RelocateSubject(segTarget, segment));

        bodies.Remove((PileBodyInput)body);
        Assert.IsNull(vm.RelocateSubject(bodyTarget, body), "消した杭体を見つけたことにしています");
    }

    [TestMethod]
    public void AMovedGroundOrLayer_IsFoundAtItsNewNumber()
    {
        var vm = new MainWindowViewModel();
        var grounds = vm.CurrentInputModel!.GroundsInput;
        var layerTarget = DiagnosticTarget.GroundLayer(1, 1);
        object layer = vm.SubjectOf(layerTarget)!;
        grounds.Insert(0, grounds[0].DeepCopy());
        Assert.AreEqual(DiagnosticTarget.GroundLayer(2, 1), vm.RelocateSubject(layerTarget, layer));
    }
}

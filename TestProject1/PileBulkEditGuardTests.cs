using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.ObjectModel;

namespace TestProject1;

[TestClass]
public class PileBulkEditGuardTests
{
    [TestMethod] public void DerivedPileHeadOverflowIsRejectedBeforeChangingZ()
    {
        var pile = new PileLayoutDataItem { IsSelected = true, Z = 0, FoundationBeamDeltaZc = -double.MaxValue };
        Assert.ThrowsException<ArgumentException>(() => new PileLayoutService().BulkEditSelectedPiles([pile],
            new PileLayoutService.BulkEditOptions { ApplyPileTopLevel = true, PileTopLevel = double.MaxValue }));
        Assert.AreEqual(0.0, pile.Z);
    }

    [TestMethod]
    [DoNotParallelize]
    public void OverflowInDependentEarthquakeLoadsIsRejected()
    {
        bool mode = PileDesign.Common.AxialForceModeContext.IsVariationMode;
        try
        {
            PileDesign.Common.AxialForceModeContext.IsVariationMode = true;
            var pile = new PileLayoutDataItem { IsSelected = true };
            pile.AxialForceLevel1s[0] = double.MaxValue;
            Assert.ThrowsException<ArgumentException>(() => new PileLayoutService().BulkEditSelectedPiles([pile],
                new PileLayoutService.BulkEditOptions { ApplyAxialForceVL = true, AxialForceVL = double.MaxValue }));
            Assert.AreEqual(0.0, pile.AxialForceVL0);
            Assert.AreEqual(double.MaxValue, pile.AxialForceLevel1s[0]);
        }
        finally { PileDesign.Common.AxialForceModeContext.IsVariationMode = mode; }
    }

    [TestMethod] public void LaterOverflowDoesNotChangeEarlierPilesOrAttributes()
    {
        var first = new PileLayoutDataItem { PileNo = 1, IsSelected = true, Z = 1, PileBodyNo = 1 };
        var second = new PileLayoutDataItem { PileNo = 2, IsSelected = true, Z = double.MaxValue, PileBodyNo = 1 };
        var options = new PileLayoutService.BulkEditOptions { ApplyPileTopLevel = true, IsAddPileTopLevel = true,
            PileTopLevel = double.MaxValue, ApplyPileBodyNo = true, PileBodyNo = 2 };
        Assert.ThrowsException<ArgumentException>(() => new PileLayoutService().BulkEditSelectedPiles([first, second], options));
        Assert.AreEqual(1.0, first.Z); Assert.AreEqual(1, first.PileBodyNo); Assert.AreEqual(1, second.PileBodyNo);
    }

    [DataTestMethod]
    [DataRow(0.0)] [DataRow(-1.0)] [DataRow(1.1)] [DataRow(double.NaN)] [DataRow(double.PositiveInfinity)]
    public void InvalidFactorIsRejected(double factor)
    {
        var pile = new PileLayoutDataItem { IsSelected = true, GroupPileFactor = 0.5 };
        Assert.ThrowsException<ArgumentException>(() => new PileLayoutService().BulkEditSelectedPiles([pile],
            new PileLayoutService.BulkEditOptions { ApplyPileGroupFactor = true, PileGroupFactor = factor }));
        Assert.AreEqual(0.5, pile.GroupPileFactor);
    }

    [TestMethod] public void FactorAdditionChecksEachFinalValue()
    {
        var a = new PileLayoutDataItem { IsSelected = true, GroupPileFactor = 0.5 };
        var b = new PileLayoutDataItem { IsSelected = true, GroupPileFactor = 0.9 };
        Assert.ThrowsException<ArgumentException>(() => new PileLayoutService().BulkEditSelectedPiles([a, b],
            new PileLayoutService.BulkEditOptions { ApplyPileGroupFactor = true, IsAddPileGroupFactor = true, PileGroupFactor = 0.2 }));
        Assert.AreEqual(0.5, a.GroupPileFactor); Assert.AreEqual(0.9, b.GroupPileFactor);
    }

    [TestMethod] public void ValidAdditionAndUnusedInvalidValuesAreAllowed()
    {
        var pile = new PileLayoutDataItem { IsSelected = true, Z = 3, GroupPileFactor = 0.5 };
        new PileLayoutService().BulkEditSelectedPiles([pile], new PileLayoutService.BulkEditOptions
        { ApplyPileTopLevel = true, IsAddPileTopLevel = true, PileTopLevel = 2,
            ApplyPileGroupFactor = true, IsAddPileGroupFactor = true, PileGroupFactor = 0.25, AxialForceVL = double.NaN });
        Assert.AreEqual(5.0, pile.Z); Assert.AreEqual(0.75, pile.GroupPileFactor);
    }

    [TestMethod] public void InvalidLoadDoesNotCommitOtherChanges()
    {
        var pile = new PileLayoutDataItem { IsSelected = true, Z = 1 };
        Assert.ThrowsException<ArgumentException>(() => new PileLayoutService().BulkEditSelectedPiles([pile],
            new PileLayoutService.BulkEditOptions { ApplyPileTopLevel = true, PileTopLevel = 10,
                ApplyLevel2 = [false, false, true, false], Level2Values = [0, 0, double.PositiveInfinity, 0] }));
        Assert.AreEqual(1.0, pile.Z);
    }
}

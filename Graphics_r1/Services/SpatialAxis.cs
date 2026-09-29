using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;
namespace PileDesign.Services;
internal static class SpatialAxis
{
    internal static int Choose(IEnumerable<Point3D> points, System.Threading.CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        double[] min = [double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity];
        double[] max = [double.NegativeInfinity,double.NegativeInfinity,double.NegativeInfinity];
        foreach (var p in points)
        {
            token.ThrowIfCancellationRequested();
            double[] v = [p.X,p.Y,p.Z];
            for (int i=0;i<3;i++) { min[i]=Math.Min(min[i],v[i]); max[i]=Math.Max(max[i],v[i]); }
        }
        int axis=0;
        for(int i=1;i<3;i++) if(max[i]*0.5-min[i]*0.5 > max[axis]*0.5-min[axis]*0.5) axis=i;
        return axis;
    }
}

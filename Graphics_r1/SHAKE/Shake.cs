//using DocumentFormat.OpenXml.Drawing;
//using DocumentFormat.OpenXml.Wordprocessing;
//using ExCSS;
//using HelixToolkit.SharpDX.Core.Core;
//using MathNet.Numerics.Optimization;
//using ScottPlot.Rendering.RenderActions;
//using SharpVectors.Converters;
//using SkiaSharp;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Numerics;
//using System.Runtime.Intrinsics.X86;
//using System.Security.Permissions;
//using System.Text;
//using System.Threading.Tasks;
//using System.Windows.Forms;
//using System.Xml.Schema;
/////
//namespace PileDesign.SHAKE
//{
//    public class Shake
//    {
//        public List<double> X { get; set; }
//        public double Time { get; set; }
//        public List<double> T { get; set; }
//        public double Wgk { get; set; }
//        public double Ww { get; set; }
//        public double Gt { get; set; }
//        public double Sko { get; set; }

//        public void main(double t, double ww, double gt, double sko)
//        {

//            // input file

//            // output file1

//            // output file2

//            Ww = 0.624;
//            Gt = 32.2;
//            int mamax = 4096;

//            int nax = mamax + 5;
//            douintble naa = nax + 3 * (mamax + 4);
//            int ns = naa + 2 * mamax;
//            int ninv = ns + nax / 8 + 1;
//            int ntot = ninv + nax / 8 + 1;
//            if(sko < 0.00001)
//            {
//                sko = 0.45;
//            }

//            SHAKIT(X[1], X[nax], X[naa], X[ns], X[ninv]);



//        }


//        public void SHAKIT(Complex[] x, Complex[] ax, double[] s, double[] inv)
//        {
//            double[] ll = new double [3];
//            double[] lt = new double[3];
//            double[] lnsw = new double[3];

//            double[] lll = new double[2];
//            double[] llgs = new double[2];
//            double[] llpch = new double[2];
//            double[] llpl = new double[2];
//            double[] lnv = new double[2];
//            double[] sk = new double[2];

//            double[] ll5 = new double[15];
//            double[] lt5 = new double[15];
//            double[] lp5 = new double[15];
//            double[] lp3 = new double[3];
//            double[,] idamp = new double[27, 11];
//            double[] mm = new double[3];

//            for (int i = 1; i <= 3; i++)
//            {
//                ll[i] = 0;
//                lt[i] = 0;
//            }

//            for (int l = 1; l <= 9; l++)
//            {
//                for(int i = 1; i <= 3; i++)
//                {
//                    idamp[l, i] = 0;
//                }
//            }
//        }


//        public void EARTHQ(List<Complex> x, Complex[,] ax, double[] s, double[] inv)
//        {
//            double pi2 = 6.283185307179586;

//            int nv = 1000; // number of acc. values to be read
//            int ma = 4096; // length of motion inculuding trailing zeros
//            double dt = 0.01; // time step between values to be read
//            double finpeq = 0.0; // frequency of peak ground motion
//            double fmat = 0.0; // frequency of max. acc. value
//            int mma = 4096; // length of significant part of motion
//            double df = 0.01; // frequency steps in freq. domain

//            double xf = 1.0; // multiplication factor for acceleration values

//            int ma2 = 2;
//            while (true) // 2
//            {
//                if (ma2 >= ma)
//                { break; }
//                ma2 *= 2;
//            }
//            ma = ma2; // 3

//            mma = nv + nv / 10; //////// int / int

//            if (mma > ma)
//            {
//                mma = ma;
//            }
//            ma2 = ma + 2;
//            int mfold = ma2 / 2; //////// int / int
//            mfold = mfold + 1;
//            df = 1.0 / (ma * dt);
//            double fma = (double)ma;
//            double mx = (Math.Log10(fma) / Math.Log10(2.0)) - 1;
//            while (true)
//            {
//                double nmx = Math.Pow(2, mx + 1); // 1
//                if (ma <= nmx)
//                {
//                    break;
//                }
//                mx += 1;
//            }
//            int npl = 999;
//            int ncards = (nv - 1) / npl + 1; // 11
//            int jl = npl * ncards - nv;
//            nv += 1;
//            int n = 0;
//            int lc = 0;

//            List<double> xr = []; ////////////////////

//            for (int i = 1; i <= ncards; i++) // <- 31
//            {
//                lc += 1;
//                if (i == ncards || jl != 0)
//                {
//                    jl = npl + 1 - jl;
//                    for (int j = jl; j <= npl; j++) // 5
//                    {
//                        xr[j] = 0.0;
//                    }
//                }
//                int icheck = ncards - i; // 6

//                for (int j = 1; j <= npl; j++) // 311
//                {
//                    n += 1;
//                    x[n] = new Complex(xr[j], xr[j + 1]);
//                }
//            } // 31

//            n += 1;
//            for (int i = n; i <= mfold; i++) // 32
//            {
//                x[i] = new Complex(0.0, 0.0);

//            }

//            XMX(x, ma, xm, nxmax);
//            double xmax = new();

//            if (xmax >= 0.00001)
//            {
//                xf = xmax / xm;
//            }

//            for (int i = 1; i <= n; i++) // 300
//            {
//                x[i] = x[i] * xf; // 30
//            }

//            xmax = xm * xf;
//            double tmax = (double)(nxmax - 1) * dt;

//            RFFT(x, mx, inv, s, iferr, 1);

//            // remove frequencies above fmax and find max. acc. of new motion

//            double freq = 0;
//            double sxx = 0;
//            double sfx = 0;
//            double ncut = 0;
//            for (int i = 1; i <= mfold; i++) // 33
//            {
//                freq = (double)(i - 1) * df;
//                if (freq > fmax)
//                {
//                    ncut += 1;
//                    x[i] = 0;
//                }
//                double Xa = Math.Abs(x[i]);
//                double sxx = sxx + Xa * Xa;
//                double sfx = sfx + freq * Xa * Xa;
//                ax[1, i] = x[i];
//                freq = freq + df;
//            } // 33
//            sfx = sfx / sxx;
//            ncut = mfold - ncut;
//            int nzero = ncut + 1;

//            RFSN(x, mx, inv, s, iferr, -2);
//            XMX(x, ma, xmax, nxmax);

//            for (int i = 1; i <= mfold; i++)
//            {
//                x[i] = ax[1, i];

//            }
//        }

//        public void CURVEG(int nc, List<int> nv, int k1, double a, double b, 
//            int nn, double tstep, int nt, double t, double v, double x, double y, int nstep)
//        {
//            int xmin = 100_000_000;
//            double xmax = 0.0;
//            for(int l = 1; l <= nc; l++)
//            {
//                int m = nv[l];

//                if(xmax < x[l,m])
//                {
//                    xmax = x[l, m];
//                }
//                if (xmin > x[l, 1])
//                {
//                    xmin = x[l, 1];
//                }
//                m = m - 1;
//                for(int i = 1; i <= m; i++)
//                {
//                    double x1 = x[l, i];
//                    double x2 = x[l, i + 1];
//                    if(k1 == 2)
//                    {
//                        x1 = Math.Log10(x1);
//                        x2 = Math.Log10(x2);
//                    }
//                    x[l, i] = x[l, i + 1];
//                    a[l, i] = (y[l, i + 1] - y[l, i]) / (x2 - x1);
//                    b[l, i] = -a[l, i] * x1 + y[l, i];
//                } // 1

//                STEPG(k1, nn, tstep, nt, xmin, xmax, t, nstep);

//                for(int l = 1; l <= nc; l++)
//                {
//                    int m = nv[l] - 1;
//                    for (int i = 1; i <= nstep; i++)
//                    {
//                        for (int j = 1; j <= m; j++)
//                        {
//                            if (t[i] < x[l, j])
//                            {
//                                break;
//                            }
//                        }
//                        int j = m; //////////
//                        double tt = t[i]; // 31

//                        if (k1 == 2)
//                        {
//                            tt = Math.Log10(tt);
//                        }
//                    }
//                }
//                v[l, i] = a[l, J] * tt + b[l, j];
//            }
//        }

//        public void STEPG(int kk, int nn, double[] tstep, int[] nt, double t1, double tn, double[] t, int nstep)
//        {

//            if (kk == 1)
//            {
//                int k = 1;
//                t[k] = 0.0;
//                double save = 0.0;
//                for (int n = 1; n <= nn; n++)
//                {
//                    int m = nt[n];
//                    double step = (tstep[n] - save) / (double)m;
//                    save = tstep[n];
//                    for (int i = 1; i <= m; i++)
//                    {
//                        k = k + 1;
//                    }
//                }
//                t[k] = t[k - 1] + STEP;
//                nstep = k;
//            }
//            else if (kk == 2)
//            {
//                double nst = Math.Log10(t1);
//                if (t1 < 1.0)
//                {
//                    nst = nst - 1.0;
//                }
//                double step = 1 / (double)nn;
//                int k = 1;
//                double ta = Math.Pow(10, nst);
//                t[1] = ta;
//                for (int j = 2; j <= nn; j++) // 22
//                {
//                    k += 1;
//                    t[k] = ta * Math.Pow(10, step * j);
//                    if (t[k] > t1)
//                    {
//                        break;
//                    }
//                }
//                ta = t[k - 1]; // 221
//                k = 0;

//                bool isloop = true;
//                while (true)
//                {
//                    if (!isloop)
//                    {
//                        break;
//                    }

//                    for (int j = 1; j <= nn; j++) //211
//                    {
//                        k += 1;
//                        t[k] = ta * Math.Pow(10, step * j);
//                        if (t[k] > tn)
//                        {
//                            isloop = false;
//                            break;
//                        }
//                    }
//                }

//                if (isloop)
//                {
//                    ta = ta * 10;
//                }
//                nstep = k; // 212
//            }
//        }

//        public void RESP(int ln, int ls, int nn, Complex[] x, Complex[,] ax, double[,] a, double[] s, double[] inv)
//        {
//            int nn;
//            int nd;
//            double[,] id;

//            int kper;

//            if(kper != 0)
//            {
//                for(int i = 1; i <= nlines; i++)
//                {

//                }
//            }
//            else
//            {
//                int nnm = 152;
//                List<double> t = [0.01, 0.02];

//                for (int i = 1; i <= mfold; i++) // 101
//                {
//                    a[1, i] = x[i].Real;
//                    a[2, i] = x[i].Imaginary;
//                    if(ls == 0)
//                    { break; }
//                    x[i] = ax[ls, i];
//                } // 11

//                for(int l = 1; l <= nd; l++) // 13
//                {
//                    for (int i = 1; i <= nn; i++)
//                    {
//                        s[i] = 0.0;
//                    }
//                    for (int i = 1; i <= nn; i++)
//                    {
//                        inv[i] = 0.0;
//                    }
//                    for (int i = 1; i <= nn; i++)
//                    {
//                        for (int j = 1; j <= nn; j++)
//                        {
//                            id[i, j] = 0.0;
//                        }
//                    }
//                }
//            }

//        }


//    }
//}

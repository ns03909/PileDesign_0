//using System;
//using System.Collections.Generic;
//using System.Windows.Documents;
//using PileDesign.Models.InputData;

//namespace PileDesign.FEM
//{

//    internal class Controller : Models.BaseModel
//    {
//        private Controller _controller;

//        public AnaModel AnaModel { get; private set; }

//        private InputModel _inputModel;
//        public InputModel InputModel
//        {
//            get => _inputModel;
//            set => SetProperty(ref _inputModel, value);
//        }

//        // コンストラクタ
//        public Controller()
//        {
//            InputModel = InputModel.Instance;
//            _controller = this;

//        }

//        public void Run1(string filepath)
//        {
//           anaMo
//        }

//        public void Run2()
//        {
//            // NEWTON-RAPHSON METHOD *
//            double alpha = 1.0 * Math.Pow(10, -8);  // 残余力収束判定許容誤差
//            List<object> anaResults = new List<object>();  // 解析結果の空リスト

//            foreach(LoadCase loadCase in InputModel.LoadCasesInput.LoadCasesLevel1) // 荷重ケースごとに実行
//            //for (int i_LC = 0; i_LC < InputModel.LoadCasesInput.LoadCasesLevel1.Count; i_LC++)  // 荷重ケースごとに実行
//            {
//                foreach(LoadCombination loadCombination in InputModel.LoadCasesInput.LoadCombinations) // 荷重組み合わせごとに実行
//                //for (int i_LCOM = 0; i_LCOM < InputModel.LoadCasesInput.LoadCombinations.Count; i_LCOM++)  // 荷重組み合わせごとに実行
//                {
//                    // 初期状態(常時荷重状態)
//                    foreach (var node in AnaModel.Nodes)
//                    {
//                        node.SetInitialDisp();  // set zero
//                        node.SetInitialReact();  // set zero
//                    }
//                    foreach (var beam in AnaModel.Beams)
//                    {
//                        beam.SetInitialForce();  // 慣性力、杭軸力のセット set zero
//                        beam.SetInitialDisp();  // set zero
//                    }
//                    AnaModel.SetInitialF();  // set zero
//                    AnaModel.SetInitialD();  // set zero
//                    PileDesign.SetPy(loadCase);  // 塑性地盤反力度の作成 PPs[i_WS].py 前方杭、後方杭
//                    PileDesign.SetInitialF(loadCase);  // 常時荷重時の割線剛性判断用荷重状態へのセット F 慣性力0、常時杭軸力
//                    PileDesign.SetInitialDisp(loadCase);  // 常時荷重時の変位 Disp = 0
//                    AnaModel.SetInitialR();  // 残余力の初期値      R = 0
//                    Console.WriteLine("");

//                    // nstep = 3
//                    int nstep;
//                    if (!loadCase.IsSoilNonLinear &&
//                        !loadCase.IsPileNonLinear)  // 線形解析
//                    {
//                        nstep = 1;
//                    }
//                    else  // 非線形解析
//                    {
//                        nstep = 10;  // 20
//                    }

//                    PileDesign.SetDF(loadCase, loadCombination, nstep);  // piledesign.慣性力荷重増分dFのセット
//                    AnaModel.MapOnLoads(true);

//                    //PrintStatus1(loadCase, InputModel.LoadCasesInput.LoadCasesLevel1.Count, loadCombination,
//                    //    InputModel.LoadCasesInput.LoadCombinations.Count);
//                    for (int step = 0; step < nstep; step++)
//                    {
//                        // 残差ノルム初期値
//                        AnaModel.SetInitialNormsqR_onNormsqFint();  // ||R|| / ||fint|| = 999.
//                                                                   // 接線剛性判断のための荷重状態の更新
//                        PileDesign.SetFPlusDF(loadCase);  //  F = F + dF

//                        // # Node.dF のAnaModel.dF, AnaModel.F　(荷重ベクトル)へのマッピング
//                        // # 残余力更新  AnaModel.R = AnaModel.R - AnaModel.dF
//                        int n_iter = 1;  // 収束計算回数初期値
//                                         // 剛性マトリクス生成用剛性の作成
//                        PileDesign.PrepareKmat(loadCase);

//                        // 要素剛性、節点剛性の剛性マトリクスKmatへのマッピング
//                        AnaModel.MapOnKmat(true);

//                        // Node.dF のAnaModel.dF, AnaModel.F　(荷重ベクトル)へのマッピング
//                        AnaModel.MapOnLoads(false);  // node.load, による F 更新
//                                                   // AnaModel.F  = node.map_on_global_load(AnaModel.F, IsTan=False) replace
//                        AnaModel.UpdateR();  // R = R - dF

//                        while (AnaModel.NormsROnNormsFint >= alpha)
//                        {
//                            // 残余力収束判定条件　(||R|| / ||Fint|| > 許容値)
//                            //PrintIterationStatus(i_LC, PileDesign.LCs.Count, i_LCOM, PileDesign.LCOMs.Count, step, nstep, n_iter);
//                            // 外力ベクトル地盤外力の計算F[i+1]
//                            // self.ana_model.map_on_loads() # Node.dLoad、Node.Loadのから荷重F[i]のマッピング
//                            Solver.Solve(AnaModel);  // Services/solver.py # 増分変位  [dd] = inv([Kaa_tan])[-R]

//                            PileDesign.PrepareKmat(loadCase);

//                            // 要素剛性、節点剛性の剛性マトリクスKmatへのマッピングKtmat[i+1]
//                            AnaModel.MapOnKmat(true);  // d[i+1]でのKtmat
//                            AnaModel.MapOnKmat(false);  // d[i+1]でのKsmat

//                            // 内力ベクトルの計算
//                            AnaModel.SetFint();  // np.dot(self.KAA_sec, self.D)

//                            // 残余力、残差ノルムの更新
//                            AnaModel.SetR();  // R = Fint - F

//                            // 残差ノルムの出力
//                            PrintNorm(AnaModel.NormsROnNormsFint, alpha);
//                            n_iter += 1;
//                        }
///
//                    }
//                    //anaResults.Add(new Out(AnaModel, PileDesign, i_LC));
//                }
//            }
//            //Viewer.Frame3D(this);
//            // Viewer.view_frame(self.ana_model.Beams)  # View/viewer.py 3D graph
////
//        }

//        public static void PrintStatus1(int i_LC, int LCs, int i_LCOM, int LCOMs)
//        {
//            // 解析ステータスのプリント
//            Console.WriteLine(
//                "Load Case: "
//                + (i_LC + 1).ToString()
//                + " / "
//                + LCs.ToString()
//                + ",  "
//                + "Superstructure-Foundation Force Combination: "
//                + (i_LCOM + 1).ToString()
//                + " / "
//                + LCOMs.ToString()
//            );
//        }

//        public static void PrintIterationStatus(int i_LC, int LCs, int i_LCOM, int LCOMs, int step, int nstep, int n_iter)
//        {
//            // 解析ステータスのプリント
//            Console.WriteLine(
//                "    Load Case: "
//                + (i_LC + 1).ToString()
//                + " / "
//                + LCs.ToString()
//                + ",  "
//                + "Superstructure-Foundation Force Combination: "
//                + (i_LCOM + 1).ToString()
//                + " / "
//                + LCOMs.ToString()
//                + ",  "
//                + "Load step: "
//                + (step + 1).ToString()
//                + " / "
//                + nstep.ToString()
//                + ",  "
//                + "Iteration "
//                + n_iter.ToString()
//            );
//        }

//        public static void PrintNorm(double normsq_R_on_normsq_fint, double alpha)
//        {
//            // 収束状況のプリント
//            if (normsq_R_on_normsq_fint <= alpha)
//            {
//                Console.WriteLine(
//                    $"||R||**2 / ||Fint||**2 = {normsq_R_on_normsq_fint:e3} <= {alpha:e3},      (Converged)"
//                );
//                Console.WriteLine("");
//            }
//            else
//            {
//                Console.WriteLine(
//                    $"||R||**2 / ||Fint||**2 = {normsq_R_on_normsq_fint:e3} >  {alpha:e3}"
//                );
//            }
//        }
//    }
//}
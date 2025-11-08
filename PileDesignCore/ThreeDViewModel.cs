using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Policy;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace PileDesignCore
{
    [Serializable]
    public class ThreeDViewModel : INotifyPropertyChanged
    {
        private ApplicationViewModel _dataContextApp;
        public ApplicationViewModel DataContextApp
        {
            get { return _dataContextApp; }
            set
            {
                if (_dataContextApp != value)
                {
                    _dataContextApp = value;
                    OnPropertyChanged(nameof(DataContextApp));
                }
            }
        }

        private PerspectiveCamera _camera;
        public PerspectiveCamera Camera
        {
            get { return _camera; }
            set
            {
                if (_camera != value)
                {
                    _camera = value;
                    OnPropertyChanged(nameof(Camera));
                }
            }
        }

        private Viewport3D _viewport3D;
        public Viewport3D Viewport3D
        {
            get { return _viewport3D; }
            set
            {
                if (_viewport3D != value)
                {
                    _viewport3D = value;
                    OnPropertyChanged(nameof(Viewport3D));
                }
            }
        }

        private Model3DGroup _modelGroup;
        public Model3DGroup ModelGroup
        {
            get { return _modelGroup; }
            set
            {
                if (_modelGroup != value)
                {
                    _modelGroup = value;
                    OnPropertyChanged(nameof(ModelGroup));
                }
            }
        }

        private Model3DGroup _wireframeModelGroup;
        public Model3DGroup WireframeModelGroup
        {
            get { return _wireframeModelGroup; }
            set
            {
                if (_wireframeModelGroup != value)
                {
                    _wireframeModelGroup = value;
                    OnPropertyChanged(nameof(WireframeModelGroup));
                }
            }
        }
        private Model3DGroup _pointModelGroup;

        public Model3DGroup PointModelGroup
        {
            get { return _pointModelGroup; }
            set
            {
                if (_pointModelGroup != value)
                {
                    _pointModelGroup = value;
                    OnPropertyChanged(nameof(PointModelGroup));
                }
            }
        }

        private bool _showWireframe;
        public bool ShowWireframe
        {
            get { return _showWireframe; }
            set
            {
                if (_showWireframe != value)
                {
                    _showWireframe = value;
                    UpdateModelGroups();
                    OnPropertyChanged(nameof(ShowWireframe));
                }
            }
        }

        private bool _showPoints;
        public bool ShowPoints
        {
            get { return _showPoints; }
            set
            {
                if (_showPoints != value)
                {
                    _showPoints = value;
                    UpdateModelGroups();
                    OnPropertyChanged(nameof(ShowPoints));
                }
            }
        }

        public Model3D Model { get; set; }


        // 初期状態のカメラパラメータを保持するプロパティ
        private Point3D _initialCameraPosition;
        public Point3D InitialCameraPosition
        {
            get { return _initialCameraPosition; }
            set
            {
                if (_initialCameraPosition != value)
                {
                    _initialCameraPosition = value;
                    UpdateModelGroups();
                    OnPropertyChanged(nameof(InitialCameraPosition));
                }
            }
        }

        private Point3D _initialLookDirection;
        public Point3D InitialLookDirection
        {
            get { return _initialLookDirection; }
            set
            {
                if (_initialLookDirection != value)
                {
                    _initialLookDirection = value;
                    UpdateModelGroups();
                    OnPropertyChanged(nameof(InitialLookDirection));
                }
            }
        }

        private Point3D _initialUpDirection;
        public Point3D InitialUpDirection
        {
            get { return _initialUpDirection; }
            set
            {
                if (_initialUpDirection != value)
                {
                    _initialUpDirection = value;
                    UpdateModelGroups();
                    OnPropertyChanged(nameof(InitialUpDirection));
                }
            }
        }

        // コンストラクタ
        public ThreeDViewModel(ApplicationViewModel appViewModel)
        {
            //_dataContextApp = appViewModel;
            DataContextApp = appViewModel;

            //_showWireframe = true;
            //_showPoints = true;

            // Viewport3D の初期化
            Viewport3D = new Viewport3D();
            ModelGroup = new Model3DGroup();
            MeshGeometry3D cubeGeometry = GetCubeGeometry();
            CalculateModels(cubeGeometry);
            //WireframeModelGroup = GenerateWireframeModel(cubeGeometry);
            //PointModelGroup = GeneratePointModel(cubeGeometry);

            // カメラの初期化
            Camera = new PerspectiveCamera(new Point3D(0, 0, 10), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 45);

            // カメラの設定
            Viewport3D.Camera = Camera;

            // ModelVisual3D の作成と ModelGroup の追加
            var modelVisual3D = new ModelVisual3D
            {
                Content = ModelGroup
            };
            Viewport3D.Children.Add(modelVisual3D);

            // ApplicationViewModel をプロパティに設定
            DataContextApp = appViewModel;

            // カメラのデフォルトパラメータを設定
            SetDefaultCameraParameters();

            // モデルグループを更新してビューに反映
            UpdateModelGroups();
        }

        private MeshGeometry3D GetCubeGeometry()
        {
            // 直方体の作成
            var cubeGeometry = new MeshGeometry3D();
            // ...（直方体の頂点や面の設定）

            cubeGeometry.Positions.Add(new Point3D(-1, -1, -1)); // 0
            cubeGeometry.Positions.Add(new Point3D(1, -1, -1)); //1 
            cubeGeometry.Positions.Add(new Point3D(1, 1, -1)); // 2
            cubeGeometry.Positions.Add(new Point3D(-1, 1, -1)); // 3
            cubeGeometry.Positions.Add(new Point3D(-1, -1, 1)); // 4
            cubeGeometry.Positions.Add(new Point3D(1, -1, 1)); // 5
            cubeGeometry.Positions.Add(new Point3D(1, 1, 1)); // 6
            cubeGeometry.Positions.Add(new Point3D(-1, 1, 1)); // 7

            cubeGeometry.TriangleIndices.Add(0);
            cubeGeometry.TriangleIndices.Add(1);
            cubeGeometry.TriangleIndices.Add(2);
            cubeGeometry.TriangleIndices.Add(0);
            cubeGeometry.TriangleIndices.Add(2);
            cubeGeometry.TriangleIndices.Add(3);
            cubeGeometry.TriangleIndices.Add(4);
            cubeGeometry.TriangleIndices.Add(5);
            cubeGeometry.TriangleIndices.Add(6);
            cubeGeometry.TriangleIndices.Add(4);
            cubeGeometry.TriangleIndices.Add(6);
            cubeGeometry.TriangleIndices.Add(7);
            cubeGeometry.TriangleIndices.Add(0);
            cubeGeometry.TriangleIndices.Add(3);
            cubeGeometry.TriangleIndices.Add(7);
            cubeGeometry.TriangleIndices.Add(0);
            cubeGeometry.TriangleIndices.Add(7);
            cubeGeometry.TriangleIndices.Add(4);
            cubeGeometry.TriangleIndices.Add(1);
            cubeGeometry.TriangleIndices.Add(6);
            cubeGeometry.TriangleIndices.Add(5);
            cubeGeometry.TriangleIndices.Add(1);
            cubeGeometry.TriangleIndices.Add(2);
            cubeGeometry.TriangleIndices.Add(6);
            cubeGeometry.TriangleIndices.Add(3);
            cubeGeometry.TriangleIndices.Add(2);
            cubeGeometry.TriangleIndices.Add(6);
            cubeGeometry.TriangleIndices.Add(3);
            cubeGeometry.TriangleIndices.Add(7);
            cubeGeometry.TriangleIndices.Add(6);

            return cubeGeometry;
        }

        private void CalculateModels(MeshGeometry3D cubeGeometry)
        {
            // 直方体の作成
            var cubeMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.PaleGreen));
            var cubeModel = new GeometryModel3D(cubeGeometry, cubeMaterial);
            ModelGroup.Children.Add(cubeModel);

            // 光源を追加
            //var directionalLight = new DirectionalLight(Colors.White, new Vector3D(-1, -1, -1));
            var ambientLight = new AmbientLight(Colors.White);
            //ModelGroup.Children.Add(directionalLight);
            ModelGroup.Children.Add(ambientLight);

            // X軸、Y軸、Z軸のラインを追加
            //var xAxisVisual = new ModelVisual3D();
            var xAxis = new LinesVisual3D();
            xAxis.Points.Add(new Point3D(-10, 0, 0));
            xAxis.Points.Add(new Point3D(10, 0, 0));
            xAxis.Color = Colors.Red;
            xAxis.IsRendering = true;
            xAxis.Thickness = 1;
            Viewport3D.Children.Add(xAxis);

            var yAxis = new LinesVisual3D();
            yAxis.Points.Add(new Point3D(0, -10, 0));
            yAxis.Points.Add(new Point3D(0, 10, 0));
            yAxis.Color = Colors.Green;
            yAxis.IsRendering = true;
            yAxis.Thickness = 1;
            Viewport3D.Children.Add(yAxis);

            var zAxis = new LinesVisual3D();
            zAxis.Points.Add(new Point3D(0, 0, -10));
            zAxis.Points.Add(new Point3D(0, 0, 10));
            zAxis.Color = Colors.Blue;
            zAxis.IsRendering = true;
            zAxis.Thickness = 1;
            Viewport3D.Children.Add(zAxis);
        }

        // ワイヤフレームモデル作成メソッド
        private Model3DGroup GenerateWireframeModel(MeshGeometry3D cubeGeometry)
        {
            var wireframeModelGroup = new Model3DGroup();

            foreach (var triangleIndices in AsChunks(cubeGeometry.TriangleIndices, 3))
            {
                var wireframeGeometry = new MeshGeometry3D();

                foreach (var index in triangleIndices)
                {
                    wireframeGeometry.Positions.Add(cubeGeometry.Positions[index]);
                }

                wireframeGeometry.TriangleIndices.Add(0);
                wireframeGeometry.TriangleIndices.Add(1);
                wireframeGeometry.TriangleIndices.Add(1);
                wireframeGeometry.TriangleIndices.Add(2);
                wireframeGeometry.TriangleIndices.Add(2);
                wireframeGeometry.TriangleIndices.Add(0);

                var wireframeMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.Red));
                var wireframeModel = new GeometryModel3D(wireframeGeometry, wireframeMaterial);
                wireframeModelGroup.Children.Add(wireframeModel);
            }

            return wireframeModelGroup;
        }

        // 節点モデル作成メソッド
        private Model3DGroup GeneratePointModel(MeshGeometry3D cubeGeometry)
        {
            var pointModelGroup = new Model3DGroup();

            foreach (var position in cubeGeometry.Positions)
            {
                var sphereGeometry = new MeshGeometry3D();

                sphereGeometry.Positions.Add(position);

                var sphereMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.Green));
                var sphereModel = new GeometryModel3D(sphereGeometry, sphereMaterial);
                pointModelGroup.Children.Add(sphereModel);
            }
            return pointModelGroup;
        }

        // カメラパラメータ設定メソッド
        public void SetCameraParameters(Point3D position, Vector3D lookDirection, Vector3D upDirection)
        {
            Camera.Position = position;
            Camera.LookDirection = lookDirection;
            Camera.UpDirection = upDirection;
        }

        // デフォルトのカメラパラメータを設定するメソッド
        private void SetDefaultCameraParameters()
        {
            // 例: デフォルトのカメラ位置、方向、上向きベクトルを設定する
            Camera.Position = new Point3D(0, 0, 10);
            Camera.LookDirection = new Vector3D(0, 0, -1);
            Camera.UpDirection = new Vector3D(0, 1, 0);
        }


        public static IEnumerable<IEnumerable<T>> AsChunks<T>(IEnumerable<T> source, int chunkSize)
        {
            while (source.Any())
            {
                yield return source.Take(chunkSize);
                source = source.Skip(chunkSize);
            }
        }

        private void UpdateModelGroups()
        {
            var updatedModelGroup = new Model3DGroup();

            if (_showWireframe)
                updatedModelGroup.Children.Add(WireframeModelGroup);

            if (_showPoints)
                updatedModelGroup.Children.Add(PointModelGroup);

            updatedModelGroup.Children.Add(ModelGroup);

            ModelGroup = updatedModelGroup;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
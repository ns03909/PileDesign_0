using System.Collections.Generic;

namespace PileDesignCore
{
    //    internal class Class1
    //    {
    //    }
    //}

    //using System;
    //using System.Collections.Generic;

    //namespace YourNamespace
    //{
    internal class Controller
    {
        private Controller _controller;

        public Controller()
        {
            _controller = this;
        }

        public void Run()
        {
            AnaModel anaModel = GetSampleModel(3);
            Solver.Solve(anaModel);
            //Viewer.ViewFrame(anaModel.Beams);
        }

        public AnaModel GetSampleModel(int index)
        {
            // 門形ラーメン3D（水平荷重）
            Node node1 = new Node("node1", 0.0, 0.0, 0.0); // 支点
            Node node2 = new Node("node2", 0.0, 0.0, 3.0);
            Node node3 = new Node("node3", 3.0, 0.0, 3.0);
            Node node4 = new Node("node4", 3.0, 0.0, 0.0); // 支点
            Node node5 = new Node("node5", 0.0, 3.0, 0.0); // 支点
            Node node6 = new Node("node6", 0.0, 3.0, 3.0);
            Node node7 = new Node("node7", 3.0, 3.0, 3.0);
            Node node8 = new Node("node8", 3.0, 3.0, 0.0); // 支点

            Boundary boundaryPin = new Boundary(BoundaryType.Fix, BoundaryType.Fix, BoundaryType.Fix, BoundaryType.Fix, BoundaryType.Free, BoundaryType.Fix);
            Boundary boundaryFix = new Boundary(BoundaryType.Fix, BoundaryType.Fix, BoundaryType.Fix, BoundaryType.Fix, BoundaryType.Fix, BoundaryType.Fix);
            Boundary boundaryFree = new Boundary(BoundaryType.Free, BoundaryType.Fix, BoundaryType.Free, BoundaryType.Fix, BoundaryType.Free, BoundaryType.Fix);

            node1.SetBoundary(boundaryFix);
            node2.SetBoundary(boundaryFree);
            node3.SetBoundary(boundaryFree);
            node4.SetBoundary(boundaryFix);
            node5.SetBoundary(boundaryFix);
            node6.SetBoundary(boundaryFree);
            node7.SetBoundary(boundaryFree);
            node8.SetBoundary(boundaryFix);

            Material material1 = new Material(1.0, 1.0, 1.0);
            Section section1 = new Section(material1, 10000.0, 1.0, 1.0, 1.0, 1.0, 1.0);

            Beam beam1 = new Beam("beam1", section1, node1, node2);
            Beam beam2 = new Beam("beam2", section1, node2, node3);
            Beam beam3 = new Beam("beam3", section1, node4, node3);
            Beam beam4 = new Beam("beam1", section1, node5, node6);
            Beam beam5 = new Beam("beam2", section1, node6, node7);
            Beam beam6 = new Beam("beam3", section1, node8, node7);
            Beam beam7 = new Beam("beam1", section1, node2, node6);
            Beam beam8 = new Beam("beam2", section1, node3, node7);

            LoadNode loadNode = new LoadNode(1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f);
            node2.SetLoad(loadNode);

            List<Node> listNode = new List<Node> { node1, node2, node3, node4, node5, node6, node7, node8 };
            List<Beam> listBeam = new List<Beam> { beam1, beam2, beam3, beam4, beam5, beam6, beam7, beam8 };

            AnaModel anaModel = new AnaModel(listNode, listBeam);
            return anaModel;
        }
    }
}


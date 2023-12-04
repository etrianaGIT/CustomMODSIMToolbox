using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Csu.Modsim.ModsimIO;
using Csu.Modsim.ModsimModel;
using MODSIMModeling.DiversionRotation;
using MODSIMModeling;
using MODSIMModeling.ReservoirOps;
using MODSIMModeling.Routing;

namespace MODSIMModeling.MainMODSIMRun
{
    class Program
    {
        public static Model myModel = new Model();
        // declaring the plug-ins
        public static ReservoirLayers resTool;
        public static RoutingUtils routeTool;
        //public static EconoModeling econoTool;

        static void Main(string[] CmdArgs)
        {
            string FileName = CmdArgs[0];
            myModel.OnMessage += OnMessage;
            myModel.OnModsimError += OnError;

            XYFileReader.Read(myModel, FileName);

            //Adding 'plug-ins'
            //Demtool = new DemRotation(ref myModel);

            //econoTool = new EconoModeling(ref myModel);
            //econoTool.messageOutRun += OnMessage;

            routeTool = new RoutingUtils(ref myModel);
            routeTool.messageOutRun += OnMessage;

            resTool = new ReservoirLayers(ref myModel,saveXYRun: true);
            resTool.messageOutRun += OnMessage;

            //foreach (Node res in myModel.Nodes_Reservoirs)
            //	res.m.min_volume = res.m.min_volume;// 0;

            Modsim.RunSolver(myModel);

            Console.ReadLine();
        }

        private static void OnMessage(string message)
        {
            Console.WriteLine(message);
        }

        private static void OnError(string message)
        {
            Console.WriteLine(message);
        }
    }
}

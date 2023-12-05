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
using MODSIMModeling.Preprocessing;

namespace MODSIMModeling.MainMODSIMRun
{
    class Program
    {
        public static Model myModel = new Model();
        // declaring the plug-ins
        public static ReservoirLayers resTool;
        public static RoutingUtils routeTool;
        public static ObservedFLowImport obsFlowImport;
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
            //Process reservoir targets
            routeTool.SetRoutingParams("C:\\Users\\etriana\\Research Triangle Institute\\USGS Coop Agreement - Documents\\Modeling\\WAlloc\\MODSIM\\routing\\UCOL_NHM_MK_Params.csv");   

            //resTool = new ReservoirLayers(ref myModel,saveXYRun: true);
            //resTool.messageOutRun += OnMessage;

            //obsFlowImport = new ObservedFLowImport(ref myModel);
            //obsFlowImport.ImportTimeseries("C:\\Users\\etriana\\Research Triangle Institute\\USGS Coop Agreement - Documents\\Modeling\\Data\\gage_search");
            
            XYFileWriter.Write(myModel, myModel.fname);

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

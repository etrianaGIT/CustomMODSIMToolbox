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
        private static DemandProcessing procDemands;
        public static ResOpsReleaseRampRates resOps;
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

            //routeTool = new RoutingUtils(ref myModel);
            //routeTool.messageOutRun += OnMessage;
            ////Process reservoir targets
            //routeTool.SetRoutingParams("C:\\Users\\etriana\\Research Triangle Institute\\USGS Coop Agreement - Documents\\Modeling\\WAlloc\\MODSIM\\routing\\UCOL_NHM_MK_Params.csv");

            resTool = new ReservoirLayers(ref myModel);
            resTool.messageOutRun += OnMessage;
            //Process reservoir targets
            // resTool.SetReservvoirTargets("C:\\Users\\etriana\\Research Triangle Institute\\USGS Coop Agreement - Documents\\Modeling\\starfit_minimal\\starfit\\ISTARF-CONUS.csv");
            //recalculated Fontanelle Reservoir parameters
            resTool.SetReservvoirTargets("C:\\Users\\etriana\\Research Triangle Institute\\USGS Coop Agreement - Documents\\Modeling\\starfit_minimal\\starfit\\RevisedFontenelle\\ISTARF-CONUS (1).csv");

            //obsFlowImport = new ObservedFLowImport(ref myModel);
            //obsFlowImport.ImportTimeseries("C:\\Users\\etriana\\Research Triangle Institute\\USGS Coop Agreement - Documents\\Modeling\\Data\\gage_search");

            //procDemands = new DemandProcessing(ref myModel);
            ////      This file includes the original demands compildas for the GSFLOW model
            ////procDemands.ImportDeamandTimeseries("C:\\Users\\etriana\\Research Triangle Institute\\USGS Coop Agreement - Documents\\Modeling\\Data\\gaged_streamflow\\diversions_combined_by_gage_cmd_irrcu60.csv");
            ////      This demand file was calculated for gages having 80% complete data, using geofabric 2.0
            ////      Cost set to pull water from the operating band, but not from the dead pool
            //procDemands.ImportDeamandTimeseries("C:\\Users\\etriana\\Research Triangle Institute\\USGS Coop Agreement - Documents\\Modeling\\Data\\MassBalance\\diversions_cfs_aggregated_NoahGagescsv.csv",
            //                                    -20000,"cfs");

            //obsFlowImport = new ObservedFLowImport(ref myModel);
            //obsFlowImport.ClearInflows();

            resOps = new ResOpsReleaseRampRates(ref myModel);
            resOps.messageOutRun += OnMessage;
            resOps.LoadRampingCurves("C:\\Users\\etriana\\Research Triangle Institute\\USGS Coop Agreement - Documents\\Modeling\\Data\\RampingRates_ExceedanceProbabilities_ET.csv");

            XYFileWriter.Write(myModel, myModel.fname);

            Modsim.RunSolver(myModel);

            //Console.ReadLine();
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

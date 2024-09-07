using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Csu.Modsim.ModsimIO;
using Csu.Modsim.ModsimModel;
using Csu.Modsim.NetworkUtils;

namespace MODSIMModeling.ReservoirOps
{
    /// <summary>
    /// This class implements the 
    /// </summary>
    public class ResOpsReleaseMinMax
    {
        public delegate void ProcessMessage(string msg);  // delegate

        public Model myModel;
        private DataTable _DtParams;

        public event ProcessMessage messageOutRun;     //event
        private ModelOutputSupport modsimoutputsupport;

        public Dictionary<string, FlowConstraint> flowThresholdsInfo;
        private int currentMon;

        public ResOpsReleaseMinMax(ref Model m_Model)
		{
			m_Model.Init += OnInitialize;
			m_Model.IterBottom += OnIterationBottom;
			m_Model.IterTop += OnIterationTop;
			m_Model.Converged += OnIterationConverge;
			m_Model.End += OnFinished;
			
			myModel = m_Model;
        }

        public void LoadExceedCurves(string ratesCsv)
        {
            //Read parameters into the ramping rate objects
            DataTable _MinMaxRates;
            _MinMaxRates = ReadCsv(ratesCsv);
            flowThresholdsInfo = new Dictionary<string, FlowConstraint>();

            foreach (DataRow row in _MinMaxRates.Rows)
            {
                string objName = row["poi_id"].ToString().Trim('\"');
                string resName = row["reservoir_name"].ToString();
                if (!flowThresholdsInfo.ContainsKey(objName))
                {
                    FlowConstraint rr = new FlowConstraint(objName,resName);
                    flowThresholdsInfo.Add(objName,rr);
                }
                int mon = int.Parse(row["month"].ToString());
                flowThresholdsInfo[objName].flowPerMonthTbl[mon].Rows.Add(new object[] { row["type"].ToString(), 
                                                                        double.Parse(row["exceedance_probability"].ToString()),
                                                                        double.Parse(row["flow_cfs"].ToString()) * 1.98347 }); // flow change converted from cfs to acre-feet per day
                
            }

            foreach (FlowConstraint rr in flowThresholdsInfo.Values)
            {
                //Calcualte the increase and decrease rates for the exceedance provided 
                rr.CalculateRates(0.15,"max", 0.85,"min");

            }

        }

       private  void OnInitialize() 
		{
            // Setup user output variable to display the cost in reservoirs.
            modsimoutputsupport = myModel.OutputSupportClass as ModelOutputSupport;
            modsimoutputsupport.AddUserDefinedOutputVariable(myModel, "MaxThreshold",linkOutputVar: true,nodeOutputVar: false, "Flow");
            modsimoutputsupport.AddUserDefinedOutputVariable(myModel, "MinThreshold", linkOutputVar: true, nodeOutputVar: false, "Flow");
            modsimoutputsupport.AddCurrentUserLinkOutput += AddMyLnkOutput;
        }

        private void AddMyLnkOutput(Link link, DataRow row)
        {
            if (flowThresholdsInfo.ContainsKey(link.name))
            {
                row["MaxThreshold"] = flowThresholdsInfo[link.name].flowsPerMonth[currentMon]["max"];
                row["MinThreshold"] = flowThresholdsInfo[link.name].flowsPerMonth[currentMon]["min"];
            }
        }

        private  void OnIterationTop()
		{
            if (myModel.mInfo.Iteration == 0)
            {
                currentMon = myModel.TimeStepManager.Index2Date(myModel.mInfo.CurrentModelTimeStepIndex, TypeIndexes.ModelIndex).Month;
                //foreach (FlowConstraint rr in flowThresholdsInfo.Values)
                //{
                //    Link l = myModel.FindLink(rr.myID, silent: true);
                //    if (l != null)
                //        rr.previousFlow = l.mlInfo.flow; //uses the last converged flow from the previous time step
                //}
            }

        }

       
        private  void OnIterationBottom()
		{
            //Skips the first time step to start the flow at a reasonable value.
            if (myModel.mInfo.CurrentModelTimeStepIndex >=0)
            {
                foreach (FlowConstraint rr in flowThresholdsInfo.Values)
                {
                    //set bounds on lower flow link 
                    Link ll = myModel.FindLink(rr.myResID + "_MinRel", silent: true);
                    if (ll != null)
                    {
                        //Uses the flow in the gage link for the calculation
                        ll.mlInfo.hi = Math.Max(0,(long)Math.Round(rr.flowsPerMonth[currentMon]["min"] * myModel.ScaleFactor, 0));
                    }
                    //set bounds on up the links 
                    // uses the hi in the lower link (assume that is flowing full because of the cost)
                    Link lh = myModel.FindLink(rr.myResID + "_NormalRel", silent: true);
                    if (lh != null)
                    {
                        lh.mlInfo.hi = Math.Max(0,(long)Math.Round(rr.flowsPerMonth[currentMon]["max"] * myModel.ScaleFactor, 0)
                                                                    - ll.mlInfo.hi);
                    }

                }
            }
        }

        private  void OnIterationConverge()
		{
			
		}

		private  void OnFinished()
		{
		}

        private DataTable ReadCsv(string filePath)
        {
            string connectionString = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" +
                Path.GetDirectoryName(filePath) + ";Extended Properties=\"Text;HDR=YES;FMT=Delimited;IMEX=1\"";
            string fileName = Path.GetFileName(filePath);
            string selectString = "SELECT * FROM [" + fileName + "]";
            using (OleDbConnection connection = new OleDbConnection(connectionString))
            {
                using (OleDbDataAdapter adapter = new OleDbDataAdapter(selectString, connection))
                {
                    DataTable dataTable = new DataTable();
                    //// Fill the schema first
                    //adapter.FillSchema(dataTable, SchemaType.Source);

                    //// Modify the column type if necessary
                    //dataTable.Columns["poi_id"].DataType = typeof(string);

                    adapter.Fill(dataTable);
                    return dataTable;
                }
            }
        }
    }

}

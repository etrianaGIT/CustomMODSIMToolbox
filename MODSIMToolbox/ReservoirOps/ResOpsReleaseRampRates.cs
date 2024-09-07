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
    public class ResOpsReleaseRampRates
    {
        public delegate void ProcessMessage(string msg);  // delegate

        public Model myModel;
        private DataTable _DtParams;

        public event ProcessMessage messageOutRun;     //event
        private ModelOutputSupport modsimoutputsupport;

        public Dictionary<string, FlowConstraint> rampRatesInfo;
        private int currentMon;

        public ResOpsReleaseRampRates(ref Model m_Model)
		{
			m_Model.Init += OnInitialize;
			m_Model.IterBottom += OnIterationBottom;
			m_Model.IterTop += OnIterationTop;
			m_Model.Converged += OnIterationConverge;
			m_Model.End += OnFinished;
			
			myModel = m_Model;
            
        }

        public void LoadRampingCurves(string ratesCsv)
        {
            //Read parameters into the ramping rate objects
            DataTable _rampRates;
            _rampRates = ReadCsv(ratesCsv);
            rampRatesInfo = new Dictionary<string, FlowConstraint>();

            foreach (DataRow row in _rampRates.Rows)
            {
                string objName = row["poi_id"].ToString().Trim('\"');// "0" + row["poi_id"].ToString();
                string resName = row["reservoir_name"].ToString();
                if (!rampRatesInfo.ContainsKey(objName))
                {
                    FlowConstraint rr = new FlowConstraint(objName,resName);
                    rampRatesInfo.Add(objName,rr);
                }
                int mon = int.Parse(row["month"].ToString());
                rampRatesInfo[objName].flowPerMonthTbl[mon].Rows.Add(new object[] { row["Type"].ToString(), 
                                                                        double.Parse(row["exceedance_probability"].ToString()),
                                                                        double.Parse(row["flow_cfs"].ToString()) * 1.98347 }); // flow change converted from cfs to acre-feet per day
                
            }

            foreach (FlowConstraint rr in rampRatesInfo.Values)
            {
                //Calcualte the increase and decrease rates for the exceedance provided 
                rr.CalculateRates(0.05,"increase", 0.05,"decrease");


            }

        }

       private  void OnInitialize()
		{
            // Setup user output variable to display the cost in reservoirs.
            modsimoutputsupport = myModel.OutputSupportClass as ModelOutputSupport;
            modsimoutputsupport.AddUserDefinedOutputVariable(myModel, "RampingRateUp",linkOutputVar: true,nodeOutputVar: false, "Flow");
            modsimoutputsupport.AddUserDefinedOutputVariable(myModel, "RampingRateDown", linkOutputVar: true, nodeOutputVar: false, "Flow");
            modsimoutputsupport.AddCurrentUserLinkOutput += AddMyLnkOutput;
        }

        private void AddMyLnkOutput(Link link, DataRow row)
        {
            if (rampRatesInfo.ContainsKey(link.name))
            {
                row["RampingRateUp"] = (rampRatesInfo[link.name].previousFlow / myModel.ScaleFactor) + rampRatesInfo[link.name].flowsPerMonth[currentMon]["increase"];
                row["RampingRateDown"] = (rampRatesInfo[link.name].previousFlow / myModel.ScaleFactor) - rampRatesInfo[link.name].flowsPerMonth[currentMon]["decrease"];
            }
        }

        private  void OnIterationTop()
		{
            if (myModel.mInfo.Iteration == 0)
            {
                currentMon = myModel.TimeStepManager.Index2Date(myModel.mInfo.CurrentModelTimeStepIndex, TypeIndexes.ModelIndex).Month;
                foreach (FlowConstraint rr in rampRatesInfo.Values)
                {
                    Link l = myModel.FindLink(rr.myID, silent: true);
                    if (l != null)
                        rr.previousFlow = l.mlInfo.flow; //uses the last converged flow from the previous time step
                }
            }

        }

       
        private  void OnIterationBottom()
		{
            //Skips the first time step to start the flow at a reasonable value.
            if (myModel.mInfo.CurrentModelTimeStepIndex > 2)
            {
                foreach (FlowConstraint rr in rampRatesInfo.Values)
                {
                    //set bounds on lower flow link 
                    Link ll = myModel.FindLink(rr.myResID + "_RampLimitDw", silent: true);
                    if (ll != null)
                    {
                        //Uses the flow in the gage link for the calculation
                        ll.mlInfo.hi = Math.Max(0,rr.previousFlow - (long)Math.Round(rr.flowsPerMonth[currentMon]["decrease"] * myModel.ScaleFactor, 0));
                    }
                    //set bounds on up the links 
                    // uses the hi in the lower link (assume that is flowing full because of the cost)
                    Link lh = myModel.FindLink(rr.myResID + "_RampLimitUp", silent: true);
                    if (lh != null)
                    {
                        lh.mlInfo.hi = Math.Max(0,rr.previousFlow + (long)Math.Round(rr.flowsPerMonth[currentMon]["increase"] * myModel.ScaleFactor, 0)
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

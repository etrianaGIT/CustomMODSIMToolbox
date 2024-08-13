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
    public class ReservoirLayers
    {
        public delegate void ProcessMessage(string msg);  // delegate

        public Model myModel;
        private DataTable _DtParams;

        public event ProcessMessage messageOutRun;     //event
        private ModelOutputSupport modsimoutputsupport;

        public ReservoirLayers(ref Model m_Model)
		{
			m_Model.Init += OnInitialize;
			m_Model.IterBottom += OnIterationBottom;
			m_Model.IterTop += OnIterationTop;
			m_Model.Converged += OnIterationConverge;
			m_Model.End += OnFinished;
			
			myModel = m_Model;
            
            ////Save changes to the XY (run)
            //if(saveXYRun)
            //    XYFileWriter.Write(myModel, myModel.fname.Replace(".xy","Run.xy"));
        }

        public void SetReservvoirTargets(string paramsCsv, bool onlyResWithMeasured = false)
        {
            //Read parameters in a datatable
            _DtParams = ReadCsv(paramsCsv);


            foreach (Node res in myModel.Nodes_Reservoirs)
            {
                //Other reservoir characteristic
                // [7/16/24] The capacity was fixed in the source shapefile, so no need to readjust here
                //res.m.max_volume = GetParameterValue(res, "GRanD_CAP_MCM");  //This value is used in the target calculations (in MCM)
                ModsimUnits muOrig = res.m.reservoir_units;
                //res.m.reservoir_units = ModsimUnits.FromLabel("MCM");

                //res.m.min_volume = res.m.min_volume;
                DataTable dt = res.m.adaTargetsM.dataTable;
                if (onlyResWithMeasured && dt.Rows.Count == 0)
                    continue;
                res.m.adaTargetsM.Interpolate = true;
                res.m.adaTargetsM.units = ModsimUnits.FromLabel("MCM");
                dt.Rows.Clear();
                foreach (DataRow dr in myModel.TimeStepManager.timeStepsList.Rows)
                {
                    DataRow newdr = dt.NewRow();
                    DateTime dtime = DateTime.Parse(dr["IniDate"].ToString());
                    Calendar calendar = CultureInfo.InvariantCulture.Calendar;
                    int weekNumber = calendar.GetWeekOfYear(dtime, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

                    newdr[0] = dr["EndDate"].ToString();
                    newdr[1] = GetMaxNormal(res, weekNumber);// * myModel.ScaleFactor; // * 1233.48 / 1000000
                    dt.Rows.Add(newdr); 

                    
                }
                //Set the lower layer placeholder
                res.m.resBalance = new ResBalance();
                res.m.resBalance.PercentBasedOnMaxCapacity = false;
                res.m.resBalance.incrPriorities = new long[] { -3000 + res.number, -1000 + res.number };
                res.m.resBalance.targetPercentages = new double[] { 20, 100 };
                if(res.m.resOutLink != null)
                    res.m.resOutLink.m.cost = 1;
                else
                    Console.WriteLine("No release link for reservoir " + res.name + " defined.");

                //Set starting volume
                DateTime dtime0 = myModel.TimeStepManager.Index2Date(0, TypeIndexes.ModelIndex);
                Calendar calendar0 = CultureInfo.InvariantCulture.Calendar;
                int weekNumber0 = calendar0.GetWeekOfYear(dtime0, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                double minNormal = GetMinNormal(res, weekNumber0);
                double maxNormal = GetMaxNormal(res, weekNumber0);
                //reservoir units are MCM - need to convert from AF to MCM
                if (res.m.starting_volume == 0)
                {
                    res.m.starting_volume = (long)Math.Round((minNormal + maxNormal) / 2.0, 0);//* 1233.48 / 1000000
                    Console.WriteLine($"\tSetting starting volume for res {res.name} to {res.m.starting_volume}.");
                }
                else
                {
                    // [7/16/24] The initial storage is imported in WaterALLOC from a csv file.  No need to overwrite here.
                    ////convert units for the initial storage
                    //res.m.starting_volume = Convert.ToInt64(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(res.m.starting_volume, muOrig), res.m.reservoir_units));
                }
            }
        }

        /// <summary>
        /// Gets the value of a parameter for a reservoir.
        /// This value is in MODSIM units because it is multiplied by the ScaleFactor.
        /// </summary>
        /// <param name="res">The reservoir node.</param>
        /// <param name="colName">The name of the column containing the parameter value.</param>
        /// <returns>The parameter value for the reservoir in MODSIM units.</returns>
        private long GetParameterValue(Node res, string colName)
        {
            DataRow[] dr = _DtParams.Select($"[GRanD_ID] = '{res.name}'");
            long value = -999;

            if (dr.Length > 0)
            {
                value = long.Parse(Math.Round(double.Parse(dr[0][colName].ToString()) * myModel.ScaleFactor, 0).ToString());
            }
            return value;
        }

        private  void OnInitialize()
		{
            // Setup user output variable to display the cost in reservoirs.
            modsimoutputsupport = myModel.OutputSupportClass as ModelOutputSupport;
            modsimoutputsupport.AddUserDefinedOutputVariable(myModel, "Layer_Target", false, true, "Volume");
            modsimoutputsupport.AddCurrentUserReservoir_STOROutput += AddMyResOutput;
        }

        private void AddMyResOutput(Node node, DataRow row)
        {
            if (node.mnInfo.balanceLinks != null)
            {
                Link tgtLink = node.mnInfo.balanceLinks.link;
                if (node.m.resBalance.incrPriorities.Length > 1)
                {
                    //Assumes that a single layer is used in the reservoir
                    tgtLink = node.mnInfo.balanceLinks.link;
                }
                row["Layer_Target"] = tgtLink.mlInfo.hi/myModel.ScaleFactor;
            }
        }

        private  void OnIterationTop()
		{
            DateTime dtime = myModel.TimeStepManager.Index2Date(myModel.mInfo.CurrentModelTimeStepIndex,TypeIndexes.ModelIndex);
            Calendar calendar = CultureInfo.InvariantCulture.Calendar;
            int weekNumber = calendar.GetWeekOfYear(dtime, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

            foreach (Node res in myModel.Nodes_Reservoirs)
            {
                double minNormal = GetMinNormal(res, weekNumber);
                double maxNormal = GetMaxNormal(res, weekNumber);

                DataTable dt = res.m.adaTargetsM.dataTable;
                
                if(dt.Rows.Count>0)
                    //Set the lower layer percent (as a function of the max normal = target)
                    res.m.resBalance.targetPercentages[0] =  (double) (minNormal/maxNormal*100);

            }
        }

        /// <summary>
        /// Gets the maximum normal value for a reservoir based on the week number.
        /// This value is in MODSIM units because it is based on the internal reservoir capacity variable.
        /// </summary>
        /// <param name="res">The reservoir node.</param>
        /// <param name="weekNumber">The week number.</param>
        /// <returns>The maximum normal value for the reservoir.</returns>
        private double GetMaxNormal(Node res, int weekNumber)
        {
            DataRow[] dr = _DtParams.Select($"[GRanD_NAME] = '{res.name}'");
            double maxNormal = 1.0;// = res.m.max_volume;

            if (dr.Length > 0)
            {
                res.description = "ID:"+dr[0]["GRanD_ID"].ToString();
                double upper_max = double.MaxValue;
                if (!dr[0]["NORhi_max"].ToString().Contains("Infinity"))
                    upper_max = double.Parse(dr[0]["NORhi_max"].ToString());
                double upper_min = !dr[0]["NORhi_min"].ToString().Contains("Infinity") ? dr[0].Field<double>("NORhi_min") : double.MinValue;
                double upper_mu = double.Parse(dr[0]["NORhi_mu"].ToString());
                double upper_alpha = double.Parse(dr[0]["NORhi_alpha"].ToString());
                double omega = 1.0 / 52.0;
                double upper_beta = double.Parse(dr[0]["NORhi_beta"].ToString());
                maxNormal = Math.Min(upper_max,
                                        Math.Max(upper_min,
                                               upper_mu +
                upper_alpha * Math.Sin(2.0 * Math.PI * omega * weekNumber) +
                upper_beta * Math.Cos(2.0 * Math.PI * omega * weekNumber)));
            }
            // Assuming that the max normal is in MCM
            long maxMCM = Convert.ToInt64(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(res.m.max_volume, res.m.reservoir_units), ModsimUnits.FromLabel("MCM")));
            return maxNormal / 100 * maxMCM;
        }

        private double GetMinNormal(Node res, int weekNumber)
        {
            DataRow[] dr = _DtParams.Select($"[GRanD_NAME] = '{res.name}'");
            double minNormal = 1.0;// = res.m.min_volume;
            if (dr.Length > 0)
            {
                double lower_max = double.MaxValue;
                if (dr[0]["NORhi_max"].ToString() != "Infinity")
                    lower_max = double.Parse(dr[0]["NORlo_max"].ToString());
                
                double lower_min = dr[0]["NORlo_min"].ToString() != "-Infinity" ? dr[0].Field<double>("NORlo_min") : double.MinValue;
                double lower_mu = double.Parse(dr[0]["NORlo_mu"].ToString());
                double lower_alpha = double.Parse(dr[0]["NORlo_alpha"].ToString()); 
                double omega = 1.0 / 52.0;
                double lower_beta = double.Parse(dr[0]["NORlo_beta"].ToString()); 
                minNormal = Math.Min(lower_max,
                                        Math.Max(lower_min,
                                               lower_mu +
                lower_alpha * Math.Sin(2.0 * Math.PI * omega * weekNumber) +
                lower_beta * Math.Cos(2.0 * Math.PI * omega * weekNumber)));
            }
            long maxMCM = Convert.ToInt64(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(res.m.max_volume, res.m.reservoir_units), ModsimUnits.FromLabel("MCM")));
            return minNormal /100 * maxMCM;
        }

        private  void OnIterationBottom()
		{
            foreach (Node res in myModel.Nodes_Reservoirs)
            {
                //min/max release
                long resInflow = GetResInflow(res);
                if (res.m.resBypassL != null)
                    resInflow += res.m.resBypassL.mlInfo.flow;
                if(res.m.resOutLink!=null)
                    //Using the link downstream to capture both bypass and release
                    res.m.resOutLink.to.OutflowLinks.link.mlInfo.hi = (long) Math.Round((1D + GetMaxReleaseParameter(res)) * resInflow,0);
            }
        }

        private double GetMaxReleaseParameter(Node res)
        {
            DataRow[] dr = _DtParams.Select($"[GRanD_ID] = '{res.name}'");
            double maxRelease = myModel.defaultMaxCap;
            if (dr.Length > 0)
            {
                maxRelease = !dr[0]["Release_max"].ToString().Contains("Infinity") ? dr[0].Field<double>("Release_max"):maxRelease;                
            }
            return maxRelease;
        }

        private long GetResInflow(Node res)
        {
            long sumFlow = 0;
            LinkList ll = res.InflowLinks;
            while (ll!=null)
            {
                sumFlow += ll.link.mlInfo.flow;
                ll = ll.next;
            }
            return sumFlow; 
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
                Path.GetDirectoryName(filePath) + ";Extended Properties=\"Text;HDR=YES;FMT=Delimited\"";
            string fileName = Path.GetFileName(filePath);
            string selectString = "SELECT * FROM [" + fileName + "]";
            using (OleDbConnection connection = new OleDbConnection(connectionString))
            {
                using (OleDbDataAdapter adapter = new OleDbDataAdapter(selectString, connection))
                {
                    DataTable dataTable = new DataTable();
                    adapter.Fill(dataTable);
                    return dataTable;
                }
            }
        }
    }

}

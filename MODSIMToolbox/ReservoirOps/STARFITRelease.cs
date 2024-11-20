using System;
using System.Data;
using System.Data.OleDb;
using System.Data.SqlTypes;
using System.Globalization;
using System.IO;
using System.Runtime.Remoting.Messaging;
using Csu.Modsim.ModsimIO;
using Csu.Modsim.ModsimModel;
using Csu.Modsim.NetworkUtils;
using MODSIMModeling.ReservoirOps.StarFit;

namespace MODSIMModeling.ReservoirOps
{
    public class STARFITRelease
    {
        public delegate void ProcessMessage(string msg);  // Delegate

        public Model myModel;
        //private DataTable _DtParams;

        public event ProcessMessage messageOutRun;     // Event
        private ModelOutputSupport modsimoutputsupport;
        private StarFitUtils _MyStarFitUtils;
        private double flowMODSIMToCMS;
        private double storageMODSIMToMCM;
        private double storageMODSIMToCM;

        public STARFITRelease(ref Model m_Model)
        {
            m_Model.Init += OnInitialize;
            m_Model.IterBottom += OnIterationBottom;
            m_Model.IterTop += OnIterationTop;
            m_Model.Converged += OnIterationConverge;
            m_Model.End += OnFinished;

            myModel = m_Model;

            _MyStarFitUtils = new StarFitUtils();
            _MyStarFitUtils.messageOut += OnMessage;

            //Get conversion factors
            flowMODSIMToCMS = Convert.ToDouble(myModel.FlowUnits.ConvertTo(myModel.FlowUnits.ConvertFrom(1/myModel.ScaleFactor, myModel.FlowUnits), ModsimUnits.FromLabel("CMS")));
            storageMODSIMToMCM = Convert.ToDouble(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(1 / myModel.ScaleFactor, myModel.StorageUnits), ModsimUnits.FromLabel("MCM")));
            storageMODSIMToCM = Convert.ToDouble(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(1 / myModel.ScaleFactor, myModel.StorageUnits), ModsimUnits.FromLabel("CM")));
        }

        private void OnInitialize()
        {
            foreach (Node res in myModel.Nodes_Reservoirs)
            {
                double accumInflow = 0;
                res.Tag = accumInflow;
            }

        }

        private void OnFinished()
        {
        }

        private void OnIterationConverge()
        {
            // add the current inflow to the reservoir nodes Tag object.
            foreach (Node res in myModel.Nodes_Reservoirs)
            {
                double inflow = _MyStarFitUtils.GetResInflow(res); // Current inflow in MODSIM units
                res.Tag = (double) res.Tag + inflow;  //accumulated inflow in MODSIM units and including the scaling factor.
            }
        }

        public void LoadSTARFITParameters(string paramsCsv)
        {
            // Read parameters into a DataTable
            //_DtParams = ReadCsv(paramsCsv);
            _MyStarFitUtils.LoadParameters(paramsCsv);

        }

        private void OnMessage(string msg)
        {
            messageOutRun(msg);
        }

        
        /* private void AddMyResOutput(Node node, DataRow row) // can commment this out, unless if we want to add any custom output to the MODSIM database 
         {
             if (node.mnInfo.balanceLinks != null)
             {
                 Link tgtLink = node.mnInfo.balanceLinks.link;
                 row["Layer_Target"] = tgtLink.mlInfo.hi / myModel.ScaleFactor;
                 if (node.mnInfo.balanceLinks.next != null)
                 {
                     row["MidLayer_Target"] = (tgtLink.mlInfo.hi + node.mnInfo.balanceLinks.next.link.mlInfo.hi) / myModel.ScaleFactor;
                 }
             }
         }*/

        private void OnIterationTop()
        {
            DateTime dtime = myModel.TimeStepManager.Index2Date(myModel.mInfo.CurrentModelTimeStepIndex, TypeIndexes.ModelIndex);
            Calendar calendar = CultureInfo.InvariantCulture.Calendar;
            int weekNumber = calendar.GetWeekOfYear(dtime, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

            //foreach (Node res in myModel.Nodes_Reservoirs)
            //{
            //    long maxMCM = (long)Math.Round(res.m.max_volume * storageMODSIMToMCM,0);// Convert.ToInt64(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(res.m.max_volume, res.m.reservoir_units), ModsimUnits.FromLabel("MCM")));

            //    double minNormal = _MyStarFitUtils.GetMinNormal(res, weekNumber,maxMCM);
            //    double maxNormal = _MyStarFitUtils.GetMaxNormal(res, weekNumber, maxMCM);

            //    //DataTable dt = res.m.adaTargetsM.dataTable;
            //  /*  if (dt.Rows.Count > 0) // can commment out
            //    {
            //        res.m.resBalance.targetPercentages[0] = (double)(minNormal / maxNormal * 100);
            //        res.m.resBalance.targetPercentages[1] = (double)(((minNormal + maxNormal) / 2) / maxNormal * 100);
            //    }*/
            //}
        }

        private void OnIterationBottom()
        {
            DateTime dtime = myModel.TimeStepManager.Index2Date(myModel.mInfo.CurrentModelTimeStepIndex, TypeIndexes.ModelIndex);
            Calendar calendar = CultureInfo.InvariantCulture.Calendar;
            int weekNumber = calendar.GetWeekOfYear(dtime, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

            foreach (Node res in myModel.Nodes_Reservoirs)
            {
                Link sfLink = myModel.FindLink(res.name + "_StarfitRel",myModel.mInfo.CurrentModelTimeStepIndex>0);
                if (sfLink != null)
                {
                    double release = ComputeSTARFITRelease(res, weekNumber);

                    if (release >= 0)
                    {
                        sfLink.mlInfo.hi = (long)Math.Round(release);
                    }
                    else
                        sfLink.mlInfo.hi = 0;
                }
            }
        }

        /// <summary>
        /// Compute the release from a reservoir for the current week.
        /// Relese in MODSIM units.
        /// </summary>
        /// <param name="res">Reservoir node to calculate the release</param>
        /// <param name="weekNumber">week number for the release</param>
        /// <returns></returns>
        private double ComputeSTARFITRelease(Node res, int weekNumber)
        {
            //DataRow[] dr = _DtParams.Select($"[GRanD_NAME] = '{res.name}'");

            //if (dr.Length == 0)
            //{
            //    Console.WriteLine($"No parameter data found for reservoir: {res.name}");
            //    return 0;
            //}

            // Retrieve parameters from the DataRow
            //double upper_min = _MyStarFitUtils.GetVarValue(res, "NORhi_min"); //double.Parse(dr[0]["NORhi_min"].ToString());
            //double upper_max = _MyStarFitUtils.GetVarValue(res, "NORhi_max");
            //double upper_mu = _MyStarFitUtils.GetVarValue(res, "NORhi_mu");
            //double upper_alpha = _MyStarFitUtils.GetVarValue(res, "NORhi_alpha");
            //double upper_beta = _MyStarFitUtils.GetVarValue(res, "NORhi_beta");

            //double lower_min = _MyStarFitUtils.GetVarValue(res, "NORlo_min");
            //double lower_max = _MyStarFitUtils.GetVarValue(res, "NORlo_max");
            //double lower_mu = _MyStarFitUtils.GetVarValue(res, "NORlo_mu");
            //double lower_alpha = _MyStarFitUtils.GetVarValue(res, "NORlo_alpha");
            //double lower_beta = _MyStarFitUtils.GetVarValue(res, "NORlo_beta");

            double release_alpha1 = _MyStarFitUtils.GetVarValue(res, "Release_alpha1");
            double release_alpha2 = _MyStarFitUtils.GetVarValue(res, "Release_alpha2");
            double release_beta1 = _MyStarFitUtils.GetVarValue(res, "Release_beta1");
            double release_beta2 = _MyStarFitUtils.GetVarValue(res, "Release_beta2");
            double release_c = _MyStarFitUtils.GetVarValue(res, "Release_c");
            double release_max = _MyStarFitUtils.GetVarValue(res, "Release_max");
            double release_min = _MyStarFitUtils.GetVarValue(res, "Release_min");
            double release_p1 = _MyStarFitUtils.GetVarValue(res, "Release_p1");
            double release_p2 = _MyStarFitUtils.GetVarValue(res, "Release_p2");

            double storage = (double) res.mnInfo.start;
            // Convert to m³
            storage = storage * storageMODSIMToCM;// Convert.ToDouble(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(storage, res.m.reservoir_units), ModsimUnits.FromLabel("CM"))); 
            double capacity = res.m.max_volume; 
            // Convert to m³
            capacity = capacity * storageMODSIMToCM;//Convert.ToDouble(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(capacity, res.m.reservoir_units), ModsimUnits.FromLabel("CM")));

            double inflow_mean = _MyStarFitUtils.GetVarValue(res, "Obs_MEANFLOW_CUMECS"); // average inflow from csv 
            double inflow = _MyStarFitUtils.GetResInflow(res); // Current inflow in MODSIM units
            // Extrapolated reservoirs don't have inflow in the csv.
            // Python code noted that Turner used inflow from ResOpsUS in these cases.
            if (inflow_mean<0)
            {
                //Calculate a rolling inflow average
                // res.Tag has the sum of simulated inflows. the Tag value is added at convergence of the current time step, so the value here
                // doesn't have the current inflow. Need to use the inflow in MODSIM units.
                inflow_mean = ((double)res.Tag + inflow) / (myModel.mInfo.CurrentModelTimeStepIndex + 1);
                // convert mean inflow to m³/s
                //  conversion factor has the MODSIM scalefactor included
                inflow_mean = inflow_mean * flowMODSIMToCMS;//Convert.ToDouble(myModel.FlowUnits.ConvertTo(myModel.FlowUnits.ConvertFrom(inflow_mean, myModel.FlowUnits), ModsimUnits.FromLabel("CMS")));
            }
            // convert inflow to m³/s
            //  conversion factor has the MODSIM scalefactor included
            inflow = inflow * flowMODSIMToCMS;// Convert.ToDouble(myModel.FlowUnits.ConvertTo(myModel.FlowUnits.ConvertFrom(inflow, myModel.FlowUnits), ModsimUnits.FromLabel("CMS")));


            //if (_MyStarFitUtils.IsDataIssues(res.name))
            //    return 0;

            double omega = 1.0 / 52.0;

            // Calculate maxNormal and minNormal using sine and cosine functions
            long maxMCM = (long)Math.Round(res.m.max_volume*storageMODSIMToMCM,0);// Convert.ToInt64(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(res.m.max_volume, res.m.reservoir_units), ModsimUnits.FromLabel("MCM")));
            double maxNormal = _MyStarFitUtils.GetMaxNormal(res, weekNumber,maxMCM);
            double maxNormalPerc = maxNormal / maxMCM * 100;
            double minNormal = _MyStarFitUtils.GetMinNormal(res, weekNumber, maxMCM);
            double minNormalPerc = minNormal / maxMCM * 100;

            // Calculate forecasted and mean weekly volume
            double forecasted_weekly_volume = 7.0 * inflow * 24.0 * 60.0 * 60.0;
            double mean_weekly_volume = 7.0 * inflow_mean * 24.0 * 60.0 * 60.0;
            double standardized_inflow = (forecasted_weekly_volume / mean_weekly_volume) - 1.0;

            double standardized_weekly_release = (
                release_alpha1 * Math.Sin(2.0 * Math.PI * omega * weekNumber) +
                release_alpha2 * Math.Sin(4.0 * Math.PI * omega * weekNumber) +
                release_beta1 * Math.Cos(2.0 * Math.PI * omega * weekNumber) +
                release_beta2 * Math.Cos(4.0 * Math.PI * omega * weekNumber)
            );

            double release_min_volume = mean_weekly_volume * (1 + release_min) / 7.0;
            double release_max_volume = mean_weekly_volume * (1 + release_max) / 7.0;

            // Calculate availability status
            double availability_status = (100.0 * storage / capacity - minNormalPerc) / (maxNormalPerc - minNormalPerc);

            // Calculate release based on availability status
            double release;
            if (availability_status > 1)
            {
                release = (storage - (capacity * maxNormalPerc / 100.0) + forecasted_weekly_volume) / 7.0;
            }
            else if (availability_status < 0)
            {
                release = (storage - (capacity * minNormalPerc / 100.0) + forecasted_weekly_volume) / 7.0;
            }
            else
            {
                release = (mean_weekly_volume * (1 + (
                    standardized_weekly_release + release_c +
                    release_p1 * availability_status +
                    release_p2 * standardized_inflow
                ))) / 7.0;
            }

            // Enforce boundaries on release
            release = Math.Max(release_min_volume, Math.Min(release, release_max_volume));
            //release in m3/d -> convert to MODSIM units
            //  conversion factor includes the MODSIM scale factor
            release = release / 24 / 60 / 60 / flowMODSIMToCMS;// Convert.ToDouble(myModel.FlowUnits.ConvertTo(myModel.FlowUnits.ConvertFrom(release / 24 / 60 / 60, ModsimUnits.FromLabel("CMS")), myModel.FlowUnits));

            return release;
        }

        //private double GetMaxNormal(Node res, int weekNumber)
        //{
        //    DataRow[] dr = _DtParams.Select($"[GRanD_NAME] = '{res.name}'");
        //    double maxNormal = 1.0;

        //    if (dr.Length > 0)
        //    {
        //        res.description = "ID:" + dr[0]["GRanD_ID"].ToString();
        //        double upper_max = double.MaxValue;
        //        if (!dr[0]["NORhi_max"].ToString().Contains("Infinity"))
        //            upper_max = double.Parse(dr[0]["NORhi_max"].ToString());
        //        double upper_min = !dr[0]["NORhi_min"].ToString().Contains("#NAME?") & !dr[0]["NORhi_min"].ToString().Contains("Infinity") ? double.Parse(dr[0]["NORhi_min"].ToString()): double.MinValue;
        //        double upper_mu = double.Parse(dr[0]["NORhi_mu"].ToString());
        //        double upper_alpha = double.Parse(dr[0]["NORhi_alpha"].ToString());
        //        double omega = 1.0 / 52.0;
        //        double upper_beta = double.Parse(dr[0]["NORhi_beta"].ToString());
        //        maxNormal = Math.Min(upper_max,
        //            Math.Max(upper_min,
        //                upper_mu + upper_alpha * Math.Sin(2.0 * Math.PI * omega * weekNumber) +
        //                upper_beta * Math.Cos(2.0 * Math.PI * omega * weekNumber)));
        //    }

        //    long maxMCM = Convert.ToInt64(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(res.m.max_volume, res.m.reservoir_units), ModsimUnits.FromLabel("MCM")));
        //    return maxNormal / 100 * maxMCM;
        //}

        //private double GetMinNormal(Node res, int weekNumber)
        //{
        //    DataRow[] dr = _DtParams.Select($"[GRanD_NAME] = '{res.name}'");
        //    double minNormal = 1.0;

        //    if (dr.Length > 0)
        //    {
        //        double lower_max = double.MaxValue;
        //        if (dr[0]["NORlo_max"].ToString() != "Infinity")
        //            lower_max = double.Parse(dr[0]["NORlo_max"].ToString());

        //        double lower_min = dr[0]["NORlo_min"].ToString() != "" & dr[0]["NORlo_min"].ToString() != "-Infinity" ? double.Parse(dr[0]["NORlo_min"].ToString()) : double.MinValue;
        //        double lower_mu = double.Parse(dr[0]["NORlo_mu"].ToString());
        //        double lower_alpha = double.Parse(dr[0]["NORlo_alpha"].ToString());
        //        double omega = 1.0 / 52.0;
        //        double lower_beta = double.Parse(dr[0]["NORlo_beta"].ToString());
        //        minNormal = Math.Min(lower_max,
        //            Math.Max(lower_min,
        //                lower_mu + lower_alpha * Math.Sin(2.0 * Math.PI * omega * weekNumber) +
        //                lower_beta * Math.Cos(2.0 * Math.PI * omega * weekNumber)));
        //    }

        //    long maxMCM = Convert.ToInt64(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(res.m.max_volume, res.m.reservoir_units), ModsimUnits.FromLabel("MCM")));
        //    return minNormal / 100 * maxMCM;
        //}

        //private double GetResInflow(Node res)
        //{
        //    double sumFlow = 0.0;
        //    LinkList ll = res.InflowLinks;

        //    while (ll != null)
        //    {
        //        sumFlow += ll.link.mlInfo.flow;  // Summing the flow from each inflow link
        //        ll = ll.next;
        //    }

        //    return sumFlow;
        //}

        //private DataTable ReadCsv(string filePath)
        //{
        //    string connectionString = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" +
        //        Path.GetDirectoryName(filePath) + ";Extended Properties=\"Text;HDR=YES;FMT=Delimited\"";
        //    string fileName = Path.GetFileName(filePath);
        //    string selectString = "SELECT * FROM [" + fileName + "]";
        //    using (OleDbConnection connection = new OleDbConnection(connectionString))
        //    {
        //        using (OleDbDataAdapter adapter = new OleDbDataAdapter(selectString, connection))
        //        {
        //            DataTable dataTable = new DataTable();
        //            adapter.Fill(dataTable);
        //            return dataTable;
        //        }
        //    }
        //}
    }
}

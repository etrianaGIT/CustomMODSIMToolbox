using System;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using Csu.Modsim.ModsimIO;
using Csu.Modsim.ModsimModel;
using Csu.Modsim.NetworkUtils;

namespace MODSIMModeling.ReservoirOps
{
    public class STARFITRelease
    {
        public delegate void ProcessMessage(string msg);  // Delegate

        public Model myModel;
        private DataTable _DtParams;

        public event ProcessMessage messageOutRun;     // Event
        private ModelOutputSupport modsimoutputsupport;

        public STARFITRelease(ref Model m_Model)
        {
            m_Model.Init += OnInitialize;
            m_Model.IterBottom += OnIterationBottom;
            m_Model.IterTop += OnIterationTop;
            m_Model.Converged += OnIterationConverge;
            m_Model.End += OnFinished;

            myModel = m_Model;
        }

        private void OnFinished()
        {
        }

        private void OnIterationConverge()
        {
        }

        public void LoadSTARFITParameters(string paramsCsv)
        {
            // Read parameters into a DataTable
            _DtParams = ReadCsv(paramsCsv);
        }

        private void OnInitialize()
        {
            modsimoutputsupport = myModel.OutputSupportClass as ModelOutputSupport;
            modsimoutputsupport.AddUserDefinedOutputVariable(myModel, "Layer_Target", false, true, "Volume");
            modsimoutputsupport.AddUserDefinedOutputVariable(myModel, "MidLayer_Target", false, true, "Volume");
            modsimoutputsupport.AddCurrentUserReservoir_STOROutput += AddMyResOutput;
        }

        private void AddMyResOutput(Node node, DataRow row)
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
        }

        private void OnIterationTop()
        {
            DateTime dtime = myModel.TimeStepManager.Index2Date(myModel.mInfo.CurrentModelTimeStepIndex, TypeIndexes.ModelIndex);
            Calendar calendar = CultureInfo.InvariantCulture.Calendar;
            int weekNumber = calendar.GetWeekOfYear(dtime, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

            foreach (Node res in myModel.Nodes_Reservoirs)
            {
                double minNormal = GetMinNormal(res, weekNumber);
                double maxNormal = GetMaxNormal(res, weekNumber);

                DataTable dt = res.m.adaTargetsM.dataTable;
                if (dt.Rows.Count > 0)
                {
                    res.m.resBalance.targetPercentages[0] = (double)(minNormal / maxNormal * 100);
                    res.m.resBalance.targetPercentages[1] = (double)(((minNormal + maxNormal) / 2) / maxNormal * 100);
                }
            }
        }

        private void OnIterationBottom()
        {
            DateTime dtime = myModel.TimeStepManager.Index2Date(myModel.mInfo.CurrentModelTimeStepIndex, TypeIndexes.ModelIndex);
            Calendar calendar = CultureInfo.InvariantCulture.Calendar;
            int weekNumber = calendar.GetWeekOfYear(dtime, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

            foreach (Node res in myModel.Nodes_Reservoirs)
            {
                double release = ComputeSTARFITRelease(res, weekNumber);
                if (res.m.resOutLink != null)
                {
                    res.m.resOutLink.to.OutflowLinks.link.mlInfo.hi = (long)Math.Round(release);
                }
            }
        }

        private double ComputeSTARFITRelease(Node res, int weekNumber)
        {
            DataRow[] dr = _DtParams.Select($"[GRanD_NAME] = '{res.name}'");

            if (dr.Length == 0)
            {
                Console.WriteLine($"No parameter data found for reservoir: {res.name}");
                return 0;
            }

            // Retrieve parameters from the DataRow
            double upper_min = double.Parse(dr[0]["NORhi_min"].ToString());
            double upper_max = double.Parse(dr[0]["NORhi_max"].ToString());
            double upper_mu = double.Parse(dr[0]["NORhi_mu"].ToString());
            double upper_alpha = double.Parse(dr[0]["NORhi_alpha"].ToString());
            double upper_beta = double.Parse(dr[0]["NORhi_beta"].ToString());

            double lower_min = double.Parse(dr[0]["NORlo_min"].ToString());
            double lower_max = double.Parse(dr[0]["NORlo_max"].ToString());
            double lower_mu = double.Parse(dr[0]["NORlo_mu"].ToString());
            double lower_alpha = double.Parse(dr[0]["NORlo_alpha"].ToString());
            double lower_beta = double.Parse(dr[0]["NORlo_beta"].ToString());

            double release_alpha1 = double.Parse(dr[0]["Release_alpha1"].ToString());
            double release_alpha2 = double.Parse(dr[0]["Release_alpha2"].ToString());
            double release_beta1 = double.Parse(dr[0]["Release_beta1"].ToString());
            double release_beta2 = double.Parse(dr[0]["Release_beta2"].ToString());
            double release_c = double.Parse(dr[0]["Release_c"].ToString());
            double release_max = double.Parse(dr[0]["Release_max"].ToString());
            double release_min = double.Parse(dr[0]["Release_min"].ToString());
            double release_p1 = double.Parse(dr[0]["Release_p1"].ToString());
            double release_p2 = double.Parse(dr[0]["Release_p2"].ToString());

            double storage = res.m.starting_volume * 1.0e6; // Convert MCM to m³
            double capacity = res.m.max_volume * 1.0e6; // Convert MCM to m³

           
            double inflow_mean = 1.0e6; // 
            double inflow = GetResInflow(res); // Current inflow in m³/s

            double omega = 1.0 / 52.0;

            // Calculate maxNormal and minNormal using sine and cosine functions
            double maxNormal = GetMaxNormal(res, weekNumber);
            double minNormal = GetMinNormal(res, weekNumber);

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
            double availability_status = (100.0 * storage / capacity - minNormal) / (maxNormal - minNormal);

            // Calculate release based on availability status
            double release;
            if (availability_status > 1)
            {
                release = (storage - (capacity * maxNormal / 100.0) + forecasted_weekly_volume) / 7.0;
            }
            else if (availability_status < 0)
            {
                release = (storage - (capacity * minNormal / 100.0) + forecasted_weekly_volume) / 7.0;
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

            return release;
        }

        private double GetMaxNormal(Node res, int weekNumber)
        {
            DataRow[] dr = _DtParams.Select($"[GRanD_NAME] = '{res.name}'");
            double maxNormal = 1.0;

            if (dr.Length > 0)
            {
                res.description = "ID:" + dr[0]["GRanD_ID"].ToString();
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
                        upper_mu + upper_alpha * Math.Sin(2.0 * Math.PI * omega * weekNumber) +
                        upper_beta * Math.Cos(2.0 * Math.PI * omega * weekNumber)));
            }

            long maxMCM = Convert.ToInt64(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(res.m.max_volume, res.m.reservoir_units), ModsimUnits.FromLabel("MCM")));
            return maxNormal / 100 * maxMCM;
        }

        private double GetMinNormal(Node res, int weekNumber)
        {
            DataRow[] dr = _DtParams.Select($"[GRanD_NAME] = '{res.name}'");
            double minNormal = 1.0;

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
                        lower_mu + lower_alpha * Math.Sin(2.0 * Math.PI * omega * weekNumber) +
                        lower_beta * Math.Cos(2.0 * Math.PI * omega * weekNumber)));
            }

            long maxMCM = Convert.ToInt64(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(res.m.max_volume, res.m.reservoir_units), ModsimUnits.FromLabel("MCM")));
            return minNormal / 100 * maxMCM;
        }

        private double GetResInflow(Node res)
        {
            double sumFlow = 0.0;
            LinkList ll = res.InflowLinks;

            while (ll != null)
            {
                sumFlow += ll.link.mlInfo.flow;  // Summing the flow from each inflow link
                ll = ll.next;
            }

            return sumFlow;
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

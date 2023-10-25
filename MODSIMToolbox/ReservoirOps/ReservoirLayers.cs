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

namespace MODSIMModeling.ReservoirOps
{
    public class ReservoirLayers
    {
        public delegate void ProcessMessage(string msg);  // delegate

        public Model myModel;
        private DataTable _DtParams;

        public event ProcessMessage messageOutRun;     //event

        public ReservoirLayers(ref Model m_Model, bool saveXYRun)
		{
			m_Model.Init += OnInitialize;
			m_Model.IterBottom += OnIterationBottom;
			m_Model.IterTop += OnIterationTop;
			m_Model.Converged += OnIterationConverge;
			m_Model.End += OnFinished;
			
			myModel = m_Model;

            //Read parameters in a datatable
            _DtParams = ReadCsv("C:\\Users\\etriana\\Research Triangle Institute\\USGS Coop Agreement - Documents\\Modeling\\starfit_minimal\\starfit\\ISTARF-CONUS.csv");
            
            //Process reservoir targets
			SetReservvoirTargets();

            //Save changes to the XY (run)
            if(saveXYRun)
                XYFileWriter.Write(myModel, myModel.fname.Replace(".xy","Run.xy"));
        }

        private void SetReservvoirTargets()
        {
            foreach (Node res in myModel.Nodes_Reservoirs)
            {
                //res.m.min_volume = res.m.min_volume;
                DataTable dt = res.m.adaTargetsM.dataTable;
                res.m.adaTargetsM.Interpolate = true;
                dt.Rows.Clear();
                foreach (DataRow dr in myModel.TimeStepManager.timeStepsList.Rows)
                {
                    DataRow newdr = dt.NewRow();
                    DateTime dtime = DateTime.Parse(dr["IniDate"].ToString());
                    Calendar calendar = CultureInfo.InvariantCulture.Calendar;
                    int weekNumber = calendar.GetWeekOfYear(dtime, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

                    newdr[0] = dr["EndDate"].ToString();
                    newdr[1] = GetMaxNormal(res, weekNumber) * myModel.ScaleFactor;
                    dt.Rows.Add(newdr); 
                }
                //Set the lower layer placeholder
                res.m.resBalance = new ResBalance();
                res.m.resBalance.PercentBasedOnMaxCapacity = false;
                res.m.resBalance.incrPriorities = new long[] { -100, 0 };
                res.m.resBalance.targetPercentages = new double[] { 20, 100 };

            }
        }

        private  void OnInitialize()
		{
			
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
                
                //Set the lower layer percent (as a function of the max normal = target)
                res.m.resBalance.targetPercentages[1] =  (double) (minNormal/maxNormal*100);

            }
        }

        private double GetMaxNormal(Node res, int weekNumber)
        {
            DataRow[] dr = _DtParams.Select($"[GRanD_NAME] = '{res.name}'");
            double maxNormal = res.m.max_volume;
            if(dr.Length>0)
            {
                double upper_max = double.MaxValue;
                if(dr[0]["NORhi_max"].ToString()!= "Infinity")
                    upper_max= double.Parse(dr[0]["NORhi_max"].ToString());
                double upper_min = dr[0]["NORhi_min"].ToString() != "-Infinity" ?dr[0].Field<double>("NORhi_min"):double.MinValue;
                double upper_mu = double.Parse(dr[0]["NORhi_mu"].ToString()); 
                double upper_alpha = double.Parse(dr[0]["NORhi_alpha"].ToString()); 
                double omega = 1.0 / 52.0;
                double upper_beta = double.Parse(dr[0]["NORhi_beta"].ToString());
                maxNormal = Math.Min(upper_max,
                                        Math.Max(upper_min,
                                               upper_mu +
                upper_alpha *  Math.Sin(2.0 *  Math.PI * omega * weekNumber) +
                upper_beta *  Math.Cos(2.0 * Math.PI * omega * weekNumber)));
            }
            return maxNormal;
        }

        private double GetMinNormal(Node res, int weekNumber)
        {
            DataRow[] dr = _DtParams.Select($"[GRanD_NAME] = '{res.name}'");
            double minNormal = res.m.min_volume;
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
            return minNormal;
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
                    res.m.resOutLink.mlInfo.hi = (long) Math.Round((1D + GetMaxReleaseParameter(res.name)) * resInflow,0);
            }
        }

        private double GetMaxReleaseParameter(string name)
        {
            return 0.2;
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

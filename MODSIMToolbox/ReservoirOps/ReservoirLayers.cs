using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
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
        public event ProcessMessage messageOutRun;     //event

        public ReservoirLayers(ref Model m_Model, bool saveXYRun)
		{
			m_Model.Init += OnInitialize;
			m_Model.IterBottom += OnIterationBottom;
			m_Model.IterTop += OnIterationTop;
			m_Model.Converged += OnIterationConverge;
			m_Model.End += OnFinished;
			
			myModel = m_Model;

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
                    newdr[1] = GetMaxNormal(res.name, weekNumber) * (decimal) myModel.ScaleFactor;
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
                decimal minNormal = GetMinNormal(res.name, weekNumber);
                decimal maxNormal = GetMaxNormal(res.name, weekNumber);

                DataTable dt = res.m.adaTargetsM.dataTable;
                
                //Set the lower layer percent (as a function of the max normal = target)
                res.m.resBalance.targetPercentages[1] =  (double) (minNormal/maxNormal*100);

            }
        }

        private decimal GetMaxNormal(string name, int weekNumber)
        {
            return weekNumber * 2 * 10;
        }

        private decimal GetMinNormal(string name, int weekNumber)
        {
            return weekNumber * 10;
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
	}

}

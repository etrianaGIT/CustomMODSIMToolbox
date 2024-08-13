using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MODSIMModeling.ReservoirOps
{
    /// <summary>
    /// This class holds the data and calculations to implement ramping rates in MODSIM
    /// links, using a user specified probability for setting the limit of the ramping.
    /// It uses tables with exceedance probablity of the rates of flow change by month.
    /// The values to be used for the rates can be specified at runtime.
    /// </summary>
    public class RampingRate
    {
        /// <summary>
        /// Store the source exceedance curves tables, indexed by month.
        /// </summary>
        public Dictionary<int, DataTable> ratesPerMonthTbl;
        /// <summary>
        /// Name of the MODSIM link (gage) for which the rates were developed.
        /// </summary>
        public string myID;
        /// <summary>
        /// Name of the MODSIM node (reservoir) where the operations links are associates with.
        /// </summary>
        public string myResID;
        /// <summary>
        /// Stores the flow in the main link (gage) in the previous time step, to be used
        /// in the calculations for setting limits in the current time step.
        /// </summary>
        public long previousFlow;
        /// <summary>
        /// Stores the results of the current rates values for "increase" and "decrease" indexes for the 
        /// probability defined by the user in CalculateRates.
        /// </summary>
        public Dictionary<int, Dictionary<string, double>> ratesPerMonth;

        public RampingRate(string id, string resID)
        {
            myID = id;
            myResID = resID;

            ratesPerMonthTbl = new Dictionary<int, DataTable>();
            for (int i = 1; i <= 12; i++)
            {
                DataTable newTbl = new DataTable();
                newTbl.Columns.Add("type", typeof(string));
                newTbl.Columns.Add("exceedance_probability", typeof(double));
                newTbl.Columns.Add("flow_change", typeof(double));
                ratesPerMonthTbl.Add(i, newTbl);
            }
        }

        public void CalculateRates(double increaseProb, double decreaseProb)
        {
            ratesPerMonth = new Dictionary<int, Dictionary<string, double>>();
            for (int i = 1; i <= 12; i++)
            {
                Dictionary<string, double> rates = new Dictionary<string, double>();

                //Calculate the increasing rate for the exceedance prob.
                double incValue = 50000;
                DataRow[] drs = ratesPerMonthTbl[i].Select($"[type] = 'increase' AND [exceedance_probability] >= {increaseProb * 100}", "exceedance_probability");
                if (drs != null && drs.Length > 0)
                    incValue = double.Parse(drs[0]["flow_change"].ToString());
                rates.Add("increase", incValue);

                //Calculate the decreasing rate for the exceedance prob.
                double decValue = 50000;
                drs = ratesPerMonthTbl[i].Select($"[type] = 'decrease' AND [exceedance_probability] >= {decreaseProb * 100}", "exceedance_probability");
                if (drs != null && drs.Length > 0)
                    decValue = double.Parse(drs[0]["flow_change"].ToString());
                rates.Add("decrease", decValue);

                ratesPerMonth.Add(i, rates);
            }
        }
    }
}

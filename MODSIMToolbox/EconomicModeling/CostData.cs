using MODSIMModeling.MODSIMUtils;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MODSIMModeling.EconomicModeling
{
    internal class CostData
    {
        public Csu.Modsim.ModsimModel.ModsimUnits units;
        public Csu.Modsim.ModsimModel.Link totFlowLink = null;
        private double value;
        //private DataTable valueTbl;
        private int db_pkid;
        private string type;
        private double prevCost=0;
        private Dictionary<int, Dictionary<string, double>> dataBounds;
        private double _costScaleFactor;

        public CostData(double value, double costScaleFactor ) 
        {
            this.value = value;
            type = "Constant";
            _costScaleFactor = costScaleFactor;
        }
        public CostData(int db_pkid, string costType, double costScaleFactor)
        {
            this.db_pkid = db_pkid;
            type = costType;
            _costScaleFactor = costScaleFactor;
        }

        public void SetDataBounds(MyDBSqlite m_db)
        {
            if (type == "MonthlyVar") //Is this needed here?
            {
                dataBounds = new Dictionary<int, Dictionary<string, double>>();

                string sql = $@"SELECT pkid,month, Max(capacity) as Maxflow, cost as MaxCost 
                                FROM CostData
                                WHERE (pkid = {db_pkid})
                                GROUP BY month";
                DataTable dt = m_db.GetTableFromDB(sql, "DataLimits");
                foreach (DataRow dr in dt.Rows)
                {
                    int mon = int.Parse(dr["month"].ToString());
                    Dictionary<string, double> datas = new Dictionary<string, double>();
                    datas.Add("MaxFlow", double.Parse(dr["Maxflow"].ToString()));
                    datas.Add("MaxCost", double.Parse(dr["Maxcost"].ToString()));
                    dataBounds.Add(mon, datas);
                }


                sql = $@"SELECT pkid,month, Min(capacity) as Minflow, cost as Mincost 
                                FROM CostData
                                WHERE (pkid = {db_pkid})
                                GROUP BY month";
                dt = m_db.GetTableFromDB(sql, "DataLimits");
                foreach (DataRow dr in dt.Rows)
                {
                    int mon = int.Parse(dr["month"].ToString());
                    Dictionary<string, double> datas = dataBounds[mon];
                    datas.Add("MinFlow", double.Parse(dr["Minflow"].ToString()));
                    datas.Add("MinCost", double.Parse(dr["Mincost"].ToString()));
                }
            }
        }

        /// <summary>
        ///  It reads the cost in the original magnitude and applies the active cost scale factor. 
        /// </summary>
        /// <param name="m_db"></param>
        /// <param name="dateTime"></param>
        /// <param name="flow_kAF">It takes the current level of flow in kAF</param>
        /// <param name="converged"></param>
        /// <returns></returns>
        public long GetCostValue(ref MyDBSqlite m_db, DateTime dateTime, double flow_kAF, ref bool converged)
        {

            //if (flow_kAF != prevCost)
            //{
            if (type == "MonthlyVar") //Is this needed here?
            {
                int mon = dateTime.Month;
                if (flow_kAF >= dataBounds[mon]["MaxFlow"])
                    value = dataBounds[mon]["MaxCost"];
                else
                {
                    if (flow_kAF <= dataBounds[mon]["MinFlow"])
                        value = dataBounds[mon]["MinCost"];
                    else
                    {

                        int _mon = dateTime.Month;
                        //string sql = $@"SELECT (cost1 + (({flow_kAF}-flow1)*(cost2-cost1)/(flow2-flow1))) as IntCost FROM (
                        //  SELECT * FROM (
                        //   SELECT pkid,capacity as flow2,cost as cost2 FROM CostData
                        //   WHERE (pkid = {db_pkid} and month = {_mon} and capacity >= {flow_kAF})
                        //   ORDER by capacity 
                        //   LIMIT 1)as a2 
                        //  JOIN (
                        //   SELECT * FROM (
                        //   SELECT pkid,capacity as flow1,cost as cost1  FROM CostData
                        //   WHERE (pkid = {db_pkid} and month = {_mon} and capacity < {flow_kAF})
                        //   ORDER by capacity DESC
                        //   LIMIT 1) 
                        //   ) as a1 ON a1.pkid = a2.pkid
                        //  )";
                        string sql = $@"SELECT cost 
                                                FROM CostData
			                                    WHERE (pkid = {db_pkid} and month = {_mon} and capacity <= {flow_kAF})
			                                    ORDER by capacity DESC 
			                                    LIMIT 1;";
                        value = double.Parse(m_db.ExecuteScalar(sql).ToString());

                    }
                }
            }
            converged = converged && (prevCost == value);
            prevCost = value;
            //}
            //Apply the cost factor for intenal MODSIM variables.
            return (long)Math.Round(value * _costScaleFactor);
        }

        internal void ResetFlow()
        {
            prevCost = -1;
        }
    }
}

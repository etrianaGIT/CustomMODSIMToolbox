﻿using MODSIMModeling.MODSIMUtils;
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
        private double value;
        //private DataTable valueTbl;
        private int db_pkid;
        private string type;
        private double prevFlow=-1;
        private Dictionary<int, Dictionary<string, double>> dataBounds;

        public CostData(double value) 
        {
            this.value = value;
            type = "Constant";
        }
        public CostData(int db_pkid, string costType) 
        {
            this.db_pkid = db_pkid;
            type = costType;
        }

        public void SetDataBounds(MyDBSqlite m_db)
        {
            if (type == "MonthlyVar") //Is this needed here?
            {
                dataBounds = new Dictionary<int, Dictionary<string, double>>();

                string sql = $@"SELECT pkid,month, Max(capacity) as Maxflow,Min(capacity) as Minflow, Max(cost) as MaxCost,Min(cost) as Mincost 
                                FROM CostData
                                WHERE (pkid = {db_pkid})
                                GROUP BY month";
                DataTable dt = m_db.GetTableFromDB(sql, "DataLimits");
                foreach (DataRow dr in dt.Rows)
                {
                    int mon = int.Parse(dr["month"].ToString());
                    Dictionary<string, double> datas = new Dictionary<string, double>();
                    datas.Add("MaxFlow", double.Parse(dr["Maxflow"].ToString()));
                    datas.Add("MinFlow", double.Parse(dr["Minflow"].ToString()));
                    datas.Add("MaxCost", double.Parse(dr["Maxcost"].ToString()));
                    datas.Add("MinCost", double.Parse(dr["Mincost"].ToString()));
                    dataBounds.Add(mon, datas);
                }
            }
        }

        public long GetCostValue(ref MyDBSqlite m_db, DateTime dateTime,double flow)
        {
            if (flow != prevFlow)
            {
                if (type == "MonthlyVar") //Is this needed here?
                {
                    int mon = dateTime.Month;
                    if (flow >= dataBounds[mon]["MaxFlow"])
                        value = dataBounds[mon]["MaxCost"];
                    else
                    {
                        if (flow <= dataBounds[mon]["MinFlow"])
                            value = dataBounds[mon]["MinCost"];
                        else
                        {

                            int _mon = dateTime.Month;
                            string sql = $@"SELECT (cost1 + (({flow}-flow1)*(cost2-cost1)/(flow2-flow1))) as IntCost FROM (
		                            SELECT * FROM (
			                            SELECT pkid,capacity as flow2,cost as cost2 FROM CostData
			                            WHERE (pkid = {db_pkid} and month = {_mon} and capacity >= {flow})
			                            ORDER by capacity 
			                            LIMIT 1)as a2 
		                            JOIN (
			                            SELECT * FROM (
			                            SELECT pkid,capacity as flow1,cost as cost1  FROM CostData
			                            WHERE (pkid = {db_pkid} and month = {_mon} and capacity < {flow})
			                            ORDER by capacity DESC
			                            LIMIT 1) 
			                            ) as a1 ON a1.pkid = a2.pkid
		                            )";
                            value = double.Parse(m_db.ExecuteScalar(sql).ToString());

                        }
                    }
                }
                prevFlow = flow;
            }
            return (long) Math.Round(value);
        }
    }
}

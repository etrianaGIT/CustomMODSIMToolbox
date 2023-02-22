﻿using Csu.Modsim.ModsimModel;
using MODSIMModeling.MODSIMUtils;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace MODSIMModeling.EconomicModeling
{
    internal class BoundsData
    {
        public string lName;
        private string _uID;
        private string _type;
        public int featureID;
        public string layer;
        
        private double _scaleFactor;
        private int initialTSYear = -1;

        public BoundsData(DataRow dRow, double scaleFactor) 
        {
            lName= dRow["MOD_Name"].ToString();
            _uID= dRow["uid"].ToString();
            _type = dRow["BoundsType"].ToString();
            featureID = int.Parse(dRow["FeatureID"].ToString());
            layer = dRow["GroupingKey"].ToString();
            _scaleFactor = scaleFactor;
            
        }

        internal void ProcessLinkBounds(ref MyDBSqlite m_db, ref Link l, DateTime currentBegOfPeriodDate)
        {
            string dbDate;
            string sql;
            switch (_type)
            {
                case string a when a.Contains("EQT"):
                    // code block
                    //l.mlInfo.lo = l.mlInfo.hi;
                    break;
                case string a when a.Contains("LBT"):
                    // code block
                     dbDate = currentBegOfPeriodDate.Year + "-" + currentBegOfPeriodDate.Month.ToString("00") + "-" + currentBegOfPeriodDate.Day.ToString("00");
                    sql = $@"SELECT TSValue FROM Timeseries
                                    WHERE TSTypeID = (SELECT TSTypeID FROM TSTypes
				                                       WHERE MODSIMTSType = ""minVariable"" AND IsPattern = 0) 
	                                    AND FeatureID = {featureID} AND TSDate = '{dbDate}'";
                    //l.mlInfo.lo =(long) Math.Round(double.Parse(m_db.ExecuteScalar(sql).ToString())*_scaleFactor);
                    break;
                case string a when a.Contains("LBM"):
                    // code block
                    if (initialTSYear == -1)
                        initialTSYear = GetInitYear(ref m_db);
                    dbDate = initialTSYear + "-" + currentBegOfPeriodDate.Month.ToString("00") + "-" + currentBegOfPeriodDate.Day.ToString("00");
                    sql = $@"SELECT TSValue FROM Timeseries
                                    WHERE TSTypeID = (SELECT TSTypeID FROM TSTypes
				                                       WHERE MODSIMTSType = ""minVariable"" AND IsPattern = 1) 
	                                    AND FeatureID = {featureID} AND TSDate = '{dbDate}'";
                    l.mlInfo.lo = (long)Math.Round(double.Parse(m_db.ExecuteScalar(sql).ToString()) * _scaleFactor);
                    break;
                default:
                    // code block
                    break;
            }
        }

        private int GetInitYear(ref MyDBSqlite m_db)
        {
            string sql = $@"SELECT min(TSDate) FROM Timeseries
                            WHERE TSTypeID = (SELECT TSTypeID FROM TSTypes
				                               WHERE MODSIMTSType = ""minVariable"" AND IsPattern = 1) 
	                            AND FeatureID = {featureID}";
            DateTime dt = DateTime.Parse(m_db.ExecuteScalar(sql).ToString());
            return dt.Year;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Csu.Modsim.ModsimModel;

namespace MODSIMModeling.ReservoirOps.StarFit
{
    internal class StarFitUtils
    {
        public delegate void ProcessMessage(string msg);  // delegate
        public event ProcessMessage messageOut;
        private DataTable _DtblParams { get; set; }
        private Dictionary<string, bool> dataIssues;
        private string queryRes;

        public StarFitUtils()
        {
            dataIssues = new Dictionary<string, bool>();
        }

        public void LoadParameters(string paramsCsv)
        {
            //Read parameters in a datatable
            _DtblParams = ReadCsv(paramsCsv);
            
        }

        private DataTable ReadCsv(string filePath)
        {
            string connectionString = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" +
                Path.GetDirectoryName(filePath) + ";Extended Properties=\"Text;HDR=YES;FMT=Delimited\"";
            string fileName = Path.GetFileName(filePath);
            string selectString = "SELECT * FROM [" + fileName + "]";
            DataTable dataTable = new DataTable();
            using (OleDbConnection connection = new OleDbConnection(connectionString))
            {
                using (OleDbDataAdapter adapter = new OleDbDataAdapter(selectString, connection))
                {   
                    adapter.Fill(dataTable);
                }
            }
            messageOut("INFO:\tFinished loading starfit parameters.");
            return dataTable;
        }

        /// <summary>
        /// Gets the maximum normal value for a reservoir based on the week number.
        /// This value is in MODSIM units because it is based on the internal reservoir capacity variable.
        /// </summary>
        /// <param name="res">The reservoir node.</param>
        /// <param name="weekNumber">The week number.</param>
        /// <returns>The maximum normal value for the reservoir.</returns>
        public double GetMaxNormal(Node res, int weekNumber, long maxMCM)
        {
            DataRow[] dr = _DtblParams.Select($"[GRanD_NAME] = '{res.name}'");
            queryRes = res.name;
            double maxNormal = 1.0;

            if (dr.Length > 0)
            {
                res.description = "ID:" + dr[0]["GRanD_ID"].ToString();
                double upper_max = GetParam(dr[0], "NORhi_max", double.MaxValue);
                double upper_min = GetParam(dr[0], "NORhi_min", double.MinValue); //!dr[0]["NORhi_min"].ToString().Contains("Infinity") & !dr[0]["NORhi_min"].ToString().Contains("#NAME?") ? double.Parse(dr[0]["NORhi_min"].ToString()) : double.MinValue;
                double upper_mu = GetParam(dr[0], "NORhi_mu", 0); //double.Parse(dr[0]["NORhi_mu"].ToString());
                double upper_alpha = GetParam(dr[0], "NORhi_alpha", 0); // double.Parse(dr[0]["NORhi_alpha"].ToString());
                double omega = 1.0 / 52.0;
                double upper_beta = GetParam(dr[0], "NORhi_beta", 0); //double.Parse(dr[0]["NORhi_beta"].ToString());
                maxNormal = Math.Min(upper_max,
                                        Math.Max(upper_min,
                                               upper_mu +
                upper_alpha * Math.Sin(2.0 * Math.PI * omega * weekNumber) +
                upper_beta * Math.Cos(2.0 * Math.PI * omega * weekNumber)));
            }
            else
            {
                if (!dataIssues.ContainsKey(res.name))
                {
                    messageOut($"ERROR [StarFit GetMaxMormal] No parameter data found for reservoir: {queryRes}");
                    dataIssues.Add(queryRes, true);
                }
            }
            return maxNormal / 100 * maxMCM;
        }

        public double GetVarValue(Node res,string var, double defaultValue = 0)
        {
            DataRow[] dr = _DtblParams.Select($"[GRanD_NAME] = '{res.name}'");
            queryRes = res.name;
            if (dr.Length == 0)
            {
                if (!dataIssues.ContainsKey(res.name))
                {
                    messageOut($"ERROR [StarFit GetValue] No parameter data found for reservoir: {queryRes}");
                    dataIssues.Add(queryRes, true);
                }
                return -1;
            }
            return GetParam(dr[0], var, defaultValue);
        }

        /// <summary>
        /// Gets the value of a variable (column) in the parameters, csv, table
        /// it handles potential errors with infinity, #Name? and blanks
        /// if the parameter is not available ("NA") it will return -1.
        /// if the reservoir is not found it will return -1.
        /// </summary>
        /// <param name="dr"></param>
        /// <param name="var">name of the variable or column in the csv</param>
        /// <param name="defaultValue">value returned if the parameter is not found. For max and min values use the double.MaxValue or MinValue</param>
        /// <returns></returns>
        private double GetParam(DataRow dr, string var, double defaultValue)
        {
            if (dr[var].ToString() == "NA")
            {
                if (!dataIssues.ContainsKey(queryRes))
                    dataIssues.Add(queryRes, true);
                return -1;
            }
            double value = defaultValue;
            if (!dr[var].ToString().Contains("Infinity")
                & dr[var].ToString() != ""
                & !dr[var].ToString().Contains("#NAME?"))
            {
                value = double.Parse(dr[var].ToString());
            }
            else
            {
                if (!dataIssues.ContainsKey(queryRes))
                    dataIssues.Add(queryRes, true);
            }
            return value;   
        }

        public double GetMinNormal(Node res, int weekNumber, long maxMCM)
        {
            DataRow[] dr = _DtblParams.Select($"[GRanD_NAME] = '{res.name}'");
            queryRes = res.name;
            double minNormal = 1.0;
            if (dr.Length > 0)
            {
                double lower_max = GetParam(dr[0], "NORlo_max", double.MaxValue); 
                double lower_min = GetParam(dr[0], "NORlo_min", double.MinValue); //dr[0]["NORlo_min"].ToString() != "" & dr[0]["NORlo_min"].ToString() != "-Infinity" ? dr[0].Field<double>("NORlo_min") : double.MinValue;
                double lower_mu = GetParam(dr[0], "NORlo_mu", 0);
                double lower_alpha = GetParam(dr[0], "NORlo_alpha", 0);
                double omega = 1.0 / 52.0;
                double lower_beta = GetParam(dr[0], "NORlo_beta", 0);
                minNormal = Math.Min(lower_max,
                                        Math.Max(lower_min,
                                               lower_mu +
                lower_alpha * Math.Sin(2.0 * Math.PI * omega * weekNumber) +
                lower_beta * Math.Cos(2.0 * Math.PI * omega * weekNumber)));
            }
            else
            {
                if (!dataIssues.ContainsKey(res.name))
                {
                    messageOut($"ERROR [GetMinMormal] No parameter data found for reservoir: {queryRes}");
                    dataIssues.Add(queryRes, true);
                }
            }
            //long maxMCM = Convert.ToInt64(myModel.StorageUnits.ConvertTo(myModel.StorageUnits.ConvertFrom(res.m.max_volume, res.m.reservoir_units), ModsimUnits.FromLabel("MCM")));
            return minNormal / 100 * maxMCM;
        }

        public long GetResInflow(Node res, bool addByPass = false)
        {
            long sumFlow = 0;
            LinkList ll = res.InflowLinks;
            while (ll != null)
            {
                if(!ll.link.mlInfo.isArtificial)
                    sumFlow += ll.link.mlInfo.flow;
                ll = ll.next;
            }
            if (addByPass && res.m.resBypassL != null)
                sumFlow += res.m.resBypassL.mlInfo.flow;
            
            return sumFlow;
        }

        public bool IsDataIssues(string resName)
        { return dataIssues.ContainsKey(resName); }

    }
}

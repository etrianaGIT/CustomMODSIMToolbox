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

namespace MODSIMModeling.Routing
{
    public class RoutingUtils
    {
        public delegate void ProcessMessage(string msg);  // delegate

        public Model myModel;
        private DataTable _DtParams;

        public event ProcessMessage messageOutRun;     //event
        
        public RoutingUtils(ref Model m_Model)
		{
			myModel = m_Model;           
        }

        public void SetRoutingParams(string paramCSV)
        {
            //Read parameters in a datatable
            _DtParams = ReadCsv(paramCSV);
            //Setting the "Model Generated" flag
            myModel.useLags = 0;
            //Setting the number of lag factors
            myModel.nlags = 16;

            foreach (Link l in myModel.Links_All)
            {
                DataRow[] dr = _DtParams.Select($"[LinkName] = '{l.name}'");
                if(dr!=null && dr.Length>0)
                {
                    //Setting losses (routing flag) in the link
                    l.m.loss_coef = 1;
                    //Setting return node to the next downstream node.
                    l.m.returnNode = l.to;
                    //Setting Muskingum parameter
                    l.m.spyldc = double.Parse(dr[0]["MX"].ToString());
                    l.m.transc = double.Parse(dr[0]["MK"].ToString());
                    l.m.distc = 1;// double.Parse(dr[0]["MT"].ToString());
                    //Cleaning lag factors
                    for (int i = 0; i < l.m.lagfactors.Length; i++)
                    {
                        l.m.lagfactors[i] = 0;
                    }
                }
            }
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

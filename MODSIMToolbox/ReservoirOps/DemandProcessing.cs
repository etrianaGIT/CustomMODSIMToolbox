using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Csu.Modsim.ModsimIO;
using Csu.Modsim.ModsimModel;

using Microsoft.VisualBasic.FileIO;

namespace MODSIMModeling.Preprocessing
{
    public class DemandProcessing
    {
        public delegate void ProcessMessage(string msg);  // delegate

        public Model myModel;
        private DateTime MODSIMIniDate;
        private DataTable _DtDems;
        private DataTable _resNodes;
        private int coordFactor = 100;

        public event ProcessMessage messageOutRun;     //event

        /// <summary>
        /// This class imports demand node data from a CSV file and create the nodes in the network.
        /// The csv file should have a datetime column with the date of the time series and columns with 
        /// headings matching the MODSIM name of the location were the demand node will be created.
        /// Demand nodes are created at the upstream end of gauge stations and reservoirs.  
        /// </summary>
        /// <param name="m_Model">reference pointer to the MODSIM model</param>
        public DemandProcessing(ref Model m_Model)
        {
            myModel = m_Model;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="demandFileCSV">csv file should have a datetime column with the date of the time series and columns with 
        /// headings matching the MODSIM name of the location were the demand node will be created.</param>
        /// <param name="costBase"></param>
        /// <param name="resIDsCSV">Path to a csv file containing columns for DAM_NAME, GRAND_ID</param>
        /// <param name="unitsStr"></param>
        public void ImportDeamandTimeseries(string demandFileCSV, long costBase, string resIDsCSV, string unitsStr = "m³/d")
        {
            //Clear demand nodes
            // loop through all nodes and remove demand nodes
            foreach (Node n in myModel.Nodes_All)
            {
                if (n.nodeType == NodeType.Demand)
                {
                    myModel.FireOnMessage("INFO:\tRemoving demand node: " + n.name);
                    myModel.Remove(n, true);
                }
            }

            //Get the initial date
            MODSIMIniDate = myModel.TimeStepManager.dataStartDate;

            //Read parameters in a datatable
            _DtDems = ReadCsv(demandFileCSV);
            _resNodes = ReadCsv(resIDsCSV);
            List<Link> links = new List<Link>();

            foreach (DataColumn col in _DtDems.Columns)
            {
                Link gageLnk = myModel.FindLink(col.ColumnName,silent:true);

                if (gageLnk != null)
                {
                    myModel.FireOnMessage($"INFO:\tFound link for gage {col.ColumnName}.");
                    // Create a new node and link for the gage in the middle of the link 
                    Node origFromNode = gageLnk.from;
                    CreateDemNode(origFromNode, gageLnk.name, costBase, ref links, col.ColumnName, unitsStr);
                    
                }

                //Check if the column corresponds to a reservoir node
                string resName = FindResName(col.ColumnName);
                Node resNode = myModel.FindNode(resName);
                if (resNode != null)
                {
                    myModel.FireOnMessage($"INFO:\tFound demand for reservoir {resNode.name} - {col.ColumnName}.");
                    CreateDemNode(resNode, resNode.name, costBase, ref links, col.ColumnName,unitsStr);
                }
            }
            // Report on the maximum and minimum cost set to the links
            myModel.FireOnMessage($"INFO:\tMaximum cost set to the demand links: {links.Max(l => l.m.cost)}");
            myModel.FireOnMessage($"INFO:\tMinimum cost set to the demand links: {links.Min(l => l.m.cost)}");
        }

        private void CreateDemNode(Node origFromNode, string name, long costBase, ref List<Link> links, string ColumnName, string unitsStr)
        {
            double x = (origFromNode.graphics.nodeLoc.X + 5.0 * coordFactor);
            double y = origFromNode.graphics.nodeLoc.Y;
            //Node demNode = GeoToModsim_AddNode(x, y, nName, null, ModsimNodeType.NonStorage);
            string nName = "Dem_" + name;
            Node demNode = myModel.FindNode(nName);
            if (demNode == null)
            {
                myModel.FireOnMessage($"INFO:\t\tCreating demand node...");
                demNode = myModel.AddNewNode(true);
                demNode.nodeType = NodeType.Demand;
                demNode.graphics.nodeLoc.X = (float)(x);
                demNode.graphics.nodeLoc.Y = (float)(y);
                demNode.name = nName;
            }
            // create a link from return node to the downstream node
            Link m_link = myModel.AddNewLink(true);
            m_link.name = "DiverionDem_" + name;
            m_link.m.cost = costBase - m_link.number;
            Csu.Modsim.ModsimModel.Utils.ConnectFromNode(m_link, origFromNode);
            Csu.Modsim.ModsimModel.Utils.ConnectToNode(m_link, demNode);
            links.Add(m_link);

            myModel.FireOnMessage($"INFO:\t\tProcessing data...");
            bool isFirstDate = true;
            try
            {
                foreach (DataRow dr in _DtDems.Rows)
                {
                    string datetime = dr["datetime"].ToString();
                    int q = (int)Math.Round(double.Parse(dr[ColumnName].ToString()) * myModel.ScaleFactor, 0);
                    if (isFirstDate && DateTime.Parse(datetime) > MODSIMIniDate)
                    {
                        demNode.m.adaDemandsM.dataTable.Rows.Add(MODSIMIniDate, 0);
                    }
                    if (DateTime.Parse(datetime) >= MODSIMIniDate)
                        demNode.m.adaDemandsM.dataTable.Rows.Add(datetime, q);
                    isFirstDate = false;
                }
                demNode.m.adaDemandsM.VariesByYear = true;
                demNode.m.adaDemandsM.units = new ModsimUnits(unitsStr);
            }
            catch (Exception ex)
            {
                myModel.FireOnError($"ERROR: [Processing demand {demNode.name}] " + ex.Message);
            }

        }

        private string FindResName(string columnName)
        {
            foreach(DataRow dr in _resNodes.Rows)
            {
                if (dr["GRAND_ID"].ToString() == columnName) 
                    return dr["DAM_NAME"].ToString();
            }
            return "";
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

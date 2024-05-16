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
        private int coordFactor = 100;

        public event ProcessMessage messageOutRun;     //event

        public DemandProcessing(ref Model m_Model)
        {
            myModel = m_Model;
        }

        public void ImportDeamandTimeseries(string demandFileCSV, long costBase ,string unitsStr = "m³/d")
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
            List<Link> links = new List<Link>();

            foreach (DataColumn col in _DtDems.Columns)
            {
                Link gageLnk = myModel.FindLink(col.ColumnName,silent:true);

                if (gageLnk != null)
                {
                    myModel.FireOnMessage($"INFO:\tFound link for gage {col.ColumnName}.");
                    // Create a new node and link for the gage in the middle of the link 
                    Node origFromNode = gageLnk.from;
                    double x = (origFromNode.graphics.nodeLoc.X + 5.0 * coordFactor);
                    double y = origFromNode.graphics.nodeLoc.Y;
                    //Node demNode = GeoToModsim_AddNode(x, y, nName, null, ModsimNodeType.NonStorage);
                    string nName = "Dem_" + gageLnk.name;
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
                    m_link.name = "DiverionDem_" + gageLnk.name;
                    m_link.m.cost = costBase - m_link.number;
                    Csu.Modsim.ModsimModel.Utils.ConnectFromNode(m_link, origFromNode);
                    Csu.Modsim.ModsimModel.Utils.ConnectToNode(m_link, demNode);
                    links.Add(m_link);

                    myModel.FireOnMessage($"INFO:\t\tProcessing data...");
                    bool isFirstDate = true;
                    foreach (DataRow dr in _DtDems.Rows)
                    {
                        string datetime = dr["datetime"].ToString();
                        int q = (int)Math.Round(double.Parse(dr[col.ColumnName].ToString()) * myModel.ScaleFactor, 0);
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
            }
            // Report on the maximum and minimum cost set to the links
            myModel.FireOnMessage($"INFO:\tMaximum cost set to the demand links: {links.Max(l => l.m.cost)}");
            myModel.FireOnMessage($"INFO:\tMinimum cost set to the demand links: {links.Min(l => l.m.cost)}");
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

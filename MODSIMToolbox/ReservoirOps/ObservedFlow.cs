using System;
using System.Data;
using System.Data.OleDb;
using System.IO;
using Csu.Modsim.ModsimIO;
using Csu.Modsim.ModsimModel;

using Microsoft.VisualBasic.FileIO;

namespace MODSIMModeling.Preprocessing
{
    public class ObservedFLowImport
    {
        public delegate void ProcessMessage(string msg);  // delegate

        public Model myModel;
        private DateTime MODSIMIniDate;

        public event ProcessMessage messageOutRun;     //event
        
        public ObservedFLowImport(ref Model m_Model)
		{
			myModel = m_Model;
        }

        public void ImportTimeseries(string dataFolder)
        {
            //Get the initial date
            MODSIMIniDate = myModel.TimeStepManager.dataStartDate;

            string[] files = Directory.GetFiles(dataFolder, "*.*", System.IO.SearchOption.AllDirectories);
            foreach (string file in files)
            {
                FileInfo fileInfo = new FileInfo(file);
                string fileName = fileInfo.Name;
                fileName = Path.GetFileNameWithoutExtension(fileName);
                if (fileName.StartsWith("DV_"))
                {
                    fileName = fileName.Replace("DV_", "");
                }
                Link gageLink = myModel.FindLink(fileName);
                if(gageLink != null)
                {

                    myModel.FireOnMessage($"INFO:\tFound link for gage {gageLink.name}. Processing data...");
                    string filePath = fileInfo.FullName;
                    var dataTable = new DataTable();

                    using (var parser = new TextFieldParser(filePath))
                    {
                        parser.TextFieldType = FieldType.Delimited;
                        parser.SetDelimiters(",");
                        parser.HasFieldsEnclosedInQuotes = true;

                        // Read the column names from the first line of the CSV file
                        string[] columnNames = parser.ReadFields();
                        int datetimeIndex = -1;
                        int qIndex = -1;
                        int index = 0;
                        foreach (string columnName in columnNames)
                        {
                            
                            if (columnName == "datetime")
                            {
                                dataTable.Columns.Add(columnName);
                                datetimeIndex = index;
                            }
                            if ( columnName == "Q(cfs)")
                            {
                                dataTable.Columns.Add(columnName);
                                qIndex = index;
                            }
                            index++;
                        }

                        // Read the data from the remaining lines of the CSV file
                        if (datetimeIndex > -1 && qIndex > -1)
                        {
                            bool isFirstDate = true;
                            while (!parser.EndOfData)
                            {
                                string[] fields = parser.ReadFields();
                                string datetime = fields[datetimeIndex];
                                int q = (int)Math.Round(-999 * myModel.ScaleFactor, 0); 
                                if(fields[qIndex].ToString()!="")
                                    q = (int) Math.Round(double.Parse(fields[qIndex].ToString())*myModel.ScaleFactor,0);
                                if (isFirstDate && DateTime.Parse(datetime) > MODSIMIniDate)
                                {
                                    dataTable.Rows.Add(MODSIMIniDate, -999);
                                }
                                if (DateTime.Parse(datetime) >= MODSIMIniDate)
                                    dataTable.Rows.Add(datetime, q);
                                isFirstDate = false;
                            }
                        }
                        else
                        {
                            myModel.FireOnError($"ERROR [Parsing csv file] Could not find datetime and Q(csv) columns for {gageLink.name}.");
                            continue;
                        }
                    }

                    gageLink.m.adaMeasured.dataTable = dataTable;
                    gageLink.m.adaMeasured.VariesByYear = true;
                    gageLink.m.adaMeasured.units = new ModsimUnits("cfs");

                }
            }
        }

        public void ClearInflows()
        {
            foreach (Node nsnode in myModel.Nodes_NonStorage)
            {
                //for (int i = nsnode.m.adaInflowsM.dataTable.Rows.Count - 1; i > 0; i--)
                //{
                //    nsnode.m.adaInflowsM.dataTable.Rows.RemoveAt(i);
                //}
                myModel.FireOnMessage($"\tCleaning inflow node {nsnode.name}");
                //nsnode.m.adaInflowsM.dataTable.Rows.Clear();
                //nsnode.m.adaInflowsM.dataTable.Rows.Add(nsnode.m.adaInflowsM.dataTable.Rows[0]);
                
                //DataRow dr0 = nsnode.m.adaInflowsM.dataTable.Rows[0];
                //nsnode.m.adaInflowsM.dataTable.Rows.Remove(dr0);
                //dr0[1] = 0;
                nsnode.m.adaInflowsM.dataTable.Rows.Clear();
                nsnode.m.adaInflowsM.dataTable.Rows.Add(new object[] {myModel.TimeStepManager.dataStartDate,0 });
            }
        }
    }

}

using System;
using System.Data;
using System.IO;
using Csu.Modsim.ModsimModel;
using Csu.Modsim.NetworkUtils;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RTI.CWR.MODSIMModeling.MODSIMUtils;

namespace RTI.CWR.MODSIM.WQModelingModule
{
    public class MODSIMTimeSeriesIO
    {
        public enum TSTypeID
        {
            INFLOW = 0,
            TARGET = 1,
            DEMAND = 2,
            CONCENTRATION = 3
        }
        // Dim conn As OleDb.OleDbConnection
        private string WQ_TSDatabase;
        private Model myModel;
        public MODSIMTimeSeriesIO(ref Model myModel)
        {
            WQ_TSDatabase = GetTSDatabaseName(myModel);
            this.myModel = myModel;
        }
        private static string GetTSDatabaseName(Model mModel)
        {
            return ModelOutputSupport.BaseNameString(mModel.fname) + "TS.mdb";
        }
        public static event ErrorMessageEventHandler ErrorMessage;

        public delegate void ErrorMessageEventHandler(Exception ex);
        public static event MessageEventHandler Message;

        public delegate void MessageEventHandler(string msg);
        public void SaveCurveFittingData(DataTable m_InfoTable)
        {
            m_InfoTable.TableName = "NodesWQCalibrationInfo";
            if (!File.Exists(WQ_TSDatabase))
            {
                if (!CreateTSDatabase(WQ_TSDatabase))
                    return;
            }
            try
            {
                MyDBSqlite m_DB = new MyDBSqlite(WQ_TSDatabase);
                //m_DB.DeleteExistingTables(m_InfoTable.TableName);
                //m_DB.CreateTableInDB(m_InfoTable);
                m_DB.UpdateTableFromDB(m_InfoTable);
            }
            catch (Exception ex)
            {
                ErrorMessage?.Invoke(ex);
            }
        }

        public void SaveDataToDatabase()
        {
            Message?.Invoke("     Saving Water Quality Data ...");
            if (!File.Exists(WQ_TSDatabase))
            {
                if (!CreateTSDatabase(WQ_TSDatabase))
                    return;
            }
            try
            {
                var m_DB = new MyDBSqlite(WQ_TSDatabase);
                //m_DB.DeleteExistingTables();
                var cur_Node = new Node();
                cur_Node = myModel.firstNode;
                if (cur_Node is null)
                    return;
                do
                {
                    string TableName = cur_Node.name + "___CONCENTRATION";
                    // Create Table
                    if (cur_Node.Tag != null)
                    {
                        if (((QualityNodeData)cur_Node.Tag).InflowConcentration.dataTable.Rows.Count > 0)
                        {
                            DataTable conTable = ((QualityNodeData)cur_Node.Tag).InflowConcentration.dataTable;
                            if (Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(conTable.Rows[0][0], DateTime.Parse("0001-01-01"), false)))
                                goto skipTable;
                            try
                            {
                                conTable.TableName = TableName;
                                //m_DB.CreateTableInDB(conTable);
                                m_DB.UpdateTableFromDB(conTable);
                            }
                            catch (Exception ex)
                            {
                                ErrorMessage?.Invoke(ex);
                            }

                        skipTable:
                            ;

                        }
                        if (((QualityNodeData)cur_Node.Tag).m_CurveFitting.m_CurveFittingTable != null)
                        {
                            TableName = cur_Node.name + "___FITTINGCOEFS";
                            ((QualityNodeData)cur_Node.Tag).m_CurveFitting.m_CurveFittingTable.TableName = TableName;
                            //m_DB.CreateTableInDB(((QualityNodeData)cur_Node.Tag).m_CurveFitting.m_CurveFittingTable);
                            m_DB.UpdateTableFromDB(((QualityNodeData)cur_Node.Tag).m_CurveFitting.m_CurveFittingTable);
                        }
                    }
                    cur_Node = cur_Node.next;
                }
                while (!(cur_Node is null));
            }
            catch (Exception ex)
            {
                ErrorMessage?.Invoke(ex);
            }
            finally
            {
            }
        }

        public void LoadDataFromDatabase()
        {
            // Dim WQ_TSDatabase As String = GetTSDatabaseName(myModel)
            if (File.Exists(WQ_TSDatabase))
            {
                var m_DB = new MyDBSqlite(WQ_TSDatabase);
                try
                {
                    // Get the available table names
                    var tableNames = new Collection();
                    //tableNames = m_DB.GetAvailableTables();
                    // Populate existing time series from the database
                    int i;
                    var loopTo = tableNames.Count;
                    for (i = 1; i <= loopTo; i++)
                    {
                        string[] nodeinfo = Strings.Split(Conversions.ToString(tableNames[i]), "___");
                        if (nodeinfo.Length == 2)
                        {
                            Node mNode = default;
                            mNode = myModel.FindNode(nodeinfo[0]);
                            if (mNode != null)
                            {
                                var mTable = new DataTable();
                                mTable = m_DB.GetTableFromDB(Conversions.ToString(Operators.ConcatenateObject(Operators.ConcatenateObject("SELECT * FROM ", tableNames[i]), ";")), nodeinfo[1]);
                                switch (nodeinfo[1] ?? "")
                                {
                                    case var @case when @case == (TSTypeID.INFLOW.ToString() ?? ""):
                                        {
                                            break;
                                        }

                                    case "CONCENTRATION":
                                        {
                                            QualityNodeData m_WQ_Data = (QualityNodeData) mNode.Tag;
                                            m_WQ_Data.InflowConcentration.dataTable = mTable;
                                            break;
                                        }
                                    case "FITTINGCOEFS":
                                        {
                                            QualityNodeData m_WQ_Data = (QualityNodeData)mNode.Tag;
                                            if (m_WQ_Data.m_CurveFitting is null)
                                            {
                                                m_WQ_Data.m_CurveFitting = new CurveFittingUtils();
                                            }
                                            m_WQ_Data.m_CurveFitting.m_CurveFittingTable = mTable;
                                            break;
                                        }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorMessage?.Invoke(ex);
                }
            }
        }
        public DataTable GetCurveFittingData()
        {
            if (File.Exists(WQ_TSDatabase))
            {
                var m_DB = new MyDBSqlite(WQ_TSDatabase);
                return m_DB.GetTableFromDB("SELECT * FROM NodesWQCalibrationInfo;", "NodesWQCalibrationInfo");
            }
            else
            {
                return null;
            }
        }
        private static bool CreateTSDatabase(string WQ_TSDatabase)
        {
            //var cat = new ADOX.Catalog();
            bool bAns = false;
            //try
            //{
            //    // Make sure the folder
            //    // provided in the path exists. If file name w/o path 
            //    // is  specified,  the database will be created in your
            //    // application folder.
            //    string sCreateString;
            //    sCreateString = "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + WQ_TSDatabase + ";";

            //    cat.Create(sCreateString);
            //    bAns = true;
            //}
            //catch (System.Runtime.InteropServices.COMException Excep)
            //{
            //    bAns = false;
            //    ErrorMessage?.Invoke(Excep);
            //}
            //finally
            //{
            //    cat = default;
            //}
            return bAns;
        }
    }
}
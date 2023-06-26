using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Csu.Modsim.ModsimModel;
//using ESRI.ArcGIS.Carto;
//using ESRI.ArcGIS.Framework;
//using ESRI.ArcGIS.Geodatabase;


namespace RTI.CWR.MODSIM.WQModelingModule
{


    public class MODSIMWQuality
    {
        private Model myModel;
        public event WQ_MessageEventHandler WQ_Message;

        public delegate void WQ_MessageEventHandler(string msg);
        private bool initialized;
        private Collection nodeCalcOrder;
        private bool calibMode;
        private static IFeatureClass m_FeatureClass;
        private bool calibSecondPass;
        private bool useANNResTranp = false;
        // Private MatlabExpFolder As String
        private bool useMeasConcentration;
        public bool debugON = false;
        private bool finalCalib = false;

        public MODSIMWQuality(ref Model myModel, bool calibMode = false, IApplication m_app = default, bool ReservoirANNTrnsp = false, bool useMeasConc = false)
        {
            this.myModel = myModel;
            // Assign a new WQuality support class to links and nodes
            NewQualityInLinks();
            NewQualityInNodes();
            initialized = false;
            this.calibMode = calibMode;
            if (m_app is null)
            {
                if (this.calibMode)
                {
                    WQ_Message?.Invoke("Calibration Not possible with current inicialization parameters");
                    this.calibMode = false;
                }
            }
            else
            {
                // Me.m_app = m_app
                ILayer pLayer;
                IFeatureLayer pfl;
                pLayer = GEODSS.ANNGeoInputs.GISUtilities.GetLayer(m_app, "Modsim_Gauges");
                pfl = pLayer;
                m_FeatureClass = pfl.FeatureClass;
            }
            useANNResTranp = ReservoirANNTrnsp;
            useMeasConcentration = useMeasConc;
        }
        private void AddRowToNodeStationTable(ref DataTable m_NodeStationRelation, string curNodeName, int typeID = 0, string UpSNodeName = null)
        {
            if (m_NodeStationRelation != null)
            {
                DataRow[] m_testRows;
                m_testRows = m_NodeStationRelation.Select("Node = '" + curNodeName + "' AND TypeID=" + typeID);
                if (m_testRows.Length == 0)
                {
                    // add entry if it's not in the table only
                    var m_row = m_NodeStationRelation.NewRow();
                    m_row["Node"] = curNodeName;
                    m_row["TypeID"] = typeID;
                    if (UpSNodeName != null)
                    {
                        m_row["UpSNode"] = UpSNodeName;
                    }
                    m_NodeStationRelation.Rows.Add(m_row);
                }
                else if (m_testRows.Length == 1)
                {
                    // This case when typeID = 0 already exist. A new row is added with the new station.
                    if (typeID != 0)
                    {
                        var m_newrow = m_NodeStationRelation.NewRow();
                        m_newrow.ItemArray = (object[])m_testRows[0].ItemArray.Clone();
                        m_NodeStationRelation.Rows.Add(m_newrow);
                    }
                }
            }
        }
        public void initializeWQualityStructure(Model myModelforQuality)
        {
            // Trace the network and build a list of nodes from the most upstream to the downstream.
            nodeCalcOrder = NetTopology.findNetworkUpStream(myModelforQuality);
            initialized = true;
        }
        private ModelOutputSupport _m_OutputSupport;

        private ModelOutputSupport m_OutputSupport
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return _m_OutputSupport;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                _m_OutputSupport = value;
            }
        }
        private ANNUtility _ANN;

        private ANNUtility ANN
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return _ANN;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                _ANN = value;
            }
        }
        private Hashtable reachesTable;
        public void InitializeANNTransportmodel(string m_RTransFilesPath, string m_RTransBaseName, bool m_debugON, string GeoDSSBasename)
        {
            // 'Initilize ANN for water quality reservoir tranport simulation 
            if (useANNResTranp)
            {
                if (!string.IsNullOrEmpty(m_RTransFilesPath) & !string.IsNullOrEmpty(m_RTransBaseName))
                {
                    // MatlabExpFolder = System.IO.Path.GetDirectoryName(m_RTransFilesPath & "\") & "\" & m_RTransBaseName
                    debugON = m_debugON;
                    ANN = new ANNUtility(Path.GetDirectoryName(m_RTransFilesPath + @"\"), Path.GetDirectoryName(myModel.fname), m_RTransBaseName);
                }
                else
                {
                    var m_WQTrans = new WQANNResTransportDialog();
                    m_WQTrans.ShowDialog();
                    debugON = m_WQTrans.CheckBoxDBugOn.Checked;
                    // MatlabExpFolder = m_WQTrans.MatlabExpFolder
                    if (string.IsNullOrEmpty(m_WQTrans.MatlabExpFolder))
                    {
                        myModel.FireOnMessage("ANN Training files not found. Reservoir Transport using ANN has been canceled.");
                        useANNResTranp = false;
                    }
                    ANN = new ANNUtility(m_WQTrans.MatlabExpFolder, Path.GetDirectoryName(myModel.fname), m_WQTrans.ANNBaseName);
                }
                // ANN = New ANNUtility
                ANN.ANNmessage += myModel.FireOnMessage;
                string m_NetScenarioName = Path.GetFileNameWithoutExtension(myModel.fname);
                ANN.Initialize("JMRes" + m_NetScenarioName, myModel.TimeStepManager.noModelTimeSteps, 1, GeoDSSBasename);
            }
        }

        private WQDebug m_DebWin;
        private DataTable m_UpSSourceTable;
        public void OnInitilize(string m_DataModelDB)
        {
            NewQualityInLinks();
            reachesTable = new Hashtable();
            m_DebWin = new WQDebug();
            if (!initialized)
                throw new Exception("Network is not initialized for Water Quality calculation.  Execute 'initializeWQualityStructure' before running MODSIM");
            Node cur_Node;
            cur_Node = myModel.firstNode;
            do
            {
                if (cur_Node.Tag is null)
                {
                    // Some nodes created after the class was initialized might not have the tag initialized.
                    cur_Node.Tag = new QualityNodeData();
                }
                var m_array = new float[myModel.TimeStepManager.noModelTimeSteps + 1, 1];
                cur_Node.Tag.INconcentrations = m_array.Clone();
                if (cur_Node.Tag.InflowConcentration.getSize > 0)
                {
                    var m_array2 = new float[myModel.TimeStepManager.noModelTimeSteps + 1, 2];
                    cur_Node.Tag.concentrations = m_array2.Clone();
                    myModel.LoadTimeSeriesArray(cur_Node.Tag.InflowConcentration, cur_Node.Tag.concentrations);
                }
                else
                {
                    // cur_Node.Tag = Nothing
                    object a = cur_Node.Tag.InflowConcentration.getSize;
                }
                // Add FTD nodes to FTNodes (Demands that flow to this node)
                if (cur_Node.m.idstrmx(0) != null & cur_Node.m.idstrmfraction(0) > 0)
                {

                }
                // Mark nodes in the WQ Station upstream reach.
                if (IsWQStation(cur_Node))
                {
                    var m_WQData = new WQStationCalibData();
                    m_WQData.m_Tbl = BuildTableDWSStations(cur_Node.name);
                    MarkReachNetworkUpStream(ref myModel, cur_Node, ref m_WQData.m_Tbl);
                    reachesTable.Add(cur_Node.name, m_WQData);
                    m_DebWin.addTable(m_WQData.m_Tbl);
                }
                // Get the next node
                cur_Node = cur_Node.next;
            }
            while (!(cur_Node is null));

            // Add FTD nodes to FTNodes (Demands that flow to this node)
            cur_Node = myModel.firstNode;
            do
            {
                if (cur_Node.m.idstrmx(0) != null & cur_Node.m.idstrmfraction(0) > 0)
                {
                    int myN;
                    var loopTo = cur_Node.m.idstrmx.Length - 1;
                    for (myN = 0; myN <= loopTo; myN++)
                    {
                        if (cur_Node.m.idstrmx(myN) != null & cur_Node.m.idstrmfraction(myN) > 0)
                        {
                            if (myN > 0)
                            {
                                throw new Exception("More than one flow thru return node is not implemented in Water Quality Modeling" + Constants.vbCrLf + "Node: " + cur_Node.name);
                                // Requires changes in the concentration calculation for the FTN
                            }
                            Node FTN = myModel.FindNode(cur_Node.m.idstrmx(myN).name);
                            FTN.Tag.myFTDNodes.add(cur_Node);
                        }
                    }
                }
                cur_Node = cur_Node.next;
            }
            while (!(cur_Node is null));

            // Get UpStream source info
            var m_Db = new GEODSS.ANNGeoInputs.DB_Utils(m_DataModelDB);
            string m_sql = "SELECT MODSIM_SYNC_Network_NODE.NodeName, MODSIM_SYNC_Network_NODE.Calib_Struct";
            m_sql += " FROM(MODSIM_SYNC_Network_NODE)";
            m_sql += " WHERE (((MODSIM_SYNC_Network_NODE.Calib_Struct) Like '%-YES'));";
            // Get upstream source nodes list
            m_UpSSourceTable = m_Db.GetTableFromDB(m_sql, "UpsTbl");

            // initialize the output variable
            m_OutputSupport = myModel.OutputSupportClass;
            m_OutputSupport.AddUserDefinedOutputVariable(myModel, "Concentration", true, false, "Concentration [mg/L]");
            m_OutputSupport.AddUserDefinedOutputVariable(myModel, "Measured Concentration", false, true, "Concentration [mg/L]");
            m_OutputSupport.AddUserDefinedOutputVariable(myModel, "Concentration", false, true, "Concentration [mg/L]");
            this.m_OutputSupport.AddCurrentUserLinkOutput += addLinkQualityOutput;
            this.m_OutputSupport.AddCurrentUserDemandOutput += addNodeQualityOutput;
            this.m_OutputSupport.AddCurrentUserNonStorageOutput += addNodeQualityOutput;
            this.m_OutputSupport.AddCurrentUserReservoirOutput += addReservoirQualityOutput; // addNodeQualityOutput
                                                                                             // AddHandler m_OutputSupport.AddCurrentUserReservoir_STOROutput, AddressOf addReservoirQualityOutput
                                                                                             // InputTable = ANN.ReadANNToMODSIMtable(MatlabExpFolder & "ANN_To_MODSIM.csv")
                                                                                             // If System.IO.File.Exists("M:\Enrique\Project Arkansas\ReservoirOperation\ResQualityANNData\ExportTest.csv") Then
                                                                                             // System.IO.File.Delete("M:\Enrique\Project Arkansas\ReservoirOperation\ResQualityANNData\ExportTest.csv")
                                                                                             // End If
        }
        // Dim foundMeasured As Boolean
        private void addLinkQualityOutput(Link m_link, ref DataRow m_row)
        {
            m_row["Concentration"] = m_link.Tag.concentration;
        }
        private void addNodeQualityOutput(Node m_node, ref DataRow m_row)
        {
            if (m_node.nodeType == NodeType.Demand)
            {
                m_row["Concentration"] = m_node.Tag.INconcentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0);
                if (m_node.Tag.InflowConcentration.getSize > 0)
                {
                    m_row["Measured Concentration"] = GetMeasuredConc(m_node); // m_node.Tag.concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0)
                }
            }
            else
            {
                // implemented for pumping - TODO: does it work for regular inflows?
                m_row["Concentration"] = m_node.mnInfo.infLink.Tag.concentration;
            }
        }
        private void addReservoirQualityOutput(Node m_node, ref DataRow m_row)
        {
            m_row["Concentration"] = m_node.mnInfo.infLink.Tag.concentration;
        }
        public void OnIterationTop()
        {
            // Clean links for the this iteration.  
            // the links should be ready when calculating ANN returns.
            if (myModel.mInfo.Iteration == 0)
                NewQualityInLinks();
        }
        private DataTable BuildTableDWSStations(string DWSStation) // ByVal m_featureclass As IFeatureClass)
        {
            // Create the table
            var m_NodeStationRelation = new DataTable();
            m_NodeStationRelation.TableName = DWSStation;
            m_NodeStationRelation.Columns.Add("Node", Type.GetType("System.String"));
            m_NodeStationRelation.Columns.Add("TypeID", Type.GetType("System.Int32"));
            m_NodeStationRelation.Columns.Add("Flow", Type.GetType("System.Single"));
            m_NodeStationRelation.Columns.Add("Conc", Type.GetType("System.Single"));
            m_NodeStationRelation.Columns.Add("OutFlow", Type.GetType("System.Single"));
            m_NodeStationRelation.Columns.Add("OutConc", Type.GetType("System.Single"));
            m_NodeStationRelation.Columns.Add("UpSNode", Type.GetType("System.String"));
            return m_NodeStationRelation;
        }
        private GEODSS.ANNGeoInputs.DB_Utils m_DB;
        private DataTable m_ConcTable, m_ResConcTable;
        public void OnIterationConverge(bool useCalibConcentrations, string calibNetworkName)
        {
            bool dbgOut = false;
            finalCalib = false; // Set flag for calibration Final Iteration
            Console.WriteLine("OnIterationConverge()");
            calibSecondPass = false;
            int iterNo = 0;
        redoWQCalc:
            ;

            m_DebWin = new WQDebug();
            iterNo += 1;
            if (calibMode)
            {
                myModel.FireOnMessage(string.Concat("    WQ Calibration Iter: ", iterNo));
            }
            else
            {
                myModel.FireOnMessage("    WQ Simulation ...");
            }

            // m_NodeStationRelation.BeginLoadData()
            bool converged = true;
            int i;
            bool SimulWCalibVals = false;

            if (!calibMode & useCalibConcentrations)
            {
                SimulWCalibVals = true;
                // Initialize the modelOUTPUT class for the calibration netowork
                // Dim CalibNetName As String = Path.GetDirectoryName(myModel.fname) & "\" & Split(Path.GetFileNameWithoutExtension(myModel.fname), "Simul", 2)(0) & "CalibOUTPUT.mdb" ' Split(myModel.fname, ".", 2)(0) & "Calib.xy"
                string CalibNetName = Path.GetFileNameWithoutExtension(calibNetworkName) + "OUTPUT.mdb";
                if (myModel.mInfo.CurrentModelTimeStepIndex == 0)
                {
                    WQ_Message?.Invoke("**** WQ SIMULATION ***** " + Constants.vbCrLf + "    Using calib file: " + CalibNetName);
                    m_DB = new GEODSS.ANNGeoInputs.DB_Utils(CalibNetName);
                }
                // Get Concentration Table for current Time Step
                m_ConcTable = GetCalibrationLinksConcTable(myModel.mInfo.CurrentModelTimeStepIndex);
                m_ResConcTable = GetCalibrationResConcTable(myModel.mInfo.CurrentModelTimeStepIndex);
            }
            var loopTo = nodeCalcOrder.Count;
            for (i = 1; i <= loopTo; i++)
            {
                IEnumerator myEnumeratorUp = (IEnumerator)nodeCalcOrder[i].getenumerator;
                while (myEnumeratorUp.MoveNext())
                {
                    Link curLink;
                    Node curNode = myModel.FindNode(myEnumeratorUp.Current);
                    // Find the table for the corresponding WQ Station
                    string stationName = curNode.Tag.DWSStation;
                    if (IsWQStation(curNode))
                    {
                        stationName = curNode.name;
                        if (iterNo == 1)
                        {
                            // Set inflow=0 and useMeasConc = True
                            reachesTable[stationName].clearData();
                        }
                    }
                    DataTable reachData = null;
                    if (stationName != null)
                    {
                        reachData = (DataTable)reachesTable[stationName].m_Tbl; // CreateNodeTable(stationName)
                    }

                    bool IsResWQNodeWithANN = false;
                    if (useANNResTranp & curNode.name == "ARKJMRCO")
                    {
                        IsResWQNodeWithANN = true;
                    }

                    // Assign concentrations to the inflows (round nodes)
                    if (curNode.nodeType == NodeType.NonStorage)
                    {
                        if (curNode.Tag.InflowConcentration.getSize > 0)
                        {
                            // get inflow link
                            curLink = curNode.mnInfo.infLink;
                            if (curLink != null)
                            {
                                // get the value from the node concentration array
                                SetTagValueWithCheck(ref curLink, GetMeasuredConc(curNode));
                                // foundSource = True
                            }
                        }
                    }

                    // Check existing concentrations in flowthru return nodes.
                    curLink = curNode.mnInfo.flowThroughReturnLink;
                    if (curLink != null)
                    {
                        if (curLink.mlInfo.flow > 0)
                        {
                            if (!curLink.Tag.valueset)
                                throw new Exception("Concentration of the flow thru demand failed");
                        }
                    }

                    float inMass = 0f;
                    float inWaterVol = 0f;
                    float totalInWaterVol = 0f;
                    float ExtInWaterVol = 0f;
                    float ExtInMass = 0f;
                    float CalibOutConc = 0f;
                    float CalibOutFlow = 0f;
                    // loop through all both artificial and real links to calculate total mass in to the node.
                    LinkList m_InListLink = curNode.InflowLinks;
                    do
                    {
                        curLink = m_InListLink.link;
                        // Assign Concentration if there is a source link to the gauging station
                        if (curLink.name.EndsWith("CALIB_SOURCE"))
                        {
                            if (SimulWCalibVals)
                            {
                                // Assign calibration concentration
                                float m_prevConc = GetCalibrationConc(curLink);
                                SetTagValueWithCheck(ref curLink, m_prevConc);
                            }
                            else if (calibSecondPass)
                            {
                                if (curNode.Tag.InflowConcentration.getSize > 0)
                                {
                                    DataRow[] m_rows = m_UpSSourceTable.Select("[NodeName] = '" + curNode.name + "'");
                                    if (m_rows.Length == 1)
                                    {
                                        // Upstream source Identified
                                        // calculate node concentration to match measured concentration (when baseflow predicion upstream)
                                        SetTagValueWithCheck(ref curLink, GetMeasuredConc(curNode, true));
                                    }
                                    else
                                    {
                                        // get the value from the node concentration array
                                        SetTagValueWithCheck(ref curLink, GetMeasuredConc(curNode));
                                    }
                                }

                                else
                                {
                                    // get calculated value for calibration
                                    float m_conc = GetNewConcentration(ref reachData, curNode.name, 1);
                                    SetTagValueWithCheck(ref curLink, m_conc);
                                }
                            }
                            else if (curNode.Tag.InflowConcentration.getSize > 0)
                            {
                                if (curNode.Tag.concentrations != null)
                                {
                                    DataRow[] m_rows = m_UpSSourceTable.Select("[NodeName] = '" + curNode.name + "'");
                                    if (m_rows.Length == 1)
                                    {
                                        // Upstream source Identified
                                        // calculate node concentration to match measured concentration (when baseflow predicion upstream)
                                        SetTagValueWithCheck(ref curLink, GetMeasuredConc(curNode, true));
                                    }
                                    else
                                    {
                                        // get the value from the node concentration array
                                        SetTagValueWithCheck(ref curLink, GetMeasuredConc(curNode));
                                    }
                                }
                                else
                                {
                                    SetTagValueWithCheck(ref curLink, 0);
                                }
                            }
                            else
                            {
                                if (myModel.mInfo.CurrentModelTimeStepIndex == 0)
                                {
                                    string msg;
                                    msg = "WARNING: TS(" + myModel.mInfo.CurrentModelTimeStepIndex + @") \n Node: " + curNode.name + "(" + curNode.number + ")" + "Water source without concentration defined";
                                    myModel.FireOnMessage(msg);
                                    // Add source to the table for DWS station tracing
                                    AddRowToNodeStationTable(ref reachData, curNode.name, 1);
                                }
                                SetTagValueWithCheck(ref curLink, 0);
                                AddFlowValuesToNodeStationTable(ref reachData, curNode.name, curLink.mlInfo.flow, 1, 0);
                            }
                            // Handle calibration with out-mass
                            if (curLink.mlInfo.flow == 0 & curLink.Tag.concentration < 0)
                            {
                                Link m_SinkLink = myModel.FindLink(string.Concat(curNode.name + "_CALIB_SINK"));
                                CalibOutConc = -curLink.Tag.concentration; // Flagged concentration is negative
                                                                           // curLink.Tag.concentration = 0 ' for output purposes
                                CalibOutFlow = m_SinkLink.mlInfo.flow;
                            }
                        }
                        // Assign Measured/Calculated Concentration of the upstream gauging station to the water provided to suplement downstream
                        if (curLink.name.EndsWith("_CALIB_DS_SUPPLY"))
                        {
                            Node upSNode = myModel.FindNode(curLink.name.Split("_CALIB_DS_SUPPLY")(0));
                            if (upSNode != null)
                            {
                                float m_concentration;
                                // Get flow from the upstream link
                                // Dim upsFlow() As Single = GetUPSFlow(curNode)
                                if (SimulWCalibVals)
                                {
                                    // Assign calibration concentration
                                    SetTagValueWithCheck(ref curLink, GetCalibrationConc(curLink));
                                }
                                else if (calibSecondPass)
                                {
                                    float m_conc = GetNewConcentration(ref reachData, curNode.name, 2);
                                    SetTagValueWithCheck(ref curLink, m_conc);
                                }
                                else
                                {
                                    if (upSNode.Tag.InflowConcentration.getSize > 0)
                                    {
                                        if (curNode.Tag.concentrations != null)
                                        {
                                            float m_UpSConc = upSNode.Tag.concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0);
                                            if (m_UpSConc > 0f)
                                            {
                                                // get the value from the node concentration array
                                                m_concentration = m_UpSConc;
                                            }
                                            else
                                            {
                                                // Get calculated value
                                                m_concentration = upSNode.Tag.INconcentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0);
                                            }
                                        }
                                        else
                                        {
                                            // Get calculated value
                                            m_concentration = upSNode.Tag.INconcentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0);
                                        }
                                    }
                                    else
                                    {
                                        // Get calculated value
                                        m_concentration = upSNode.Tag.INconcentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0);
                                    }
                                    if (myModel.mInfo.CurrentModelTimeStepIndex == 0)
                                    {
                                        AddRowToNodeStationTable(ref reachData, curNode.name, 2, upSNode.name);
                                    }
                                    SetTagValueWithCheck(ref curLink, m_concentration);
                                    AddFlowValuesToNodeStationTable(ref reachData, curNode.name, curLink.mlInfo.flow, 2, m_concentration);
                                }
                            }
                        }
                        if (curNode.nodeType == NodeType.Reservoir)
                        {
                            if (curLink.mlInfo.isArtificial & object.ReferenceEquals(curLink, curNode.mnInfo.infLink))
                            {
                                if (SimulWCalibVals)
                                {
                                    // Assign calibration concentration
                                    Link m_InfLink = curNode.mnInfo.infLink;
                                    SetTagValueWithCheck(ref m_InfLink, GetCalibrationConcResNode(curNode));
                                }
                                else if (calibSecondPass)
                                {
                                    float m_conc = GetNewConcentration(ref reachData, curNode.name, 1);
                                    SetTagValueWithCheck(ref curLink, m_conc);
                                }
                                else
                                {
                                    if (myModel.mInfo.CurrentModelTimeStepIndex == 0)
                                    {
                                        string msg;
                                        msg = "WARNING: TS(" + myModel.mInfo.CurrentModelTimeStepIndex + @") \n Node: " + curNode.name + "(" + curNode.number + ")" + "Water source without concentration defined";
                                        myModel.FireOnMessage(msg);
                                        // Add source to the table for DWS station tracing
                                        AddRowToNodeStationTable(ref reachData, curNode.name, 1);
                                    }
                                    SetTagValueWithCheck(ref curLink, 0);
                                    AddFlowValuesToNodeStationTable(ref reachData, curNode.name, curLink.mlInfo.flow, 1, 0);
                                }
                            }
                        }
                        // Only links with previously calcualted concentration are to be used
                        if (curLink.Tag.valueset)
                        {
                            // accumulate total water constituent mass
                            // concentration in [mg/l]  - Flow is not converted because later is divided by the flow in the same units.
                            inMass += curLink.Tag.concentration * curLink.mlInfo.flow;
                            // Accumulate total water volume
                            inWaterVol += curLink.mlInfo.flow;
                        }
                        else
                        {
                            string myMsg;
                            if (curLink.mlInfo.flow > 0 & curLink.mlInfo.isArtificial)
                            {
                                // Notify if there is an inflow without concentration
                                myMsg = "TS(" + myModel.mInfo.CurrentModelTimeStepIndex + ")Node: " + curNode.name + "(" + curNode.number + ") Inflow with 0 concentration";
                                if (dbgOut)
                                    myModel.FireOnMessage(myMsg);
                            }
                            else if (curLink.mlInfo.flow > 0 & !curLink.mlInfo.isArtificial)
                            {
                                myMsg = "****ERROR**** ";
                                myModel.FireOnError(myMsg);
                                myMsg = "****ERROR****  Node: " + curNode.name + "(" + curNode.number + ") real link (" + curLink.name + "_" + curLink.number + ") Inflow with no-concentration calculated";
                                myModel.FireOnError(myMsg);
                            }
                            else if (curLink.mlInfo.flow > 0)
                            {
                                if (curLink.Tag.concentration > 0)
                                {
                                    myMsg = " *** Salt MASS into the reach not accounted Node: " + curNode.name + "(" + curNode.number + ") real link (" + curLink.name + "_" + curLink.number + ")";
                                }
                            }
                        }
                        // Add flows that go out the reach (control volume)
                        if (OutOfReach(reachData, curLink.from.name))
                        {
                            ExtInWaterVol += curLink.mlInfo.flow;
                            ExtInMass += curLink.Tag.concentration * curLink.mlInfo.flow;
                        }
                        totalInWaterVol += curLink.mlInfo.flow;
                        m_InListLink = m_InListLink.next;
                    }
                    while (!(m_InListLink is null));
                    // Downstream concentration calculation
                    float outConcentration = 0f;
                    float inConcentration = 0f;
                    float ExtInConcentration = 0f;
                    float meanConcentration; // = inMass / inWaterVol
                    inMass = inMass - CalibOutConc * CalibOutFlow;
                    inWaterVol = inWaterVol - CalibOutFlow;
                    totalInWaterVol = totalInWaterVol - CalibOutFlow;
                    if (inWaterVol > 0f)
                        meanConcentration = inMass / inWaterVol;
                    if (totalInWaterVol > 0f)
                        outConcentration = inMass / totalInWaterVol;
                    if (ExtInWaterVol > 0f)
                        ExtInConcentration = ExtInMass / ExtInWaterVol;
                    inConcentration = outConcentration;

                    // Add Extertnal Inflow for nodes (typeID = 0)
                    AddFlowValuesToNodeStationTable(ref reachData, curNode.name, ExtInWaterVol, 0, ExtInConcentration);

                    // Check if outconcentration is 0 but there are measured values
                    if (!calibMode & !useCalibConcentrations)
                    {
                        // Used for runs without previous calibration to observe how the measured values route salt in the system
                        if (outConcentration == 0f & curNode.Tag.InflowConcentration.getSize > 0)
                        {
                            // get the value from the node concentration array
                            outConcentration = GetMeasuredConc(curNode);
                        }
                    }
                    float measuredConc = 0f;
                    if (calibMode & (IsWQStation(curNode) & !(IsResWQNodeWithANN & curNode.name == "ARKJMRCO")))
                    {
                        measuredConc = GetMeasuredConc(curNode);
                        // If in calibration mode assign concentration for helping downstream convergence 
                        if (measuredConc > 0f)
                        {
                            if (Conversions.ToBoolean(reachesTable[stationName].useMeasConc()))
                            {
                                // Use initially measured concentrarions until first time that it converges
                                outConcentration = measuredConc;
                            }
                        }
                    }

                    // If measured concentration calibrate unknowns
                    if (IsResWQNodeWithANN)
                    {
                        // ANN for water quality transport at JMR
                        CalculateANN_MODSIMInputs();
                        outConcentration = ANN.ANNOutput(1f, "OUTPUTInRiver", 1);
                    }

                    // Set concentration to the outbound links
                    float totalOutWaterVol = 0f;
                    float totalOutMass = 0f;
                    LinkList outLinkList = curNode.OutflowLinks;
                    do
                    {
                        curLink = outLinkList.link;
                        if (!curLink.mlInfo.isArtificial)
                        {
                            // Only assign a value >0 concentration if there is flow in the link
                            if (curLink.mlInfo.flow > 0)
                            {
                                if (curLink.name.EndsWith("_CALIB_SINK") & CalibOutConc > 0f)
                                {
                                    SetTagValueWithCheck(ref curLink, CalibOutConc);
                                }
                                else
                                {
                                    SetTagValueWithCheck(ref curLink, outConcentration);
                                }
                            }
                            else if (curLink.from.m.idstrmx(0) != null & object.ReferenceEquals(curLink.from.m.idstrmx(0), curLink.to))
                            {
                                // Check if it's a flow thru
                                SetTagValueWithCheck(ref curLink, outConcentration);
                            }
                            else
                            {
                                SetTagValueWithCheck(ref curLink, 0);
                            }
                        }
                        // Add flows that go out the reach (control volume)
                        if (OutOfReach(reachData, curLink.to.name))
                        {
                            totalOutWaterVol += curLink.mlInfo.flow;
                            if (curLink.name.EndsWith("_CALIB_SINK") & CalibOutConc > 0f)
                            {
                                totalOutMass += curLink.mlInfo.flow * CalibOutConc;
                            }
                            else
                            {
                                totalOutMass += curLink.mlInfo.flow * outConcentration;
                            }
                        }
                        outLinkList = outLinkList.next;
                    }
                    while (!(outLinkList is null));
                    // Save the Concentration at the node
                    curNode.Tag.INconcentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0) = outConcentration;


                    if (calibMode & (IsWQStation(curNode) & !(IsResWQNodeWithANN & curNode.name == "ARKJMRCO")))
                    {
                        // Allows parameters in calibration equation to use the measured concentration
                        if (measuredConc > 0f)
                        {
                            outConcentration = GetMeasuredConc(curNode);
                        }
                    }
                    // Add Inflow for nodes with type id=0 in the nodes table
                    float m_CalcOutConcentration = 0f;
                    if (totalOutWaterVol != 0f)
                    {
                        m_CalcOutConcentration = totalOutMass / totalOutWaterVol;
                    }
                    else
                    {
                        m_CalcOutConcentration = outConcentration;
                    }
                    AddOUTFlowValuesToNodeStationTable(ref reachData, curNode.name, totalOutWaterVol, 0, m_CalcOutConcentration); // outConcentration)

                    // Handle FlowThrus concentrations
                    float totFlowDws;
                    if (curNode.m.idstrmx(0) != null & curNode.m.idstrmfraction(0) > 0)
                    {
                        // Holds the artificial link that returns flowthrow to the node.
                        // This link seems to be used only if the network has storage rights.
                        curLink = curNode.m.idstrmx(0).mnInfo.flowThroughReturnLink;
                        if (curLink is null)
                        {
                            curLink = curNode.m.idstrmx(0).mnInfo.infLink;
                            totFlowDws = curLink.mlInfo.flow;
                        }
                        else
                        {
                            MessageBox.Show("Check code for errors. When flowThroughReturnLink is not nothing the code is not tested.");
                        }
                        CalcFTNConc(curNode.m.idstrmx(0));
                        // SetTagValueWithCheck(curLink, outConcentration, True)
                    }

                    // WQ Auto-Calibration with unknown calibrations.
                    if (calibMode)
                    {
                        try
                        {
                            if (IsWQStation(curNode) & !(IsResWQNodeWithANN & curNode.name == "ARKJMRCO"))
                            {
                                // If Math.Abs(reachesTable(stationName).prevInConcentration - inConcentration) > 1 And measuredConc > 0 And iterNo < 50 Then
                                if (Conversions.ToBoolean(Operators.AndObject(Operators.AndObject(measuredConc > 0f & Math.Abs(outConcentration - inConcentration) > 1f, Operators.OrObject(Operators.ConditionalCompareObjectGreater(Math.Abs(Operators.SubtractObject(reachesTable[stationName].prevInConcentration, inConcentration)), 0.01d, false), iterNo == 1)), iterNo < 60)))


                                {

                                    // The criteria to continue iterating is meeting the measured (if exist), change in the concentration in less than 50 iterations
                                    reachesTable[stationName].prevInConcentration = inConcentration;
                                    if (dbgOut)
                                        myModel.FireOnMessage(string.Concat(" WQ (", curNode.name, ") Conc difference = ", Math.Abs(outConcentration - inConcentration)));
                                    converged = false;
                                    // Adjust values
                                    DataRow[] m_UPSNodes = reachData.Select("[TypeID] = 1 OR ([TypeID] = 2 AND [Conc] = 0)");
                                    object[] sumknownInMass;
                                    object[] sumOutMass;
                                    float new_Conc, max_Conc, min_Conc;
                                    bool balanced = false;
                                    int nod;
                                    sumknownInMass = CalcSumFlows(ref reachData, curNode.name, 0, true);
                                    sumOutMass = CalcSumFlows(ref reachData, curNode.name, 0, false);
                                    // ' Calcutate the flow at the station corresponding to the measured concentration.
                                    // Dim measuredFlow As Single = curNode.mnInfo.demLink.mlInfo.flow
                                    // Correct the value in the table to reflect only mass out of the reach in the last node
                                    // AddOUTFlowValuesToNodeStationTable(reachData, curNode.name, totalOutWaterVol - measuredFlow, 0, outConcentration)
                                    DataRow[] m_Noderows;
                                    if (m_UPSNodes.Length > 0)
                                    {
                                        object[] sumUnknownFlows;
                                        sumUnknownFlows = CalcSumFlows(ref reachData, curNode.name, 1, true);
                                        if (Conversions.ToBoolean(sumUnknownFlows[0]))
                                        {
                                            balanced = true;
                                            if (iterNo == 1)
                                            {
                                                new_Conc = measuredConc;
                                            }
                                            else
                                            {
                                                new_Conc = Conversions.ToSingle(Operators.DivideObject(Operators.SubtractObject(sumOutMass[2], Operators.SubtractObject(sumknownInMass[2], sumUnknownFlows[2])), sumUnknownFlows[1]));
                                            }
                                            if (dbgOut)
                                                myModel.FireOnMessage(string.Concat("   Adjust 1 - Conc: ", new_Conc));
                                            var loopTo1 = m_UPSNodes.Length - 1;
                                            for (nod = 0; nod <= loopTo1; nod++)
                                            {
                                                string m_name = Conversions.ToString(m_UPSNodes[nod]["Node"]);
                                                Node m_node = myModel.FindNode(m_name);
                                                if (m_node.Tag != null)
                                                {
                                                    max_Conc = m_node.Tag.maxBound; // m_node.Tag.concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 1)
                                                    min_Conc = m_node.Tag.minBound; // m_node.Tag.concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 2)
                                                    if (new_Conc < min_Conc)
                                                    {
                                                        new_Conc = min_Conc;
                                                        if (dbgOut)
                                                            myModel.FireOnMessage(string.Concat("                  ^^^  = ", new_Conc));
                                                        balanced = false;
                                                    }
                                                    if (new_Conc > max_Conc)
                                                    {
                                                        new_Conc = max_Conc;
                                                        if (dbgOut)
                                                            myModel.FireOnMessage(string.Concat("                  vvv  = ", new_Conc));
                                                        balanced = false;
                                                    }
                                                }
                                                m_UPSNodes[nod]["Conc"] = new_Conc;
                                                m_Noderows = reachData.Select(Conversions.ToString(Operators.ConcatenateObject(Operators.ConcatenateObject(" Node = '", m_UPSNodes[nod]["Node"]), "' AND [TypeID] = 0")));
                                                if (m_Noderows.Length == 1)
                                                {
                                                    m_Noderows[0]["Conc"] = new_Conc;
                                                }
                                                else
                                                {
                                                    throw new Exception(Conversions.ToString(Operators.ConcatenateObject(Operators.ConcatenateObject("Node:", m_UPSNodes[nod]["Node"]), " not found on TypeID = 0")));
                                                }
                                            }
                                        }
                                    }
                                    if (!balanced | iterNo > 20)
                                    {
                                        m_UPSNodes = reachData.Select("[TypeID] = 2");
                                        if (m_UPSNodes.Length > 0)
                                        {
                                            object[] sumCalibFlows;
                                            sumCalibFlows = CalcSumFlows(ref reachData, curNode.name, 2, true);
                                            if (Conversions.ToBoolean(sumCalibFlows[0])) // Check for valid entries (rows and flows >0) in table
                                            {
                                                balanced = true;
                                                if (iterNo == 1)
                                                {
                                                    new_Conc = measuredConc;
                                                }
                                                else
                                                {
                                                    new_Conc = Conversions.ToSingle(Operators.DivideObject(Operators.SubtractObject(sumOutMass[2], Operators.SubtractObject(sumknownInMass[2], sumCalibFlows[2])), sumCalibFlows[1]));
                                                }
                                                if (dbgOut)
                                                    myModel.FireOnMessage(string.Concat("   Adjust 2 - Conc: ", new_Conc));
                                                var loopTo2 = m_UPSNodes.Length - 1;
                                                for (nod = 0; nod <= loopTo2; nod++)
                                                {
                                                    string m_name = Conversions.ToString(m_UPSNodes[nod]["UpSNode"]);
                                                    Node m_node = myModel.FindNode(m_name);
                                                    if (m_node.Tag != null)
                                                    {
                                                        max_Conc = m_node.Tag.maxBound; // m_node.Tag.concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 1)
                                                        min_Conc = m_node.Tag.minBound; // m_node.Tag.concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 2)
                                                        if (new_Conc < min_Conc)
                                                        {
                                                            new_Conc = min_Conc;
                                                            if (dbgOut)
                                                                myModel.FireOnMessage(string.Concat("                  ^^^  = ", new_Conc));
                                                            balanced = false;
                                                        }
                                                        if (new_Conc > max_Conc)
                                                        {
                                                            new_Conc = max_Conc;
                                                            if (dbgOut)
                                                                myModel.FireOnMessage(string.Concat("                  vvv  = ", new_Conc));
                                                            balanced = false;
                                                        }
                                                    }
                                                    m_UPSNodes[nod]["Conc"] = new_Conc;
                                                }
                                            }
                                            else if (iterNo == 1)
                                            {
                                                if (dbgOut)
                                                    myModel.FireOnMessage(string.Concat("   Station: ", stationName, " cannot be calibrated in time step: ", myModel.mInfo.CurrentModelTimeStepIndex));
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    reachesTable[stationName].useMeasConc = false;
                                }
                                m_DebWin.addTable(reachData);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message);
                        }
                    }
                }
            }
            // If iterNo > 19 Then m_DebWin.ShowDialog()
            // m_DebWin.ShowDialog()
            // m_NodeStationRelation.EndLoadData()
            if (calibMode & !converged)
            {
            lastIteration:
                ;

                calibSecondPass = true;
                NewQualityInLinks("ANN_RETURN_TO");
                goto redoWQCalc;
            }
            // if calibration converged or in simulation mode
            else if (calibMode & !finalCalib)
            {
                // Perform the last simulation to get the final concentration to the output
                // The last stations to converge don't have that latest concentration.
                finalCalib = true;
                goto lastIteration;
            }
            if (useANNResTranp)
            {
                AddTestANNValues();
            }
        }
        private float GetCalibrationConc(Link curLink)
        {
            // Dim m_query As String = "SELECT LinksOutput.Concentration, LinksOutput.TSIndex, LinksInfo.LName"
            // m_query += " FROM LinksOutput INNER JOIN LinksInfo ON LinksOutput.LNumber = LinksInfo.LNumber"
            // m_query += " WHERE (((LinksOutput.TSIndex)=" & myModel.mInfo.CurrentModelTimeStepIndex & ") AND ((LinksInfo.LName)='" & curLink.name & "'));"
            // Dim m_ConcTable As DataTable = m_DB.GetTableFromDB(m_query, "Concentration")
            DataRow[] m_rows = m_ConcTable.Select("LName='" + curLink.name + "'");
            if (m_rows.Length == 1)
            {
                return Conversions.ToSingle(m_rows[0]["Concentration"]);
            }
            else
            {
                myModel.FireOnError(string.Concat(" Link Calibration Concentration for Node:", curLink.to.name, " not found."));
            }

            return default;
        }
        private DataTable GetCalibrationLinksConcTable(int m_TS)
        {
            string m_query;
            m_query = "SELECT LinksOutput.Concentration, LinksInfo.LName";
            m_query += " FROM LinksOutput INNER JOIN LinksInfo ON LinksOutput.LNumber = LinksInfo.LNumber";
            m_query += " WHERE (((LinksOutput.TSIndex)=" + m_TS + "));";
            return m_DB.GetTableFromDB(m_query, "Concentrations");
        }
        private float GetCalibrationConcResNode(Node curNode)
        {
            // Dim m_query As String = "SELECT RESOutput.Concentration, RESOutput.TSIndex, NodesInfo.NName"
            // m_query += " FROM RESOutput INNER JOIN NodesInfo ON RESOutput.NNo = NodesInfo.NNumber"
            // m_query += " WHERE (((RESOutput.TSIndex)=" & myModel.mInfo.CurrentModelTimeStepIndex & ") AND ((NodesInfo.NName)='" & curNode.name & "'));"
            // Dim m_ConcTable As DataTable = m_DB.GetTableFromDB(m_query, "Concentration")
            DataRow[] m_rows = m_ResConcTable.Select("NName='" + curNode.name + "'");
            if (m_rows.Length == 1)
            {
                return Conversions.ToSingle(m_rows[0]["Concentration"]);
            }
            else
            {
                myModel.FireOnError(string.Concat(" Concentration for Node:", curNode, " not found."));
            }

            return default;
        }
        private DataTable GetCalibrationResConcTable(int m_TS)
        {
            string m_query;
            m_query = "SELECT RESOutput.Concentration, RESOutput.TSIndex, NodesInfo.NName";
            m_query += " FROM RESOutput INNER JOIN NodesInfo ON RESOutput.NNo = NodesInfo.NNumber";
            m_query += " WHERE (((RESOutput.TSIndex)=" + m_TS + "));";
            return m_DB.GetTableFromDB(m_query, "ResConcentrations");
        }
        private float GetMeasuredConc(Node curNode, bool upsSource = false)
        {
            float m_conc;
            if (curNode.Tag.useQCRelation)
            {
                // If selected use the fitted curve to calculate concentration
                float c_FLow = GetCurrentInflows(curNode);
                if (c_FLow > 0f)
                {
                    m_conc = curNode.Tag.m_CurveFitting.GetCurveYValue(c_FLow, curNode.Tag.minBound, curNode.Tag.maxBound);
                }
                else
                {
                    // aviods using a concentration value when no flow (curve intercept) - large errors in analysis
                    m_conc = 0f;
                }
            }
            else
            {
                m_conc = curNode.Tag.concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0);
            }
            if (upsSource)
            {
                // check for mass in
                LinkList m_InListLink = curNode.InflowLinks;
                float inMass = 0f;
                float outFlow = 0f; // curNode.mnInfo.nodedemand(myModel.mInfo.CurrentModelTimeStepIndex, 0)
                float sourceFlow = 0f;
                do
                {
                    Link curLink = m_InListLink.link;
                    if (curLink.name.EndsWith("CALIB_SOURCE"))
                    {
                        sourceFlow = curLink.mlInfo.flow;
                    }
                    else if (curLink.Tag.valueset)
                    {
                        // accumulate total water constituent mass
                        // concentration in [mg/l]  - Flow is not converted because later is divided by the flow in the same units.
                        inMass += curLink.Tag.concentration * curLink.mlInfo.flow;
                    }
                    outFlow += curLink.mlInfo.flow; // Equals volume water leaving
                    m_InListLink = m_InListLink.next;
                }
                while (!(m_InListLink is null));
                if (sourceFlow != 0f)
                {
                    m_conc = (outFlow * m_conc - inMass) / sourceFlow;
                }
                else
                {
                    Link m_SinkLink = myModel.FindLink(string.Concat(curNode.name + "_CALIB_SINK"));
                    if (m_SinkLink.mlInfo.flow > 0)
                    {
                        // Set concentration negative -- flag for pre-wired massout 
                        m_conc = -((inMass - (outFlow - m_SinkLink.mlInfo.flow) * m_conc) / m_SinkLink.mlInfo.flow);
                    }
                    else
                    {
                        m_conc = 0f;
                    }
                }
            }
            return m_conc;
        }
        private float GetCurrentInflows(Node curNode)
        {
            LinkList m_list = curNode.InflowLinks;
            float m_flow = 0f;
            do
            {
                Link m_link = m_list.link;
                // If Not m_link.mlInfo.isArtificial Then
                // Artificial links might bring flow thru flow
                m_flow += m_link.mlInfo.flow;
                // End If
                m_list = m_list.next;
            }
            while (!(m_list is null));
            m_list = curNode.OutflowLinks;
            do
            {
                Link m_link = m_list.link;
                if (m_link.name == curNode.name + "_CALIB_SINK")
                {
                    // substract water to sink because it's not part of the measured flow that moves downstream
                    m_flow -= m_link.mlInfo.flow;
                }
                m_list = m_list.next;
            }
            while (!(m_list is null));
            return m_flow;
        }
        private float[] GetUPSFlow(Node curnode)
        {
            var m_Val = new float[3];
            m_Val[0] = 0f;
            m_Val[1] = 0f;
            m_Val[0] += curnode.mnInfo.infLink.mlInfo.flow;
            m_Val[1] += curnode.mnInfo.infLink.mlInfo.flow * curnode.mnInfo.infLink.Tag.Concentration;
            LinkList m_list = curnode.InflowLinks;
            while (m_list != null)
            {
                if (!m_list.link.mlInfo.isArtificial)
                {
                    m_Val[0] += m_list.link.mlInfo.flow;
                    m_Val[1] += m_list.link.mlInfo.flow * m_list.link.Tag.Concentration;
                }
                m_list = m_list.next;
            }
            m_Val[1] /= m_Val[0];
            return m_Val;
        }
        private bool OutOfReach(DataTable reachTable, string nodeName)
        {
            if (reachTable != null)
            {
                if (reachTable.Rows.Count > 0)
                {
                    DataRow[] m_rows = reachTable.Select("[node] = '" + nodeName + "'");
                    if (m_rows.Length > 0)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        private void CalcFTNConc(Node FTN)
        {
            int ts = myModel.mInfo.CurrentModelTimeStepIndex;
            var sumConc = default(float);
            if (FTN.mnInfo.infLink.mlInfo.flow)
            {
                if (FTN.Tag.InflowConcentration.getSize > 0 & FTN.mnInfo.inflow.Length > 0)
                {
                    sumConc = FTN.mnInfo.inflow(ts, 0) * FTN.Tag.Concentrations(ts, 0);
                    FTN.Tag.FTDAdjusted = true;
                }
                foreach (Node mNode in FTN.Tag.myFTDNodes)
                    sumConc += mNode.Tag.INConcentrations(ts, 0) * mNode.mnInfo.ithruNF * mNode.m.idstrmfraction(0);
                sumConc /= FTN.mnInfo.infLink.mlInfo.flow;
            }
            // Add concentration and overwrite previous concentration value if it exists.
            FTN.mnInfo.infLink.Tag.setvalue(sumConc, QualityLinkData.combineType.Replace);
        }
        private void CalculateANN_MODSIMInputs()
        {
            short i, j;
            j = 1;
            int noDays;
            // Dim interv As TimeSpan = myModel.TimeStepManager.Index2EndDate(myModel.mInfo.CurrentModelTimeStepIndex, TypeIndexes.ModelIndex).Subtract(myModel.TimeStepManager.Index2Date(myModel.mInfo.CurrentModelTimeStepIndex, TypeIndexes.ModelIndex))
            // noDays = interv.TotalDays
            if (myModel.mInfo.Iteration == 4)
            {
                myModel.FireOnMessage("        Calculating Inputs for ANN WQ Tranport in John Martin Reservoir ...");
            }
            // For Each j In noGWRetRegionsColl
            // Filter row for area constant variables 
            // Dim FilterStr As String = "RetHydroID = " & j
            // Dim ANNInputsRow_Const() As DataRow = InputTable_CONST.Select(FilterStr)
            float[] m_DiverValues;
            m_DiverValues = null;
            var RechArrayIndex = new Collection();
            var aveDiversion = new Collection();
            var IrrgAreaArrayIndex = new Collection();
            var loopTo = (short)(ANN.NoInputs - 1);
            for (i = 0; i <= loopTo; i++)
            {
                float value = 0f;
                object m_name = ANN.InputNames[i].Split('_')[0];
                // If ANN.IsMODSIMCalcVar(ANN.InputNames(i)) Then
                if (!ANN.IsPrevTSInput(ANN.InputNames[i]) & !(ANN.get_OutputIndex(Strings.Split(ANN.InputNames[i], "TS")[0]) >= 0))
                {
                    // This case calculates variables that change based on MODSIM solution
                    switch (m_name)
                    {
                        case "PURLASCOSurfIn":
                            {
                                Node curnode = myModel.FindNode("PURLASCO");
                                value = GetDWSFlow(curnode);
                                break;
                            }
                        case "PURLASCOConc":
                            {
                                Node curnode = myModel.FindNode("PURLASCO");
                                if (useMeasConcentration)
                                {
                                    // value = curnode.Tag.concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0)
                                    value = GetMeasuredConc(curnode);
                                }
                                else
                                {
                                    value = curnode.Tag.INconcentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0);
                                }

                                break;
                            }
                        case "ARKLASCOSurfIn":
                            {
                                Node curnode = myModel.FindNode("ARKLASCO");
                                value = GetDWSFlow(curnode);
                                break;
                            }
                        case "ARKLASCOConc":
                            {
                                Node curnode = myModel.FindNode("ARKLASCO");
                                if (useMeasConcentration)
                                {
                                    // value = curnode.Tag.concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0)
                                    value = GetMeasuredConc(curnode);
                                }
                                else
                                {
                                    value = curnode.Tag.INconcentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0);
                                }

                                break;
                            }
                        case "ARKJMRCOSurfIn":
                            {
                                Node curnode = myModel.FindNode("ARKJMRCO");
                                value = GetDWSFlow(curnode);
                                break;
                            }
                        case "StorEnd":
                            {
                                Node curnode = myModel.FindNode("John_Martin_Res");
                                value = curnode.mnInfo.stend;
                                break;
                            }
                        case "StorBeg":
                            {
                                Node curnode = myModel.FindNode("John_Martin_Res");
                                // If myModel.mInfo.CurrentModelTimeStepIndex = 0 Then
                                // value = curnode.mnInfo.start
                                // Else
                                // value = curnode.mnInfo.stend0
                                // End If
                                value = curnode.mnInfo.start;
                                break;
                            }

                        default:
                            {
                                break;
                            }
                    }
                    ANN.ANNCalcInputs[j, i] = value;
                }
            }
            var loopTo1 = (short)(ANN.NoInputs - 1);
            for (i = 0; i <= loopTo1; i++)
            {
                float value = 0f;
                object m_name = ANN.InputNames[i].Split('_')[0];
                // If ANN.IsMODSIMCalcVar(ANN.InputNames(i)) Then
                if (ANN.IsPrevTSInput(ANN.InputNames[i]))
                {
                    ANN.ANNCalcInputs[j, i] = ANN.GetPrevANNIOs(j, i, myModel.mInfo.CurrentModelTimeStepIndex, false);
                }
                else if (ANN.get_OutputIndex(Strings.Split(ANN.InputNames[i], "TS")[0]) >= 0)
                {
                    // This case checks for output variables from previous time steps
                    ANN.ANNCalcInputs[j, i] = ANN.GetPrevANNIOs(j, i, myModel.mInfo.CurrentModelTimeStepIndex, true);
                }
            }
            ANN.preScaleCalcInputs();
            // CompareTrainingDinamicInputs(mi.mInfo.Iteration)
            ANN.CalcALLRegionsANNOutputs(myModel.mInfo.CurrentModelTimeStepIndex, debugON);
        }
        private int GetDWSFlow(Node curnode)
        {
            int value = 0;
            var m_byPass = default(Link);
            bool bypassDone = false;
            if (curnode.m.pdstrm != null)
            {
                m_byPass = curnode.m.pdstrm;
            }
            else
            {
                bypassDone = true;
            }
            LinkList m_Out = curnode.OutflowLinks;
            while (m_Out != null)
            {
                value += m_Out.link.mlInfo.flow;
                if (object.ReferenceEquals(m_byPass, m_Out.link))
                {
                    bypassDone = true;
                }
                m_Out = m_Out.next;
            }
            if (!bypassDone)
            {
                value += curnode.m.pdstrm.mlInfo.flow;
            }
            return value;
        }

        private void AddTestANNValues()
        {
            StreamWriter wtest;
            int i;
            var m_str = default(string);
            string fileName = ANN.GetMatlabExpFolderwName() + "GEODSSANNInputs.csv"; // "M:\Enrique\Project Arkansas\ReservoirOperation\ResQualityANNData\ExportTest.csv"
            if (myModel.mInfo.CurrentModelTimeStepIndex == 0)
            {
                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }
            }
            if (!File.Exists(fileName))
            {
                wtest = new StreamWriter(fileName);
                var loopTo = ANN.NoInputs - 1;
                for (i = 0; i <= loopTo; i++)
                {
                    if (i > 0)
                    {
                        m_str += ",";
                    }
                    m_str += ANN.InputNames[i].ToString();
                }
                var loopTo1 = ANN.NoOutputs;
                for (i = 1; i <= loopTo1; i++)
                {
                    m_str += ",";
                    m_str += ANN.OutputNames[i].ToString();
                }
                wtest.WriteLine(m_str);
                wtest.Close();
            }
            wtest = new StreamWriter(fileName, true);
            var loopTo2 = ANN.NoInputs - 1;
            for (i = 0; i <= loopTo2; i++)
            {
                if (i > 0)
                {
                    m_str += ",";
                    m_str += ANN.ANNCalcInputs[1, i].ToString();
                }
                else
                {
                    m_str = ANN.ANNCalcInputs[1, i].ToString();
                }
            }
            var loopTo3 = ANN.NoOutputs;
            for (i = 1; i <= loopTo3; i++)
            {
                m_str += ",";
                m_str += ANN.ANNOutput(1f, ANN.OutputNames[i].ToString(), 1).ToString();
            }
            wtest.WriteLine(m_str);
            wtest.Close();
        }
        private object[] CalcSumFlows(ref DataTable m_NodeStationRelation, string stationName, int typeID, bool inflows)
        {
            string colFLow = "flow";
            string colConc = "Conc";
            if (!inflows)
            {
                colFLow = "OutFlow";
                colConc = "OutConc";
            }
            var sums = new object[4];
            DataRow[] m_UPSNodes = m_NodeStationRelation.Select("[TypeID] = " + typeID + " AND ([" + colFLow + "] is not null AND [" + colConc + "] is not null)");
            int nod;
            sums[0] = false;
            sums[1] = new float();
            sums[2] = new float();
            sums[3] = new float();
            if (m_UPSNodes.Length > 0)
            {
                var loopTo = m_UPSNodes.Length - 1;
                for (nod = 0; nod <= loopTo; nod++)
                {
                    sums[1] += Conversions.ToSingle(m_UPSNodes[nod][colFLow]);
                    sums[2] += Conversions.ToSingle(Operators.MultiplyObject(m_UPSNodes[nod][colFLow], m_UPSNodes[nod][colConc]));
                    // Previous Concentration
                    sums[3] = Conversions.ToSingle(m_UPSNodes[nod][colConc]);
                }
                if (Conversions.ToBoolean(Operators.ConditionalCompareObjectGreater(sums[1], 0, false)))
                    sums[0] = true;
            }
            return sums;
        }
        private void AddFlowValuesToNodeStationTable(ref DataTable m_NodeStationRelation, string nodename, float inflow, int typeid, float concentration)
        {
            if (m_NodeStationRelation != null)
            {
                DataRow[] m_nodeRow = m_NodeStationRelation.Select("Node ='" + nodename + "' AND [TypeID] = " + typeid);
                if (m_nodeRow.Length == 1)
                {
                    m_nodeRow[0]["Flow"] = inflow;
                    m_nodeRow[0]["Conc"] = concentration;
                }
                // TODO: Replace for the appropiate message
                else if (typeid != 3 & typeid != 0)
                {
                    throw new Exception("Node: " + nodename + " cannot be found in the node-Station table (TypeID:" + typeid + ")");
                }
            }
        }
        private void AddOUTFlowValuesToNodeStationTable(ref DataTable m_NodeStationRelation, string nodename, float outflow, int typeid, float concentration)
        {
            if (m_NodeStationRelation != null)
            {
                m_NodeStationRelation.AcceptChanges();
                DataRow[] m_nodeRow = m_NodeStationRelation.Select("[Node] ='" + nodename + "' AND [TypeID] = " + typeid);
                if (m_nodeRow.Length == 1)
                {
                    m_nodeRow[0]["OutFlow"] = outflow;
                    m_nodeRow[0]["OutConc"] = concentration;
                }
            }
        }
        private float GetNewConcentration(ref DataTable m_NodeStationRelation, string nodename, int typeid)
        {
            float m_conc = 0f;
            if (m_NodeStationRelation != null)
            {
                DataRow[] m_nodeRow = m_NodeStationRelation.Select("[Node] ='" + nodename + "' AND [TypeID] = " + typeid);
                if (m_nodeRow.Length == 1)
                {
                    if (Conversions.ToBoolean(Operators.ConditionalCompareObjectGreater(m_nodeRow[0]["Flow"], 0, false)))
                    {
                        m_conc = Conversions.ToSingle(m_nodeRow[0]["Conc"]);
                    }
                }
            }
            return m_conc;
        }

        // Private Sub AddFlowValuesToID3NodeStationTable(ByVal nodename As String, ByVal inflow As Single, ByVal concentration As Single)
        // Dim m_nodeRow() As DataRow = m_NodeStationRelation.Select("Node ='" & nodename & "' AND [TypeID] = 3")
        // If m_nodeRow.Length = 1 Then
        // 'The value added to the table is actually mass in the case of nodes with typeid =3
        // m_nodeRow(0)("Flow") = inflow * concentration
        // End If
        // End Sub
        private void SetTagValueWithCheck(ref Link curLink, float m_concentration) // , Optional ByVal allowCombine As Boolean = False)
        {
            if (curLink.Tag.valueset)
            {
                if (curLink.Tag.concentration == m_concentration)
                {
                    // inconsistency doesn't cause problems in calculation
                    return;
                }
                else if (curLink.Tag.concentration == 0)
                {
                    curLink.Tag.setvalue(m_concentration, QualityLinkData.combineType.Replace);
                    return;
                }
                if (!curLink.to.Tag.FTDAdjusted)
                {
                    string msg = "ERROR - Link " + curLink.name + "(" + curLink.number + ") to node " + curLink.to.name + " already has concentration defined in this time step" + '\r' + "New concentration (" + m_concentration + ") will be averaged with previous one (" + curLink.Tag.concentration + ")";
                    myModel.FireOnError(msg);
                    // If Not allowCombine Then 
                    throw new Exception("Concentration combination not allowed in this link." + Constants.vbCrLf + msg);
                }
                else
                {
                    return;
                }
            }
            curLink.Tag.setvalue(m_concentration);
        }

        public void OnFinished()
        {
            this.m_OutputSupport.AddCurrentUserLinkOutput -= addLinkQualityOutput;
            this.m_OutputSupport.AddCurrentUserDemandOutput -= addNodeQualityOutput;
            this.m_OutputSupport.AddCurrentUserReservoirOutput -= addNodeQualityOutput;
        }
        private float[] InLinksAnalysis(Node curNode)
        {
            var m_CalibrationLink = default(Link);
            bool calibLinkExists = false;
            float sumConcentration = 0f;
            int linksCount = 0;
            LinkList m_InLinks = curNode.InflowLinks;
            while (m_InLinks != null)
            {
                Link thisLink = m_InLinks.link;
                if (thisLink.name.EndsWith("CALIB_DS_SUPPLY") | m_InLinks.link.name.EndsWith("CALIB_SOURCE"))
                {
                    calibLinkExists = true;
                    m_CalibrationLink = thisLink;
                }
                if (thisLink.Tag != null)
                {
                    linksCount += 1;
                    sumConcentration += thisLink.Tag.concentration;
                }
                m_InLinks = m_InLinks.next;
            }
            if (calibLinkExists)
            {
                SetTagValueWithCheck(ref m_CalibrationLink, sumConcentration / linksCount);
                // m_CalibrationLink.Tag.setvalue(sumConcentration / linksCount)
            }

            return default;
        }
        // Creates a new Qualitylinkdata class in all links in the network
        private void NewQualityInLinks(string excludePrefix = null)
        {
            Link cur_Link;
            cur_Link = myModel.firstLink;
            do
            {
                bool skiplink = false;
                if (cur_Link.name != null & excludePrefix != null)
                {
                    if (cur_Link.name.StartsWith(excludePrefix))
                    {
                        skiplink = true;
                    }
                }
                if (!skiplink)
                    cur_Link.Tag = new QualityLinkData();
                cur_Link = cur_Link.next;
            }
            while (!(cur_Link is null));
        }

        // Creates a new Qualitylinkdata class in all links in the network
        private void NewQualityInNodes()
        {
            // New qualityNodeData for all nodes - for avilablilty in the GUI
            Node cur_Node = myModel.firstNode;
            do
            {
                // Dim TableName As String = cur_Node.name & "_CONCENTRATION"
                cur_Node.Tag = new QualityNodeData();
                cur_Node = cur_Node.next;
            }
            while (!(cur_Node is null));
            MODSIMTimeSeriesIO.ErrorMessage += WaterQualityModule.WQErrorMessage;
            MODSIMTimeSeriesIO.Message += TS_Message;
            var m_TSIO = new MODSIMTimeSeriesIO(ref myModel);
            m_TSIO.LoadDataFromDatabase();
            CurveFittingUtils.LoadGlobalTable(ref myModel, m_TSIO.GetCurveFittingData());
        }
        private void TS_Message(string msg)
        {
            WQ_Message?.Invoke(msg);
        }
        private static Node PopNode(ref Collection todoDwS)
        {
            Node PopNodeRet = default;
            PopNodeRet = todoDwS[1];
            todoDwS.Remove(1);
            return PopNodeRet;
        }
        private bool IsWQStation(Node stNode)
        {
            if (stNode.Tag.InflowConcentration.getSize > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        private bool IsGaugeNode(Node stNode)
        {
            IQueryFilter m_Query = new QueryFilter();
            m_Query.WhereClause = "[MOD_Name] = '" + stNode.name + "'";
            IFeatureCursor pFeatCursorWS = m_FeatureClass.Search(m_Query, false);
            IFeature pFeatureWS = pFeatCursorWS.NextFeature;
            if (pFeatureWS != null)
            {
                return true;
            }
            return false;
        }
        private void MarkReachNetworkUpStream(ref Model mi, Node startingNode, ref DataTable nodeTable)
        {
            var Sink = startingNode;
            Collection toDo;
            toDo = new Collection();
            Node curNode, curDown;
            toDo.Add(Sink);
            while (toDo.Count > 0)
            {
                curNode = PopNode(ref toDo);
                LinkList allLinkList = curNode.InflowLinks;
                AddRowToNodeStationTable(ref nodeTable, startingNode.name);
                while (allLinkList != null)
                {
                    Link curLink = allLinkList.link;
                    if (!curLink.mlInfo.isArtificial & curLink.from.name != "CALIB_SOURCE" & curLink.from.name != "ANN_RETURN_SOURCE")  // change "CALIB_SOURCE" to include ANN source 
                    {
                        if (curLink.from.nodeType == NodeType.Demand) // Tracing should stop upstream at demands if ther are not stations.
                        {
                            if (IsGaugeNode(curLink.from))
                            {
                                // If the node is a demand but it's a gauge check if is a WQ station to continue tracing upstream
                                if (!IsWQStation(curLink.from))
                                {
                                    toDo.Add(curLink.from, default, 1);
                                    AddRowToNodeStationTable(ref nodeTable, curLink.from.name);
                                    curLink.from.Tag.DWSStation = startingNode.name;
                                }
                                else
                                {
                                    curLink.from.Tag.DWSStation = startingNode.name;
                                }
                            }
                            else
                            {
                                // If it not gauge it's a regular demand and the upstream tracing should stop.
                                curLink.from.Tag.DWSStation = startingNode.name;
                            }
                        }
                        else
                        {
                            toDo.Add(curLink.from, default, 1);
                            AddRowToNodeStationTable(ref nodeTable, curLink.from.name);
                            curLink.from.Tag.DWSStation = startingNode.name;
                        }
                    }
                    allLinkList = allLinkList.next;
                }
            }
        }
        // Private Sub CreateDebugTable(ByRef m_dbWin As WQDebug, ByVal station As String)
        // Dim m_rows() As DataRow = m_NodeStationRelation.Select("[DWSStation] = '" & station & "'")
        // Dim m_NewTable As DataTable = m_NodeStationRelation.Clone
        // Dim i As Integer
        // For i = 0 To m_rows.Length - 1
        // Dim m_row As DataRow = m_NewTable.NewRow
        // m_row.ItemArray = m_rows(i).ItemArray
        // m_NewTable.Rows.Add(m_row)
        // Next
        // m_NewTable.TableName = station
        // m_dbWin.addTable(m_NewTable)
        // End Sub
    }

    static class WaterQualityModule
    {

        public static void WQErrorMessage(Exception ex)
        {
            MessageBox.Show(null, ex.Message + " " + Conversions.ToString(ControlChars.Lf) + Conversions.ToString(ControlChars.Lf) + ex.ToString(), "Error in Time Series");
        }

        private static void ShowQualityResults()
        {
            // Dim cur_Link As Link
            // cur_Link = myModel.firstLink
            // Console.WriteLine("Time Step: " & myModel.mInfo.CurrentModelTimeStepIndex)
            // Do
            // If Not cur_Link.mlInfo.isArtificial Then
            // Console.WriteLine("Link:    " & cur_Link.name & "  Flow:   " & cur_Link.mlInfo.flow & "    Conc:   " & cur_Link.Tag.concentration)
            // End If
            // cur_Link = cur_Link.next
            // Loop Until cur_Link Is Nothing
            // Stop
        }


    }
}
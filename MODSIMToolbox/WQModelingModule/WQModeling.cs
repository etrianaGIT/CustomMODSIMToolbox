using Csu.Modsim.ModsimModel;
using Csu.Modsim.NetworkUtils;
using Microsoft.VisualBasic;
using RTI.CWR.MODSIMModeling.MODSIMUtils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTI.CWR.MODSIM.WQModelingModule
{
    public class WQModeling
    {
        public Model myModel;
        private bool initialized;
        private object calibMode;
        private Hashtable reachesTable;
        private WQDebug m_DebWin;
        private bool finalCalib;
        private bool calibSecondPass;
        private DataTable m_UpSSourceTable;
        private ModelOutputSupport m_OutputSupport;
        private ModelOutputSupport _m_OutputSupport;

        public delegate void WQ_MessageEventHandler(string msg);
        public event WQ_MessageEventHandler WQ_Message;

        public WQModeling(ref Model m_Model)
		{
			m_Model.Init += OnInitialize;
			m_Model.IterBottom += OnIterationBottom;
			m_Model.IterTop += OnIterationTop;
			m_Model.Converged += OnIterationConverge;
			m_Model.End += OnFinished;

			myModel = m_Model;

			// Assign a new WQuality support class to links and nodes
			NewQualityInLinks();
			NewQualityInNodes();
			initialized = false;
			this.calibMode = false;
			//useANNResTranp = ReservoirANNTrnsp;
			//useMeasConcentration = useMeasConc;
		}

		private void OnInitialize(string m_DataModelDB)
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
                //float[,] m_array = new float[myModel.TimeStepManager.noModelTimeSteps + 1, 1];
                ((QualityNodeData)cur_Node.Tag).INconcentrations = new float[myModel.TimeStepManager.noModelTimeSteps + 1, 1];
                if (((QualityNodeData)cur_Node.Tag).InflowConcentration.dataTable.Rows.Count > 0)
                {
                    //var m_array2 = new float[myModel.TimeStepManager.noModelTimeSteps + 1, 2];
                    ((QualityNodeData)cur_Node.Tag).concentrations = new float[myModel.TimeStepManager.noModelTimeSteps + 1, 2];
                    myModel.LoadTimeSeriesArray(((QualityNodeData)cur_Node.Tag).InflowConcentration, ref ((QualityNodeData)cur_Node.Tag).concentrations);
                }
                else
                {
                    // cur_Node.Tag = Nothing
                    object a = ((QualityNodeData)cur_Node.Tag).InflowConcentration.dataTable.Rows.Count;
                }
                // Add FTD nodes to FTNodes (Demands that flow to this node)
                if (cur_Node.m.idstrmx[0] != null & cur_Node.m.idstrmfraction[0] > 0)
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
                if (cur_Node.m.idstrmx[0] != null & cur_Node.m.idstrmfraction[0] > 0)
                {
                    int myN;
                    var loopTo = cur_Node.m.idstrmx.Length - 1;
                    for (myN = 0; myN <= loopTo; myN++)
                    {
                        if (cur_Node.m.idstrmx[myN] != null & cur_Node.m.idstrmfraction[myN] > 0)
                        {
                            if (myN > 0)
                            {
                                throw new Exception("More than one flow thru return node is not implemented in Water Quality Modeling" + Constants.vbCrLf + "Node: " + cur_Node.name);
                                // Requires changes in the concentration calculation for the FTN
                            }
                            Node FTN = myModel.FindNode(cur_Node.m.idstrmx[myN].name);
                            ((QualityNodeData)FTN.Tag).myFTDNodes.Add(cur_Node);
                        }
                    }
                }
                cur_Node = cur_Node.next;
            }
            while (!(cur_Node is null));

            // Get UpStream source info
            var m_Db = new MyDBSqlite(m_DataModelDB);
            string m_sql = "SELECT MODSIM_SYNC_Network_NODE.NodeName, MODSIM_SYNC_Network_NODE.Calib_Struct";
            m_sql += " FROM(MODSIM_SYNC_Network_NODE)";
            m_sql += " WHERE (((MODSIM_SYNC_Network_NODE.Calib_Struct) Like '%-YES'));";
            // Get upstream source nodes list
            m_UpSSourceTable = m_Db.GetTableFromDB(m_sql, "UpsTbl");

            // initialize the output variable
            m_OutputSupport = (ModelOutputSupport) myModel.OutputSupportClass;
            m_OutputSupport.AddUserDefinedOutputVariable(myModel, "Concentration", true, false, "Concentration [mg/L]");
            m_OutputSupport.AddUserDefinedOutputVariable(myModel, "Measured Concentration", false, true, "Concentration [mg/L]");
            m_OutputSupport.AddUserDefinedOutputVariable(myModel, "Concentration", false, true, "Concentration [mg/L]");
            m_OutputSupport.AddCurrentUserLinkOutput += addLinkQualityOutput;
            this.m_OutputSupport.AddCurrentUserDemandOutput += addNodeQualityOutput;
            this.m_OutputSupport.AddCurrentUserNonStorageOutput += addNodeQualityOutput;
            this.m_OutputSupport.AddCurrentUserReservoirOutput += addReservoirQualityOutput; // addNodeQualityOutput
                                                                                             // AddHandler m_OutputSupport.AddCurrentUserReservoir_STOROutput, AddressOf addReservoirQualityOutput
                                                                                             // InputTable = ANN.ReadANNToMODSIMtable(MatlabExpFolder & "ANN_To_MODSIM.csv")
                                                                                             // If System.IO.File.Exists("M:\Enrique\Project Arkansas\ReservoirOperation\ResQualityANNData\ExportTest.csv") Then
                                                                                             // System.IO.File.Delete("M:\Enrique\Project Arkansas\ReservoirOperation\ResQualityANNData\ExportTest.csv")
                                                                                             // End If

        }

        private void OnIterationTop()
		{
            // Clean links for the this iteration.  
            // the links should be ready when calculating ANN returns.
            if (myModel.mInfo.Iteration == 0)
                NewQualityInLinks();
        }

		private void OnIterationBottom()
		{
		}

		private void OnIterationConverge()
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
                    m_DB = new MyDBSqlite(CalibNetName);
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
                    string stationName = ((QualityNodeData)curNode.Tag).DWSStation;
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
                        if (((QualityNodeData)curNode.Tag).InflowConcentration.getSize > 0)
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
                                if (((QualityNodeData)curNode.Tag).InflowConcentration.getSize > 0)
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
                            else if (((QualityNodeData)curNode.Tag).InflowConcentration.getSize > 0)
                            {
                                if (((QualityNodeData)curNode.Tag).concentrations != null)
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
                                        if (((QualityNodeData)curNode.Tag).concentrations != null)
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
                        if (outConcentration == 0f & ((QualityNodeData)curNode.Tag).InflowConcentration.getSize > 0)
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
                            else if (curLink.from.m.idstrmx[0] != null & object.ReferenceEquals(curLink.from.m.idstrmx[0], curLink.to))
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
                    ((QualityNodeData)curNode.Tag).INconcentrations[myModel.mInfo.CurrentModelTimeStepIndex, 0] = outConcentration;


                    if (calibMode & (IsWQStation(curNode) && !(IsResWQNodeWithANN & curNode.name == "ARKJMRCO")))
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
                    if (curNode.m.idstrmx[0] != null & curNode.m.idstrmfraction[0] > 0)
                    {
                        // Holds the artificial link that returns flowthrow to the node.
                        // This link seems to be used only if the network has storage rights.
                        curLink = curNode.m.idstrmx[0].mnInfo.flowThroughReturnLink;
                        if (curLink is null)
                        {
                            curLink = curNode.m.idstrmx[0].mnInfo.infLink;
                            totFlowDws = curLink.mlInfo.flow;
                        }
                        else
                        {
                            MessageBox.Show("Check code for errors. When flowThroughReturnLink is not nothing the code is not tested.");
                        }
                        CalcFTNConc(curNode.m.idstrmx[0]);
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
                                                    max_Conc = ((QualityNodeData)m_node.Tag).maxBound; // ((QualityNodeData)m_Node.Tag).concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 1)
                                                    min_Conc = ((QualityNodeData)m_node.Tag).minBound; // ((QualityNodeData)m_Node.Tag).concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 2)
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
                                                        max_Conc = ((QualityNodeData)m_Node.Tag).maxBound; // ((QualityNodeData)m_Node.Tag).concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 1)
                                                        min_Conc = ((QualityNodeData)m_Node.Tag).minBound; // ((QualityNodeData)m_Node.Tag).concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 2)
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

		private void OnFinished()
		{
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
            PopNodeRet = (Node) todoDwS[1];
            todoDwS.Remove(1);
            return PopNodeRet;
        }


        private bool IsWQStation(Node stNode)
        {
            if (((QualityNodeData)stNode.Tag).InflowConcentration.dataTable.Rows.Count > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
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
                                    ((QualityNodeData) curLink.from.Tag).DWSStation = startingNode.name;
                                }
                                else
                                {
                                    ((QualityNodeData)curLink.from.Tag).DWSStation = startingNode.name;
                                }
                            }
                            else
                            {
                                // If it not gauge it's a regular demand and the upstream tracing should stop.
                                ((QualityNodeData)curLink.from.Tag).DWSStation = startingNode.name;
                            }
                        }
                        else
                        {
                            toDo.Add(curLink.from, default, 1);
                            AddRowToNodeStationTable(ref nodeTable, curLink.from.name);
                            ((QualityNodeData)curLink.from.Tag).DWSStation = startingNode.name;
                        }
                    }
                    allLinkList = allLinkList.next;
                }
            }
        }

        private bool IsGaugeNode(Node stNode)
        {
            //IQueryFilter m_Query = new QueryFilter();
            //m_Query.WhereClause = "[MOD_Name] = '" + stNode.name + "'";
            //IFeatureCursor pFeatCursorWS = m_FeatureClass.Search(m_Query, false);
            //IFeature pFeatureWS = pFeatCursorWS.NextFeature;
            //if (pFeatureWS != null)
            //{
            //    return true;
            //}
            return false;
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
        private void addLinkQualityOutput(Link m_link, ref DataRow m_row)
        {
            m_row["Concentration"] = ((QualityLinkData) m_link.Tag).concentration;
        }
        private void addNodeQualityOutput(Node m_node, ref DataRow m_row)
        {
            if (m_node.nodeType == NodeType.Demand)
            {
                m_row["Concentration"] = ((QualityNodeData)m_node.Tag).INconcentrations[myModel.mInfo.CurrentModelTimeStepIndex, 0];
                if (((QualityNodeData)m_node.Tag).InflowConcentration.dataTable.Rows.Count > 0)
                {
                    m_row["Measured Concentration"] = GetMeasuredConc(m_node); // ((QualityNodeData)m_Node.Tag).concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0)
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
            m_row["Concentration"] = ((QualityLinkData)m_node.mnInfo.infLink.Tag).concentration;
        }

        private float GetMeasuredConc(Node curNode, bool upsSource = false)
        {
            float m_conc;
            if (((QualityNodeData)curNode.Tag).useQCRelation)
            {
                // If selected use the fitted curve to calculate concentration
                float c_FLow = GetCurrentInflows(curNode);
                if (c_FLow > 0f)
                {
                    m_conc = ((QualityNodeData)curNode.Tag).m_CurveFitting.GetCurveYValue(c_FLow, ((QualityNodeData)curNode.Tag).minBound, ((QualityNodeData)curNode.Tag).maxBound);
                }
                else
                {
                    // aviods using a concentration value when no flow (curve intercept) - large errors in analysis
                    m_conc = 0f;
                }
            }
            else
            {
                m_conc = ((QualityNodeData)curNode.Tag).concentrations(myModel.mInfo.CurrentModelTimeStepIndex, 0);
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
    }
}

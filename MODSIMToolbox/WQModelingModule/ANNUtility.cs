using System;
using System.Data;
using System.IO;
using System.Windows.Forms;
//using ESRI.ArcGIS.Geodatabase;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace RTI.CWR.MODSIM.WQModelingModule
{

    public class ANNUtility
    {
        private double[][] ANNWeights;
        public double[,] ANNOrigInputs;
        public double[,] ANNCalcInputs;
        private double[,] ANNInputs;
        private double[] b, b2;
        private double[,] lw, lw2, ElmanLW;
        private int NoNeurons;
        public int NoInputs, NoOutputs;
        public string[] TypePostProcessing, TypePreProcessing, InputNames, OutputNames;
        private string ANNType, TransferF1, TransferF2;
        private double[] mint, maxt, minp, maxp;
        public int m_noGWRetRegions;
        private float[,] ANNOutputs;
        public float[,,] prevANNOutputs;
        public float[,,] prevANNInputs;
        public float[,,] initialVals;
        public int[] indexPrevANNInputs;
        private DataTable InputTable_AveOutputs, InputTable_AveInputs, InputTable_OutLabels;
        private DataTable InputTable_Testing;
        // Private conn As System.Data.OleDb.OleDbConnection
        // Units Conversion Constant
        private const float ConvAFToM3 = 1233.486771f;
        private string m_MatlabExpFolder, m_ExpFolder;
        private float[,] pcaMatrix;
        private double[,] ElmanPrevL1Out;
        private string ANNName;
        public int prevTStepIndex = -1;
        private int iterCount;
        public event ANNmessageEventHandler ANNmessage;

        public delegate void ANNmessageEventHandler(string msg);
        public DataTable InputTable_CONST;
        public DataTable TrainingInputsTable;
        private string GeoDSSBaseName;
        private int msgSilentMode = 0;

        public ANNUtility(string MatlabExpFolder, string workingFolder, string ANNBaseName)
        {
            string m_destination;
            // make a reference to a directory
            var di = new DirectoryInfo(MatlabExpFolder);
            FileInfo[] diar1 = di.GetFiles(ANNBaseName + "*");
            // Create working folder
            workingFolder = Path.GetDirectoryName(workingFolder + @"\") + @"\" + @"ANNFiles\";
            if (!Directory.Exists(workingFolder))
            {
                Directory.CreateDirectory(workingFolder);
            }
            // list the names of all files in the specified directory
            foreach (var dra in diar1)
            {
                m_destination = workingFolder + dra.Name;
                if (!dra.FullName.Equals(m_destination))
                    File.Copy(dra.FullName, m_destination, true);
            }
            m_MatlabExpFolder = workingFolder + ANNBaseName;
        }
        // Public Sub New(ByVal noGWRetRegions As Integer, ByVal BuffersDatabase As String)
        public void Initialize(string ANNName, int noTSteps, int userNoGroupRegions = 0, string GeoDSSBaseNetwork = "")
        {
            InputTable_CONST = ReadANNToMODSIMtable(m_MatlabExpFolder + "ANN_To_MODSIM_CONST.csv");
            m_noGWRetRegions = InputTable_CONST.Rows.Count; // noGWRetRegions
            if (userNoGroupRegions > 0)
            {
                m_noGWRetRegions = userNoGroupRegions;
            }
            this.ANNName = ANNName;
            ReadANNData(noTSteps);
            // Populate array with info about the output per unit length
            PopulateOutPerULength();
            // conn = New System.Data.OleDb.OleDbConnection
            // conn.ConnectionString = _
            // "Provider=Microsoft.Jet.OLEDB.4.0;Data source=" & BuffersDatabase
            // conn.Open()
            if (string.IsNullOrEmpty(GeoDSSBaseNetwork))
                GeoDSSBaseNetwork = Path.GetTempPath() + @"\GeoDSSANN.xy";
            GeoDSSBaseName = "GeoDSSANN_" + Path.GetFileNameWithoutExtension(GeoDSSBaseNetwork);
        }
        public DataTable GetInputTable_CONST()
        {
            return InputTable_CONST;
        }
        private void ReadANNData(int noTSteps)
        {
            ANNmessage?.Invoke(" Reading MatLab Export Files...");
            ReadANNWeights(m_MatlabExpFolder + "ANNWeights.csv");
            ReadANNInputs(m_MatlabExpFolder + "ANNInputs.csv");
            ANNmessage?.Invoke(" Reading MS-Access Export Files...");
            InputTable_AveOutputs = ReadANNToMODSIMtable(m_MatlabExpFolder + "ANN_To_MODSIM_AvgOutputs.csv");
            InputTable_AveInputs = ReadANNToMODSIMtable(m_MatlabExpFolder + "ANN_To_MODSIM_AvgInputs.csv");
            InputTable_OutLabels = ReadANNToMODSIMtable(m_MatlabExpFolder + "ANN_OutputVarLabels.csv", true);
            InputTable_Testing = ReadANNToMODSIMtable(m_MatlabExpFolder + "ANNTest_1.csv");
            prevANNOutputs = new float[m_noGWRetRegions + 1, NoOutputs, noTSteps + 1];
            // Prepare for storing ANN input variables that repeat.
            int i;
            int countPrevTSVars = 0;
            var loopTo = NoInputs - 1;
            for (i = 0; i <= loopTo; i++)
            {
                if (IsPrevTSInput(InputNames[Conversions.ToInteger(i)]))
                {
                    object varBase = Strings.Split(InputNames[Conversions.ToInteger(i)], "TS")[0];
                    indexPrevANNInputs[FindANNInputIndex(Conversions.ToString(varBase))] = 1;
                }
                else
                {
                    indexPrevANNInputs[Conversions.ToInteger(i)] = -1;
                }
            }
            var loopTo1 = NoInputs - 1;
            for (i = 0; i <= loopTo1; i++)
            {
                if (indexPrevANNInputs[Conversions.ToInteger(i)] == 1)
                {
                    indexPrevANNInputs[Conversions.ToInteger(i)] = countPrevTSVars;
                    countPrevTSVars += 1;
                }
                else
                {
                    indexPrevANNInputs[Conversions.ToInteger(i)] = -1;
                }
            }
            prevANNInputs = new float[m_noGWRetRegions + 1, countPrevTSVars + 1, noTSteps + 1];

        }
        private void ReadANNWeights(string Filename)
        {
            var sr = new StreamReader(Filename);
            string[] split = null;
            int i, j, Outs;
            string Line = sr.ReadLine();
            if (Line.StartsWith("ANNWeightsVer2"))
            {
                Line = sr.ReadLine();
                split = Line.Split(',');
                NoNeurons = Convert.ToInt32(Conversions.ToDouble(split[0]) - 1d);
                NoInputs = Convert.ToInt32(split[1]);
                NoOutputs = Convert.ToInt32(split[2]);
                ANNType = split[3];
                TransferF1 = split[4];
                TransferF2 = split[5];
                mint = new double[NoOutputs + 1];
                maxt = new double[NoOutputs + 1];
                OutputNames = new string[NoOutputs + 1];
                TypePostProcessing = new string[NoOutputs + 1];
                OutPerULength = new bool[NoOutputs];
                var loopTo = NoOutputs;
                for (i = 1; i <= loopTo; i++)
                {
                    Line = sr.ReadLine();
                    split = Line.Split(',');
                    OutputNames[i] = Convert.ToString(split[0]);
                    TypePostProcessing[i] = Convert.ToString(split[1]);
                    mint[i] = Convert.ToDouble(split[2]);
                    maxt[i] = Convert.ToDouble(split[3]);
                }
                ANNWeights = new double[NoNeurons + 1][];
                b = new double[NoNeurons + 1];
                lw = new double[NoOutputs + 1, NoNeurons + 1];
                var loopTo1 = NoNeurons;
                for (i = 0; i <= loopTo1; i++)
                {
                    Line = sr.ReadLine();
                    split = Line.Split(',');
                    ANNWeights[i] = new double[NoInputs];
                    var loopTo2 = NoInputs - 1;
                    for (j = 0; j <= loopTo2; j++)
                        ANNWeights[i][j] = Convert.ToDouble(split[j]);
                    b[i] = Convert.ToDouble(split[j]);
                    var loopTo3 = NoOutputs;
                    for (Outs = 1; Outs <= loopTo3; Outs++)
                        lw[Outs, i] = Convert.ToDouble(split[j + Outs]);
                }
                if (ANNType == "cascade")
                {
                    lw2 = new double[NoOutputs + 1, NoInputs + 1];
                    b2 = new double[NoOutputs + 1];
                    Line = sr.ReadLine();
                    var loopTo4 = NoOutputs;
                    for (Outs = 1; Outs <= loopTo4; Outs++)
                    {
                        Line = sr.ReadLine();
                        split = Line.Split(',');
                        var loopTo5 = NoInputs - 1;
                        for (j = 0; j <= loopTo5; j++)
                            // Weights form the inputs to the second layer of the ANN
                            lw2[Outs, j] = Convert.ToDouble(split[j]);
                        b2[Outs] = Convert.ToDouble(split[j]);
                    }
                }
                if (ANNType == "feed" | ANNType == "Elman" | ANNType == "RBNN")
                {
                    b2 = new double[NoOutputs + 1];
                    Line = sr.ReadLine();
                    var loopTo6 = NoOutputs;
                    for (Outs = 1; Outs <= loopTo6; Outs++)
                    {
                        Line = sr.ReadLine();
                        split = Line.Split(',');
                        b2[Outs] = Convert.ToDouble(split[0]);
                    }
                }
                if (ANNType == "Elman")
                {
                    ElmanLW = new double[NoNeurons + 1, NoNeurons + 1];
                    Line = sr.ReadLine();
                    if (Line == "Elman-LayerWeights") // Check for keyword
                    {
                        var loopTo7 = NoNeurons;
                        for (Outs = 0; Outs <= loopTo7; Outs++)
                        {
                            Line = sr.ReadLine();
                            split = Line.Split(',');
                            var loopTo8 = NoNeurons;
                            for (j = 0; j <= loopTo8; j++)
                                // Weights form the inputs to the second layer of the ANN
                                ElmanLW[Outs, j] = Convert.ToDouble(split[j]);
                        }
                    }
                    else
                    {
                        MessageBox.Show("ERROR - Unexpected keyword in Elman NN Layer weights File" + Constants.vbCrLf + "Weights not read.");
                    }

                }
            }
            else
            {
                sr.Close();
                throw new Exception("Invalid format to read ANN Weights");
            }
            sr.Close();
        }
        private void ReadANNInputs(string Filename)
        {
        readFile:
            try
            {
           
                var sr = new StreamReader(Filename);
                string[] split = null;
                int i, j;
                string Line = sr.ReadLine();
                if (Line == "ANNInputs")
                {
                    Line = sr.ReadLine();
                    NoInputs = Convert.ToInt32(Line);
                    ANNOrigInputs = new double[m_noGWRetRegions + 1 + 1, NoInputs + 1]; // = New Double(NoInputs - 1) {}
                    ANNCalcInputs = new double[m_noGWRetRegions + 1 + 1, NoInputs + 1];
                    minp = new double[NoInputs];
                    maxp = new double[NoInputs];
                    InputNames = new string[NoInputs];
                    indexPrevANNInputs = new int[NoInputs];
                    TypePreProcessing = new string[NoInputs];
                    var loopTo = NoInputs - 1;
                    for (i = 0; i <= loopTo; i++)
                    {
                        Line = sr.ReadLine();
                        split = Line.Split(',');
                        InputNames[i] = Convert.ToString(split[0]);
                        TypePreProcessing[i] = Convert.ToString(split[1]);
                        minp[i] = Convert.ToDouble(split[2]);
                        maxp[i] = Convert.ToDouble(split[3]);
                        // Values for previous output ANN inputs in the initial time steps. (Testing Only)
                        ANNOrigInputs[0, i] = Convert.ToDouble(split[4]);
                    }
                    Line = sr.ReadLine();
                    if (Line != null)
                    {
                        split = Line.Split(',');
                        int pcaRows = Conversions.ToInteger(split[1]);
                        int pcaCols = Conversions.ToInteger(split[2]);
                        pcaMatrix = new float[pcaRows + 1, pcaCols + 1];
                        var loopTo1 = pcaRows;
                        for (i = 1; i <= loopTo1; i++)
                        {
                            Line = sr.ReadLine();
                            split = Line.Split(',');
                            var loopTo2 = pcaCols;
                            for (j = 1; j <= loopTo2; j++)
                                pcaMatrix[i, j] = Conversions.ToSingle(split[j - 1]);
                        }
                    }
                }
                else
                {
                    sr.Close();
                    throw new Exception("Invalid format to read ANN Weights");
                }
                sr.Close();
            }
            catch (Exception ex)
            {
                var mRst = MessageBox.Show(ex.ToString(), "Error Reading ANN File", MessageBoxButtons.AbortRetryIgnore);
                if (mRst == DialogResult.Retry)
                {
                    goto readFile;
                }
                else
                {
                    throw new Exception("Execution Aborted.");
                }
            }
        }
        public float ANNOutput(float streamLength, string outputName, int regionID)
        {
            float ANNOutputRet = default;
            int outIndex = get_OutputIndex(outputName);
            if (outIndex == -1)
            {
                // Try this when an output variable doesn't exist.
                ANNOutputRet = 0f;
                if (msgSilentMode < 5)
                {
                    msgSilentMode += 1;
                    ANNmessage?.Invoke("   **ANN Output variable " + outputName + " doesn't exist.");
                }
            }
            // Throw New Exception("   **ANN Output variable " & outputName & " doesn't exist. ANN Prediction aborted.")
            else if (OutPerULength[outIndex])
            {
                ANNOutputRet = ANNOutputs[regionID, outIndex] * streamLength; // Obtain returned flow in M3n from m3/Km
            }
            else
            {
                ANNOutputRet = ANNOutputs[regionID, outIndex];
            }  // Obtain returned flow in M3 

            return ANNOutputRet;
        }
        private bool[] OutPerULength;
        public bool PopulateOutPerULength()
        {
            foreach (DataRow m_row in InputTable_OutLabels.Rows)
            {
                int outIndex = get_OutputIndex(m_row["VarName"].ToString());
                if (outIndex >= 0)
                {
                    OutPerULength[outIndex] = Conversions.ToBoolean(m_row["PerUnitLength"]);
                }
            }

            return default;
        }
        public void CalcALLRegionsANNOutputs(int tStepIndex, bool debugON = false, float maxPercVarAllow = 1000f)
        {
            int regionID;
            if (prevTStepIndex != tStepIndex)
            {
                if (tStepIndex == 0 & debugON)
                    CreateFileForANNInputs();
                iterCount = 0;
            }
            else
            {
                iterCount += 1;
            }
            ANNOutputs = new float[m_noGWRetRegions + 1, NoOutputs]; // = New Single(NoOutputs) {}
            var loopTo = m_noGWRetRegions;
            for (regionID = 1; regionID <= loopTo; regionID++)
            {
                if (ANNType == "GRNN" | ANNType == "RBNN")
                {
                    CalcRadialBasisANNOutputs(Conversions.ToInteger(regionID), tStepIndex);
                }
                else
                {
                    CalcBackPropANNOutputs(Conversions.ToInteger(regionID), tStepIndex);
                }
                if (debugON)
                    AddDebugANNValues(Conversions.ToInteger(regionID), tStepIndex);
            }
            // save input variables that are repeated
            int j, i;
            var loopTo1 = m_noGWRetRegions;
            for (j = 1; j <= loopTo1; j++)
            {
                var loopTo2 = NoInputs - 1;
                for (i = 0; i <= loopTo2; i++)
                {
                    if (indexPrevANNInputs[i] >= 0)
                    {
                        // Is the base for a variable that repeats
                        prevANNInputs[j, indexPrevANNInputs[i], tStepIndex] = (float)ANNCalcInputs[j, i];
                    }
                }
                // 'Implement limit change in prediction == FAULTY for positive negative switching
                // If maxPercVarAllow < 1000 Then
                // For Outs As Integer = 1 To NoOutputs
                // If tStepIndex >= 1 Then
                // Dim prevOut As Single = prevANNOutputs(j, Outs - 1, tStepIndex - 1)
                // Dim currOutput As Single = ANNOutputs(j, Outs - 1)
                // If prevOut > 0 Then
                // If prevOut * (1 + maxPercVarAllow) < currOutput Then
                // ANNOutputs(j, Outs - 1) = prevOut * (1 + maxPercVarAllow)
                // ElseIf Math.Abs(prevOut) * (1 - maxPercVarAllow) > currOutput Then
                // ANNOutputs(j, Outs - 1) = prevOut * (1 - maxPercVarAllow)
                // End If
                // Else

                // End If

                // prevANNOutputs(j, Outs - 1, tStepIndex) = ANNOutputs(j, Outs - 1)
                // End If
                // Next
                // End If
            }
            prevTStepIndex = tStepIndex;
        }
        private void CalcRadialBasisANNOutputs(int regionID, int tStepIndex)
        {
            int m_TempNoNInputs = NoInputs;
            NoInputs = Math.Min(ANNWeights[0].Length, ANNInputs.GetLength(1));
            int EachNeuron, EachInput, Outs;
            double NeuronSum = 0d;
            double TempDist, Prod;
            double[] Dist = new double[NoNeurons + 1], a = new double[NoNeurons + 1];
            var loopTo = NoNeurons;
            for (EachNeuron = 0; EachNeuron <= loopTo; EachNeuron++)
            {
                NeuronSum = 0d;
                var loopTo1 = NoInputs - 1;
                for (EachInput = 0; EachInput <= loopTo1; EachInput++)
                {
                    TempDist = Math.Pow(ANNInputs[regionID, EachInput] - ANNWeights[EachNeuron][EachInput], 2d);
                    NeuronSum += TempDist;
                }
                Dist[EachNeuron] = Math.Pow(NeuronSum, 0.5d);
            }
            var loopTo2 = NoNeurons;
            for (EachNeuron = 0; EachNeuron <= loopTo2; EachNeuron++)
            {
                Prod = Dist[EachNeuron] * b[EachNeuron];
                // Redial Transfer Function calculation
                a[EachNeuron] = Math.Exp(-Math.Pow(Prod, 2d));
            }
            if (ANNType == "GRNN")
            {
                double SumA = 0d;
                var loopTo3 = NoNeurons;
                for (EachNeuron = 0; EachNeuron <= loopTo3; EachNeuron++)
                    SumA += a[EachNeuron];
                var loopTo4 = NoOutputs;
                for (Outs = 1; Outs <= loopTo4; Outs++)
                {
                    Prod = 0d;
                    var loopTo5 = NoNeurons;
                    for (EachNeuron = 0; EachNeuron <= loopTo5; EachNeuron++)
                        Prod += a[EachNeuron] * lw[Outs, EachNeuron];
                    if (SumA > 0d)
                    {
                        Prod = Prod / SumA;
                    }
                    else
                    {
                        string text = "    Region :" + regionID + " failed to calculate return. Division by zero";
                        ANNmessage?.Invoke(text); // MessageBox.Show(text)
                        text = "     WARNING: Prediction is outside of the training dataset:";
                        ANNmessage?.Invoke(text);
                    }

                    // Transfor to original units

                    if (TypePostProcessing[Outs] == " MnMx ")
                    {
                        ANNOutputs[regionID, Outs - 1] = (float)(0.5d * (Prod + 1d) * (maxt[Outs] - mint[Outs]) + mint[Outs]);
                    }
                    else
                    {
                        // Transformation for 'std' that is used also in 'pca' transformation
                        ANNOutputs[regionID, Outs - 1] = (float)(maxt[Outs] * Prod + mint[Outs]);
                    }

                    prevANNOutputs[regionID, Outs - 1, tStepIndex] = ANNOutputs[regionID, Outs - 1];
                    // ANNOutputs(Outs) *= streamLength  'Obtain returned flow in M3n from m3/m
                    // If (OutputNames(Outs) = "OUTPUTINRIVER") Then ANNOutputs(Outs) /= 1233.486771 'Unit conversion from m3 to AF
                }
                NoInputs = m_TempNoNInputs;
            }
            else
            {
                var loopTo6 = NoOutputs;
                for (Outs = 1; Outs <= loopTo6; Outs++)
                {
                    NeuronSum = 0d;
                    var loopTo7 = NoNeurons;
                    for (EachNeuron = 0; EachNeuron <= loopTo7; EachNeuron++)
                        NeuronSum += a[EachNeuron] * lw[Outs, EachNeuron];
                    Prod = NeuronSum + b2[Outs];
                    Prod = ANNLayerTransfer(Prod, TransferF2);

                    // Transform to original units
                    if (TypePostProcessing[Outs] == " MnMx ")
                    {
                        float m_outValue = (float)(0.5d * (Prod + 1d) * (maxt[Outs] - mint[Outs]) + mint[Outs]);
                        // Checks for max/min prediction in training to set max/min predicion in simulation
                        float m_fct = 0.25f;
                        float m_room = (float)(m_fct * (maxt[Outs] - mint[Outs]) / 2d);
                        if (m_outValue > maxt[Outs] + Math.Abs(m_room))
                        {
                            // This works for both positive and negative
                            m_outValue = (float)(maxt[Outs] + Math.Abs(m_room));
                        }
                        if (m_outValue < mint[Outs] - Math.Abs(m_room))
                        {
                            m_outValue = (float)(mint[Outs] - Math.Abs(m_room));
                        }
                        ANNOutputs[regionID, Outs - 1] = m_outValue;
                    }
                    else
                    {
                        ANNOutputs[regionID, Outs - 1] = (float)(maxt[Outs] * Prod + mint[Outs]);
                    }
                    prevANNOutputs[regionID, Outs - 1, tStepIndex] = ANNOutputs[regionID, Outs - 1];
                }
            }
        }
        private double ANNLayerTransfer(double inValue, string m_transfer)
        {
            switch (m_transfer ?? "")
            {
                case "purelin":
                    {
                        inValue = inValue;
                        break;
                    }
                case "tansig":
                    {
                        if (1d + Math.Exp(-2 * inValue) == 0d)
                        {
                            if (inValue > 1d)
                                inValue = 1d;
                            if (inValue < -1)
                                inValue = -1;
                            if (inValue == 0d)
                                inValue = 0d;
                        }
                        else
                        {
                            inValue = 2d / (1d + Math.Exp(-2 * inValue)) - 1d;
                        }

                        break;
                    }
                case "logsig":
                    {
                        if (1d + Math.Exp(-inValue) == 0d)
                        {
                            if (inValue > 1d)
                                inValue = 1d;
                            if (inValue < -1)
                                inValue = -1;
                            if (inValue == 0d)
                                inValue = 0d;
                        }
                        else
                        {
                            inValue = 1d / (1d + Math.Exp(-inValue));
                        }

                        break;
                    }

                default:
                    {
                        MessageBox.Show("Transfer function not implemented", "Error calculating ANN", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        break;
                    }
            }
            return inValue;
        }
        private void CalcBackPropANNOutputs(int regionID, int tStepIndex)
        {
            int m_TempNoNInputs = NoInputs;
            NoInputs = Math.Min(ANNWeights[0].Length, ANNInputs.GetLength(1));
            if (ANNType == "Elman" & ElmanPrevL1Out == null)
            {
                ElmanPrevL1Out = new double[m_noGWRetRegions + 1, NoNeurons + 1];
            }
            int EachNeuron, EachInput, Outs, EachLOut;
            double NeuronSum = 0d;
            double Prod;
            double[] a = new double[NoNeurons + 1];
            var loopTo = NoNeurons;
            for (EachNeuron = 0; EachNeuron <= loopTo; EachNeuron++)
            {
                NeuronSum = 0d;
                var loopTo1 = NoInputs - 1;
                for (EachInput = 0; EachInput <= loopTo1; EachInput++)
                    NeuronSum += ANNInputs[regionID, EachInput] * ANNWeights[EachNeuron][EachInput];
                a[EachNeuron] = NeuronSum;
            }
            // If ANNType = "Elman" Then
            // 'Add feedback from previous calculation
            // For EachNeuron = 0 To NoNeurons
            // NeuronSum = 0
            // For EachLOut = 0 To NoNeurons
            // NeuronSum += (ElmanPrevL1Out(regionID, EachLOut) * ElmanLW(EachNeuron, EachLOut))
            // Next
            // a(EachNeuron) += NeuronSum
            // Next
            // End If
            // Add bias
            var loopTo2 = NoNeurons;
            for (EachNeuron = 0; EachNeuron <= loopTo2; EachNeuron++)
                a[EachNeuron] += b[EachNeuron];
            // Apply Transferfunction
            var loopTo3 = NoNeurons;
            for (EachNeuron = 0; EachNeuron <= loopTo3; EachNeuron++)
            {
                a[EachNeuron] = ANNLayerTransfer(a[EachNeuron], TransferF1);
                // Select Case TransferF1
                // Case "purelin"
                // a(EachNeuron) = a(EachNeuron)
                // Case "tansig"
                // If (1 + Math.Exp(-2 * a(EachNeuron))) = 0 Then
                // If a(EachNeuron) > 1 Then a(EachNeuron) = 1
                // If a(EachNeuron) < -1 Then a(EachNeuron) = -1
                // If a(EachNeuron) = 0 Then a(EachNeuron) = 0
                // Else
                // a(EachNeuron) = 2 / (1 + Math.Exp(-2 * a(EachNeuron))) - 1
                // End If
                // Case "logsig"
                // If (1 + Math.Exp(-a(EachNeuron))) = 0 Then
                // If a(EachNeuron) > 1 Then a(EachNeuron) = 1
                // If a(EachNeuron) < -1 Then a(EachNeuron) = -1
                // If a(EachNeuron) = 0 Then a(EachNeuron) = 0
                // Else
                // a(EachNeuron) = 1 / (1 + Math.Exp(-a(EachNeuron)))
                // End If
                // Case Else
                // MessageBox.Show("Transfer function not implemented", "Error calculating ANN", MessageBoxButtons.OK, MessageBoxIcon.Error)
                // End Select
                if (ANNType == "Elman")
                    ElmanPrevL1Out[regionID, EachNeuron] = a[EachNeuron];
            }
            // ANNOutputs = New Single(NoOutputs) {}
            var loopTo4 = NoOutputs;
            for (Outs = 1; Outs <= loopTo4; Outs++)
            {
                NeuronSum = 0d;
                var loopTo5 = NoNeurons;
                for (EachNeuron = 0; EachNeuron <= loopTo5; EachNeuron++)
                    NeuronSum += a[EachNeuron] * lw[Outs, EachNeuron];
                if (ANNType == "cascade")
                {
                    var loopTo6 = NoInputs - 1;
                    for (EachInput = 0; EachInput <= loopTo6; EachInput++)
                        NeuronSum += ANNInputs[regionID, EachInput] * lw2[Outs, EachInput];
                }
                Prod = NeuronSum + b2[Outs];
                Prod = ANNLayerTransfer(Prod, TransferF2);
                // Select Case TransferF2
                // Case "purelin"
                // Prod = Prod
                // Case "tansig"
                // If (1 + Math.Exp(-2 * Prod)) = 0 Then
                // If Prod > 1 Then Prod = 1
                // If Prod < -1 Then Prod = -1
                // Else
                // Prod = 2 / (1 + Math.Exp(-2 * Prod)) - 1
                // End If
                // Case "logsig"
                // If (1 + Math.Exp(-Prod)) = 0 Then
                // If Prod > 1 Then Prod = 1
                // If Prod < -1 Then Prod = -1
                // Else
                // Prod = 1 / (1 + Math.Exp(-Prod))
                // End If
                // Case Else
                // MessageBox.Show("Transfer function not implemented", "Error calculating ANN", MessageBoxButtons.OK, MessageBoxIcon.Error)
                // End Select

                // Transform to original units

                if (TypePostProcessing[Outs] == " MnMx ")
                {
                    ANNOutputs[regionID, Outs - 1] = (float)(0.5d * (Prod + 1d) * (maxt[Outs] - mint[Outs]) + mint[Outs]);
                }
                else
                {
                    ANNOutputs[regionID, Outs - 1] = (float)(maxt[Outs] * Prod + mint[Outs]);
                }

                prevANNOutputs[regionID, Outs - 1, tStepIndex] = ANNOutputs[regionID, Outs - 1];
                // ANNOutputs(Outs) *= streamLength  'Obtain returned flow in M3n from m3/m
                // If (OutputNames(Outs) = "OUTPUTINRIVER" Or OutputNames(Outs) = "OUTPUTOUTRIVER") Then ANNOutputs(Outs) /= 1233.486771 'Unit conversion from m3 to AF
            }
            NoInputs = m_TempNoNInputs;
        }
        public void preScaleOrigInputs()
        {
            int i, region;
            ANNInputs = new double[m_noGWRetRegions + 1, NoInputs + 1]; // = New Double(NoInputs) {}
            var loopTo = m_noGWRetRegions;
            for (region = 0; region <= loopTo; region++)
            {
                var loopTo1 = NoInputs - 1;
                for (i = 0; i <= loopTo1; i++)
                {
                    if (TypePreProcessing[Conversions.ToInteger(i)] == " MnMx ")
                    {
                        ANNInputs[Conversions.ToInteger(region), Conversions.ToInteger(i)] = 2d * (ANNOrigInputs[Conversions.ToInteger(region), Conversions.ToInteger(i)] - minp[Conversions.ToInteger(i)]) / (maxp[Conversions.ToInteger(i)] - minp[Conversions.ToInteger(i)]) - 1d;
                    }
                    else
                    {
                        ANNInputs[Conversions.ToInteger(region), Conversions.ToInteger(i)] = (ANNOrigInputs[Conversions.ToInteger(region), Conversions.ToInteger(i)] - minp[Conversions.ToInteger(i)]) / maxp[Conversions.ToInteger(i)];
                    }
                }
                if (TypePreProcessing[1] == " pca ")
                {
                    var trans_val = new double[m_noGWRetRegions + 1, NoInputs + 1];
                    var loopTo2 = pcaMatrix.GetLength(0) - 1;
                    for (i = 1; i <= loopTo2; i++)
                    {
                        int m_i;
                        var loopTo3 = pcaMatrix.GetLength(1) - 1;
                        for (m_i = 1; m_i <= loopTo3; m_i++)
                            trans_val[Conversions.ToInteger(region), Conversions.ToInteger(Operators.SubtractObject(i, 1))] += ANNInputs[Conversions.ToInteger(region), m_i - 1] * pcaMatrix[Conversions.ToInteger(i), m_i];
                    }
                    ANNInputs = (double[,])trans_val.Clone();
                }
            }
        }
        public void preScaleCalcInputs()
        {
            int i, region;
            ANNInputs = new double[m_noGWRetRegions + 1, NoInputs + 1]; // = New Double(NoInputs) {}
            var loopTo = m_noGWRetRegions;
            for (region = 1; region <= loopTo; region++)
            {
                var loopTo1 = NoInputs - 1;
                for (i = 0; i <= loopTo1; i++)
                {
                    if (TypePreProcessing[Conversions.ToInteger(i)] == " MnMx ")
                    {
                        ANNInputs[Conversions.ToInteger(region), Conversions.ToInteger(i)] = 2d * (ANNCalcInputs[Conversions.ToInteger(region), Conversions.ToInteger(i)] - minp[Conversions.ToInteger(i)]) / (maxp[Conversions.ToInteger(i)] - minp[Conversions.ToInteger(i)]) - 1d;
                    }
                    else
                    {
                        ANNInputs[Conversions.ToInteger(region), Conversions.ToInteger(i)] = (ANNCalcInputs[Conversions.ToInteger(region), Conversions.ToInteger(i)] - minp[Conversions.ToInteger(i)]) / maxp[Conversions.ToInteger(i)];
                    }
                }
                if (TypePreProcessing[1] == " pca ")
                {
                    var trans_val = new double[m_noGWRetRegions + 1, NoInputs + 1];
                    var loopTo2 = pcaMatrix.GetLength(0) - 1;
                    for (i = 1; i <= loopTo2; i++)
                    {
                        int m_i;
                        var loopTo3 = pcaMatrix.GetLength(1) - 1;
                        for (m_i = 1; m_i <= loopTo3; m_i++)
                            trans_val[Conversions.ToInteger(region), Conversions.ToInteger(Operators.SubtractObject(i, 1))] += ANNInputs[Conversions.ToInteger(region), m_i - 1] * pcaMatrix[Conversions.ToInteger(i), m_i];
                    }
                    ANNInputs = (double[,])trans_val.Clone();
                }
            }
        }
        // Returns zero based output index
        public int get_OutputIndex(string IDString)
        {
            int OutputIndexRet = default;
            OutputIndexRet = -1;
            short i;
            var loopTo = (short)NoOutputs;
            for (i = 1; i <= loopTo; i++)
            {
                if ((OutputNames[i] ?? "") == (IDString ?? ""))
                {
                    OutputIndexRet = i - 1;
                    return default;
                }
            }

            return OutputIndexRet;
        }
        public int FindANNInputIndex(string name)
        {
            int i;
            var loopTo = NoInputs - 1;
            for (i = 0; i <= loopTo; i++)
            {
                if ((InputNames[i] ?? "") == (name ?? ""))
                {
                    return i;
                    return default;
                }
            }
            return -1;
        }
        public bool IsMODSIMCalcVar(string varName)
        {
            string m_name = varName.Split(Conversions.ToChar("TS"))[0];
            m_name = m_name.Split('_')[0];
            switch (m_name)
            {
                case "RiverFlow":
                case "AveVolSeep":
                case "Rech":
                case "AveDiversion":
                case "PercRech":
                case "PercSeep":
                    {
                        return true;
                    }

                default:
                    {
                        return false;
                    }
            }
        }
        public bool IsPumpingInput(string varName)
        {
            string m_name = varName.Split(Conversions.ToChar("TS"))[0];
            m_name = m_name.Split('_')[0];
            switch (m_name)
            {
                case "AvePumped":
                case "NoPumps":
                    {
                        return true;
                    }

                default:
                    {
                        return false;
                    }
            }
        }
        public bool IsPrevTSInput(string varName)
        {
            string[] m_name = Strings.Split(varName, "TS");
            if (m_name.Length > 1)
            {
                // and is not an output variable
                if (get_OutputIndex(m_name[0]) == -1 & !string.IsNullOrEmpty(m_name[0]))
                {
                    return true;
                    return default;
                }
            }
            return false;
        }
        public bool IsPrecipInput(string varName)
        {
            string m_name = varName.Split(Conversions.ToChar("TS"))[0];
            m_name = m_name.Split('_')[0];
            switch (m_name)
            {
                case "Precip":
                    {
                        return true;
                    }

                default:
                    {
                        return false;
                    }
            }
        }
        public DataTable ReadANNToMODSIMtable(string fileName, bool asText = false)
        {
            var inputsTable = new DataTable();
            // Dim filename As String = filePath & "ANN_To_MODSIM.csv"
            if (File.Exists(fileName))
            {
                ANNmessage?.Invoke("     " + fileName + " ...");
                var sr = new StreamReader(fileName);
                string[] m_split = null;
                int i, j;
                string Line = sr.ReadLine();
                foreach (var colName in Line.Split(','))
                {
                    var m_Column = new DataColumn();
                    m_Column.ColumnName = colName;
                    if (asText)
                    {
                        m_Column.DataType = typeof(string);
                    }
                    else
                    {
                        m_Column.DataType = typeof(float);
                    }
                    inputsTable.Columns.Add(m_Column);
                }
                Line = sr.ReadLine();
                while (Line != null)
                {
                    var m_row = inputsTable.NewRow();
                    m_split = Line.Split(',');
                    int col;
                    var loopTo = inputsTable.Columns.Count - 1;
                    for (col = 0; col <= loopTo; col++)
                    {
                        if (!asText)
                        {
                            if (string.IsNullOrEmpty(m_split[col]))
                            {
                                m_row[col] = 0;
                            }
                            else
                            {
                                m_row[col] = Conversions.ToSingle(m_split[col]);
                            }
                        }
                        else
                        {
                            m_row[col] = m_split[col];
                        }
                    }
                    inputsTable.Rows.Add(m_row);
                    Line = sr.ReadLine();
                }
                sr.Close();
                return inputsTable;
            }
            else
            {
                throw new Exception("File: " + fileName + " Not found.  Aborting execution.");
                return null;
            }
        }
        public float GetPrevANNIOs(int noGWReg, int ANNVarIndex, int currentTSIndex, bool isOutput)
        {
            float GetPrevANNIOsRet = default;
            int prevTimeStep = (int)Math.Round(currentTSIndex - (Conversions.ToDouble(Strings.Split(InputNames[ANNVarIndex], "TS")[1]) - 1d));
            string colName = Strings.Split(InputNames[ANNVarIndex], "TS")[0];
            if (prevTimeStep >= 0)
            {
                int outIndex = get_OutputIndex(colName);
                if (outIndex >= 0)
                {
                    GetPrevANNIOsRet = prevANNOutputs[noGWReg, outIndex, prevTimeStep];
                }
                else
                {
                    // Find base variable previous timestep values
                    object varBase = Strings.Split(InputNames[ANNVarIndex], "TS")[0];
                    object m_index = FindANNInputIndex(Conversions.ToString(varBase));
                    GetPrevANNIOsRet = prevANNInputs[noGWReg, indexPrevANNInputs[Conversions.ToInteger(m_index)], prevTimeStep];
                }
            }
            else if (!isOutput)
            {
                // Value read corresponding to the averages of the training Inputs 
                DataRow[] ANNInputsRow_AveIns = InputTable_AveInputs.Select("RetHydroID = " + noGWReg);
                foreach (var m_row in ANNInputsRow_AveIns)
                    GetPrevANNIOsRet = Conversions.ToSingle(m_row[colName]);
                // Use the same timestep value
                GetPrevANNIOsRet = (float)ANNCalcInputs[noGWReg, FindANNInputIndex(colName)];
            }
            // Value read corresponding to the averages of the training outputs data groups
            else if (initialVals is null)
            {
                if (Conversions.ToBoolean(1))
                {
                    DataRow[] ANNInputsRow_AveOuts = InputTable_AveOutputs.Select();
                    foreach (var m_row in ANNInputsRow_AveOuts)
                        GetPrevANNIOsRet = Conversions.ToSingle(GetPrevANNIOsRet + float.Parse(m_row[colName].ToString()));
                    // Calculate the average of all groups
                    GetPrevANNIOsRet /= ANNInputsRow_AveOuts.Length;
                    // Use the ouptut per unit length 
                    int outIndex = get_OutputIndex(colName);
                    if (OutPerULength[outIndex])
                    {
                        // output variables based on the "ANN_InputOutput_" tables that don't have the normalization 
                        int inindex;
                        if (colName.EndsWith("Ark") | colName == "DrainReturn")
                        {
                            // inindex = FindANNInputIndex("StreamLengthArk")
                            GetPrevANNIOsRet = Conversions.ToSingle(GetPrevANNIOsRet / float.Parse(InputTable_CONST.Rows[noGWReg - 1]["StreamLengthArk"].ToString()));
                        }
                        else
                        {
                            // inindex = FindANNInputIndex("StreamLengthTrib")
                            GetPrevANNIOsRet = Conversions.ToSingle(GetPrevANNIOsRet / float.Parse(InputTable_CONST.Rows[noGWReg - 1]["StreamLengthTrib"].ToString()));
                        }
                        // GetPrevANNIOs /= ANNCalcInputs(noGWReg, inindex)
                    }
                }
                else
                {
                    DataRow[] ANNInputsRow_AveOuts = InputTable_Testing.Select("DataID = 140 AND TStep= " + currentTSIndex);
                    float m_ave = 0f;
                    foreach (var m_row in ANNInputsRow_AveOuts)
                    {
                        m_ave = Conversions.ToSingle(m_ave + float.Parse(m_row[InputNames[ANNVarIndex]].ToString()));
                        if (Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(m_row["RetHydroID"], noGWReg, false)))
                        {
                            m_ave = Conversions.ToSingle(Operators.MultiplyObject(m_row[InputNames[ANNVarIndex]], ANNInputsRow_AveOuts.Length));
                            break;
                        }
                    }
                    GetPrevANNIOsRet = m_ave / ANNInputsRow_AveOuts.Length;
                }
            }
            else
            {
                int outIndex = get_OutputIndex(colName);
                GetPrevANNIOsRet = initialVals[noGWReg, outIndex, currentTSIndex];

                // If True Then
                // 'Value read corresponding to the averages of the training outputs data groups
                // Dim ANNInputsRow_AveOuts() As DataRow = InputTable_AveOutputs.Select()
                // Dim m_row As DataRow
                // For Each m_row In ANNInputsRow_AveOuts
                // GetPrevANNIOs += m_row(colName)
                // Next
                // 'Calculate the average of all groups
                // GetPrevANNIOs /= ANNInputsRow_AveOuts.Length
                // Else
                // 'Value read in inputs file the file for testing 
                // GetPrevANNIOs = CType(ANNOrigInputs(noGWReg, ANNVarIndex), Single)
                // End If
            }

            return GetPrevANNIOsRet;
        }
        // conn.Close()
        ~ANNUtility()
        {
        }
        public void CreateFileForANNInputs()
        {
            m_ExpFolder = Path.GetDirectoryName(m_MatlabExpFolder) + @"\" + GeoDSSBaseName;
            if (!Directory.Exists(m_ExpFolder))
            {
                Directory.CreateDirectory(m_ExpFolder);
            }
            m_ExpFolder = m_ExpFolder + @"\" + Path.GetFileNameWithoutExtension(m_MatlabExpFolder) + @"\";
            if (!Directory.Exists(m_ExpFolder))
            {
                Directory.CreateDirectory(m_ExpFolder);
            }
            string Filename = m_ExpFolder + @"\" + ANNName + ".csv"; // "M:\Enrique\Project Arkansas\ReservoirOperation\ResQualityANNData\ExportTest.csv"
            if (File.Exists(Filename))
            {
                File.Delete(Filename);
            }
            // Create the degub file
            StreamWriter wtest;
            string m_str;
            int i;
            wtest = new StreamWriter(Filename);
            // Add Column heading
            m_str = "Region, Iteration, Time Step";
            var loopTo = NoInputs - 1;
            for (i = 0; i <= loopTo; i++)
            {
                m_str += ",";
                m_str += InputNames[i].ToString();
            }
            var loopTo1 = NoOutputs;
            for (i = 1; i <= loopTo1; i++)
            {
                m_str += ",";
                m_str += OutputNames[i].ToString();
            }
            wtest.WriteLine(m_str);
            wtest.Close();
        }
        // Public Sub WriteANNInputs()
        // Dim Filename As String = m_MatlabExpFolder & "GEODSSANNInputs.csv"
        // Dim w As StreamWriter = File.AppendText(Filename)
        // Dim region, i As Integer
        // For region = 1 To m_noGWRetRegions
        // For i = 0 To NoInputs - 1
        // If i > 0 Then w.Write(",")
        // w.Write(CStr(ANNCalcInputs(region, i)))
        // Next
        // Next
        // w.Write(vbCrLf)
        // w.Close()
        // End Sub
        private void AddDebugANNValues(int region_ID, int tStepIndex)
        {
            StreamWriter wtest;
            int i;
            string m_str;
            string fileName = m_ExpFolder + @"\" + ANNName + ".csv"; // "M:\Enrique\Project Arkansas\ReservoirOperation\ResQualityANNData\ExportTest.csv"
            wtest = new StreamWriter(fileName, true);
            m_str = region_ID.ToString() + "," + iterCount + "," + tStepIndex.ToString();
            var loopTo = NoInputs - 1;
            for (i = 0; i <= loopTo; i++)
            {
                m_str += ",";
                m_str += ANNCalcInputs[region_ID, i].ToString();
            }
            var loopTo1 = NoOutputs;
            for (i = 1; i <= loopTo1; i++)
            {
                m_str += ",";
                m_str += ANNOutput(1f, OutputNames[i].ToString(), region_ID).ToString();
            }
            wtest.WriteLine(m_str);
            wtest.Close();
        }
        public void OnMessage(object m_Str)
        {
            ANNmessage?.Invoke(Conversions.ToString(m_Str));
        }
        public void LoadTrainingInputsTable()
        {
            TrainingInputsTable = ReadANNToMODSIMtable(m_MatlabExpFolder + "ANN_To_MODSIM.csv");
        }
        public string GetMatlabExpFolderwName()
        {
            return m_MatlabExpFolder;
        }
    }
}
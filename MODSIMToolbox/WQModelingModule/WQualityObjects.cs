using System;
using System.Data;
using Microsoft.VisualBasic;

namespace RTI.CWR.MODSIM.WQModelingModule
{

    // This class contains the link w.q. data and is placed in the link.tag
    public class QualityLinkData
    {
        public float concentration;
        // Flag = true if the quality concentration has been calculated.
        public bool valueSet;
        public QualityLinkData()
        {
            valueSet = false;
        }
        public enum combineType
        {
            AverageCombine,
            Replace,
            None
        }
        public void SetValue(float aConc, combineType combineType = combineType.None)
        {
            if (valueSet)
            {
                if (combineType == combineType.AverageCombine)
                {
                    concentration = (concentration + aConc) / 2f;
                }
                else if (combineType == combineType.Replace)
                {
                    concentration = aConc;
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show("Concentration combine not specified");
                }
            }
            else
            {
                concentration = aConc;
                valueSet = true;
            }
        }
    }
    // This class contains the Inflow w.q. data that is placed in the node.tag
    public class QualityNodeData
    {
        public TimeSeries InflowConcentration;
        public float[,] concentrations;
        public float[,] INconcentrations;
        public Collection myFTDNodes;
        public string DWSStation;
        public bool useQCRelation;
        public float maxBound;
        public float minBound;
        public CurveFittingUtils m_CurveFitting;
        public bool FTDAdjusted;
        public QualityNodeData()
        {
            InflowConcentration = new TimeSeries(true);
            InflowConcentration.MultiColumn = true;
            InflowConcentration.VariesByYear = true;
            var m_TSTable = new DataTable();
            m_TSTable.Columns.Add("Date", Type.GetType("System.DateTime"), "");
            m_TSTable.Columns.Add("Concentration", Type.GetType("System.Single"), "");
            // m_TSTable.Columns.Add("Min Concentration", System.Type.GetType("System.Single"), "")
            // m_TSTable.Columns.Add("Max Concentration", System.Type.GetType("System.Single"), "")
            var m_units = new ModsimUnits(AreaUnitsType.Acres);
            InflowConcentration.dataTable = m_TSTable;
            InflowConcentration.units = m_units; // "[mg/L]"
            myFTDNodes = new Collection();
            m_CurveFitting = new CurveFittingUtils();
            useQCRelation = false;
            maxBound = 99999f;
            FTDAdjusted = false;
        }
    }

    public class WQStationCalibData
    {
        public DataTable m_Tbl = new DataTable();
        public float prevInConcentration;
        public float concentration;
        public bool useMeasConc;
        public WQStationCalibData()
        {
            m_Tbl = new DataTable();
            prevInConcentration = 0f;
            concentration = 0f;
            useMeasConc = true;
        }
        public void clearData()
        {
            prevInConcentration = 0f;
            concentration = 0f;
            useMeasConc = true;
        }
    }
}
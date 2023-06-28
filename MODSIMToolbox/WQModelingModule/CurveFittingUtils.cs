using System;
using System.Data;
using Csu.Modsim.ModsimModel;
using Microsoft.VisualBasic.CompilerServices;

namespace RTI.CWR.MODSIM.WQModelingModule
{
    public class CurveFittingUtils
    {
        public enum CurveFittingTypes
        {
            Polynomial,
            Exponential,
            Power,
            Logarithmic
        }
        public CurveFittingTypes type;
        public DataTable m_CurveFittingTable;
        public float x_min;
        public float y_min;
        public string dataFilter;
        public CurveFittingUtils()
        {
        }
        public void CreateCurveFittingTable(int noRows = 0)
        {
            m_CurveFittingTable = new DataTable();
            m_CurveFittingTable.Columns.Add("Coefficients", Type.GetType("System.Double"));
            if (noRows > 0)
            {
                int i;
                var loopTo = noRows - 1;
                for (i = 1; i <= loopTo; i++)
                {
                    var m_row = m_CurveFittingTable.NewRow();
                    m_CurveFittingTable.Rows.Add(m_row);
                }
            }
        }
        public string CreateEquationString()
        {
            var m_EQ = default(string);
            switch (type)
            {
                case CurveFittingTypes.Polynomial:
                    {
                        m_EQ = PolyEquation();
                        break;
                    }
                case CurveFittingTypes.Exponential:
                    {
                        if (m_CurveFittingTable.Rows.Count == 2)
                        {
                            m_EQ = "Equation: y = " + m_CurveFittingTable.Rows[0]["Coefficients"].ToString() + "exp(" + m_CurveFittingTable.Rows[1]["Coefficients"].ToString() + " x )";
                        }

                        break;
                    }
                case CurveFittingTypes.Logarithmic:
                    {
                        if (m_CurveFittingTable.Rows.Count == 2)
                        {
                            m_EQ = "Equation: y = " + m_CurveFittingTable.Rows[0]["Coefficients"].ToString() + " * Ln(x) +" + m_CurveFittingTable.Rows[1]["Coefficients"].ToString();
                        }

                        break;
                    }
                case CurveFittingTypes.Power:
                    {
                        if (m_CurveFittingTable.Rows.Count == 2)
                        {
                            m_EQ = "Equation: y = " + m_CurveFittingTable.Rows[0]["Coefficients"].ToString() + " * x ^ " + m_CurveFittingTable.Rows[1]["Coefficients"].ToString() + ")";
                        }

                        break;
                    }
            }
            return m_EQ;
        }
        private string PolyEquation()
        {
            string txt = "Equation ";
            int t;
            if (m_CurveFittingTable.Rows.Count > 0)
            {
                txt += ": y = " + m_CurveFittingTable.Rows[0]["Coefficients"].ToString() + y_min.ToString();
                var loopTo = m_CurveFittingTable.Rows.Count - 1;
                for (t = 1; t <= loopTo; t++)
                {
                    txt += " ";
                    // If PolyFitting1.Coefficient(t) >= 0.0 Then
                    txt += "+";
                    txt += m_CurveFittingTable.Rows[t]["Coefficients"].ToString(); // ("0.00##")
                    if (t > 0)
                    {
                        txt += "(x - " + x_min.ToString() + ")";
                        if (t > 1)
                        {
                            txt += "^" + t.ToString();
                        }
                    }
                    // End If
                }
            }
            return txt;
        }
        public float GetCurveYValue(float XValue, float minBound, float maxbound)
        {
            float YValue = 0f;
            switch (type)
            {
                case CurveFittingTypes.Polynomial:
                    {
                        if (m_CurveFittingTable.Rows.Count > 0)
                        {
                            if (NotNulls())
                            {
                                YValue = Conversions.ToSingle(Operators.AddObject(m_CurveFittingTable.Rows[0]["Coefficients"], y_min));
                                int t;
                                var loopTo = m_CurveFittingTable.Rows.Count - 1;
                                for (t = 1; t <= loopTo; t++)
                                {
                                    if (t > 0)
                                    {
                                        YValue = Conversions.ToSingle((double)YValue + (double)Operators.MultiplyObject(double.Parse(m_CurveFittingTable.Rows[t]["Coefficients"].ToString()), Math.Pow((double)(XValue - x_min), (double)t)));
                                    }
                                }
                            }
                        }

                        break;
                    }
                case CurveFittingTypes.Exponential:
                    {
                        if (NotNulls())
                        {
                            YValue = Conversions.ToSingle(Operators.MultiplyObject(m_CurveFittingTable.Rows[0]["Coefficients"], Math.Exp(Conversions.ToDouble(Operators.MultiplyObject(m_CurveFittingTable.Rows[1]["Coefficients"], XValue)))));
                        }

                        break;
                    }
                case CurveFittingTypes.Logarithmic:
                    {
                        if (NotNulls())
                        {
                            YValue = Conversions.ToSingle(Operators.AddObject(Operators.MultiplyObject(m_CurveFittingTable.Rows[0]["Coefficients"], Math.Log(XValue)), m_CurveFittingTable.Rows[1]["Coefficients"]));
                        }

                        break;
                    }
                case CurveFittingTypes.Power:
                    {
                        if (NotNulls())
                        {
                            YValue = Conversions.ToSingle(Operators.MultiplyObject(m_CurveFittingTable.Rows[0]["Coefficients"], Math.Pow(XValue, Conversions.ToDouble(m_CurveFittingTable.Rows[1]["Coefficients"]))));
                        }

                        break;
                    }
            }
            // Troncate predictions to zero concentration
            if (YValue < minBound)
                YValue = minBound;
            if (YValue > maxbound)
                YValue = maxbound;
            return YValue;
        }
        private bool NotNulls()
        {
            int i;
            var loopTo = m_CurveFittingTable.Rows.Count - 1;
            for (i = 0; i <= loopTo; i++)
            {
                if (m_CurveFittingTable.Rows[i][0] is DBNull)
                {
                    return false;
                }
            }
            return true;
        }
        public static DataTable CreateGlobalTable(Model mi)
        {
            var m_globalTable = new DataTable();
            m_globalTable.Columns.Add("N_Name", Type.GetType("System.String"));
            m_globalTable.Columns.Add("useQCFunction", Type.GetType("System.String"));
            m_globalTable.Columns.Add("CurveType", Type.GetType("System.String"));
            m_globalTable.Columns.Add("x_Min", Type.GetType("System.Single"));
            m_globalTable.Columns.Add("y_Min", Type.GetType("System.Single"));
            m_globalTable.Columns.Add("Min_Bound", Type.GetType("System.Single"));
            m_globalTable.Columns.Add("Max_Bound", Type.GetType("System.Single"));
            m_globalTable.Columns.Add("DataFilter", Type.GetType("System.String"));
            Node m_Node = mi.firstNode;
            do
            {
                if (m_Node.Tag != null)
                {
                    if (((QualityNodeData)m_Node.Tag).useQCRelation | ((QualityNodeData)m_Node.Tag).maxBound != 99999)
                    {
                        var m_row = m_globalTable.NewRow();
                        m_row["N_Name"] = m_Node.name;
                        m_row["useQCFunction"] = ((QualityNodeData)m_Node.Tag).useQCRelation.ToString();
                        if (((QualityNodeData)m_Node.Tag).m_CurveFitting != null)
                        {
                            m_row["CurveType"] = ((QualityNodeData)m_Node.Tag).m_CurveFitting.type.ToString();
                            m_row["x_Min"] = ((QualityNodeData)m_Node.Tag).m_CurveFitting.x_min;
                            m_row["y_Min"] = ((QualityNodeData)m_Node.Tag).m_CurveFitting.y_min;
                        }
                        m_row["Min_Bound"] = ((QualityNodeData)m_Node.Tag).minBound;
                        m_row["Max_Bound"] = ((QualityNodeData)m_Node.Tag).maxBound;
                        m_row["DataFilter"] = ((QualityNodeData)m_Node.Tag).m_CurveFitting.dataFilter;
                        m_globalTable.Rows.Add(m_row);
                    }
                }
                m_Node = m_Node.next;
            }
            while (!(m_Node is null));
            return m_globalTable;
        }
        public static void LoadGlobalTable(ref Model mi, DataTable m_globalTable)
        {
            int i;
            var loopTo = m_globalTable.Rows.Count - 1;
            for (i = 0; i <= loopTo; i++)
            {
                Node mNode = default;
                string mName = Conversions.ToString(m_globalTable.Rows[i]["N_Name"]);
                mNode = mi.FindNode(mName);
                if (mNode != null)
                {
                    if (((QualityNodeData)mNode.Tag).m_CurveFitting is null)
                    {
                        ((QualityNodeData)mNode.Tag).m_CurveFitting = new CurveFittingUtils();
                    }
                    ((QualityNodeData)mNode.Tag).useQCRelation = Conversions.ToBoolean(m_globalTable.Rows[i]["useQCFunction"]);
                    ((QualityNodeData)mNode.Tag).m_CurveFitting.type = (CurveFittingTypes)Conversions.ToInteger(Enum.Parse(typeof(CurveFittingTypes), Conversions.ToString(m_globalTable.Rows[i]["CurveType"])));
                    ((QualityNodeData)mNode.Tag).m_CurveFitting.x_min = float.Parse(m_globalTable.Rows[i]["x_Min"].ToString());
                    ((QualityNodeData)mNode.Tag).m_CurveFitting.y_min =float.Parse( m_globalTable.Rows[i]["y_Min"].ToString());
                    ((QualityNodeData)mNode.Tag).minBound = float.Parse(m_globalTable.Rows[i]["Min_Bound"].ToString());
                    ((QualityNodeData)mNode.Tag).maxBound = float.Parse(m_globalTable.Rows[i]["Max_Bound"].ToString());
                    if (!(m_globalTable.Rows[i]["DataFilter"] is DBNull))
                        ((QualityNodeData)mNode.Tag).m_CurveFitting.dataFilter = m_globalTable.Rows[i]["DataFilter"].ToString();
                }
            }
        }
    }
}
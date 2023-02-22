using System;
using System.Collections.Generic;
using System.Text;

using Csu.Modsim.ModsimIO;
using Csu.Modsim.ModsimModel;

using System.IO;
using System.Data;
using MODSIMModeling.MODSIMUtils;
using System.Threading.Tasks;

namespace MODSIMModeling.EconomicModeling
{
    public delegate void ProcessMessage(string msg);  // delegate
    public class EconoModeling
    {
        public Model m_Model = new Model();

        private Dictionary<string, CostData> linksCost;
        private string dbPath;
        private MyDBSqlite m_db;


        public event ProcessMessage messageOutRun;     //event

        public EconoModeling(ref Model model, int riparianCost = -999)
        {

            model.Init += OnInitialize;
            model.IterBottom += OnIterationBottom;
            model.IterTop += OnIterationTop;
            model.Converged += OnIterationConverge;
            //model.OnMessage += OnMessageOut;
            //model.OnModsimError += OnMessageOut;
            model.End += OnFinished;

            m_Model = model;

        }

        private void OnMessageOut(string message)
        {
            messageOutRun(message);
        }

        private long AccuracyConversionToReal(long value)
        {
            long newValue = (long)Math.Round(value / m_Model.ScaleFactor, 0);
            return newValue;
        }

        private void OnInitialize()
        {
            InitilizeVariables();
        }

        private void InitilizeVariables()
        {
            linksCost = new Dictionary<string, CostData>(); // links custom cost object

            m_db = new MyDBSqlite(dbPath);
            m_db.messageOut += OnMessageOut;

            dbPath = m_Model.fname.Replace(".xy", ".sqlite");
            if (File.Exists(dbPath))
            {

                m_db.messageOut += OnMessageOut;

                DataTable costInfo = m_db.GetTableFromDB("Select * FROM CostTableInfo", "CostTableInfo");
                foreach (DataRow row in costInfo.Rows)
                {
                    CostData cd;
                    if (row["Type"].ToString() == "Constant")
                        cd = new CostData(double.Parse(row["value"].ToString()));
                    else
                    {
                        cd = new CostData(int.Parse(row["pkid"].ToString()), row["Type"].ToString());
                        cd.SetDataBounds(m_db);
                    }
                    linksCost.Add(row["ObjName"].ToString(), cd);
                }
                messageOutRun($"Found {costInfo.Rows.Count} links with cost info.");

            }
            else
                messageOutRun("ERROR: Database with cost for economic modeling not found.");

        }

        private void OnIterationTop()
        {
            
        }

        private void OnIterationBottom()
        {
            
            //Parallel.ForEach<string>(linksCost.Keys, lName =>
            foreach (string lName in linksCost.Keys) 
            {
                CostData _cd = linksCost[lName];
                Link l = m_Model.FindLink(lName);
                l.mlInfo.cost = _cd.GetCostValue(ref m_db, m_Model.mInfo.CurrentBegOfPeriodDate, (double)l.mlInfo.flow);
            }
            //);

        }

        private void OnIterationConverge()
        {
            
        }

        private void OnFinished()
        {
        }
    }
}


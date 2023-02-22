﻿using System;
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
        private BoundsData m_boundsData;
        private Dictionary<string, BoundsData> linksBounds;

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
            dbPath = m_Model.fname.Replace(".xy", ".sqlite");

            m_db = new MyDBSqlite(dbPath);
            m_db.messageOut += OnMessageOut;
                     

            //Initialize Cost Data
            linksCost = new Dictionary<string, CostData>(); // links custom cost object
            if (File.Exists(dbPath))
            {

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

            //Initialize Link Bounds Data
            linksBounds = new Dictionary<string, BoundsData>(); // links custom cost object
            if (File.Exists(dbPath))
            {

                DataTable linkInfoTbl = m_db.GetTableFromDB("SELECT * FROM Features WHERE BoundsType IS NOT NULL AND BoundsType <> ''", "linkInfoTbl");
                foreach (DataRow row in linkInfoTbl.Rows)
                {
                    BoundsData bd;
                    bd = new BoundsData(row,m_Model.ScaleFactor);
                    linksBounds.Add(bd.lName, bd);
                }
                messageOutRun($"Found {linksBounds.Count} links with capacity info.");

            }
            else
                messageOutRun("ERROR: Database with features data for economic modeling not found.");
        }

        private void OnIterationTop()
        {
            
        }

        private void OnIterationBottom()
        {
            //Set Cost as a function of flow
            //Parallel.ForEach<string>(linksCost.Keys, lName =>
            foreach (string lName in linksCost.Keys) 
            {
                CostData _cd = linksCost[lName];
                Link l = m_Model.FindLink(lName);
                l.mlInfo.cost = _cd.GetCostValue(ref m_db, m_Model.mInfo.CurrentBegOfPeriodDate, (double)l.mlInfo.flow);
            }
            //);


            //Set link bounds
            foreach (string lName in linksBounds.Keys)
            {
                BoundsData _bd = linksBounds[lName];
                //This version uses the link name to get the bounds info - we could use the uid as well.
                Link l = m_Model.FindLink(lName);
                _bd.ProcessLinkBounds(ref m_db, ref l, m_Model.mInfo.CurrentBegOfPeriodDate);
            }
        }

        private void OnIterationConverge()
        {
            
        }

        private void OnFinished()
        {
        }
    }
}


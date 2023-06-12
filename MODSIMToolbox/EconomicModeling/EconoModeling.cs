using System;
using System.Collections.Generic;
using System.Text;

using Csu.Modsim.ModsimIO;
using Csu.Modsim.ModsimModel;

using System.IO;
using System.Data;
using MODSIMModeling.MODSIMUtils;
using System.Threading.Tasks;
using Csu.Modsim.NetworkUtils;

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
        private ModelOutputSupport modsimoutputsupport;
        private int CostIter;

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

            // Setup user output variable to display the cost.
            modsimoutputsupport = m_Model.OutputSupportClass as ModelOutputSupport;
            modsimoutputsupport.AddUserDefinedOutputVariable(m_Model, "Cost", true, false, "Cost");
            modsimoutputsupport.AddCurrentUserLinkOutput += AddMyLinkOutput;

            // Setup user output variable to display the cost in reservoirs.
            modsimoutputsupport.AddUserDefinedOutputVariable(m_Model, "Cost", false, true, "Cost");
            modsimoutputsupport.AddCurrentUserReservoirOutput += AddMyResOutput;

        }

        private void AddMyResOutput(Node node, DataRow row)
        {
            if (node.mnInfo.balanceLinks != null)
            {
                Link tgtLink = node.mnInfo.balanceLinks.link;
                if (node.m.resBalance.incrPriorities.Length > 1)
                {
                    //Assumes that a single layer is used in the reservoir
                    tgtLink = node.mnInfo.balanceLinks.next.link;
                }
                row["Cost"] = tgtLink.mlInfo.cost;
            }
        }

        private void AddMyLinkOutput(Link link, DataRow row)
        {
            row["Cost"] = link.mlInfo.cost;
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
                int resCount = 0;
                DataTable costInfo = m_db.GetTableFromDB("Select * FROM CostTableInfo", "CostTableInfo");
                foreach (DataRow row in costInfo.Rows)
                {
                    CostData cd;
                    string lName = row["ObjName"].ToString();
                    bool isRes = m_Model.NodeNameExists(lName, silent: true);
                    Link lTrgt = null;
                    if (isRes)
                    {
                        Node res = m_Model.FindNode(lName);
                        //note: first link carries the min storage;
                        lTrgt = res.mnInfo.balanceLinks.link;
                        if (res.m.resBalance.incrPriorities.Length > 1)
                        {
                            //Assumes that a single layer is used in the reservoir
                            lTrgt = res.mnInfo.balanceLinks.next.link;
                        }
                        lName = lTrgt.name + "_Trgt";
                        lTrgt.name = lName;
                        resCount += 1;
                    }
                    if (row["Type"].ToString() == "Constant")
                        cd = new CostData(double.Parse(row["value"].ToString()));
                    else
                    {
                        cd = new CostData(int.Parse(row["pkid"].ToString()), row["Type"].ToString());
                        cd.SetDataBounds(m_db);
                    }
                    if (isRes && lTrgt != null)
                    {
                        cd.totFlowLink = lTrgt.to.OutflowLinks.link;
                    }
                    linksCost.Add(lName, cd);
                }
                messageOutRun($"Found {costInfo.Rows.Count} links with cost info.");
                messageOutRun($"\t and {resCount} reservoir nodes with cost info.");

                //Adjust cost for additional storage in the reservoir nodes - based on the cost structure
                //  +8,000,000 is the leaste cost in this network
                int resExcCount = 0;
                foreach (Node res in m_Model.Nodes_Reservoirs)
                {
                    res.mnInfo.excessStoLink.mlInfo.cost = 8000000 + resExcCount;
                    resExcCount++;
                }

            }
            else
                messageOutRun("ERROR: Database with cost for economic modeling not found.");

            //Initialize Link Bounds Data
            linksBounds = new Dictionary<string, BoundsData>(); // links custom cost object
            if (File.Exists(dbPath))
            {

                DataTable linkInfoTbl = m_db.GetTableFromDB("SELECT * FROM Features WHERE BoundsType IS NOT NULL AND BoundsType <> '' AND MOD_Type <> 'Reservoir'", "linkInfoTbl");
                foreach (DataRow row in linkInfoTbl.Rows)
                {
                    BoundsData bd;
                    bd = new BoundsData(row,m_Model.ScaleFactor);
                    linksBounds.Add(bd.lName, bd);
                }
                messageOutRun($"Found {linksBounds.Count} links with capacity info.");

                DataTable ResInfoTbl = m_db.GetTableFromDB("SELECT * FROM Features WHERE BoundsType IS NOT NULL AND BoundsType <> '' AND MOD_Type = 'Reservoir'", "linkInfoTbl");
                foreach (DataRow row in ResInfoTbl.Rows)
                {
                    BoundsData bd;
                    Node res = m_Model.FindNode(row["MOD_Name"].ToString());
                    Link lresmin = res.mnInfo.balanceLinks.link;
                    bd = new BoundsData(row, m_Model.ScaleFactor,lresmin.name,lresmin.uid.ToString());
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
            
            /// bool costConverged= true;
            ///SetCostsBasedOnFlows(ref costConverged);


            //Set link bounds
            foreach (string lName in linksBounds.Keys)
            {

                BoundsData _bd = linksBounds[lName];
                //This version uses the link name to get the bounds info - we could use the uid as well.
                Link l = m_Model.FindLink(lName);
                _bd.ProcessLinkBounds(ref m_db, ref l, m_Model.mInfo.CurrentBegOfPeriodDate);
                //if (m_Model.mInfo.Iteration > 4)
                //{
                //    _bd.ProcessLinkBounds(ref m_db, ref l, m_Model.mInfo.CurrentBegOfPeriodDate);
                //}
                //else
                //    l.mlInfo.lo = 0;
            }
        }

        private void OnIterationConverge()
        {
            bool costConverged= true;
            SetCostsBasedOnFlows(ref costConverged);
            
            //Check for cost convergence
            if(CostIter>=m_Model.maxit)
                costConverged = true;   

            m_Model.mInfo.convg = costConverged;

            if (m_Model.mInfo.convg)
            {
                CostIter = 0;
                foreach (string lName in linksCost.Keys)
                {
                    CostData _cd = linksCost[lName];
                    _cd.ResetFlow();

                }
            }
            else
            {
                CostIter++;
                Console.WriteLine($"\tCost iteration: {CostIter}");
            }

        }

        private void SetCostsBasedOnFlows(ref bool costConverged)
        {
            //Set Cost as a function of flow
            //Parallel.ForEach<string>(linksCost.Keys, lName =>
            foreach (string lName in linksCost.Keys)
            {
                CostData _cd = linksCost[lName];
                Link l = m_Model.FindLink(lName);
                double totFlow = (double)l.mlInfo.flow / m_Model.ScaleFactor / 1000D;
                if(_cd.totFlowLink!=null)
                {
                    totFlow = (double)_cd.totFlowLink.mlInfo.flow / m_Model.ScaleFactor / 1000D;
                }
                l.mlInfo.cost = _cd.GetCostValue(ref m_db, m_Model.mInfo.CurrentBegOfPeriodDate, totFlow, ref costConverged);

            }
            //);
        }

        private void OnFinished()
        {
        }
    }
}


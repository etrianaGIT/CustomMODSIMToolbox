using Csu.Modsim.ModsimModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MODSIMModeling.Preprocessing
{
    public class CostStructure
    {
        public delegate void ProcessMessage(string msg);  // delegate
        public Model myModel;
        private Dictionary<string,int> costBraket;

        public CostStructure(ref Model m_Model)
        {
            myModel = m_Model;
            //Create cost structure 
            //The key should be the end of the link name.
            costBraket = new Dictionary<string, int>
            {
                { "_MinRel", -30000 },
                { "_RampLimitDw", -25000 },
                { "_StarfitRel", -15000 },
                { "_Spill", 35000 },
                { "_Spill2", 40000 },
                { "_Spill3", 31000 }
            };
        }
        public void ApplyLinksCostStructure()
        {
            foreach(Link l in myModel.Links_Real)
            {
                foreach(string key in costBraket.Keys)
                {
                    if (l.name.EndsWith(key))
                    {
                        //reset the cost of the link to the cost bracket value and the link number
                        //  Need to check that the potential number of links is not greater than the cost breaks
                        if (costBraket[key] == 0)
                            l.m.cost = 0;
                        else
                            l.m.cost = costBraket[key] + l.number;
                        break;
                    }
                }
            }
        }
    }
}

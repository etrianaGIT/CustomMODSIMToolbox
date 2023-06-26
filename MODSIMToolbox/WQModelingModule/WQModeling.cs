using Csu.Modsim.ModsimModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTI.CWR.MODSIM.WQModelingModule
{
    public class WQModeling
    {
        public Model myModel;
		public WQModeling(ref Model m_Model)
		{
			m_Model.Init += OnInitialize;
			m_Model.IterBottom += OnIterationBottom;
			m_Model.IterTop += OnIterationTop;
			m_Model.Converged += OnIterationConverge;
			m_Model.End += OnFinished;

			myModel = m_Model;
		}

		private void OnInitialize()
		{
			
		}

		private void OnIterationTop()
		{

		}

		private void OnIterationBottom()
		{
		}

		private void OnIterationConverge()
		{
			
		}

		private void OnFinished()
		{
		}
	}
}

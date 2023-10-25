using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Csu.Modsim.ModsimIO;
using Csu.Modsim.ModsimModel;

namespace MODSIMModeling.DiversionRotation
{
    public class DemRotation
    {

	
		public Model myModel;
		// Setting the number of period to reopen link
		private int noTSOFF=6;
		public  DemRotation(ref Model m_Model)
		{
			m_Model.Init += OnInitialize;
			m_Model.IterBottom += OnIterationBottom;
			m_Model.IterTop += OnIterationTop;
			m_Model.Converged += OnIterationConverge;
			m_Model.End += OnFinished;
			
			myModel = m_Model;
		}

		private  void OnInitialize()
		{
			foreach (Link l in myModel.Links_Real)
			{
				if (l.m.lnkallow > 0)
				{
					l.Tag = (int)0;
				}
			}
		}

		private  void OnIterationTop()
		{
			
		}

		private  void OnIterationBottom()
		{
		}

		private  void OnIterationConverge()
		{
			foreach (Link l in myModel.Links_Real)
			{
				if (l.m.lnkallow > 0)
				{
					if (l.mrlInfo.lnktot == l.m.lnkallow)
					{
						//It has reach the seasoal capacity
						l.Tag = (int)l.Tag + 1;
					}
					//Resets when has reached the number of periods after it reached the seasonal capacity
					//Resets when the month is January
					if ((int)l.Tag == noTSOFF || myModel.TimeStepManager.Index2Date(myModel.mInfo.CurrentModelTimeStepIndex,TypeIndexes.ModelIndex).Month == 1)
					{
						//resetting the link to get water
						l.mrlInfo.lnktot = 0;
						l.Tag = 0;
					}
				}
			}
		}

		private  void OnFinished()
		{
		}
	}

}

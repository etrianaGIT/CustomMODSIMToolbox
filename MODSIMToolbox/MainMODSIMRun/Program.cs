﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Csu.Modsim.ModsimIO;
using Csu.Modsim.ModsimModel;
using DiversionRotation;

namespace MainMODSIMRun
{
    class Program
    {
        public static Model myModel = new Model();
		public static DemRotation Demtool;
		static void Main(string[] CmdArgs)
		{
			string FileName = CmdArgs[0];
			myModel.OnMessage += OnMessage;
			myModel.OnModsimError += OnError;

			XYFileReader.Read(myModel, FileName);

			//Adding 'plug-ins'
			Demtool = new DemRotation(ref myModel);

			Modsim.RunSolver(myModel);
		}

		private static void OnMessage(string message)
		{
			Console.WriteLine(message);
		}

		private static void OnError(string message)
		{
			Console.WriteLine(message);
		}
	}
}

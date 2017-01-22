using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.IO;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{	
	public class SecurityDefinitionImporter
	{
		public static List<Security> Load(string file)
		{
			var securities = new List<Security>();

			using (var streamReader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)))
			{
				string line;
				while ((line = streamReader.ReadLine()) != null)
				{
					var x = new SecurityDefinition();
					x.Decode(Encoding.UTF8.GetBytes(line));
					securities.Add(x.ToSecurity());
				}
			}

			return securities;
		}

		public static Security Load(string file, string contract)
		{
			using (var streamReader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)))
			{
				string line;
				while ((line = streamReader.ReadLine()) != null)
				{
					var x = new SecurityDefinition();
					x.Decode(Encoding.UTF8.GetBytes(line));
					var sec = x.ToSecurity();
					if (sec.Contract == contract)
						return sec;
				}
			}

			return null;
		}
	}	
}
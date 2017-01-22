using System;

using Circus.Cme;

namespace Tests.Cme
{
	public class SecurityDefinitionImporterTest
	{
		public SecurityDefinitionImporterTest()
		{
			Test();
		}

		public void Test()
		{
			var sdi = SecurityDefinitionImporter.Load("Cme/Resources/secdef.dat");
		}
	}
}

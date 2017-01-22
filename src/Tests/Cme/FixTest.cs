using System;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.Linq;
using System.Diagnostics;

using Circus.Common;
using Circus.Server;
using Circus.Cme;

namespace Tests.Cme
{
	public class FixTest
	{
		public FixTest()
		{
			Logon();

			// TODO: and so on
		}

		public void Logon()
		{
			var l1 = new Logon("passw\x01rd");
			var lx1 = l1.Encode();
			var l2 = new Logon();
			l2.Decode(lx1);
			var lx2 = l2.Encode();

			Debug.Assert(lx1.SequenceEqual(lx2));
		}
	}
}

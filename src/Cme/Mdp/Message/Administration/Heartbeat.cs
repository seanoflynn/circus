using System;

using Circus.Fix;

namespace Circus.Cme
{	
	public class MdpHeartbeat : MdpMessage
	{						
		public MdpHeartbeat() : base(MessageType.Heartbeat)
		{ }
	}
}

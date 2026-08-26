using System;

namespace Seprays.MarkVision
{
	public sealed class MarkLocateCompletedEventArgs : EventArgs
	{
		public MarkLocateResult Result { get; }

		public MarkLocateCompletedEventArgs(MarkLocateResult result)
		{
			Result = result;
		}
	}
}

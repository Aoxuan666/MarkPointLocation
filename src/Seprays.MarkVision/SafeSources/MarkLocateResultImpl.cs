using System;
using System.Collections.Generic;
using System.Drawing;

namespace Seprays.MarkVision
{
	[Serializable]
	public sealed class MarkLocateResult
	{
		public bool Success { get; set; }

		public string Message { get; set; }

		public PointF ModelPoint { get; set; }

		public double Score { get; set; }

		public double AngleRadians { get; set; }

		public double Scale { get; set; } = 1.0;

		public bool CircleFound { get; set; }

		public PointF CirclePoint { get; set; }

		public float CircleRadius { get; set; }

		public List<List<PointF>> MatchedContours { get; set; } = new List<List<PointF>>();

		public static MarkLocateResult Failed(string message)
		{
			return new MarkLocateResult
			{
				Success = false,
				Message = (message ?? "定位失败。"),
				Score = 0.0
			};
		}
	}
}

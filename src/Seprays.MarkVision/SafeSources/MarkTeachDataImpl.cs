using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;

namespace Seprays.MarkVision
{
	[Serializable]
	public sealed class MarkTeachData
	{
		public bool HasModel { get; set; }

		public byte[] ModelBytes { get; set; }

		[field: OptionalField(VersionAdded = 2)]
		public List<PointF> FeaturePoints { get; set; } = new List<PointF>();

		public List<List<PointF>> ModelContours { get; set; } = new List<List<PointF>>();

		public Size TemplateSize { get; set; }

		public DateTime CreatedAt { get; set; }

		public void Clear()
		{
			HasModel = false;
			ModelBytes = null;
			FeaturePoints.Clear();
			ModelContours.Clear();
			TemplateSize = Size.Empty;
			CreatedAt = default(DateTime);
		}

		public MarkTeachData DeepClone()
		{
			return new MarkTeachData
			{
				HasModel = HasModel,
				ModelBytes = ((ModelBytes == null) ? null : ((byte[])ModelBytes.Clone())),
				FeaturePoints = ((FeaturePoints == null) ? new List<PointF>() : new List<PointF>(FeaturePoints)),
				ModelContours = ((ModelContours == null) ? new List<List<PointF>>() : ModelContours.Select((List<PointF> contour) => new List<PointF>(contour ?? new List<PointF>())).ToList()),
				TemplateSize = TemplateSize,
				CreatedAt = CreatedAt
			};
		}
	}
}

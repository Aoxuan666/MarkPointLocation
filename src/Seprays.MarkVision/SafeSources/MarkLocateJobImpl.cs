using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;

namespace Seprays.MarkVision
{
	[Serializable]
	public sealed class MarkLocateJob
	{
		public int Version { get; set; } = 3;

		public RectangleF TemplateRoi { get; set; }

		/// <summary>
		/// Shape of the template ROI. The page starts new ROIs as circles.
		/// </summary>
		[field: OptionalField(VersionAdded = 3)]
		public RegionShape TemplateRoiShape { get; set; } = RegionShape.Circle;

		public RectangleF SearchRoi { get; set; }

		[field: OptionalField(VersionAdded = 2)]
		public bool SearchRoiEnabled { get; set; } = true;

		/// <summary>
		/// Set by the host application. The setup page never changes this flag.
		/// </summary>
		[field: OptionalField(VersionAdded = 3)]
		public bool EnableTemplateMatching { get; set; } = true;

		/// <summary>
		/// Set by the host application to request circle detection/refinement.
		/// </summary>
		[field: OptionalField(VersionAdded = 3)]
		public bool EnableCircleDetection { get; set; }

		public MarkMatchParameters Parameters { get; set; } = new MarkMatchParameters();

		public List<MaskStroke> MaskStrokes { get; set; } = new List<MaskStroke>();

		public MarkTeachData TeachData { get; set; } = new MarkTeachData();

		public MarkLocateJob DeepClone()
		{
			return new MarkLocateJob
			{
				Version = Version,
				TemplateRoi = TemplateRoi,
				TemplateRoiShape = TemplateRoiShape,
				SearchRoi = SearchRoi,
				SearchRoiEnabled = SearchRoiEnabled,
				EnableTemplateMatching = EnableTemplateMatching,
				EnableCircleDetection = EnableCircleDetection,
				Parameters = (Parameters ?? new MarkMatchParameters()).DeepClone(),
				MaskStrokes = ((MaskStrokes == null) ? new List<MaskStroke>() : MaskStrokes.Select((MaskStroke stroke) => stroke?.DeepClone() ?? new MaskStroke()).ToList()),
				TeachData = (TeachData ?? new MarkTeachData()).DeepClone()
			};
		}

		public void Normalize(Size imageSize)
		{
			Parameters = Parameters ?? new MarkMatchParameters();
			if (Version < 2)
			{
				Parameters.EdgeSigma = 1.2;
				Parameters.EdgeLowThreshold = 15.0;
				Parameters.EdgeHighThreshold = 35.0;
				Parameters.FeatureMinDistancePx = 6.0;
				Parameters.MaxFeaturePoints = 12000;
				SearchRoiEnabled = !SearchRoi.IsEmpty;
				Version = 2;
			}
			if (Version < 3)
			{
				EnableTemplateMatching = true;
				EnableCircleDetection = Parameters != null && Parameters.EnableCircleRefinement;
				Version = 3;
			}
			if (!Enum.IsDefined(typeof(RegionShape), TemplateRoiShape))
			{
				TemplateRoiShape = RegionShape.Circle;
			}
			Parameters.Normalize();
			MaskStrokes = MaskStrokes ?? new List<MaskStroke>();
			TeachData = TeachData ?? new MarkTeachData();
			TeachData.FeaturePoints = TeachData.FeaturePoints ?? new List<PointF>();
			TeachData.ModelContours = TeachData.ModelContours ?? new List<List<PointF>>();
			if (imageSize.Width > 0 && imageSize.Height > 0)
			{
				RectangleF imageBounds = new RectangleF(0f, 0f, imageSize.Width, imageSize.Height);
				TemplateRoi = NormalizeRoi(TemplateRoi, imageBounds, 16f);
				SearchRoi = NormalizeRoi(SearchRoi, imageBounds, 16f);
				if (TemplateRoi.IsEmpty)
				{
					float width = Math.Max(32f, (float)imageSize.Width * 0.3f);
					float height = Math.Max(32f, (float)imageSize.Height * 0.3f);
					TemplateRoi = RectangleF.Intersect(new RectangleF(((float)imageSize.Width - width) * 0.5f, ((float)imageSize.Height - height) * 0.5f, width, height), imageBounds);
				}
				if (SearchRoi.IsEmpty)
				{
					SearchRoi = ExpandRoi(TemplateRoi, 100f, imageBounds);
				}
			}
		}

		private static RectangleF NormalizeRoi(RectangleF roi, RectangleF bounds, float minSize)
		{
			if (roi.Width <= 0f || roi.Height <= 0f)
			{
				return RectangleF.Empty;
			}
			RectangleF clipped = RectangleF.Intersect(roi, bounds);
			if (!(clipped.Width < minSize) && !(clipped.Height < minSize))
			{
				return clipped;
			}
			return RectangleF.Empty;
		}

		private static RectangleF ExpandRoi(RectangleF roi, float margin, RectangleF bounds)
		{
			if (roi.IsEmpty)
			{
				return bounds;
			}
			RectangleF expanded = RectangleF.FromLTRB(roi.Left - margin, roi.Top - margin, roi.Right + margin, roi.Bottom + margin);
			return RectangleF.Intersect(expanded, bounds);
		}
	}
}

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
		public int Version { get; set; } = 4;

		public RectangleF TemplateRoi { get; set; }

		/// <summary>
		/// Shape of the template ROI. The page starts new ROIs as rectangles.
		/// </summary>
		[field: OptionalField(VersionAdded = 3)]
		public RegionShape TemplateRoiShape { get; set; } = RegionShape.Rectangle;

		public RectangleF SearchRoi { get; set; }

		[field: OptionalField(VersionAdded = 2)]
		public bool SearchRoiEnabled { get; set; }

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
				Parameters.FeatureMinDistancePx = 2.0;
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
			if (Version < 4)
			{
				// Preserve explicit manual thresholds saved by pre-v4 recipes.
				Parameters.AutoEdgeThreshold = false;
				Version = 4;
			}
			if (!Enum.IsDefined(typeof(RegionShape), TemplateRoiShape))
			{
				TemplateRoiShape = RegionShape.Rectangle;
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
				if (!TemplateRoi.IsEmpty && TemplateRoiShape == RegionShape.Circle)
				{
					// Keep one circular domain for extraction, filtering and display.
					float side = Math.Min(Math.Max(TemplateRoi.Width, TemplateRoi.Height), Math.Min(imageBounds.Width, imageBounds.Height));
					float centerX = TemplateRoi.Left + TemplateRoi.Width * 0.5f;
					float centerY = TemplateRoi.Top + TemplateRoi.Height * 0.5f;
					float left = Math.Max(imageBounds.Left, Math.Min(imageBounds.Right - side, centerX - side * 0.5f));
					float top = Math.Max(imageBounds.Top, Math.Min(imageBounds.Bottom - side, centerY - side * 0.5f));
					TemplateRoi = new RectangleF(left, top, side, side);
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
	}
}

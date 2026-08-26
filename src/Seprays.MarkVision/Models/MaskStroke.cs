using System;
using System.Drawing;

namespace Seprays.MarkVision
{
	[Serializable]
	public sealed class MaskStroke
	{
		public PointF Center { get; set; }

		public float Radius { get; set; }

		/// <summary>
		/// Shape used for this mask area. Circle is the default for old jobs.
		/// </summary>
		public RegionShape Shape { get; set; } = RegionShape.Circle;

		/// <summary>
		/// Width and height of the area in source-image pixels. When absent in a
		/// legacy job, Radius is used to reconstruct a circular stroke.
		/// </summary>
		public float Width { get; set; }

		public float Height { get; set; }

		public MaskStrokeMode Mode { get; set; }

		public int GroupId { get; set; }

		public RectangleF GetBounds()
		{
			float width = (Width > 0f) ? Width : Radius * 2f;
			float height = (Height > 0f) ? Height : Radius * 2f;
			RegionShape shape = Enum.IsDefined(typeof(RegionShape), Shape) ? Shape : RegionShape.Circle;
			return RegionShapeGeometry.NormalizeBounds(new RectangleF(Center.X - width * 0.5f, Center.Y - height * 0.5f, width, height), shape);
		}

		public MaskStroke DeepClone()
		{
			return new MaskStroke
			{
				Center = Center,
				Radius = Radius,
				Shape = Shape,
				Width = Width,
				Height = Height,
				Mode = Mode,
				GroupId = GroupId
			};
		}
	}
}

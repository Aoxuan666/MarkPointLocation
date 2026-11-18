using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

namespace Seprays.MarkVision
{
	public sealed class MarkLocatorSdk
	{
		public MarkLocateJob OpenSetupDialog(Bitmap image, MarkLocateJob currentJob)
		{
			if (image == null) throw new ArgumentNullException(nameof(image));
			if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
				return ShowSetupDialog(image, currentJob);

			MarkLocateJob editedJob = null;
			ExceptionDispatchInfo error = null;
			Thread thread = new Thread(() =>
			{
				try { editedJob = ShowSetupDialog(image, currentJob); }
				catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			thread.Join();
			error?.Throw();
			return editedJob;
		}

		public MarkLocateResult RunInspection(Bitmap image, MarkLocateJob job, bool returnResultImage = false)
		{
			if (image == null) return MarkLocateResult.Failed("待检测图片为空。");
			MarkLocateResult result = job == null
				? MarkLocateResult.Failed("Mark 定位 Job 为空。")
				: new HalconMarkLocator().Locate(image, job);
			if (returnResultImage) result.ResultImage = RenderResultImage(image, result);
			return result;
		}

		private static MarkLocateJob ShowSetupDialog(Bitmap image, MarkLocateJob currentJob)
		{
			using (MarkSetupForm form = new MarkSetupForm(image, currentJob))
				return form.ShowDialog() == DialogResult.OK ? form.Job?.DeepClone() : null;
		}

		private static Bitmap RenderResultImage(Bitmap source, MarkLocateResult result)
		{
			// Explicitly use a drawable format, including for indexed camera images.
			Bitmap image = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
			try
			{
				using (Graphics graphics = Graphics.FromImage(image))
				using (Pen contourPen = new Pen(Color.DeepSkyBlue, 2f))
				using (Pen centerPen = new Pen(Color.LimeGreen, 2f))
				using (Pen circlePen = new Pen(Color.Orange, 2f))
				{
					graphics.DrawImageUnscaled(source, 0, 0);
					graphics.SmoothingMode = SmoothingMode.AntiAlias;
					bool hasContours = false;
					foreach (List<PointF> contour in result.MatchedContours ?? new List<List<PointF>>())
					{
						if (contour == null) continue;
						PointF? previous = null;
						foreach (PointF point in contour)
						{
							if (!IsFinite(point)) { previous = null; continue; }
							hasContours = true;
							if (previous.HasValue) graphics.DrawLine(contourPen, previous.Value, point);
							previous = point;
						}
					}
					if (result.Success || hasContours) DrawCross(graphics, centerPen, result.ModelPoint);
					if (result.CircleFound && IsFinite(result.CirclePoint) &&
						result.CircleRadius > 0 && !float.IsInfinity(result.CircleRadius))
					{
						float radius = result.CircleRadius;
						graphics.DrawEllipse(circlePen, result.CirclePoint.X - radius,
							result.CirclePoint.Y - radius, radius * 2, radius * 2);
						DrawCross(graphics, circlePen, result.CirclePoint);
					}
				}
				return image;
			}
			catch
			{
				image.Dispose();
				throw;
			}
		}

		private static void DrawCross(Graphics graphics, Pen pen, PointF point)
		{
			if (!IsFinite(point)) return;
			graphics.DrawLine(pen, point.X - 10, point.Y, point.X + 10, point.Y);
			graphics.DrawLine(pen, point.X, point.Y - 10, point.X, point.Y + 10);
		}

		private static bool IsFinite(PointF point)
		{
			return !float.IsNaN(point.X) && !float.IsInfinity(point.X) &&
				!float.IsNaN(point.Y) && !float.IsInfinity(point.Y);
		}
	}
}

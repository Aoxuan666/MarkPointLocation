using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using HalconDotNet;

namespace Seprays.MarkVision
{
	public sealed class HalconMarkLocator
	{
		private sealed class HalconImageLease : IDisposable
		{
			private GCHandle _grayHandle;

			public HImage Image { get; }

			private HalconImageLease(HImage image, GCHandle grayHandle)
			{
				Image = image;
				_grayHandle = grayHandle;
			}

			public static HalconImageLease FromBitmap(Bitmap source)
			{
				int width = source.Width;
				int height = source.Height;
				byte[] gray = new byte[width * height];
				using (Bitmap converted = new Bitmap(width, height, PixelFormat.Format24bppRgb))
				{
					using (Graphics graphics = Graphics.FromImage(converted))
					{
						graphics.DrawImageUnscaled(source, 0, 0);
					}
					BitmapData data = converted.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
					try
					{
						int stride = Math.Abs(data.Stride);
						byte[] rgb = new byte[stride * height];
						Marshal.Copy(data.Scan0, rgb, 0, rgb.Length);
						for (int y = 0; y < height; y++)
						{
							int sourceRow = ((data.Stride < 0) ? ((height - 1 - y) * stride) : (y * stride));
							int targetRow = y * width;
							for (int x = 0; x < width; x++)
							{
								int offset = sourceRow + x * 3;
								gray[targetRow + x] = (byte)(0.114 * (double)(int)rgb[offset] + 0.587 * (double)(int)rgb[offset + 1] + 0.299 * (double)(int)rgb[offset + 2]);
							}
						}
					}
					finally
					{
						converted.UnlockBits(data);
					}
				}
				GCHandle handle = GCHandle.Alloc(gray, GCHandleType.Pinned);
				HImage image = new HImage();
				try
				{
					image.GenImage1("byte", width, height, handle.AddrOfPinnedObject());
					return new HalconImageLease(image, handle);
				}
				catch
				{
					image.Dispose();
					handle.Free();
					throw;
				}
			}

			public void Dispose()
			{
				Image?.Dispose();
				if (_grayHandle.IsAllocated)
				{
					_grayHandle.Free();
				}
			}
		}

		public MarkTeachData Teach(Bitmap source, MarkLocateJob inputJob)
		{
			if (source == null)
			{
				throw new ArgumentNullException("source");
			}
			if (inputJob == null)
			{
				throw new ArgumentNullException("inputJob");
			}
			MarkLocateJob job = inputJob.DeepClone();
			job.Normalize(source.Size);
			Rectangle templateRect = ToPixelRectangle(job.TemplateRoi, source.Size);
			if (templateRect.Width < 16 || templateRect.Height < 16)
			{
				throw new InvalidOperationException("模板区域过小，请重新拖拽模板框。");
			}
			List<PointF> featurePoints = ((job.TeachData?.FeaturePoints == null) ? new List<PointF>() : new List<PointF>(job.TeachData.FeaturePoints));
			if (CountRealPoints(featurePoints) < 12)
			{
				featurePoints = FilterFeaturePointsInRoi(ExtractFeaturePoints(source, job), job.MaskStrokes, job.TemplateRoi, job.TemplateRoiShape);
			}
			if (CountRealPoints(featurePoints) < 12)
			{
				throw new InvalidOperationException("有效外轮廓特征点不足，请调整模板 ROI、边缘阈值或擦除范围。");
			}
			HObject contoursXld = null;
			HTuple modelId = null;
			try
			{
				GenContoursFromFeaturePoints(featurePoints, out contoursXld);
				HTuple numLevels = ((job.Parameters.NumLevels <= 0) ? new HTuple("auto") : new HTuple(job.Parameters.NumLevels));
				string metric = (job.Parameters.UsePolarity ? "ignore_local_polarity" : "ignore_global_polarity");
				HOperatorSet.CreateScaledShapeModelXld(contoursXld, numLevels, DegreesToRadians(job.Parameters.AngleStartDegrees), DegreesToRadians(job.Parameters.AngleExtentDegrees), "auto", job.Parameters.ScaleMin, job.Parameters.ScaleMax, "auto", "auto", metric, job.Parameters.MinContrast, out modelId);
				SetModelOriginToTemplateCenter(modelId, featurePoints, job.TemplateRoi);
				List<List<PointF>> contours = ReadModelContours(modelId);
				if (contours.Sum((List<PointF> contour) => contour.Count) < 12)
				{
					throw new InvalidOperationException("擦除后可用模型点不足，请减小擦除范围或调整边缘参数。");
				}
				return new MarkTeachData
				{
					HasModel = true,
					ModelBytes = ExportShapeModel(modelId),
					FeaturePoints = new List<PointF>(featurePoints),
					ModelContours = contours,
					TemplateSize = templateRect.Size,
					CreatedAt = DateTime.Now
				};
			}
			finally
			{
				ClearShapeModel(modelId);
				contoursXld?.Dispose();
			}
		}

		public List<PointF> ExtractFeaturePoints(Bitmap source, MarkLocateJob inputJob)
		{
			if (source == null)
			{
				throw new ArgumentNullException("source");
			}
			if (inputJob == null)
			{
				throw new ArgumentNullException("inputJob");
			}
			MarkLocateJob job = inputJob.DeepClone();
			job.Normalize(source.Size);
			Rectangle templateRect = ToPixelRectangle(job.TemplateRoi, source.Size);
			if (templateRect.Width < 16 || templateRect.Height < 16)
			{
				throw new InvalidOperationException("模板区域过小，请重新拖拽模板框。");
			}
			using (HalconImageLease image = HalconImageLease.FromBitmap(source))
			{
				HObject filtered = null;
				HObject templateRegion = null;
				HObject reduced = null;
				HObject edges = null;
				HObject selectedEdges = null;
				try
				{
					HOperatorSet.MeanImage(image.Image, out filtered, job.Parameters.TemplateMeanSize, job.Parameters.TemplateMeanSize);
					CreateTemplateRegion(job.TemplateRoi, job.TemplateRoiShape, out templateRegion);
					HOperatorSet.ReduceDomain(filtered, templateRegion, out reduced);
					HOperatorSet.EdgesSubPix(reduced, out edges, "canny", job.Parameters.EdgeSigma, job.Parameters.EdgeLowThreshold, job.Parameters.EdgeHighThreshold);
					HOperatorSet.SelectShapeXld(edges, out selectedEdges, "contlength", "and", Math.Max(6.0, job.Parameters.FeatureMinDistancePx * 1.5), 999999.0);
					return ReadFeaturePoints(selectedEdges, job.Parameters.FeatureMinDistancePx, job.Parameters.MaxFeaturePoints)
						.Where(point => RegionShapeGeometry.Contains(point, job.TemplateRoi, job.TemplateRoiShape) || IsContourSeparator(point))
						.ToList();
				}
				finally
				{
					selectedEdges?.Dispose();
					edges?.Dispose();
					reduced?.Dispose();
					templateRegion?.Dispose();
					filtered?.Dispose();
				}
			}
		}

		public static List<PointF> FilterFeaturePoints(IEnumerable<PointF> source, IEnumerable<MaskStroke> strokes)
		{
			return FilterFeaturePointsInRoi(source, strokes, RectangleF.Empty, RegionShape.Rectangle);
		}

		internal static List<PointF> FilterFeaturePointsInRoi(IEnumerable<PointF> source, IEnumerable<MaskStroke> strokes, RectangleF templateRoi, RegionShape templateShape)
		{
			List<MaskStroke> maskStrokes = ((strokes == null) ? new List<MaskStroke>() : strokes.Where((MaskStroke maskStroke) => maskStroke != null && !maskStroke.GetBounds().IsEmpty).ToList());
			List<PointF> result = new List<PointF>();
			bool segmentHasPoint = false;
			foreach (PointF point in source ?? Enumerable.Empty<PointF>())
			{
				if (IsContourSeparator(point))
				{
					AppendSeparator(result, ref segmentHasPoint);
					continue;
				}
				if (!templateRoi.IsEmpty && !RegionShapeGeometry.Contains(point, templateRoi, templateShape))
				{
					AppendSeparator(result, ref segmentHasPoint);
					continue;
				}
				bool visible = true;
				foreach (MaskStroke stroke in maskStrokes)
				{
					if (RegionShapeGeometry.Contains(point, stroke.GetBounds(), stroke.Shape))
					{
						visible = stroke.Mode == MaskStrokeMode.Restore;
					}
				}
				if (visible)
				{
					result.Add(point);
					segmentHasPoint = true;
				}
				else
				{
					AppendSeparator(result, ref segmentHasPoint);
				}
			}
			TrimSeparators(result);
			return result;
		}

		public MarkLocateResult Locate(Bitmap source, MarkLocateJob inputJob)
		{
			if (source == null)
			{
				return MarkLocateResult.Failed("待检测图片为空。");
			}
			if (inputJob == null)
			{
				return MarkLocateResult.Failed("Mark 定位参数为空。");
			}
			MarkLocateJob job = inputJob.DeepClone();
			job.Normalize(source.Size);
			bool circleEnabled = job.EnableCircleDetection;
			if (!job.EnableTemplateMatching)
			{
				return circleEnabled ? LocateCircleOnly(source, job) : MarkLocateResult.Failed("模板匹配和找圆均未启用。");
			}
			if (!job.TeachData.HasModel || job.TeachData.ModelBytes == null || job.TeachData.ModelBytes.Length == 0)
			{
				return MarkLocateResult.Failed("尚未创建带掩膜的模板模型。");
			}
			using (HalconImageLease image = HalconImageLease.FromBitmap(source))
			{
				HObject filtered = null;
				HObject searchRegion = null;
				HObject searchImage = null;
				HTuple modelId = null;
				try
				{
					int meanSize = job.Parameters.SearchMeanSize;
					HOperatorSet.MeanImage(image.Image, out filtered, meanSize, meanSize);
					HObject imageToSearch = filtered;
					if (job.SearchRoiEnabled)
					{
						Rectangle searchRect = ToPixelRectangle(job.SearchRoi, source.Size);
						HOperatorSet.GenRectangle1(out searchRegion, searchRect.Top, searchRect.Left, searchRect.Bottom - 1, searchRect.Right - 1);
						HOperatorSet.ReduceDomain(filtered, searchRegion, out searchImage);
						imageToSearch = searchImage;
					}
					modelId = ImportShapeModel(job.TeachData.ModelBytes);
					HOperatorSet.FindScaledShapeModel(imageToSearch, modelId, DegreesToRadians(job.Parameters.AngleStartDegrees), DegreesToRadians(job.Parameters.AngleExtentDegrees), job.Parameters.ScaleMin, job.Parameters.ScaleMax, job.Parameters.MinScore, 1, job.Parameters.MaxOverlap, "least_squares", 0, job.Parameters.Greediness, out var rows, out var columns, out var angles, out var scales, out var scores);
					if (scores == null || scores.Length == 0)
					{
						return MarkLocateResult.Failed("未找到满足分数要求的 Mark。");
					}
					double row = rows[0].D;
					double col = columns[0].D;
					double angle = angles[0].D;
					double scale = scales[0].D;
					MarkLocateResult result = new MarkLocateResult
					{
						Success = true,
						Message = $"定位成功，分数 {scores[0].D:F3}。",
						ModelPoint = new PointF((float)col, (float)row),
						Score = scores[0].D,
						AngleRadians = angle,
						Scale = scale,
						MatchedContours = TransformContours(job.TeachData.ModelContours, row, col, angle, scale)
					};
					job.Parameters.EnableCircleRefinement = circleEnabled;
					ApplyCircleRefinement(source, job.Parameters, result);
					return result;
				}
				catch (Exception ex)
				{
					return MarkLocateResult.Failed(ex.GetType().Name + ": " + ex.Message);
				}
				finally
				{
					ClearShapeModel(modelId);
					searchImage?.Dispose();
					searchRegion?.Dispose();
					filtered?.Dispose();
				}
			}
		}

		private static MarkLocateResult LocateCircleOnly(Bitmap source, MarkLocateJob job)
		{
			RectangleF roi = job.SearchRoiEnabled && !job.SearchRoi.IsEmpty ? job.SearchRoi : job.TemplateRoi;
			if (roi.IsEmpty)
			{
				roi = new RectangleF(0f, 0f, source.Width, source.Height);
			}
			PointF initialCenter = new PointF(roi.Left + roi.Width * 0.5f, roi.Top + roi.Height * 0.5f);
			double expectedRadius = job.Parameters.ExpectedCircleRadius;
			if (expectedRadius <= 1.0)
			{
				expectedRadius = Math.Min(roi.Width, roi.Height) * 0.5;
			}
			MarkLocateResult result = new MarkLocateResult
			{
				Success = false,
				Message = "未找到满足条件的圆。",
				ModelPoint = initialCenter,
				Score = 0.0,
				Scale = 1.0
			};
			if (TryRefineCircle(source, initialCenter, expectedRadius, job.Parameters, out var center, out var radius))
			{
				result.Success = true;
				result.Message = "找圆成功。";
				result.ModelPoint = center;
				result.CircleFound = true;
				result.CirclePoint = center;
				result.CircleRadius = radius;
				result.Score = 1.0;
			}
			return result;
		}

		private static List<PointF> ReadFeaturePoints(HObject edges, double minimumDistance, int maximumPoints)
		{
			List<Tuple<List<PointF>, double>> contours = new List<Tuple<List<PointF>, double>>();
			if (edges == null)
			{
				return new List<PointF>();
			}
			HOperatorSet.CountObj(edges, out var countTuple);
			int contourCount = ((countTuple.Length != 0) ? countTuple[0].I : 0);
			double spacing = Math.Max(1.0, minimumDistance);
			for (int index = 1; index <= contourCount; index++)
			{
				HObject contour = null;
				try
				{
					HOperatorSet.SelectObj(edges, out contour, index);
					HOperatorSet.GetContourXld(contour, out var rows, out var columns);
					List<PointF> rawPoints = new List<PointF>(Math.Min(rows.Length, columns.Length));
					for (int pointIndex = 0; pointIndex < rows.Length && pointIndex < columns.Length; pointIndex++)
					{
						rawPoints.Add(new PointF((float)columns[pointIndex].D, (float)rows[pointIndex].D));
					}
					List<PointF> sampled = ResampleContour(rawPoints, spacing, int.MaxValue);
					if (sampled.Count >= 2)
					{
						contours.Add(Tuple.Create(sampled, ContourLength(sampled)));
					}
				}
				finally
				{
					contour?.Dispose();
				}
			}
			int pointLimit = Math.Max(100, maximumPoints);
			List<PointF> result = new List<PointF>();
			int realPointCount = 0;
			foreach (Tuple<List<PointF>, double> item in contours.OrderByDescending((Tuple<List<PointF>, double> tuple) => tuple.Item2))
			{
				int remaining = pointLimit - realPointCount;
				if (remaining < 2)
				{
					break;
				}
				List<PointF> points = ((item.Item1.Count <= remaining) ? item.Item1 : ResampleContour(item.Item1, 0.0, remaining));
				if (points.Count >= 2)
				{
					if (result.Count > 0)
					{
						result.Add(CreateContourSeparator());
					}
					result.AddRange(points);
					realPointCount += points.Count;
				}
			}
			return result;
		}

		private static List<PointF> ResampleContour(IList<PointF> points, double minimumDistance, int maximumPoints)
		{
			List<PointF> result = new List<PointF>();
			if (points == null || points.Count == 0 || maximumPoints <= 0)
			{
				return result;
			}
			double distanceSquared = minimumDistance * minimumDistance;
			result.Add(points[0]);
			for (int index = 1; index < points.Count; index++)
			{
				double dx = points[index].X - result[result.Count - 1].X;
				double dy = points[index].Y - result[result.Count - 1].Y;
				if (minimumDistance <= 0.0 || dx * dx + dy * dy >= distanceSquared)
				{
					result.Add(points[index]);
				}
			}
			if (result.Count <= maximumPoints)
			{
				return result;
			}
			if (maximumPoints == 1)
			{
				return new List<PointF> { result[0] };
			}
			List<PointF> limited = new List<PointF>(maximumPoints);
			double step = ((double)result.Count - 1.0) / ((double)maximumPoints - 1.0);
			for (int i = 0; i < maximumPoints; i++)
			{
				limited.Add(result[Math.Min(result.Count - 1, (int)Math.Round((double)i * step))]);
			}
			return limited;
		}

		private static double ContourLength(IList<PointF> points)
		{
			double length = 0.0;
			int index = 1;
			while (points != null && index < points.Count)
			{
				double dx = points[index].X - points[index - 1].X;
				double dy = points[index].Y - points[index - 1].Y;
				length += Math.Sqrt(dx * dx + dy * dy);
				index++;
			}
			return length;
		}

		private static void GenContoursFromFeaturePoints(IList<PointF> points, out HObject contours)
		{
			contours = null;
			List<PointF> segment = new List<PointF>();
			for (int index = 0; index <= (points?.Count ?? 0); index++)
			{
				if (index != (points?.Count ?? 0) && !IsContourSeparator(points[index]))
				{
					segment.Add(points[index]);
					continue;
				}
				AppendContourSegment(segment, ref contours);
				segment.Clear();
			}
			if (contours == null)
			{
				throw new InvalidOperationException("特征点中没有可用于建模的连续轮廓。");
			}
		}

		private static void AppendContourSegment(IList<PointF> points, ref HObject contours)
		{
			if (points == null || points.Count < 2)
			{
				return;
			}
			HTuple rows = new HTuple();
			HTuple columns = new HTuple();
			for (int index = 0; index < points.Count; index++)
			{
				rows = rows.TupleConcat(points[index].Y);
				columns = columns.TupleConcat(points[index].X);
			}
			HObject oneContour = null;
			HObject concatenated = null;
			try
			{
				HOperatorSet.GenContourPolygonXld(out oneContour, rows, columns);
				if (contours == null)
				{
					contours = oneContour;
					oneContour = null;
					return;
				}
				HOperatorSet.ConcatObj(contours, oneContour, out concatenated);
				contours.Dispose();
				contours = concatenated;
				concatenated = null;
			}
			finally
			{
				oneContour?.Dispose();
				concatenated?.Dispose();
			}
		}

		private static void SetModelOriginToTemplateCenter(HTuple modelId, IEnumerable<PointF> featurePoints, RectangleF templateRoi)
		{
			List<PointF> realPoints = (featurePoints ?? Enumerable.Empty<PointF>()).Where((PointF point) => !IsContourSeparator(point)).ToList();
			if (modelId != null && modelId.Length != 0 && realPoints.Count != 0)
			{
				double defaultRow = (double)(realPoints.Min((PointF point) => point.Y) + realPoints.Max((PointF point) => point.Y)) * 0.5;
				double defaultColumn = (double)(realPoints.Min((PointF point) => point.X) + realPoints.Max((PointF point) => point.X)) * 0.5;
				double targetRow = (double)templateRoi.Top + (double)templateRoi.Height * 0.5;
				double targetColumn = (double)templateRoi.Left + (double)templateRoi.Width * 0.5;
				HOperatorSet.SetShapeModelOrigin(modelId, targetRow - defaultRow, targetColumn - defaultColumn);
			}
		}

		private static PointF CreateContourSeparator()
		{
			return new PointF(float.NaN, float.NaN);
		}

		private static bool IsContourSeparator(PointF point)
		{
			if (!float.IsNaN(point.X))
			{
				return float.IsNaN(point.Y);
			}
			return true;
		}

		private static int CountRealPoints(IEnumerable<PointF> points)
		{
			return points?.Count((PointF point) => !IsContourSeparator(point)) ?? 0;
		}

		private static void AppendSeparator(List<PointF> points, ref bool segmentHasPoint)
		{
			if (!segmentHasPoint || points.Count == 0 || IsContourSeparator(points[points.Count - 1]))
			{
				segmentHasPoint = false;
				return;
			}
			points.Add(CreateContourSeparator());
			segmentHasPoint = false;
		}

		private static void TrimSeparators(List<PointF> points)
		{
			while (points.Count > 0 && IsContourSeparator(points[points.Count - 1]))
			{
				points.RemoveAt(points.Count - 1);
			}
		}

		private static void CreateTemplateRegion(RectangleF roi, RegionShape shape, out HObject region)
		{
			RectangleF bounds = RegionShapeGeometry.NormalizeBounds(roi, shape);
			if (shape == RegionShape.Circle)
			{
				HOperatorSet.GenCircle(out region, bounds.Top + bounds.Height * 0.5, bounds.Left + bounds.Width * 0.5, bounds.Width * 0.5);
			}
			else if (shape == RegionShape.Diamond)
			{
				HTuple rows = new HTuple();
				HTuple columns = new HTuple();
				rows = rows.TupleConcat(bounds.Top).TupleConcat(bounds.Top + bounds.Height * 0.5).TupleConcat(bounds.Bottom).TupleConcat(bounds.Top + bounds.Height * 0.5);
				columns = columns.TupleConcat(bounds.Left + bounds.Width * 0.5).TupleConcat(bounds.Right).TupleConcat(bounds.Left + bounds.Width * 0.5).TupleConcat(bounds.Left);
				HOperatorSet.GenRegionPolygon(out region, rows, columns);
				rows.Dispose();
				columns.Dispose();
			}
			else
			{
				HOperatorSet.GenRectangle1(out region, bounds.Top, bounds.Left, bounds.Bottom - 1, bounds.Right - 1);
			}
		}

		private static HObject BuildTemplateDomain(Rectangle templateRect, IEnumerable<MaskStroke> strokes)
		{
			HObject bounds = null;
			HOperatorSet.GenRectangle1(out bounds, 0, 0, templateRect.Height - 1, templateRect.Width - 1);
			HObject domain = bounds.Clone();
			bounds.Dispose();
			foreach (MaskStroke stroke in strokes ?? Enumerable.Empty<MaskStroke>())
			{
				if (stroke == null || stroke.GetBounds().IsEmpty)
				{
					continue;
				}
				HObject brush = null;
				HObject next = null;
				HObject clipped = null;
				try
				{
					RectangleF strokeBounds = stroke.GetBounds();
					RegionShape shape = Enum.IsDefined(typeof(RegionShape), stroke.Shape) ? stroke.Shape : RegionShape.Circle;
					RectangleF localBounds = new RectangleF(strokeBounds.X - templateRect.Left, strokeBounds.Y - templateRect.Top, strokeBounds.Width, strokeBounds.Height);
					if (shape == RegionShape.Circle)
					{
						HOperatorSet.GenCircle(out brush, localBounds.Top + localBounds.Height * 0.5, localBounds.Left + localBounds.Width * 0.5, localBounds.Width * 0.5);
					}
					else if (shape == RegionShape.Diamond)
					{
						HTuple rows = new HTuple();
						HTuple columns = new HTuple();
						rows = rows.TupleConcat(localBounds.Top).TupleConcat(localBounds.Top + localBounds.Height * 0.5).TupleConcat(localBounds.Bottom).TupleConcat(localBounds.Top + localBounds.Height * 0.5);
						columns = columns.TupleConcat(localBounds.Left + localBounds.Width * 0.5).TupleConcat(localBounds.Right).TupleConcat(localBounds.Left + localBounds.Width * 0.5).TupleConcat(localBounds.Left);
						HOperatorSet.GenRegionPolygon(out brush, rows, columns);
						rows.Dispose();
						columns.Dispose();
					}
					else
					{
						HOperatorSet.GenRectangle1(out brush, localBounds.Top, localBounds.Left, localBounds.Bottom - 1, localBounds.Right - 1);
					}
					if (stroke.Mode == MaskStrokeMode.Exclude)
					{
						HOperatorSet.Difference(domain, brush, out next);
					}
					else
					{
						HOperatorSet.Union2(domain, brush, out next);
						HOperatorSet.GenRectangle1(out var clipBounds, 0, 0, templateRect.Height - 1, templateRect.Width - 1);
						try
						{
							HOperatorSet.Intersection(next, clipBounds, out clipped);
						}
						finally
						{
							clipBounds.Dispose();
						}
						next.Dispose();
						next = clipped;
						clipped = null;
					}
					domain.Dispose();
					domain = next;
					next = null;
				}
				finally
				{
					clipped?.Dispose();
					next?.Dispose();
					brush?.Dispose();
				}
			}
			return domain;
		}

		private static List<List<PointF>> ReadModelContours(HTuple modelId)
		{
			List<List<PointF>> result = new List<List<PointF>>();
			HObject allContours = null;
			try
			{
				HOperatorSet.GetShapeModelContours(out allContours, modelId, 1);
				HOperatorSet.CountObj(allContours, out var countTuple);
				int count = ((countTuple.Length != 0) ? countTuple[0].I : 0);
				for (int index = 1; index <= count; index++)
				{
					HObject contour = null;
					try
					{
						HOperatorSet.SelectObj(allContours, out contour, index);
						HOperatorSet.GetContourXld(contour, out var rows, out var columns);
						List<PointF> points = new List<PointF>(Math.Min(rows.Length, columns.Length));
						for (int i = 0; i < rows.Length && i < columns.Length; i++)
						{
							points.Add(new PointF((float)columns[i].D, (float)rows[i].D));
						}
						if (points.Count > 1)
						{
							result.Add(points);
						}
					}
					finally
					{
						contour?.Dispose();
					}
				}
				return result;
			}
			finally
			{
				allContours?.Dispose();
			}
		}

		private static List<List<PointF>> TransformContours(IEnumerable<List<PointF>> source, double row, double col, double angle, double scale)
		{
			double cos = Math.Cos(angle);
			double sin = Math.Sin(angle);
			List<List<PointF>> result = new List<List<PointF>>();
			foreach (List<PointF> item in source ?? Enumerable.Empty<List<PointF>>())
			{
				List<PointF> list = item;
				List<PointF> transformed = new List<PointF>();
				if (list == null)
				{
					list = new List<PointF>();
				}
				foreach (PointF point in list)
				{
					double x = (double)point.X * scale;
					double y = (double)point.Y * scale;
					transformed.Add(new PointF((float)(col + x * cos - y * sin), (float)(row + x * sin + y * cos)));
				}
				if (transformed.Count > 1)
				{
					result.Add(transformed);
				}
			}
			return result;
		}

		private static void ApplyCircleRefinement(Bitmap source, MarkMatchParameters parameters, MarkLocateResult result)
		{
			if (!parameters.EnableCircleRefinement)
			{
				return;
			}
			PointF center;
			float radius;
			if (parameters.ExpectedCircleRadius <= 1.0)
			{
				if (parameters.RequireCircleRefinement)
				{
					result.Success = false;
					result.Message = "形状模板已匹配，但圆心精修缺少示教圆半径。";
				}
			}
			else if (TryRefineCircle(source, result.ModelPoint, parameters.ExpectedCircleRadius * result.Scale, parameters, out center, out radius))
			{
				result.CircleFound = true;
				result.CirclePoint = center;
				result.CircleRadius = radius;
				result.ModelPoint = center;
				result.Message = $"定位成功，分数 {result.Score:F3}，圆心精修半径 {radius:F2}px。";
			}
			else
			{
				result.CircleFound = false;
				if (parameters.RequireCircleRefinement)
				{
					result.Success = false;
					result.Message = "形状模板已匹配，但圆心精修失败。";
				}
				else
				{
					result.Message += " 圆心精修未找到稳定外圆，保留模板中心。";
				}
			}
		}

		private static bool TryRefineCircle(Bitmap source, PointF initialCenter, double expectedRadius, MarkMatchParameters parameters, out PointF center, out float radius)
		{
			center = initialCenter;
			radius = (float)expectedRadius;
			byte[] gray = ReadGrayBytes(source);
			int width = source.Width;
			int height = source.Height;
			int spokeCount = parameters.CircleSpokeCount;
			double minRadius = expectedRadius * (1.0 - parameters.CircleRadialTolerance);
			double maxRadius = expectedRadius * (1.0 + parameters.CircleRadialTolerance);
			List<PointF> edgePoints = new List<PointF>(spokeCount);
			for (int index = 0; index < spokeCount; index++)
			{
				double num = Math.PI * 2.0 * (double)index / (double)spokeCount;
				double dx = Math.Cos(num);
				double dy = Math.Sin(num);
				double bestGradient = double.MinValue;
				double bestRadius = 0.0;
				for (double sampleRadius = minRadius; sampleRadius <= maxRadius; sampleRadius += 0.75)
				{
					double inner = SampleGray(gray, width, height, (double)initialCenter.X + dx * (sampleRadius - 1.25), (double)initialCenter.Y + dy * (sampleRadius - 1.25));
					double outer = SampleGray(gray, width, height, (double)initialCenter.X + dx * (sampleRadius + 1.25), (double)initialCenter.Y + dy * (sampleRadius + 1.25));
					if (!double.IsNaN(inner) && !double.IsNaN(outer))
					{
						double gradient = Math.Abs(inner - outer);
						if (gradient > bestGradient)
						{
							bestGradient = gradient;
							bestRadius = sampleRadius;
						}
					}
				}
				if (bestGradient >= parameters.CircleEdgeThreshold)
				{
					edgePoints.Add(new PointF((float)((double)initialCenter.X + dx * bestRadius), (float)((double)initialCenter.Y + dy * bestRadius)));
				}
			}
			int requiredPoints = Math.Max(12, (int)Math.Ceiling((double)spokeCount * 0.35));
			if (edgePoints.Count < requiredPoints || !FitCircle(edgePoints, out var firstCenter, out var firstRadius))
			{
				return false;
			}
			double residualLimit = Math.Max(2.0, expectedRadius * 0.08);
			List<PointF> inliers = edgePoints.Where((PointF point) => Math.Abs(Distance(point, firstCenter) - firstRadius) <= residualLimit).ToList();
			if (inliers.Count < requiredPoints || !FitCircle(inliers, out var refinedCenter, out var refinedRadius))
			{
				return false;
			}
			double num2 = Math.Abs(refinedRadius - expectedRadius) / expectedRadius;
			double centerShift = Distance(refinedCenter, initialCenter);
			if (num2 > parameters.CircleRadialTolerance || centerShift > expectedRadius * parameters.CircleMaxCenterShiftRatio)
			{
				return false;
			}
			center = refinedCenter;
			radius = (float)refinedRadius;
			return true;
		}

		private static bool FitCircle(IList<PointF> points, out PointF center, out double radius)
		{
			center = PointF.Empty;
			radius = 0.0;
			if (points == null || points.Count < 3)
			{
				return false;
			}
			double meanX = ((IEnumerable<PointF>)points).Average((Func<PointF, double>)((PointF pointF) => pointF.X));
			double meanY = ((IEnumerable<PointF>)points).Average((Func<PointF, double>)((PointF pointF) => pointF.Y));
			double suu = 0.0;
			double suv = 0.0;
			double svv = 0.0;
			double suz = 0.0;
			double svz = 0.0;
			double meanZ = 0.0;
			foreach (PointF point in points)
			{
				double u = (double)point.X - meanX;
				double v = (double)point.Y - meanY;
				double z = u * u + v * v;
				suu += u * u;
				suv += u * v;
				svv += v * v;
				suz += u * z;
				svz += v * z;
				meanZ += z;
			}
			double determinant = suu * svv - suv * suv;
			if (Math.Abs(determinant) < 1E-09)
			{
				return false;
			}
			double uc = 0.5 * (suz * svv - svz * suv) / determinant;
			double vc = 0.5 * (svz * suu - suz * suv) / determinant;
			double fittedRadiusSquared = uc * uc + vc * vc + meanZ / (double)points.Count;
			if (fittedRadiusSquared <= 0.0)
			{
				return false;
			}
			center = new PointF((float)(meanX + uc), (float)(meanY + vc));
			radius = Math.Sqrt(fittedRadiusSquared);
			if (!double.IsNaN(radius))
			{
				return !double.IsInfinity(radius);
			}
			return false;
		}

		private static byte[] ReadGrayBytes(Bitmap source)
		{
			int width = source.Width;
			int height = source.Height;
			byte[] gray = new byte[width * height];
			using (Bitmap converted = new Bitmap(width, height, PixelFormat.Format24bppRgb))
			{
				using (Graphics graphics = Graphics.FromImage(converted))
				{
					graphics.DrawImageUnscaled(source, 0, 0);
				}
				BitmapData data = converted.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
				try
				{
					int stride = Math.Abs(data.Stride);
					byte[] rgb = new byte[stride * height];
					Marshal.Copy(data.Scan0, rgb, 0, rgb.Length);
					for (int y = 0; y < height; y++)
					{
						int sourceRow = ((data.Stride < 0) ? ((height - 1 - y) * stride) : (y * stride));
						int targetRow = y * width;
						for (int x = 0; x < width; x++)
						{
							int offset = sourceRow + x * 3;
							gray[targetRow + x] = (byte)(0.114 * (double)(int)rgb[offset] + 0.587 * (double)(int)rgb[offset + 1] + 0.299 * (double)(int)rgb[offset + 2]);
						}
					}
					return gray;
				}
				finally
				{
					converted.UnlockBits(data);
				}
			}
		}

		private static double SampleGray(byte[] gray, int width, int height, double x, double y)
		{
			int x2 = (int)Math.Floor(x);
			int y2 = (int)Math.Floor(y);
			if (x2 < 0 || y2 < 0 || x2 + 1 >= width || y2 + 1 >= height)
			{
				return double.NaN;
			}
			double fx = x - (double)x2;
			double fy = y - (double)y2;
			double num = (double)(int)gray[y2 * width + x2] * (1.0 - fx) + (double)(int)gray[y2 * width + x2 + 1] * fx;
			double bottom = (double)(int)gray[(y2 + 1) * width + x2] * (1.0 - fx) + (double)(int)gray[(y2 + 1) * width + x2 + 1] * fx;
			return num * (1.0 - fy) + bottom * fy;
		}

		private static double Distance(PointF first, PointF second)
		{
			double num = first.X - second.X;
			double dy = first.Y - second.Y;
			return Math.Sqrt(num * num + dy * dy);
		}

		private static byte[] ExportShapeModel(HTuple modelId)
		{
			HTuple serialized = null;
			try
			{
				HOperatorSet.SerializeShapeModel(modelId, out serialized);
				HOperatorSet.GetSerializedItemPtr(serialized, out var pointer, out var size);
				int length = size[0].I;
				byte[] bytes = new byte[length];
				Marshal.Copy((IntPtr)pointer[0].L, bytes, 0, length);
				return bytes;
			}
			finally
			{
				ClearSerializedItem(serialized);
			}
		}

		private static HTuple ImportShapeModel(byte[] bytes)
		{
			GCHandle handle = default(GCHandle);
			HTuple serialized = null;
			try
			{
				handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
				HOperatorSet.CreateSerializedItemPtr(handle.AddrOfPinnedObject(), bytes.Length, "true", out serialized);
				HOperatorSet.DeserializeShapeModel(serialized, out var modelId);
				return modelId;
			}
			finally
			{
				ClearSerializedItem(serialized);
				if (handle.IsAllocated)
				{
					handle.Free();
				}
			}
		}

		private static void ClearShapeModel(HTuple modelId)
		{
			if (modelId == null || modelId.Length == 0)
			{
				return;
			}
			try
			{
				HOperatorSet.ClearShapeModel(modelId);
			}
			catch
			{
			}
		}

		private static void ClearSerializedItem(HTuple item)
		{
			if (item == null || item.Length == 0)
			{
				return;
			}
			try
			{
				HOperatorSet.ClearSerializedItem(item);
			}
			catch
			{
			}
		}

		private static Rectangle ToPixelRectangle(RectangleF roi, Size imageSize)
		{
			int num = Math.Max(0, Math.Min(imageSize.Width - 1, (int)Math.Floor(roi.Left)));
			int top = Math.Max(0, Math.Min(imageSize.Height - 1, (int)Math.Floor(roi.Top)));
			int right = Math.Max(num + 1, Math.Min(imageSize.Width, (int)Math.Ceiling(roi.Right)));
			int bottom = Math.Max(top + 1, Math.Min(imageSize.Height, (int)Math.Ceiling(roi.Bottom)));
			return Rectangle.FromLTRB(num, top, right, bottom);
		}

		private static double DegreesToRadians(double degrees)
		{
			return degrees * Math.PI / 180.0;
		}
	}
}

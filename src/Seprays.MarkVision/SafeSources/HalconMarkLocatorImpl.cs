using System;
using System.Collections.Generic;
using System.Diagnostics;
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
			public HObject Image { get; }

			private HalconImageLease(HObject image)
			{
				Image = image;
			}

			public static HalconImageLease FromBitmap(Bitmap source)
			{
				HObject gray = ConvertBitmapToHalconGray(source);
				return new HalconImageLease(gray);
			}

			public static HalconImageLease FromBitmapRegion(Bitmap source, Rectangle region)
			{
				HObject gray = ConvertBitmapRegionToHalconGray(source, region);
				return new HalconImageLease(gray);
			}

			public void Dispose()
			{
				Image?.Dispose();
			}
		}

		public MarkTeachData Teach(Bitmap source, MarkLocateJob inputJob)
		{
			return TeachCore(source, inputJob, "SDK建模");
		}

		internal MarkTeachData TeachCore(Bitmap source, MarkLocateJob inputJob, string callerSource)
		{
			if (source == null)
			{
				throw new ArgumentNullException(nameof(source));
			}
			if (inputJob == null)
			{
				throw new ArgumentNullException(nameof(inputJob));
			}
			Stopwatch timer = Stopwatch.StartNew();
			MarkLocateJob job = inputJob.DeepClone();
			job.Normalize(source.Size);
			Rectangle templateRect = ToPixelRectangle(job.TemplateRoi, source.Size);
			if (templateRect.Width < 16 || templateRect.Height < 16)
			{
				throw new InvalidOperationException("模板区域过小，请重新拖拽模板框。");
			}
			List<PointF> featurePoints = job.TeachData?.FeaturePoints == null
				? new List<PointF>()
				: new List<PointF>(job.TeachData.FeaturePoints);
			if (CountRealPoints(featurePoints) < 12)
			{
				double appliedLow;
				double appliedHigh;
				List<PointF> extracted = ExtractFeaturePointsCore(source, job, out appliedLow, out appliedHigh);
				if (job.Parameters.AutoEdgeThreshold)
				{
					job.Parameters.EdgeLowThreshold = appliedLow;
					job.Parameters.EdgeHighThreshold = appliedHigh;
				}
				featurePoints = FilterFeaturePointsInRoi(extracted, job.MaskStrokes, job.TemplateRoi, job.TemplateRoiShape);
			}
			featurePoints = FilterFeaturePointsInRoi(featurePoints, job.MaskStrokes, job.TemplateRoi, job.TemplateRoiShape);
			if (CountRealPoints(featurePoints) < 12)
			{
				throw new InvalidOperationException("有效外轮廓特征点不足，请调整模板 ROI、边缘阈值或擦除范围。");
			}
			HObject contoursXld = null;
			HTuple modelId = null;
			try
			{
				GenContoursFromFeaturePoints(featurePoints, out contoursXld);
				GetXldBoundingCenter(contoursXld, out double defaultRow, out double defaultColumn);
				HTuple numLevels = job.Parameters.NumLevels <= 0 ? new HTuple("auto") : new HTuple(job.Parameters.NumLevels);
				HOperatorSet.CreateScaledShapeModelXld(
					contoursXld,
					numLevels,
					DegreesToRadians(job.Parameters.AngleStartDegrees),
					DegreesToRadians(job.Parameters.AngleExtentDegrees),
					"auto",
					job.Parameters.ScaleMin,
					job.Parameters.ScaleMax,
					"auto",
					"auto",
					"ignore_local_polarity",
					job.Parameters.MinContrast,
					out modelId);
				if (!job.Parameters.UsePolarity)
				{
					// Recover edge polarity from the same MeanImage-filtered template used for extraction.
					using (HalconImageLease image = HalconImageLease.FromBitmap(source))
					{
						HObject filtered = null;
						HTuple transform = null;
						try
						{
							HObject metricImage = ApplyMeanFilter(image.Image, job.Parameters.TemplateMeanSize, out filtered);
							HOperatorSet.VectorAngleToRigid(0, 0, 0, defaultRow, defaultColumn, 0, out transform);
							HOperatorSet.SetShapeModelMetric(metricImage, modelId, transform, "ignore_global_polarity");
						}
						finally
						{
							transform?.Dispose();
							filtered?.Dispose();
						}
					}
				}
				SetModelOriginToTemplateCenter(modelId, defaultRow, defaultColumn, job.TemplateRoi);
				List<List<PointF>> contours = ReadModelContours(modelId);
				if (contours.Sum(contour => contour.Count) < 12)
				{
					throw new InvalidOperationException("擦除后可用模型点不足，请减小擦除范围或调整边缘参数。");
				}
				MarkTeachData teachData = new MarkTeachData
				{
					HasModel = true,
					ModelBytes = ExportShapeModel(modelId),
					FeaturePoints = new List<PointF>(featurePoints),
					ModelContours = contours,
					TemplateSize = templateRect.Size,
					CreatedAt = DateTime.Now
				};
				timer.Stop();
				MarkInspectionLogger.LogTeach(callerSource, source, job, CountRealPoints(featurePoints), true, timer.Elapsed.TotalMilliseconds, "模型创建成功");
				return teachData;
			}
			catch (Exception ex)
			{
				timer.Stop();
				MarkInspectionLogger.LogTeach(callerSource, source, job, CountRealPoints(featurePoints), false, timer.Elapsed.TotalMilliseconds, ex.Message);
				throw;
			}
			finally
			{
				ClearShapeModel(modelId);
				contoursXld?.Dispose();
			}
		}

		public List<PointF> ExtractFeaturePoints(Bitmap source, MarkLocateJob inputJob)
		{
			double unusedLow;
			double unusedHigh;
			return ExtractFeaturePointsCore(source, inputJob, out unusedLow, out unusedHigh);
		}

		internal List<PointF> ExtractFeaturePointsCore(Bitmap source, MarkLocateJob inputJob, out double appliedLow, out double appliedHigh)
		{
			if (source == null)
			{
				throw new ArgumentNullException(nameof(source));
			}
			if (inputJob == null)
			{
				throw new ArgumentNullException(nameof(inputJob));
			}
			MarkLocateJob job = inputJob.DeepClone();
			job.Normalize(source.Size);
			Rectangle templateRect = ToPixelRectangle(job.TemplateRoi, source.Size);
			if (templateRect.Width < 16 || templateRect.Height < 16)
			{
				throw new InvalidOperationException("模板区域过小，请重新拖拽模板框。");
			}
			appliedLow = job.Parameters.EdgeLowThreshold;
			appliedHigh = job.Parameters.EdgeHighThreshold;
			int filterMargin = Math.Max(8, (job.Parameters.TemplateMeanSize / 2) + (int)Math.Ceiling(job.Parameters.EdgeSigma * 4.0) + 4);
			Rectangle cropRect = Rectangle.Intersect(
				Rectangle.FromLTRB(
					templateRect.Left - filterMargin,
					templateRect.Top - filterMargin,
					templateRect.Right + filterMargin,
					templateRect.Bottom + filterMargin),
				new Rectangle(0, 0, source.Width, source.Height));
			RectangleF localRoi = new RectangleF(
				job.TemplateRoi.X - cropRect.Left,
				job.TemplateRoi.Y - cropRect.Top,
				job.TemplateRoi.Width,
				job.TemplateRoi.Height);
			using (HalconImageLease image = HalconImageLease.FromBitmapRegion(source, cropRect))
			{
				HObject filtered = null;
				HObject templateRegion = null;
				HObject reduced = null;
				HObject edges = null;
				HObject selectedEdges = null;
				try
				{
					HObject sourceGray = ApplyMeanFilter(image.Image, job.Parameters.TemplateMeanSize, out filtered);
					CreateTemplateRegion(localRoi, job.TemplateRoiShape, out templateRegion);
					HOperatorSet.ReduceDomain(sourceGray, templateRegion, out reduced);
					double maxGrad = 0.0;
					if (job.Parameters.AutoEdgeThreshold)
					{
						EstimateAutoEdgeThresholds(reduced, templateRegion, job.Parameters.EdgeSigma, out appliedLow, out appliedHigh, out maxGrad);
						job.Parameters.EdgeLowThreshold = appliedLow;
						job.Parameters.EdgeHighThreshold = appliedHigh;
					}
					double minLength = Math.Max(4.0, Math.Min(9.0, job.Parameters.FeatureMinDistancePx * 1.5));
					HOperatorSet.EdgesSubPix(reduced, out edges, "canny", job.Parameters.EdgeSigma, appliedLow, appliedHigh);
					HOperatorSet.SelectShapeXld(edges, out selectedEdges, "contlength", "and", minLength, 999999.0);
					if (job.Parameters.AutoEdgeThreshold && maxGrad >= 2.0 && CountHalconObjects(selectedEdges) == 0)
					{
						// Fallback pass with relaxed hysteresis thresholds when automatic thresholds are conservative.
						double relaxedHigh = Math.Round(Math.Max(2.0, appliedHigh * 0.6), 1);
						double relaxedLow = Math.Round(Math.Max(1.0, Math.Min(relaxedHigh - 1.0, appliedLow * 0.5)), 1);
						edges?.Dispose();
						selectedEdges?.Dispose();
						edges = null;
						selectedEdges = null;
						HOperatorSet.EdgesSubPix(reduced, out edges, "canny", job.Parameters.EdgeSigma, relaxedLow, relaxedHigh);
						HOperatorSet.SelectShapeXld(edges, out selectedEdges, "contlength", "and", minLength, 999999.0);
						if (CountHalconObjects(selectedEdges) > 0)
						{
							appliedLow = relaxedLow;
							appliedHigh = relaxedHigh;
							job.Parameters.EdgeLowThreshold = appliedLow;
							job.Parameters.EdgeHighThreshold = appliedHigh;
						}
					}
					List<PointF> points = ReadFeaturePoints(selectedEdges, job.Parameters.FeatureMinDistancePx, job.Parameters.MaxFeaturePoints);
					if (cropRect.Left != 0 || cropRect.Top != 0)
					{
						for (int i = 0; i < points.Count; i++)
						{
							if (!IsContourSeparator(points[i]))
							{
								points[i] = new PointF(points[i].X + cropRect.Left, points[i].Y + cropRect.Top);
							}
						}
					}
					return FilterFeaturePointsInRoi(points, null, job.TemplateRoi, job.TemplateRoiShape);
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
			List<MaskStroke> maskStrokes = strokes == null
				? new List<MaskStroke>()
				: strokes.Where(maskStroke => maskStroke != null && !maskStroke.GetBounds().IsEmpty).ToList();
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
			if (source.Width < 8 || source.Height < 8)
			{
				return MarkLocateResult.Failed("待检测图片尺寸过小。");
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
					HOperatorSet.SetSystem("border_shape_models", "true");
					HObject grayWork = ApplyMeanFilter(image.Image, job.Parameters.SearchMeanSize, out filtered);
					HObject imageToSearch = grayWork;
					if (job.SearchRoiEnabled && !job.SearchRoi.IsEmpty)
					{
						Rectangle searchRect = ToPixelRectangle(job.SearchRoi, source.Size);
						HOperatorSet.GenRectangle1(out searchRegion, searchRect.Top, searchRect.Left, searchRect.Bottom - 1, searchRect.Right - 1);
						HOperatorSet.ReduceDomain(grayWork, searchRegion, out searchImage);
						imageToSearch = searchImage;
					}
					modelId = ImportShapeModel(job.TeachData.ModelBytes);
					double angleStart = DegreesToRadians(job.Parameters.AngleStartDegrees);
					double angleExtent = DegreesToRadians(job.Parameters.AngleExtentDegrees);
					HTuple rows;
					HTuple columns;
					HTuple angles;
					HTuple scales;
					HTuple scores;
					HOperatorSet.FindScaledShapeModel(
						imageToSearch,
						modelId,
						angleStart,
						angleExtent,
						job.Parameters.ScaleMin,
						job.Parameters.ScaleMax,
						job.Parameters.MinScore,
						1,
						job.Parameters.MaxOverlap,
						"least_squares_high",
						0,
						job.Parameters.Greediness,
						out rows,
						out columns,
						out angles,
						out scales,
						out scores);
					if (scores == null || scores.Length == 0)
					{
						// Retry once with lower pyramid pruning and relaxed min_contrast while keeping MinScore strict.
						int fallbackContrast = Math.Max(2, Math.Min(6, job.Parameters.MinContrast / 2));
						double fallbackGreediness = Math.Min(job.Parameters.Greediness, 0.25);
						if (fallbackContrast < job.Parameters.MinContrast || fallbackGreediness < job.Parameters.Greediness)
						{
							try
							{
								HOperatorSet.SetShapeModelParam(modelId, "min_contrast", fallbackContrast);
							}
							catch
							{
							}
							rows?.Dispose();
							columns?.Dispose();
							angles?.Dispose();
							scales?.Dispose();
							scores?.Dispose();
							HOperatorSet.FindScaledShapeModel(
								imageToSearch,
								modelId,
								angleStart,
								angleExtent,
								job.Parameters.ScaleMin,
								job.Parameters.ScaleMax,
								job.Parameters.MinScore,
								1,
								job.Parameters.MaxOverlap,
								"least_squares_high",
								0,
								fallbackGreediness,
								out rows,
								out columns,
								out angles,
								out scales,
								out scores);
						}
					}
					if (scores == null || scores.Length == 0)
					{
						return MarkLocateResult.Failed("未找到满足分数要求的 Mark。");
					}
					double row = rows[0].D;
					double col = columns[0].D;
					double angle = angles[0].D;
					double scale = scales[0].D;
					double score = scores[0].D;
					rows?.Dispose();
					columns?.Dispose();
					angles?.Dispose();
					scales?.Dispose();
					scores?.Dispose();
					MarkLocateResult result = new MarkLocateResult
					{
						Success = true,
						Message = $"定位成功，分数 {score:F3}。",
						ModelPoint = new PointF((float)col, (float)row),
						Score = score,
						AngleRadians = angle,
						Scale = scale,
						MatchedContours = TransformContoursWithHalcon(job.TeachData.ModelContours, row, col, angle, scale)
					};
					job.Parameters.EnableCircleRefinement = circleEnabled;
					ApplyCircleRefinement(grayWork, source.Size, job.TemplateRoi, job.Parameters, result);
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
			bool hasExplicitRadius = expectedRadius > 1.0;
			if (!hasExplicitRadius)
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
			using (HalconImageLease image = HalconImageLease.FromBitmap(source))
			{
				HObject filtered = null;
				try
				{
					HObject grayWork = ApplyMeanFilter(image.Image, job.Parameters.SearchMeanSize, out filtered);
					PointF center;
					float radius;
					if (TryRefineCircleHalcon(grayWork, source.Size, initialCenter, expectedRadius, job.Parameters, out center, out radius) ||
						(!hasExplicitRadius && TryDetectCircleByEdgesHalcon(grayWork, roi, source.Size, job.Parameters, out center, out radius)))
					{
						result.Success = true;
						result.Message = $"找圆成功，半径 {radius:F2}px。";
						result.ModelPoint = center;
						result.CircleFound = true;
						result.CirclePoint = center;
						result.CircleRadius = radius;
						result.Score = 1.0;
						result.MatchedContours = CreateCircleContourPoints(center, radius);
					}
					return result;
				}
				catch (Exception ex)
				{
					return MarkLocateResult.Failed(ex.GetType().Name + ": " + ex.Message);
				}
				finally
				{
					filtered?.Dispose();
				}
			}
		}

		internal static Bitmap RenderResultImageWithHalcon(Bitmap source, MarkLocateResult result)
		{
			if (source == null)
			{
				return null;
			}
			int width = source.Width;
			int height = source.Height;
			HObject rgb = null;
			HObject current = null;
			HObject contourXld = null;
			HObject crossXld = null;
			HObject circleXld = null;
			HObject circleCrossXld = null;
			HObject interleaved = null;
			try
			{
				rgb = ConvertBitmapToHalconRgb(source);
				current = rgb;
				rgb = null;
				bool hasContours = false;
				if (result != null && result.MatchedContours != null && result.MatchedContours.Count > 0)
				{
					List<PointF> flatContours = new List<PointF>();
					foreach (List<PointF> contour in result.MatchedContours)
					{
						if (contour == null) continue;
						bool added = false;
						foreach (PointF point in contour)
						{
							if (!IsFinite(point))
							{
								if (added)
								{
									flatContours.Add(CreateContourSeparator());
									added = false;
								}
								continue;
							}
							flatContours.Add(point);
							added = true;
							hasContours = true;
						}
						if (added)
						{
							flatContours.Add(CreateContourSeparator());
						}
					}
					TrimSeparators(flatContours);
					if (CountRealPoints(flatContours) >= 2)
					{
						GenContoursFromFeaturePoints(flatContours, out contourXld);
						PaintXldStep(ref current, contourXld, 0, 191, 255);
					}
				}
				if (result != null && (result.Success || hasContours) && IsFinite(result.ModelPoint))
				{
					BuildCrossXld(result.ModelPoint, 10.0, out crossXld);
					PaintXldStep(ref current, crossXld, 50, 205, 50);
				}
				if (result != null && result.CircleFound && IsFinite(result.CirclePoint) &&
					result.CircleRadius > 0f && !float.IsInfinity(result.CircleRadius))
				{
					HOperatorSet.GenCircleContourXld(
						out circleXld,
						(double)result.CirclePoint.Y,
						(double)result.CirclePoint.X,
						(double)result.CircleRadius,
						0.0,
						Math.PI * 2.0,
						"positive",
						1.0);
					PaintXldStep(ref current, circleXld, 255, 165, 0);
					BuildCrossXld(result.CirclePoint, 10.0, out circleCrossXld);
					PaintXldStep(ref current, circleCrossXld, 255, 165, 0);
				}

				Bitmap output = new Bitmap(width, height, PixelFormat.Format24bppRgb);
				try
				{
					BitmapData outData = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
					try
					{
						int outStride = Math.Abs(outData.Stride);
						HOperatorSet.InterleaveChannels(current, out interleaved, "rgb", outStride, 0);
						HTuple pointer;
						HTuple type;
						HTuple iw;
						HTuple ih;
						HOperatorSet.GetImagePointer1(interleaved, out pointer, out type, out iw, out ih);
						IntPtr srcPtr = (IntPtr)pointer[0].L;
						if (outData.Stride > 0)
						{
							byte[] buffer = new byte[outStride * height];
							Marshal.Copy(srcPtr, buffer, 0, buffer.Length);
							Marshal.Copy(buffer, 0, outData.Scan0, buffer.Length);
						}
						else
						{
							byte[] rowBuffer = new byte[outStride];
							for (int y = 0; y < height; y++)
							{
								Marshal.Copy(IntPtr.Add(srcPtr, y * outStride), rowBuffer, 0, outStride);
								Marshal.Copy(rowBuffer, 0, IntPtr.Add(outData.Scan0, (height - 1 - y) * outStride), outStride);
							}
						}
					}
					finally
					{
						output.UnlockBits(outData);
					}
					return output;
				}
				catch
				{
					output.Dispose();
					throw;
				}
			}
			finally
			{
				interleaved?.Dispose();
				circleCrossXld?.Dispose();
				circleXld?.Dispose();
				crossXld?.Dispose();
				contourXld?.Dispose();
				current?.Dispose();
				rgb?.Dispose();
			}
		}

		private static HObject ApplyMeanFilter(HObject grayImage, int meanSize, out HObject ownedFiltered)
		{
			if (meanSize <= 1)
			{
				ownedFiltered = null;
				return grayImage;
			}
			HOperatorSet.MeanImage(grayImage, out ownedFiltered, meanSize, meanSize);
			return ownedFiltered;
		}

		private static void EstimateAutoEdgeThresholds(HObject reduced, HObject templateRegion, double sigma, out double lowThreshold, out double highThreshold, out double maxGradient)
		{
			HObject grad = null;
			HObject gradByte = null;
			HObject edgeMask = null;
			HObject smoothMask = null;
			HObject nonEdgeMask = null;
			try
			{
				HOperatorSet.DerivateGauss(reduced, out grad, sigma, "gradient");
				HOperatorSet.ConvertImageType(grad, out gradByte, "byte");
				HTuple minG;
				HTuple maxG;
				HTuple rangeG;
				HOperatorSet.MinMaxGray(templateRegion, grad, 0, out minG, out maxG, out rangeG);
				maxGradient = (maxG != null && maxG.Length > 0) ? maxG[0].D : 0.0;
				minG?.Dispose();
				maxG?.Dispose();
				rangeG?.Dispose();
				if (maxGradient < 2.0)
				{
					lowThreshold = 5.0;
					highThreshold = 15.0;
					return;
				}
				HTuple tMaxSepTuple;
				HOperatorSet.BinaryThreshold(gradByte, out edgeMask, "max_separability", "light", out tMaxSepTuple);
				double tMaxSep = (tMaxSepTuple != null && tMaxSepTuple.Length > 0) ? tMaxSepTuple[0].D : 15.0;
				tMaxSepTuple?.Dispose();

				double tSmooth = 0.0;
				try
				{
					HTuple tSmoothTuple;
					HOperatorSet.BinaryThreshold(gradByte, out smoothMask, "smooth_histo", "light", out tSmoothTuple);
					if (tSmoothTuple != null && tSmoothTuple.Length > 0)
					{
						tSmooth = tSmoothTuple[0].D;
					}
					tSmoothTuple?.Dispose();
				}
				catch
				{
					tSmooth = 0.0;
				}

				HOperatorSet.Difference(templateRegion, edgeMask, out nonEdgeMask);
				HTuple nMeanTuple;
				HTuple nDevTuple;
				HOperatorSet.Intensity(nonEdgeMask, grad, out nMeanTuple, out nDevTuple);
				double nMean = (nMeanTuple != null && nMeanTuple.Length > 0) ? nMeanTuple[0].D : 0.0;
				double nDev = (nDevTuple != null && nDevTuple.Length > 0) ? nDevTuple[0].D : 0.0;
				nMeanTuple?.Dispose();
				nDevTuple?.Dispose();

				HTuple eMeanTuple;
				HTuple eDevTuple;
				HOperatorSet.Intensity(edgeMask, grad, out eMeanTuple, out eDevTuple);
				double eMean = (eMeanTuple != null && eMeanTuple.Length > 0) ? eMeanTuple[0].D : tMaxSep;
				eMeanTuple?.Dispose();
				eDevTuple?.Dispose();

				double noiseFloor = nMean + 3.0 * nDev;
				double candHigh = Math.Max(tMaxSep, Math.Max(eMean * 0.68, nMean + 4.5 * nDev));
				if (tSmooth > 0.0 && tSmooth < 120.0 && tSmooth < maxGradient * 0.85)
				{
					candHigh = Math.Max(candHigh, tSmooth * 0.95);
				}
				double maxAllowedHigh = Math.Min(255.0, Math.Max(3.0, maxGradient * 0.85));
				highThreshold = Math.Round(Math.Max(3.0, Math.Min(maxAllowedHigh, candHigh)), 1);
				lowThreshold = Math.Round(Math.Max(1.0, Math.Min(highThreshold - 1.0, Math.Max(highThreshold * 0.42, noiseFloor))), 1);
			}
			finally
			{
				nonEdgeMask?.Dispose();
				smoothMask?.Dispose();
				edgeMask?.Dispose();
				gradByte?.Dispose();
				grad?.Dispose();
			}
		}

		private static int CountHalconObjects(HObject objects)
		{
			if (objects == null)
			{
				return 0;
			}
			HTuple countTuple;
			HOperatorSet.CountObj(objects, out countTuple);
			int count = (countTuple != null && countTuple.Length > 0) ? countTuple[0].I : 0;
			countTuple?.Dispose();
			return count;
		}

		private static List<PointF> ReadFeaturePoints(HObject edges, double minimumDistance, int maximumPoints)
		{
			List<Tuple<List<PointF>, double>> contours = new List<Tuple<List<PointF>, double>>();
			if (edges == null)
			{
				return new List<PointF>();
			}
			int contourCount = CountHalconObjects(edges);
			double spacing = Math.Max(1.0, minimumDistance);
			for (int index = 1; index <= contourCount; index++)
			{
				HObject contour = null;
				HTuple rows = null;
				HTuple columns = null;
				try
				{
					HOperatorSet.SelectObj(edges, out contour, index);
					HOperatorSet.GetContourXld(contour, out rows, out columns);
					int count = Math.Min(rows.Length, columns.Length);
					List<PointF> rawPoints = new List<PointF>(count);
					for (int pointIndex = 0; pointIndex < count; pointIndex++)
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
					rows?.Dispose();
					columns?.Dispose();
					contour?.Dispose();
				}
			}
			int pointLimit = Math.Max(100, maximumPoints);
			List<PointF> result = new List<PointF>();
			int realPointCount = 0;
			foreach (Tuple<List<PointF>, double> item in contours.OrderByDescending(tuple => tuple.Item2))
			{
				int remaining = pointLimit - realPointCount;
				if (remaining < 2)
				{
					break;
				}
				List<PointF> points = item.Item1.Count <= remaining
					? item.Item1
					: ResampleContour(item.Item1, 0.0, remaining);
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
			if (points.Count >= 2 && result.Count > 0)
			{
				PointF last = points[points.Count - 1];
				PointF currentTail = result[result.Count - 1];
				double tailDx = last.X - currentTail.X;
				double tailDy = last.Y - currentTail.Y;
				if (tailDx * tailDx + tailDy * tailDy >= 0.25)
				{
					result.Add(last);
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
			int count = points?.Count ?? 0;
			for (int index = 0; index <= count; index++)
			{
				if (index != count && !IsContourSeparator(points[index]))
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
			double[] rowArray = new double[points.Count];
			double[] colArray = new double[points.Count];
			for (int index = 0; index < points.Count; index++)
			{
				rowArray[index] = points[index].Y;
				colArray[index] = points[index].X;
			}
			HTuple rows = new HTuple(rowArray);
			HTuple columns = new HTuple(colArray);
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
				rows.Dispose();
				columns.Dispose();
				oneContour?.Dispose();
				concatenated?.Dispose();
			}
		}

		private static void GetXldBoundingCenter(HObject contoursXld, out double centerRow, out double centerCol)
		{
			HTuple r1 = null;
			HTuple c1 = null;
			HTuple r2 = null;
			HTuple c2 = null;
			HTuple rMin = null;
			HTuple cMin = null;
			HTuple rMax = null;
			HTuple cMax = null;
			try
			{
				HOperatorSet.SmallestRectangle1Xld(contoursXld, out r1, out c1, out r2, out c2);
				HOperatorSet.TupleMin(r1, out rMin);
				HOperatorSet.TupleMin(c1, out cMin);
				HOperatorSet.TupleMax(r2, out rMax);
				HOperatorSet.TupleMax(c2, out cMax);
				centerRow = (rMin[0].D + rMax[0].D) * 0.5;
				centerCol = (cMin[0].D + cMax[0].D) * 0.5;
			}
			finally
			{
				rMin?.Dispose();
				cMin?.Dispose();
				rMax?.Dispose();
				cMax?.Dispose();
				r1?.Dispose();
				c1?.Dispose();
				r2?.Dispose();
				c2?.Dispose();
			}
		}

		private static void SetModelOriginToTemplateCenter(HTuple modelId, double defaultRow, double defaultColumn, RectangleF templateRoi)
		{
			if (modelId == null || modelId.Length == 0)
			{
				return;
			}
			double targetRow = (double)templateRoi.Top + (double)templateRoi.Height * 0.5;
			double targetColumn = (double)templateRoi.Left + (double)templateRoi.Width * 0.5;
			HOperatorSet.SetShapeModelOrigin(modelId, targetRow - defaultRow, targetColumn - defaultColumn);
		}

		private static PointF CreateContourSeparator()
		{
			return new PointF(float.NaN, float.NaN);
		}

		private static bool IsContourSeparator(PointF point)
		{
			return float.IsNaN(point.X) || float.IsNaN(point.Y);
		}

		private static int CountRealPoints(IEnumerable<PointF> points)
		{
			return points?.Count(point => !IsContourSeparator(point)) ?? 0;
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
				double[] rowArray =
				{
					bounds.Top,
					bounds.Top + bounds.Height * 0.5,
					bounds.Bottom,
					bounds.Top + bounds.Height * 0.5
				};
				double[] colArray =
				{
					bounds.Left + bounds.Width * 0.5,
					bounds.Right,
					bounds.Left + bounds.Width * 0.5,
					bounds.Left
				};
				HTuple rows = new HTuple(rowArray);
				HTuple columns = new HTuple(colArray);
				try
				{
					HOperatorSet.GenRegionPolygon(out region, rows, columns);
				}
				finally
				{
					rows.Dispose();
					columns.Dispose();
				}
			}
			else
			{
				HOperatorSet.GenRectangle1(out region, bounds.Top, bounds.Left, bounds.Bottom - 1, bounds.Right - 1);
			}
		}

		private static List<List<PointF>> ReadModelContours(HTuple modelId)
		{
			HObject allContours = null;
			try
			{
				HOperatorSet.GetShapeModelContours(out allContours, modelId, 1);
				return ReadContoursXld(allContours);
			}
			finally
			{
				allContours?.Dispose();
			}
		}

		private static List<List<PointF>> ReadContoursXld(HObject allContours)
		{
			List<List<PointF>> result = new List<List<PointF>>();
			if (allContours == null)
			{
				return result;
			}
			int count = CountHalconObjects(allContours);
			for (int index = 1; index <= count; index++)
			{
				HObject contour = null;
				HTuple rows = null;
				HTuple columns = null;
				try
				{
					HOperatorSet.SelectObj(allContours, out contour, index);
					HOperatorSet.GetContourXld(contour, out rows, out columns);
					int len = Math.Min(rows.Length, columns.Length);
					List<PointF> points = new List<PointF>(len);
					for (int i = 0; i < len; i++)
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
					rows?.Dispose();
					columns?.Dispose();
					contour?.Dispose();
				}
			}
			return result;
		}

		private static List<List<PointF>> TransformContoursWithHalcon(IEnumerable<List<PointF>> source, double row, double col, double angle, double scale)
		{
			HObject modelContours = null;
			HObject transformedContours = null;
			HTuple rigid = null;
			HTuple scaled = null;
			try
			{
				foreach (List<PointF> contour in source ?? Enumerable.Empty<List<PointF>>())
				{
					AppendContourSegment(contour, ref modelContours);
				}
				if (modelContours == null)
				{
					return new List<List<PointF>>();
				}
				HOperatorSet.VectorAngleToRigid(0.0, 0.0, 0.0, row, col, angle, out rigid);
				HOperatorSet.HomMat2dScaleLocal(rigid, scale, scale, out scaled);
				HOperatorSet.AffineTransContourXld(modelContours, out transformedContours, scaled);
				return ReadContoursXld(transformedContours);
			}
			finally
			{
				scaled?.Dispose();
				rigid?.Dispose();
				transformedContours?.Dispose();
				modelContours?.Dispose();
			}
		}

		private static void ApplyCircleRefinement(HObject grayImage, Size imageSize, RectangleF templateRoi, MarkMatchParameters parameters, MarkLocateResult result)
		{
			if (!parameters.EnableCircleRefinement)
			{
				return;
			}
			double expectedRadius = parameters.ExpectedCircleRadius;
			if (expectedRadius <= 1.0 && !templateRoi.IsEmpty)
			{
				expectedRadius = Math.Min(templateRoi.Width, templateRoi.Height) * 0.5;
			}
			if (expectedRadius <= 1.0)
			{
				if (parameters.RequireCircleRefinement)
				{
					result.Success = false;
					result.Message = "形状模板已匹配，但圆心精修缺少示教圆半径。";
				}
				return;
			}
			PointF center;
			float radius;
			if (TryRefineCircleHalcon(grayImage, imageSize, result.ModelPoint, expectedRadius * result.Scale, parameters, out center, out radius))
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

		private static bool TryRefineCircleHalcon(HObject grayImage, Size imageSize, PointF initialCenter, double expectedRadius, MarkMatchParameters parameters, out PointF center, out float radius)
		{
			center = initialCenter;
			radius = (float)expectedRadius;
			if (expectedRadius <= 1.0 || !IsFinite(initialCenter))
			{
				return false;
			}
			double threshold = parameters.CircleEdgeThreshold;
			if (TryRunMetrologyCircle(grayImage, imageSize, initialCenter, expectedRadius, threshold, parameters, out center, out radius))
			{
				return true;
			}
			if (parameters.AutoEdgeThreshold && threshold > 3.5)
			{
				double relaxedThreshold = Math.Max(3.0, threshold * 0.5);
				return TryRunMetrologyCircle(grayImage, imageSize, initialCenter, expectedRadius, relaxedThreshold, parameters, out center, out radius);
			}
			return false;
		}

		private static bool TryRunMetrologyCircle(HObject grayImage, Size imageSize, PointF initialCenter, double expectedRadius, double measureThreshold, MarkMatchParameters parameters, out PointF center, out float radius)
		{
			center = initialCenter;
			radius = (float)expectedRadius;
			HTuple metrologyHandle = null;
			HTuple index = null;
			HTuple paramNames = null;
			HTuple paramValues = null;
			HTuple resultTypes = null;
			HTuple resultValues = null;
			try
			{
				HOperatorSet.CreateMetrologyModel(out metrologyHandle);
				HOperatorSet.SetMetrologyModelImageSize(metrologyHandle, imageSize.Width, imageSize.Height);
				double measureLength1 = Math.Max(4.0, expectedRadius * parameters.CircleRadialTolerance);
				double measureLength2 = Math.Max(1.5, Math.Min(4.0, expectedRadius * 0.08));
				paramNames = new HTuple(new[] { "num_measures", "min_score", "measure_transition", "measure_select" });
				paramValues = new HTuple(parameters.CircleSpokeCount)
					.TupleConcat(0.35)
					.TupleConcat("all")
					.TupleConcat("all");
				HOperatorSet.AddMetrologyObjectCircleMeasure(
					metrologyHandle,
					(double)initialCenter.Y,
					(double)initialCenter.X,
					expectedRadius,
					measureLength1,
					measureLength2,
					1.2,
					measureThreshold,
					paramNames,
					paramValues,
					out index);
				HOperatorSet.ApplyMetrologyModel(grayImage, metrologyHandle);
				resultTypes = new HTuple(new[] { "row", "column", "radius" });
				HOperatorSet.GetMetrologyObjectResult(metrologyHandle, index, "all", "result_type", resultTypes, out resultValues);
				if (resultValues == null || resultValues.Length < 3)
				{
					return false;
				}
				double fittedRow = resultValues[0].D;
				double fittedCol = resultValues[1].D;
				double fittedRadius = resultValues[2].D;
				if (double.IsNaN(fittedRadius) || double.IsInfinity(fittedRadius) || fittedRadius <= 1.0)
				{
					return false;
				}
				if (Math.Abs(fittedRadius - expectedRadius) / expectedRadius > parameters.CircleRadialTolerance)
				{
					return false;
				}
				center = new PointF((float)fittedCol, (float)fittedRow);
				radius = (float)fittedRadius;
				return true;
			}
			catch
			{
				return false;
			}
			finally
			{
				resultValues?.Dispose();
				resultTypes?.Dispose();
				paramValues?.Dispose();
				paramNames?.Dispose();
				index?.Dispose();
				if (metrologyHandle != null && metrologyHandle.Length > 0)
				{
					try
					{
						HOperatorSet.ClearMetrologyModel(metrologyHandle);
					}
					catch
					{
					}
					metrologyHandle.Dispose();
				}
			}
		}

		private static bool TryDetectCircleByEdgesHalcon(HObject grayImage, RectangleF roi, Size imageSize, MarkMatchParameters parameters, out PointF center, out float radius)
		{
			center = PointF.Empty;
			radius = 0f;
			Rectangle rect = ToPixelRectangle(roi, imageSize);
			if (rect.Width < 16 || rect.Height < 16)
			{
				return false;
			}
			HObject roiRegion = null;
			HObject reduced = null;
			HObject edges = null;
			HObject selected = null;
			HTuple rowTuple = null;
			HTuple colTuple = null;
			HTuple radTuple = null;
			HTuple sp = null;
			HTuple ep = null;
			HTuple po = null;
			try
			{
				HOperatorSet.GenRectangle1(out roiRegion, rect.Top, rect.Left, rect.Bottom - 1, rect.Right - 1);
				HOperatorSet.ReduceDomain(grayImage, roiRegion, out reduced);
				double low = parameters.EdgeLowThreshold;
				double high = parameters.EdgeHighThreshold;
				if (parameters.AutoEdgeThreshold)
				{
					double maxGrad;
					EstimateAutoEdgeThresholds(reduced, roiRegion, parameters.EdgeSigma, out low, out high, out maxGrad);
				}
				HOperatorSet.EdgesSubPix(reduced, out edges, "canny", parameters.EdgeSigma, low, high);
				HOperatorSet.SelectShapeXld(edges, out selected, "circularity", "and", 0.65, 1.0);
				if (CountHalconObjects(selected) == 0)
				{
					return false;
				}
				HOperatorSet.FitCircleContourXld(selected, "geotukey", -1, 0, 0, 3, 2.0, out rowTuple, out colTuple, out radTuple, out sp, out ep, out po);
				if (rowTuple == null || rowTuple.Length == 0)
				{
					return false;
				}
				int bestIndex = -1;
				double bestRadius = 0.0;
				double maxPossibleRadius = Math.Min(rect.Width, rect.Height) * 0.55;
				for (int i = 0; i < radTuple.Length; i++)
				{
					double r = radTuple[i].D;
					if (r >= 4.0 && r <= maxPossibleRadius && r > bestRadius)
					{
						bestRadius = r;
						bestIndex = i;
					}
				}
				if (bestIndex < 0)
				{
					return false;
				}
				center = new PointF((float)colTuple[bestIndex].D, (float)rowTuple[bestIndex].D);
				radius = (float)bestRadius;
				return true;
			}
			catch
			{
				return false;
			}
			finally
			{
				po?.Dispose();
				ep?.Dispose();
				sp?.Dispose();
				radTuple?.Dispose();
				colTuple?.Dispose();
				rowTuple?.Dispose();
				selected?.Dispose();
				edges?.Dispose();
				reduced?.Dispose();
				roiRegion?.Dispose();
			}
		}

		private static List<List<PointF>> CreateCircleContourPoints(PointF center, float radius)
		{
			HObject circle = null;
			try
			{
				HOperatorSet.GenCircleContourXld(out circle, (double)center.Y, (double)center.X, (double)radius, 0.0, Math.PI * 2.0, "positive", 1.5);
				return ReadContoursXld(circle);
			}
			finally
			{
				circle?.Dispose();
			}
		}

		private static void BuildCrossXld(PointF center, double halfSize, out HObject crossContours)
		{
			crossContours = null;
			List<PointF> points = new List<PointF>
			{
				new PointF((float)(center.X - halfSize), center.Y),
				new PointF((float)(center.X + halfSize), center.Y),
				CreateContourSeparator(),
				new PointF(center.X, (float)(center.Y - halfSize)),
				new PointF(center.X, (float)(center.Y + halfSize))
			};
			GenContoursFromFeaturePoints(points, out crossContours);
		}

		private static void PaintXldStep(ref HObject rgbImage, HObject xld, int r, int g, int b)
		{
			if (xld == null)
			{
				return;
			}
			HTuple color = new HTuple(new[] { r, g, b });
			HObject next = null;
			try
			{
				HOperatorSet.PaintXld(xld, rgbImage, out next, color);
				rgbImage.Dispose();
				rgbImage = next;
				next = null;
			}
			finally
			{
				color.Dispose();
				next?.Dispose();
			}
		}

		private static HObject ConvertBitmapToHalconGray(Bitmap source)
		{
			return ConvertBitmapRegionToHalconGray(source, new Rectangle(0, 0, source.Width, source.Height));
		}

		private static HObject ConvertBitmapRegionToHalconGray(Bitmap source, Rectangle region)
		{
			Rectangle clipped = Rectangle.Intersect(region, new Rectangle(0, 0, source.Width, source.Height));
			if (source.PixelFormat == PixelFormat.Format8bppIndexed && IsIdentityGrayscalePalette(source))
			{
				return CreateGrayImageFrom8bpp(source, clipped);
			}
			HObject rgb = ConvertBitmapRegionToHalconRgb(source, clipped);
			try
			{
				HObject gray;
				HOperatorSet.Rgb1ToGray(rgb, out gray);
				return gray;
			}
			finally
			{
				rgb.Dispose();
			}
		}

		private static HObject ConvertBitmapToHalconRgb(Bitmap source)
		{
			return ConvertBitmapRegionToHalconRgb(source, new Rectangle(0, 0, source.Width, source.Height));
		}

		private static HObject ConvertBitmapRegionToHalconRgb(Bitmap source, Rectangle region)
		{
			if (source.PixelFormat == PixelFormat.Format8bppIndexed && IsIdentityGrayscalePalette(source))
			{
				HObject gray = CreateGrayImageFrom8bpp(source, region);
				try
				{
					HObject composed;
					HOperatorSet.Compose3(gray, gray, gray, out composed);
					return composed;
				}
				finally
				{
					gray.Dispose();
				}
			}
			if (source.PixelFormat == PixelFormat.Format24bppRgb)
			{
				return CreateRgbFrom24bpp(source, region);
			}
			if (source.PixelFormat == PixelFormat.Format32bppRgb ||
				source.PixelFormat == PixelFormat.Format32bppArgb ||
				source.PixelFormat == PixelFormat.Format32bppPArgb)
			{
				return CreateRgbFrom32bpp(source, region);
			}
			using (Bitmap converted = source.Clone(region, PixelFormat.Format32bppRgb))
			{
				return CreateRgbFrom32bpp(converted, new Rectangle(0, 0, converted.Width, converted.Height));
			}
		}

		private static HObject CreateGrayImageFrom8bpp(Bitmap source, Rectangle region)
		{
			int width = region.Width;
			int height = region.Height;
			BitmapData data = source.LockBits(region, ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
			try
			{
				if (width == source.Width && data.Stride == width)
				{
					HObject gray;
					HOperatorSet.GenImage1(out gray, "byte", width, height, data.Scan0);
					return gray;
				}
				byte[] packed = PackRows(data.Scan0, data.Stride, width, height);
				GCHandle handle = GCHandle.Alloc(packed, GCHandleType.Pinned);
				try
				{
					HObject gray;
					HOperatorSet.GenImage1(out gray, "byte", width, height, handle.AddrOfPinnedObject());
					return gray;
				}
				finally
				{
					handle.Free();
				}
			}
			finally
			{
				source.UnlockBits(data);
			}
		}

		private static HObject CreateRgbFrom24bpp(Bitmap source, Rectangle region)
		{
			int width = region.Width;
			int height = region.Height;
			BitmapData data = source.LockBits(region, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
			try
			{
				int rowBytes = width * 3;
				if (width == source.Width && data.Stride == rowBytes)
				{
					HObject rgb;
					HOperatorSet.GenImageInterleaved(out rgb, data.Scan0, "bgr", width, height, -1, "byte", width, height, 0, 0, 8, 0);
					return rgb;
				}
				byte[] packed = PackRows(data.Scan0, data.Stride, rowBytes, height);
				GCHandle handle = GCHandle.Alloc(packed, GCHandleType.Pinned);
				try
				{
					HObject rgb;
					HOperatorSet.GenImageInterleaved(out rgb, handle.AddrOfPinnedObject(), "bgr", width, height, -1, "byte", width, height, 0, 0, 8, 0);
					return rgb;
				}
				finally
				{
					handle.Free();
				}
			}
			finally
			{
				source.UnlockBits(data);
			}
		}

		private static HObject CreateRgbFrom32bpp(Bitmap source, Rectangle region)
		{
			int width = region.Width;
			int height = region.Height;
			BitmapData data = source.LockBits(region, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
			try
			{
				int rowBytes = width * 4;
				if (width == source.Width && data.Stride == rowBytes)
				{
					HObject rgb;
					HOperatorSet.GenImageInterleaved(out rgb, data.Scan0, "bgrx", width, height, -1, "byte", width, height, 0, 0, 8, 0);
					return rgb;
				}
				byte[] packed = PackRows(data.Scan0, data.Stride, rowBytes, height);
				GCHandle handle = GCHandle.Alloc(packed, GCHandleType.Pinned);
				try
				{
					HObject rgb;
					HOperatorSet.GenImageInterleaved(out rgb, handle.AddrOfPinnedObject(), "bgrx", width, height, -1, "byte", width, height, 0, 0, 8, 0);
					return rgb;
				}
				finally
				{
					handle.Free();
				}
			}
			finally
			{
				source.UnlockBits(data);
			}
		}

		private static byte[] PackRows(IntPtr scan0, int stride, int rowBytes, int height)
		{
			byte[] packed = new byte[rowBytes * height];
			int absStride = Math.Abs(stride);
			for (int y = 0; y < height; y++)
			{
				int sourceIndex = stride < 0 ? (height - 1 - y) * absStride : y * absStride;
				Marshal.Copy(IntPtr.Add(scan0, sourceIndex), packed, y * rowBytes, rowBytes);
			}
			return packed;
		}

		private static bool IsIdentityGrayscalePalette(Bitmap source)
		{
			Color[] entries = source.Palette?.Entries;
			if (entries == null || entries.Length < 256)
			{
				return false;
			}
			for (int i = 0; i < 256; i++)
			{
				Color c = entries[i];
				if (c.R != i || c.G != i || c.B != i)
				{
					return false;
				}
			}
			return true;
		}

		private static bool IsFinite(PointF point)
		{
			return !float.IsNaN(point.X) && !float.IsInfinity(point.X) &&
				!float.IsNaN(point.Y) && !float.IsInfinity(point.Y);
		}

		private static byte[] ExportShapeModel(HTuple modelId)
		{
			HTuple serialized = null;
			HTuple pointer = null;
			HTuple size = null;
			try
			{
				HOperatorSet.SerializeShapeModel(modelId, out serialized);
				HOperatorSet.GetSerializedItemPtr(serialized, out pointer, out size);
				int length = size[0].I;
				byte[] bytes = new byte[length];
				Marshal.Copy((IntPtr)pointer[0].L, bytes, 0, length);
				return bytes;
			}
			finally
			{
				size?.Dispose();
				pointer?.Dispose();
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
				HTuple modelId;
				HOperatorSet.DeserializeShapeModel(serialized, out modelId);
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
			modelId.Dispose();
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
			item.Dispose();
		}

		private static Rectangle ToPixelRectangle(RectangleF roi, Size imageSize)
		{
			int left = Math.Max(0, Math.Min(imageSize.Width - 1, (int)Math.Floor(roi.Left)));
			int top = Math.Max(0, Math.Min(imageSize.Height - 1, (int)Math.Floor(roi.Top)));
			int right = Math.Max(left + 1, Math.Min(imageSize.Width, (int)Math.Ceiling(roi.Right)));
			int bottom = Math.Max(top + 1, Math.Min(imageSize.Height, (int)Math.Ceiling(roi.Bottom)));
			return Rectangle.FromLTRB(left, top, right, bottom);
		}

		private static double DegreesToRadians(double degrees)
		{
			return degrees * Math.PI / 180.0;
		}
	}
}

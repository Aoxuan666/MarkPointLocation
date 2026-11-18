using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace Seprays.MarkVision
{
	[DefaultEvent("MaskChanged")]
	public sealed class MaskImageCanvas : Control
	{
		private enum RoiDragMode
		{
			None,
			Create,
			Move,
			ResizeNorthWest,
			ResizeNorthEast,
			ResizeSouthEast,
			ResizeSouthWest,
			ResizeNorth,
			ResizeEast,
			ResizeSouth,
			ResizeWest
		}

		private Bitmap _sourceImage;

		private Bitmap _maskOverlay;

		private string _inspectionHudText;

		private bool _inspectionSucceeded;

		public void ShowInspectionResult(MarkLocateResult result, double elapsedMilliseconds)
		{
			_inspectionSucceeded = result.Success;
			_inspectionHudText = result.Success
				? $"定位成功    分数 {result.Score:F3}\nX {result.ModelPoint.X:F3}    Y {result.ModelPoint.Y:F3} px\n角度 {result.AngleRadians * 180.0 / Math.PI:F3}°    缩放 {result.Scale:F4}\n耗时 {elapsedMilliseconds:F1} ms"
				: $"定位失败    耗时 {elapsedMilliseconds:F1} ms\n{result.Message}";
			Invalidate();
		}

		public void ClearInspectionResult()
		{
			_inspectionHudText = null;
			Invalidate();
		}

		[Browsable(false)]
		public bool ShowTemplateOverlay { get; set; } = true;

		private RectangleF _templateRoi;

		private RegionShape _templateRoiShape = RegionShape.Circle;

		private RectangleF _searchRoi;

		private bool _searchRoiEnabled;

		private List<MaskStroke> _maskStrokes = new List<MaskStroke>();

		private List<PointF> _featurePoints = new List<PointF>();

		private List<PointF> _visibleFeaturePoints = new List<PointF>();

		private bool[] _featureVisibility = new bool[0];

		private bool[] _featureInTemplate = new bool[0];

		private List<List<PointF>> _modelContours = new List<List<PointF>>();

		private List<List<PointF>> _matchedContours = new List<List<PointF>>();

		private CanvasInteractionMode _interactionMode;

		private float _brushRadius = 18f;

		private RegionShape _maskShape = RegionShape.Circle;

		private float _zoom = 1f;

		private PointF _pan;

		private bool _panning;

		private bool _painting;

		// Commit one change notification per completed brush gesture.
		private bool _brushChanged;

		private PointF _lastBrushPoint;

		private bool IsBrushMode => InteractionMode == CanvasInteractionMode.BrushExclude || InteractionMode == CanvasInteractionMode.BrushRestore;

		private bool IsMaskMode => IsBrushMode || InteractionMode == CanvasInteractionMode.ExcludeMask || InteractionMode == CanvasInteractionMode.RestoreMask;

		private bool IsRestoreMode => InteractionMode == CanvasInteractionMode.RestoreMask || InteractionMode == CanvasInteractionMode.BrushRestore;

		private PointF _maskStartImagePoint;

		private RectangleF _maskPreviewBounds;

		private Point _lastScreenPoint;

		private bool _hasCursor;

		private PointF _cursorImagePoint;

		private int _activeStrokeGroup;

		private int _nextStrokeGroup = 1;

		private RoiDragMode _roiDragMode;

		private PointF _dragStartImagePoint;

		private RectangleF _dragStartRoi;

		[Browsable(false)]
		public Bitmap SourceImage
		{
			get
			{
				return _sourceImage;
			}
			set
			{
				EndPointerInteraction();
				_sourceImage?.Dispose();
				ClearInspectionResult();
				_sourceImage = ((value == null) ? null : new Bitmap(value));
				RebuildMaskOverlay();
				RebuildFeatureVisibility();
				FitToWindow();
				Invalidate();
			}
		}

		[Category("Mark 编辑")]
		[Description("模板建模区域，坐标单位为原图像素。")]
		public RectangleF TemplateRoi
		{
			get
			{
				return _templateRoi;
			}
			set
			{
				RectangleF normalized = ClampShapeBounds(value, _templateRoiShape);
				if (!ApproximatelyEqual(_templateRoi, normalized))
				{
					_templateRoi = normalized;
					RebuildMaskOverlay();
					RebuildFeatureVisibility();
					Invalidate();
					this.TemplateRoiChanged?.Invoke(this, EventArgs.Empty);
				}
			}
		}

		[Category("Mark 编辑")]
		[DefaultValue(RegionShape.Circle)]
		public RegionShape TemplateRoiShape
		{
			get { return _templateRoiShape; }
			set
			{
				RegionShape normalized = Enum.IsDefined(typeof(RegionShape), value) ? value : RegionShape.Circle;
				if (_templateRoiShape != normalized)
				{
					_templateRoiShape = normalized;
					_templateRoi = ClampShapeBounds(_templateRoi, _templateRoiShape);
					RebuildMaskOverlay();
					RebuildFeatureVisibility();
					Invalidate();
					this.TemplateRoiChanged?.Invoke(this, EventArgs.Empty);
				}
			}
		}

		[Category("Mark 编辑")]
		[Description("运行时搜索区域，坐标单位为原图像素。")]
		public RectangleF SearchRoi
		{
			get
			{
				return _searchRoi;
			}
			set
			{
				RectangleF normalized = ClampRoi(value);
				if (!ApproximatelyEqual(_searchRoi, normalized))
				{
					_searchRoi = normalized;
					Invalidate();
					this.SearchRoiChanged?.Invoke(this, EventArgs.Empty);
				}
			}
		}

		[Category("Mark 编辑")]
		[DefaultValue(true)]
		[Description("是否限制运行时搜索区域。")]
		public bool SearchRoiEnabled
		{
			get
			{
				return _searchRoiEnabled;
			}
			set
			{
				if (_searchRoiEnabled != value)
				{
					_searchRoiEnabled = value;
					if (!value && InteractionMode == CanvasInteractionMode.EditSearchRoi)
					{
						InteractionMode = CanvasInteractionMode.Pan;
					}
					Invalidate();
				}
			}
		}

		[Category("Mark 编辑")]
		[DefaultValue(CanvasInteractionMode.Pan)]
		public CanvasInteractionMode InteractionMode
		{
			get
			{
				return _interactionMode;
			}
			set
			{
				if (_interactionMode != value)
				{
					EndPointerInteraction();
				}
				_interactionMode = value;
				Cursor = ((value == CanvasInteractionMode.Pan) ? Cursors.Hand : Cursors.Cross);
				Invalidate();
			}
		}

		[Category("Mark 编辑")]
		[DefaultValue(18f)]
		public float BrushRadius
		{
			get
			{
				return _brushRadius;
			}
			set
			{
				_brushRadius = Math.Max(1f, Math.Min(500f, value));
				Invalidate();
			}
		}

		[Category("Mark 编辑")]
		[DefaultValue(RegionShape.Circle)]
		public RegionShape MaskShape
		{
			get { return _maskShape; }
			set
			{
				_maskShape = Enum.IsDefined(typeof(RegionShape), value) ? value : RegionShape.Circle;
				Invalidate();
			}
		}

		[Browsable(false)]
		public IList<MaskStroke> MaskStrokes
		{
			get
			{
				return _maskStrokes.Select((MaskStroke stroke) => stroke.DeepClone()).ToList();
			}
			set
			{
				_maskStrokes = ((value == null) ? new List<MaskStroke>() : value.Select((MaskStroke stroke) => stroke?.DeepClone() ?? new MaskStroke()).ToList());
				_nextStrokeGroup = Math.Max(1, _maskStrokes.Select((MaskStroke stroke) => stroke.GroupId).DefaultIfEmpty(0).Max() + 1);
				RebuildMaskOverlay();
				RebuildFeatureVisibility();
				Invalidate();
			}
		}

		[Browsable(false)]
		public IList<PointF> FeaturePoints
		{
			get
			{
				return new List<PointF>(_featurePoints);
			}
			set
			{
				_featurePoints = ((value == null) ? new List<PointF>() : new List<PointF>(value));
				RebuildFeatureVisibility();
				Invalidate();
			}
		}

		[Browsable(false)]
		public IList<PointF> VisibleFeaturePoints => new List<PointF>(_visibleFeaturePoints);

		[Browsable(false)]
		public IList<List<PointF>> ModelContours
		{
			get
			{
				return CloneContours(_modelContours);
			}
			set
			{
				_modelContours = CloneContours(value);
				Invalidate();
			}
		}

		[Browsable(false)]
		public IList<List<PointF>> MatchedContours
		{
			get
			{
				return CloneContours(_matchedContours);
			}
			set
			{
				_matchedContours = CloneContours(value);
				Invalidate();
			}
		}

		[Browsable(false)]
		public float ZoomFactor => _zoom;

		[Category("操作")]
		public event EventHandler TemplateRoiChanged;

		[Category("操作")]
		public event EventHandler TemplateRoiEditCompleted;

		[Category("操作")]
		public event EventHandler SearchRoiChanged;

		[Category("操作")]
		public event EventHandler MaskChanged;

		internal event EventHandler MaskEditStarted;

		[Category("操作")]
		public event EventHandler ZoomChanged;

		public MaskImageCanvas()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Color.FromArgb(24, 27, 31);
			base.TabStop = true;
		}

		public void FitToWindow()
		{
			if (_sourceImage != null && base.ClientSize.Width > 0 && base.ClientSize.Height > 0)
			{
				float zoomX = ((float)base.ClientSize.Width - 20f) / (float)_sourceImage.Width;
				float zoomY = ((float)base.ClientSize.Height - 20f) / (float)_sourceImage.Height;
				_zoom = ClampZoom(Math.Min(zoomX, zoomY));
				_pan = new PointF(((float)base.ClientSize.Width - (float)_sourceImage.Width * _zoom) * 0.5f, ((float)base.ClientSize.Height - (float)_sourceImage.Height * _zoom) * 0.5f);
				Invalidate();
				this.ZoomChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public void ActualSize()
		{
			if (_sourceImage != null)
			{
				_zoom = 1f;
				_pan = new PointF((float)(base.ClientSize.Width - _sourceImage.Width) * 0.5f, (float)(base.ClientSize.Height - _sourceImage.Height) * 0.5f);
				Invalidate();
				this.ZoomChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public void UndoMaskStroke()
		{
			EndPointerInteraction();
			if (_maskStrokes.Count == 0)
			{
				return;
			}
			int lastGroup = _maskStrokes[_maskStrokes.Count - 1].GroupId;
			if (lastGroup <= 0)
			{
				_maskStrokes.RemoveAt(_maskStrokes.Count - 1);
			}
			else
			{
				_maskStrokes.RemoveAll((MaskStroke stroke) => stroke.GroupId == lastGroup);
			}
			RebuildMaskOverlay();
			RebuildFeatureVisibility();
			Invalidate();
			this.MaskChanged?.Invoke(this, EventArgs.Empty);
		}

		public void ClearMask()
		{
			EndPointerInteraction();
			if (_maskStrokes.Count != 0)
			{
				_maskStrokes.Clear();
				RebuildMaskOverlay();
				RebuildFeatureVisibility();
				Invalidate();
				this.MaskChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public PointF ScreenToImage(Point screenPoint)
		{
			return new PointF(((float)screenPoint.X - _pan.X) / _zoom, ((float)screenPoint.Y - _pan.Y) / _zoom);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_sourceImage?.Dispose();
				_maskOverlay?.Dispose();
			}
			base.Dispose(disposing);
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			Invalidate();
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);
			if (_painting || _roiDragMode != RoiDragMode.None)
			{
				return;
			}
			if (_sourceImage != null)
			{
				PointF imagePoint = ScreenToImage(e.Location);
				float factor = ((e.Delta > 0) ? 1.15f : 0.86956525f);
				_zoom = ClampZoom(_zoom * factor);
				PointF newScreenPoint = ImageToScreen(imagePoint);
				_pan = new PointF(_pan.X + (float)e.X - newScreenPoint.X, _pan.Y + (float)e.Y - newScreenPoint.Y);
				Invalidate();
				this.ZoomChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			Focus();
			if (!Enabled || _sourceImage == null || _painting || _panning || _roiDragMode != RoiDragMode.None)
			{
				return;
			}
			PointF rawImagePoint = ScreenToImage(e.Location);
			bool insideImage = IsInsideImage(rawImagePoint);
			PointF imagePoint = ClampImagePoint(rawImagePoint);
			_cursorImagePoint = IsBrushMode ? rawImagePoint : imagePoint;
			_hasCursor = insideImage;
			if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && InteractionMode == CanvasInteractionMode.Pan))
			{
				_panning = true;
				_lastScreenPoint = e.Location;
				base.Capture = true;
				Cursor = Cursors.SizeAll;
			}
			else
			{
				if (e.Button != MouseButtons.Left)
				{
					return;
				}
				if (!insideImage)
				{
					return;
				}
				if (IsMaskMode)
				{
					if (!_templateRoi.IsEmpty)
					{
						_activeStrokeGroup = _nextStrokeGroup++;
						_painting = true;
						base.Capture = true;
						if (IsBrushMode)
						{
							_lastBrushPoint = rawImagePoint;
							PaintBrushSegment(rawImagePoint, rawImagePoint);
						}
						else
						{
							_maskStartImagePoint = imagePoint;
							_maskPreviewBounds = CreateMaskBounds(imagePoint, imagePoint);
						}
						Invalidate();
						if (IsBrushMode) Update();
					}
				}
				else if ((InteractionMode == CanvasInteractionMode.EditTemplateRoi || InteractionMode == CanvasInteractionMode.EditSearchRoi) && (InteractionMode != CanvasInteractionMode.EditSearchRoi || SearchRoiEnabled))
				{
					RectangleF roi = ((InteractionMode == CanvasInteractionMode.EditTemplateRoi) ? _templateRoi : _searchRoi);
					_dragStartImagePoint = imagePoint;
					_dragStartRoi = roi;
					_roiDragMode = HitTestRoi(roi, e.Location, (InteractionMode == CanvasInteractionMode.EditTemplateRoi) ? _templateRoiShape : RegionShape.Rectangle);
					if (_roiDragMode == RoiDragMode.None)
					{
						_roiDragMode = RoiDragMode.Create;
						_dragStartRoi = new RectangleF(imagePoint, SizeF.Empty);
					}
					base.Capture = true;
					Cursor = GetCursorForDragMode(_roiDragMode);
				}
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (_sourceImage == null)
			{
				return;
			}
			PointF previousCursor = _cursorImagePoint;
			bool hadCursor = _hasCursor;
			PointF rawImagePoint = ScreenToImage(e.Location);
			PointF imagePoint = ClampImagePoint(rawImagePoint);
			_cursorImagePoint = IsBrushMode ? rawImagePoint : imagePoint;
			_hasCursor = IsInsideImage(rawImagePoint) && ClientRectangle.Contains(e.Location);
			if (_panning)
			{
				_pan = new PointF(_pan.X + (float)e.X - (float)_lastScreenPoint.X, _pan.Y + (float)e.Y - (float)_lastScreenPoint.Y);
				_lastScreenPoint = e.Location;
				Invalidate();
			}
			else if (_painting)
			{
				if (IsBrushMode)
				{
					if (rawImagePoint != _lastBrushPoint)
					{
						PaintBrushSegment(_lastBrushPoint, rawImagePoint);
						_lastBrushPoint = rawImagePoint;
					}
				}
				else
				{
					_maskPreviewBounds = CreateMaskBounds(_maskStartImagePoint, imagePoint);
					Invalidate();
				}
				if (IsBrushMode)
				{
					InvalidateBrushCursor(previousCursor, hadCursor, _cursorImagePoint);
					// Paint the dirty area now, even while mouse messages keep arriving.
					Update();
				}
			}
			else if (_roiDragMode != RoiDragMode.None)
			{
				RectangleF next = CalculateDraggedRoi(imagePoint);
				if (InteractionMode == CanvasInteractionMode.EditTemplateRoi)
				{
					TemplateRoi = next;
				}
				else
				{
					SearchRoi = next;
				}
			}
			else
			{
				InvalidateBrushCursor(previousCursor, hadCursor, _cursorImagePoint);
				UpdateIdleCursor(e.Location);
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			if (_painting && e.Button != MouseButtons.Left)
			{
				return;
			}
			bool num = _roiDragMode != RoiDragMode.None && InteractionMode == CanvasInteractionMode.EditTemplateRoi;
			if (_painting)
			{
				bool maskAdded = false;
				if (IsBrushMode)
				{
					PointF end = ScreenToImage(e.Location);
					if (end != _lastBrushPoint)
					{
						PaintBrushSegment(_lastBrushPoint, end);
					}
				}
				else
				{
					maskAdded = AddMaskStroke(CreateMaskBounds(_maskStartImagePoint, ClampImagePoint(ScreenToImage(e.Location))));
				}
				_painting = false;
				bool brushChanged = _brushChanged;
				_brushChanged = false;
				base.Capture = false;
				_maskPreviewBounds = RectangleF.Empty;
				if (maskAdded || brushChanged)
				{
					this.MaskChanged?.Invoke(this, EventArgs.Empty);
				}
				Invalidate();
			}
			if (_panning)
			{
				_panning = false;
				Cursor = ((InteractionMode == CanvasInteractionMode.Pan) ? Cursors.Hand : Cursors.Cross);
			}
			_roiDragMode = RoiDragMode.None;
			base.Capture = false;
			if (num)
			{
				this.TemplateRoiEditCompleted?.Invoke(this, EventArgs.Empty);
			}
			UpdateIdleCursor(e.Location);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);
			_hasCursor = false;
			Invalidate();
		}

		protected override void OnMouseCaptureChanged(EventArgs e)
		{
			base.OnMouseCaptureChanged(e);
			if (!Capture)
			{
				EndPointerInteraction();
			}
		}

		protected override void OnEnabledChanged(EventArgs e)
		{
			base.OnEnabledChanged(e);
			if (!Enabled)
			{
				EndPointerInteraction();
			}
		}

		private void EndPointerInteraction()
		{
			bool brushChanged = _brushChanged;
			_brushChanged = false;
			_painting = false;
			_panning = false;
			_roiDragMode = RoiDragMode.None;
			_maskPreviewBounds = RectangleF.Empty;
			_hasCursor = false;
			if (Capture)
			{
				Capture = false;
			}
			Invalidate();
			if (brushChanged)
			{
				this.MaskChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			Graphics graphics = e.Graphics;
			graphics.Clear(BackColor);
			if (_sourceImage == null)
			{
				return;
			}
			graphics.InterpolationMode = ((_zoom >= 1f) ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic);
			graphics.PixelOffsetMode = PixelOffsetMode.Half;
			graphics.DrawImage(rect: new RectangleF(_pan.X, _pan.Y, (float)_sourceImage.Width * _zoom, (float)_sourceImage.Height * _zoom), image: _sourceImage);
			if (ShowTemplateOverlay && _maskOverlay != null)
			{
				Rectangle destination = Rectangle.Round(new RectangleF(_pan.X, _pan.Y, (float)_sourceImage.Width * _zoom, (float)_sourceImage.Height * _zoom));
				using (ImageAttributes attributes = new ImageAttributes())
				{
					ColorMatrix matrix = new ColorMatrix();
					matrix.Matrix33 = 0.44f;
					attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
					graphics.DrawImage(_maskOverlay, destination, 0, 0, _maskOverlay.Width, _maskOverlay.Height, GraphicsUnit.Pixel, attributes);
				}
			}
			if (ShowTemplateOverlay)
				DrawFeaturePoints(graphics, _visibleFeaturePoints, Color.FromArgb(255, 176, 32), 2f);
			DrawContourPoints(graphics, _matchedContours, Color.DeepSkyBlue, 2.2f);
			if (SearchRoiEnabled)
			{
				DrawRoi(graphics, _searchRoi, RegionShape.Rectangle, Color.FromArgb(255, 255, 196, 64), "搜索区域", InteractionMode == CanvasInteractionMode.EditSearchRoi, drawCenterLines: false);
			}
			if (ShowTemplateOverlay)
				DrawRoi(graphics, _templateRoi, _templateRoiShape, Color.FromArgb(255, 0, 220, 160), "模板区域", InteractionMode == CanvasInteractionMode.EditTemplateRoi, drawCenterLines: true);
			if (_painting && !_maskPreviewBounds.IsEmpty && (InteractionMode == CanvasInteractionMode.ExcludeMask || InteractionMode == CanvasInteractionMode.RestoreMask))
			{
				DrawMaskShape(graphics, _maskPreviewBounds, _maskShape, (InteractionMode == CanvasInteractionMode.ExcludeMask) ? Color.FromArgb(235, 70, 76) : Color.FromArgb(48, 210, 132), true);
			}
			else if (_hasCursor && IsMaskMode)
			{
				Color cursorColor = IsRestoreMode ? Color.FromArgb(48, 210, 132) : Color.FromArgb(235, 70, 76);
				RectangleF cursorBounds = GetMaskCursorBounds(_cursorImagePoint);
				DrawMaskShape(graphics, cursorBounds, IsBrushMode ? RegionShape.Circle : _maskShape, cursorColor, true);
			}
			DrawInspectionHud(graphics);
		}

		private void DrawInspectionHud(Graphics graphics)
		{
			if (string.IsNullOrEmpty(_inspectionHudText) || ClientSize.Width <= 44) return;
			var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
			int width = Math.Min(360, ClientSize.Width - 44);
			Size textSize = TextRenderer.MeasureText(graphics, _inspectionHudText, Font, new Size(width, int.MaxValue), flags);
			var panel = new Rectangle(12, 12, textSize.Width + 20, textSize.Height + 16);
			using (var background = new SolidBrush(Color.FromArgb(225, 20, 25, 31)))
				graphics.FillRectangle(background, panel);
			Color statusColor = _inspectionSucceeded ? Color.FromArgb(94, 234, 170) : Color.FromArgb(255, 120, 120);
			using (var accent = new SolidBrush(statusColor))
				graphics.FillRectangle(accent, panel.Left, panel.Top, 3, panel.Height);
			TextRenderer.DrawText(graphics, _inspectionHudText, Font,
				new Rectangle(panel.Left + 10, panel.Top + 8, textSize.Width, textSize.Height), statusColor, flags);
		}

		private RectangleF GetMaskCursorBounds(PointF point)
		{
			return IsBrushMode ? new RectangleF(point.X - BrushRadius, point.Y - BrushRadius, BrushRadius * 2f, BrushRadius * 2f) : CreateMaskBounds(point, point);
		}

		private void PaintBrushSegment(PointF start, PointF end)
		{
			float dx = end.X - start.X;
			float dy = end.Y - start.Y;
			double distance = Math.Sqrt(dx * dx + dy * dy);
			int steps = Math.Max(1, (int)Math.Ceiling(distance / Math.Max(0.5f, BrushRadius * 0.5f)));
			List<MaskStroke> added = new List<MaskStroke>();
			for (int index = 1; index <= steps; index++)
			{
				float t = (float)index / steps;
				PointF center = new PointF(start.X + dx * t, start.Y + dy * t);
				RectangleF bounds = new RectangleF(center.X - BrushRadius, center.Y - BrushRadius, BrushRadius * 2f, BrushRadius * 2f);
				if (!bounds.IntersectsWith(_templateRoi) || !bounds.IntersectsWith(new RectangleF(PointF.Empty, _sourceImage.Size)))
				{
					continue;
				}
				added.Add(new MaskStroke { Center = center, Radius = BrushRadius, Width = BrushRadius * 2f, Height = BrushRadius * 2f,
					Shape = RegionShape.Circle, Mode = IsRestoreMode ? MaskStrokeMode.Restore : MaskStrokeMode.Exclude, GroupId = _activeStrokeGroup });
			}
			if (added.Count == 0)
			{
				return;
			}
			_maskStrokes.AddRange(added);
			RenderMaskStrokes(added);
			ApplyStrokesToFeatureVisibility(added);
			InvalidateMaskArea(added);
			if (!_brushChanged)
			{
				_brushChanged = true;
				this.MaskEditStarted?.Invoke(this, EventArgs.Empty);
			}
		}

		private bool AddMaskStroke(RectangleF bounds)
		{
			RectangleF normalized = ClampShapeBounds(RegionShapeGeometry.NormalizeBounds(bounds, _maskShape), _maskShape);
			if (normalized.IsEmpty || _templateRoi.IsEmpty || !normalized.IntersectsWith(_templateRoi))
			{
				return false;
			}
			PointF center = GetCenter(normalized);
			MaskStroke stroke = new MaskStroke
			{
				Center = center,
				Radius = Math.Max(normalized.Width, normalized.Height) * 0.5f,
				Shape = _maskShape,
				Width = normalized.Width,
				Height = normalized.Height,
				Mode = (InteractionMode == CanvasInteractionMode.RestoreMask) ? MaskStrokeMode.Restore : MaskStrokeMode.Exclude,
				GroupId = _activeStrokeGroup
			};
			_maskStrokes.Add(stroke);
			RenderMaskStrokes(new[] { stroke });
			ApplyStrokeToFeatureVisibility(stroke);
			RebuildVisibleFeaturePointList();
			InvalidateMaskArea(new[] { stroke });
			return true;
		}

		private RectangleF CreateMaskBounds(PointF start, PointF end)
		{
			RectangleF bounds;
			if (Math.Abs(end.X - start.X) < 2f && Math.Abs(end.Y - start.Y) < 2f)
			{
				bounds = new RectangleF(start.X - BrushRadius, start.Y - BrushRadius, BrushRadius * 2f, BrushRadius * 2f);
			}
			else if (_maskShape == RegionShape.Circle)
			{
				bounds = CreateCircleBounds(start, end);
			}
			else
			{
				bounds = RectangleF.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
			}
			return ClampShapeBounds(bounds, _maskShape);
		}

		private RectangleF CalculateDraggedRoi(PointF current)
		{
			RegionShape shape = GetActiveRoiShape();
			if (_roiDragMode == RoiDragMode.Create)
			{
				return (shape == RegionShape.Circle) ? CreateCircleBounds(_dragStartImagePoint, current) : ClampShapeBounds(RectangleFromPoints(_dragStartImagePoint, current), shape);
			}
			if (_roiDragMode == RoiDragMode.Move)
			{
				float dx = current.X - _dragStartImagePoint.X;
				float dy = current.Y - _dragStartImagePoint.Y;
				return ClampRoi(new RectangleF(_dragStartRoi.X + dx, _dragStartRoi.Y + dy, _dragStartRoi.Width, _dragStartRoi.Height));
			}
			if (shape == RegionShape.Circle)
			{
				return CreateCircleBounds(GetCenter(_dragStartRoi), current);
			}
			PointF fixedCorner;
			switch (_roiDragMode)
			{
			case RoiDragMode.ResizeNorth:
				return ClampShapeBounds(RectangleF.FromLTRB(_dragStartRoi.Left, Math.Min(current.Y, _dragStartRoi.Bottom - 1f), _dragStartRoi.Right, _dragStartRoi.Bottom), shape);
			case RoiDragMode.ResizeEast:
				return ClampShapeBounds(RectangleF.FromLTRB(_dragStartRoi.Left, _dragStartRoi.Top, Math.Max(current.X, _dragStartRoi.Left + 1f), _dragStartRoi.Bottom), shape);
			case RoiDragMode.ResizeSouth:
				return ClampShapeBounds(RectangleF.FromLTRB(_dragStartRoi.Left, _dragStartRoi.Top, _dragStartRoi.Right, Math.Max(current.Y, _dragStartRoi.Top + 1f)), shape);
			case RoiDragMode.ResizeWest:
				return ClampShapeBounds(RectangleF.FromLTRB(Math.Min(current.X, _dragStartRoi.Right - 1f), _dragStartRoi.Top, _dragStartRoi.Right, _dragStartRoi.Bottom), shape);
			case RoiDragMode.ResizeNorthWest:
				fixedCorner = new PointF(_dragStartRoi.Right, _dragStartRoi.Bottom);
				break;
			case RoiDragMode.ResizeNorthEast:
				fixedCorner = new PointF(_dragStartRoi.Left, _dragStartRoi.Bottom);
				break;
			case RoiDragMode.ResizeSouthEast:
				fixedCorner = new PointF(_dragStartRoi.Left, _dragStartRoi.Top);
				break;
			default:
				fixedCorner = new PointF(_dragStartRoi.Right, _dragStartRoi.Top);
				break;
			}
			return ClampShapeBounds(RectangleFromPoints(fixedCorner, current), shape);
		}

		private RoiDragMode HitTestRoi(RectangleF roi, Point screenPoint, RegionShape shape)
		{
			if (roi.IsEmpty)
			{
				return RoiDragMode.None;
			}
			GetResizeHandles(roi, shape, out var handles, out var modes);
			for (int index = 0; index < handles.Length; index++)
			{
				PointF handle = ImageToScreen(handles[index]);
				if (Math.Abs(handle.X - (float)screenPoint.X) <= 9f && Math.Abs(handle.Y - (float)screenPoint.Y) <= 9f)
				{
					return modes[index];
				}
			}
			if (!RegionShapeGeometry.Contains(ScreenToImage(screenPoint), roi, shape))
			{
				return RoiDragMode.None;
			}
			return RoiDragMode.Move;
		}

		private static void GetResizeHandles(RectangleF roi, RegionShape shape, out PointF[] handles, out RoiDragMode[] modes)
		{
			float centerX = roi.Left + roi.Width * 0.5f;
			float centerY = roi.Top + roi.Height * 0.5f;
			if (shape == RegionShape.Circle || shape == RegionShape.Diamond)
			{
				handles = new[]
				{
					new PointF(centerX, roi.Top),
					new PointF(roi.Right, centerY),
					new PointF(centerX, roi.Bottom),
					new PointF(roi.Left, centerY)
				};
				modes = new[] { RoiDragMode.ResizeNorth, RoiDragMode.ResizeEast, RoiDragMode.ResizeSouth, RoiDragMode.ResizeWest };
				return;
			}
			handles = new[]
			{
				new PointF(roi.Left, roi.Top),
				new PointF(centerX, roi.Top),
				new PointF(roi.Right, roi.Top),
				new PointF(roi.Right, centerY),
				new PointF(roi.Right, roi.Bottom),
				new PointF(centerX, roi.Bottom),
				new PointF(roi.Left, roi.Bottom),
				new PointF(roi.Left, centerY)
			};
			modes = new[]
			{
				RoiDragMode.ResizeNorthWest,
				RoiDragMode.ResizeNorth,
				RoiDragMode.ResizeNorthEast,
				RoiDragMode.ResizeEast,
				RoiDragMode.ResizeSouthEast,
				RoiDragMode.ResizeSouth,
				RoiDragMode.ResizeSouthWest,
				RoiDragMode.ResizeWest
			};
		}

		private RegionShape GetActiveRoiShape()
		{
			return (InteractionMode == CanvasInteractionMode.EditTemplateRoi) ? _templateRoiShape : RegionShape.Rectangle;
		}

		private void UpdateIdleCursor(Point location)
		{
			if (_panning || _painting || _roiDragMode != RoiDragMode.None)
			{
				return;
			}
			if (InteractionMode == CanvasInteractionMode.Pan)
			{
				Cursor = Cursors.Hand;
				return;
			}
			if (InteractionMode == CanvasInteractionMode.ExcludeMask || InteractionMode == CanvasInteractionMode.RestoreMask)
			{
				Cursor = Cursors.Cross;
				return;
			}
			RectangleF roi = (InteractionMode == CanvasInteractionMode.EditTemplateRoi) ? _templateRoi : _searchRoi;
			Cursor = GetCursorForDragMode(HitTestRoi(roi, location, GetActiveRoiShape()));
		}

		private static Cursor GetCursorForDragMode(RoiDragMode mode)
		{
			switch (mode)
			{
			case RoiDragMode.Move:
				return Cursors.SizeAll;
			case RoiDragMode.ResizeNorth:
			case RoiDragMode.ResizeSouth:
				return Cursors.SizeNS;
			case RoiDragMode.ResizeEast:
			case RoiDragMode.ResizeWest:
				return Cursors.SizeWE;
			case RoiDragMode.ResizeNorthWest:
			case RoiDragMode.ResizeSouthEast:
				return Cursors.SizeNWSE;
			case RoiDragMode.ResizeNorthEast:
			case RoiDragMode.ResizeSouthWest:
				return Cursors.SizeNESW;
			default:
				return Cursors.Cross;
			}
		}

		private void DrawRoi(Graphics graphics, RectangleF roi, RegionShape shape, Color color, string label, bool selected, bool drawCenterLines)
		{
			if (roi.IsEmpty)
			{
				return;
			}
			RectangleF screen = ImageRectangleToScreen(roi);
			using (Pen pen = new Pen(color, selected ? 3f : 2f))
			{
				pen.DashStyle = ((!selected) ? DashStyle.Dash : DashStyle.Solid);
				using (GraphicsPath path = RegionShapeGeometry.CreatePath(screen, shape))
				{
					graphics.DrawPath(pen, path);
				}
			}
			if (drawCenterLines)
			{
				DrawTemplateCenterLines(graphics, screen, shape);
			}
			using (SolidBrush background = new SolidBrush(Color.FromArgb(190, 24, 27, 31)))
			{
				using (SolidBrush textBrush = new SolidBrush(color))
				{
					SizeF textSize = graphics.MeasureString(label, Font);
					RectangleF textRect = new RectangleF(screen.Left, screen.Top - textSize.Height - 2f, textSize.Width + 8f, textSize.Height + 2f);
					graphics.FillRectangle(background, textRect);
					graphics.DrawString(label, Font, textBrush, textRect.Left + 4f, textRect.Top + 1f);
				}
			}
			if (!selected)
			{
				return;
			}
			GetResizeHandles(roi, shape, out var handles, out var modes);
			foreach (PointF imageHandle in handles)
			{
				PointF handle = ImageToScreen(imageHandle);
				using (SolidBrush fill = new SolidBrush(Color.White))
				using (Pen outline = new Pen(color, 2f))
				{
					graphics.FillRectangle(fill, handle.X - 4.5f, handle.Y - 4.5f, 9f, 9f);
					graphics.DrawRectangle(outline, handle.X - 4.5f, handle.Y - 4.5f, 9f, 9f);
				}
			}
		}

		private static void DrawTemplateCenterLines(Graphics graphics, RectangleF screenBounds, RegionShape shape)
		{
			PointF center = new PointF(screenBounds.Left + screenBounds.Width * 0.5f, screenBounds.Top + screenBounds.Height * 0.5f);
			GraphicsState state = graphics.Save();
			try
			{
				using (GraphicsPath clipPath = RegionShapeGeometry.CreatePath(screenBounds, shape))
				using (Pen shadow = new Pen(Color.FromArgb(175, 0, 0, 0), 3f))
				using (Pen centerPen = new Pen(Color.FromArgb(245, 255, 235, 80), 1.25f))
				{
					graphics.SetClip(clipPath, CombineMode.Intersect);
					graphics.DrawLine(shadow, screenBounds.Left, center.Y, screenBounds.Right, center.Y);
					graphics.DrawLine(shadow, center.X, screenBounds.Top, center.X, screenBounds.Bottom);
					graphics.DrawLine(centerPen, screenBounds.Left, center.Y, screenBounds.Right, center.Y);
					graphics.DrawLine(centerPen, center.X, screenBounds.Top, center.X, screenBounds.Bottom);
				}
			}
			finally
			{
				graphics.Restore(state);
			}
			using (SolidBrush centerFill = new SolidBrush(Color.FromArgb(245, 255, 235, 80)))
			using (Pen centerOutline = new Pen(Color.FromArgb(210, 0, 0, 0), 2f))
			{
				graphics.FillEllipse(centerFill, center.X - 3f, center.Y - 3f, 6f, 6f);
				graphics.DrawEllipse(centerOutline, center.X - 3f, center.Y - 3f, 6f, 6f);
			}
		}

		private void DrawMaskShape(Graphics graphics, RectangleF imageBounds, RegionShape shape, Color color, bool selected)
		{
			RectangleF screen = ImageRectangleToScreen(imageBounds);
			using (GraphicsPath path = RegionShapeGeometry.CreatePath(screen, shape))
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(45, color)))
			using (Pen pen = new Pen(color, selected ? 2f : 1f))
			{
				graphics.FillPath(brush, path);
				graphics.DrawPath(pen, path);
			}
		}

		private void DrawContours(Graphics graphics, IEnumerable<List<PointF>> contours, Color color, DashStyle dashStyle)
		{
			using (Pen pen = new Pen(color, 1.5f)
			{
				DashStyle = dashStyle
			})
			{
				foreach (List<PointF> contour in contours ?? Enumerable.Empty<List<PointF>>())
				{
					if (contour != null && contour.Count >= 2)
					{
						graphics.DrawLines(pen, contour.Select(ImageToScreen).ToArray());
					}
				}
			}
		}

		private void DrawContourPoints(Graphics graphics, IEnumerable<List<PointF>> contours, Color color, float radius)
		{
			DrawFeaturePoints(graphics, (contours ?? Enumerable.Empty<List<PointF>>()).SelectMany((List<PointF> contour) => contour ?? new List<PointF>()), color, radius);
		}

		private void DrawFeaturePoints(Graphics graphics, IEnumerable<PointF> points, Color color, float radius)
		{
			RectangleF clip = graphics.ClipBounds;
			using (SolidBrush brush = new SolidBrush(color))
			{
				foreach (PointF point in points ?? Enumerable.Empty<PointF>())
				{
					if (!float.IsNaN(point.X) && !float.IsNaN(point.Y))
					{
						PointF screen = ImageToScreen(point);
						if (screen.X + radius >= clip.Left && screen.X - radius <= clip.Right && screen.Y + radius >= clip.Top && screen.Y - radius <= clip.Bottom)
						{
							graphics.FillEllipse(brush, screen.X - radius, screen.Y - radius, radius * 2f, radius * 2f);
						}
					}
				}
			}
		}

		private void RebuildMaskOverlay()
		{
			_maskOverlay?.Dispose();
			_maskOverlay = null;
			if (_sourceImage != null)
			{
				_maskOverlay = new Bitmap(_sourceImage.Width, _sourceImage.Height, PixelFormat.Format32bppArgb);
				RenderMaskStrokes(_maskStrokes);
			}
		}

		private void RenderMaskStrokes(IEnumerable<MaskStroke> strokes)
		{
			if (_maskOverlay == null || _templateRoi.IsEmpty)
			{
				return;
			}
			using (Graphics graphics = Graphics.FromImage(_maskOverlay))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
				using (GraphicsPath templatePath = RegionShapeGeometry.CreatePath(_templateRoi, _templateRoiShape))
				{
				graphics.SetClip(templatePath);
				using (SolidBrush excludeBrush = new SolidBrush(Color.FromArgb(255, 235, 60, 65)))
				{
					using (SolidBrush restoreBrush = new SolidBrush(Color.Transparent))
					{
						foreach (MaskStroke stroke in strokes ?? Enumerable.Empty<MaskStroke>())
						{
							if (stroke != null && !stroke.GetBounds().IsEmpty)
							{
								RectangleF bounds = stroke.GetBounds();
								using (GraphicsPath path = RegionShapeGeometry.CreatePath(bounds, stroke.Shape))
								{
								if (stroke.Mode == MaskStrokeMode.Exclude)
								{
									graphics.CompositingMode = CompositingMode.SourceOver;
									graphics.FillPath(excludeBrush, path);
								}
								else
								{
									graphics.CompositingMode = CompositingMode.SourceCopy;
									graphics.FillPath(restoreBrush, path);
								}
								}
							}
						}
					}
				}
				graphics.ResetClip();
				}
			}
		}

		private void RebuildFeatureVisibility()
		{
			_featureVisibility = new bool[_featurePoints.Count];
			_featureInTemplate = new bool[_featurePoints.Count];
			for (int index = 0; index < _featureVisibility.Length; index++)
			{
				_featureInTemplate[index] = !IsContourSeparator(_featurePoints[index]) && RegionShapeGeometry.Contains(_featurePoints[index], _templateRoi, _templateRoiShape);
				_featureVisibility[index] = _featureInTemplate[index];
			}
			foreach (MaskStroke stroke in _maskStrokes)
			{
				ApplyStrokeToFeatureVisibility(stroke);
			}
			RebuildVisibleFeaturePointList();
		}

		private void ApplyStrokesToFeatureVisibility(IEnumerable<MaskStroke> strokes)
		{
			foreach (MaskStroke stroke in strokes ?? Enumerable.Empty<MaskStroke>())
			{
				ApplyStrokeToFeatureVisibility(stroke);
			}
			RebuildVisibleFeaturePointList();
		}

		private void ApplyStrokeToFeatureVisibility(MaskStroke stroke)
		{
			if (stroke == null || _featureVisibility.Length != _featurePoints.Count)
			{
				return;
			}
			RectangleF bounds = stroke.GetBounds();
			if (bounds.IsEmpty) return;
			bool visible = stroke.Mode == MaskStrokeMode.Restore;
			for (int index = 0; index < _featurePoints.Count; index++)
			{
				if (!_featureInTemplate[index] || _featureVisibility[index] == visible) continue;
				PointF point = _featurePoints[index];
				if (point.X < bounds.Left || point.X > bounds.Right || point.Y < bounds.Top || point.Y > bounds.Bottom) continue;
				if (RegionShapeGeometry.Contains(point, bounds, stroke.Shape))
				{
					_featureVisibility[index] = visible;
				}
			}
		}

		private void RebuildVisibleFeaturePointList()
		{
			_visibleFeaturePoints.Clear();
			bool segmentHasPoint = false;
			for (int index = 0; index < _featurePoints.Count; index++)
			{
				PointF point = _featurePoints[index];
				if (IsContourSeparator(point) || !_featureVisibility[index])
				{
					if (segmentHasPoint)
					{
						_visibleFeaturePoints.Add(new PointF(float.NaN, float.NaN));
						segmentHasPoint = false;
					}
				}
				else
				{
					_visibleFeaturePoints.Add(point);
					segmentHasPoint = true;
				}
			}
			while (_visibleFeaturePoints.Count > 0 && IsContourSeparator(_visibleFeaturePoints[_visibleFeaturePoints.Count - 1]))
			{
				_visibleFeaturePoints.RemoveAt(_visibleFeaturePoints.Count - 1);
			}
		}

		private void InvalidateMaskArea(IEnumerable<MaskStroke> strokes)
		{
			RectangleF imageBounds = RectangleF.Empty;
			foreach (MaskStroke stroke in strokes ?? Enumerable.Empty<MaskStroke>())
			{
				if (stroke != null)
				{
					RectangleF strokeBounds = stroke.GetBounds();
					float padding = 6f / Math.Max(0.02f, _zoom);
					strokeBounds.Inflate(padding, padding);
					imageBounds = (imageBounds.IsEmpty ? strokeBounds : RectangleF.Union(imageBounds, strokeBounds));
				}
			}
			if (!imageBounds.IsEmpty)
			{
				Invalidate(Rectangle.Ceiling(ImageRectangleToScreen(imageBounds)));
			}
		}

		private void InvalidateBrushCursor(PointF previousCursor, bool hadPrevious, PointF currentCursor)
		{
			if (IsMaskMode)
			{
				// Area cursors move inward at image edges. Invalidate their actual drawn bounds.
				RectangleF dirty = ImageRectangleToScreen(GetMaskCursorBounds(currentCursor));
				if (hadPrevious)
				{
					RectangleF old = ImageRectangleToScreen(GetMaskCursorBounds(previousCursor));
					dirty = RectangleF.Union(dirty, old);
				}
				dirty.Inflate(4f, 4f);
				Invalidate(Rectangle.FromLTRB((int)Math.Floor(dirty.Left), (int)Math.Floor(dirty.Top), (int)Math.Ceiling(dirty.Right), (int)Math.Ceiling(dirty.Bottom)));
			}
		}

		private static bool IsContourSeparator(PointF point)
		{
			if (!float.IsNaN(point.X))
			{
				return float.IsNaN(point.Y);
			}
			return true;
		}

		private bool IsInsideImage(PointF point)
		{
			return _sourceImage != null && point.X >= 0f && point.Y >= 0f && point.X < _sourceImage.Width && point.Y < _sourceImage.Height;
		}

		private static PointF GetCenter(RectangleF bounds)
		{
			return new PointF(bounds.Left + bounds.Width * 0.5f, bounds.Top + bounds.Height * 0.5f);
		}

		private RectangleF CreateCircleBounds(PointF center, PointF edge)
		{
			float dx = edge.X - center.X;
			float dy = edge.Y - center.Y;
			float radius = (float)Math.Sqrt(dx * dx + dy * dy);
			if (_sourceImage == null)
			{
				radius = Math.Max(4f, radius);
				return new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
			}
			float maximumRadius = Math.Min(Math.Min(center.X, _sourceImage.Width - center.X), Math.Min(center.Y, _sourceImage.Height - center.Y));
			if (maximumRadius <= 0.5f)
			{
				return RectangleF.Empty;
			}
			radius = Math.Min(maximumRadius, Math.Max(Math.Min(4f, maximumRadius), radius));
			return new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
		}

		private RectangleF ClampShapeBounds(RectangleF bounds, RegionShape shape)
		{
			if (bounds.Width <= 0f || bounds.Height <= 0f)
			{
				return RectangleF.Empty;
			}
			bounds = RegionShapeGeometry.NormalizeBounds(bounds, shape);
			if (shape == RegionShape.Circle && _sourceImage != null)
			{
				float side = Math.Min(bounds.Width, Math.Min(_sourceImage.Width, _sourceImage.Height));
				float left = Math.Max(0f, Math.Min(_sourceImage.Width - side, bounds.Left + (bounds.Width - side) * 0.5f));
				float top = Math.Max(0f, Math.Min(_sourceImage.Height - side, bounds.Top + (bounds.Height - side) * 0.5f));
				return new RectangleF(left, top, side, side);
			}
			return ClampRoi(bounds);
		}

		private RectangleF ClampRoi(RectangleF roi)
		{
			if (_sourceImage == null || roi.Width <= 0f || roi.Height <= 0f)
			{
				if (!(roi.Width <= 0f) && !(roi.Height <= 0f))
				{
					return roi;
				}
				return RectangleF.Empty;
			}
			float width = Math.Max(8f, Math.Min(_sourceImage.Width, roi.Width));
			float height = Math.Max(8f, Math.Min(_sourceImage.Height, roi.Height));
			float num = Math.Max(0f, Math.Min((float)_sourceImage.Width - width, roi.X));
			float y = Math.Max(0f, Math.Min((float)_sourceImage.Height - height, roi.Y));
			return new RectangleF(num, y, width, height);
		}

		private PointF ClampImagePoint(PointF point)
		{
			if (_sourceImage == null)
			{
				return point;
			}
			return new PointF(Math.Max(0f, Math.Min((float)_sourceImage.Width - 1f, point.X)), Math.Max(0f, Math.Min((float)_sourceImage.Height - 1f, point.Y)));
		}

		private RectangleF RectangleFromPoints(PointF first, PointF second)
		{
			RectangleF rectangle = RectangleF.FromLTRB(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));
			return ClampRoi(rectangle);
		}

		private PointF ImageToScreen(PointF imagePoint)
		{
			return new PointF(imagePoint.X * _zoom + _pan.X, imagePoint.Y * _zoom + _pan.Y);
		}

		private RectangleF ImageRectangleToScreen(RectangleF rectangle)
		{
			PointF topLeft = ImageToScreen(new PointF(rectangle.Left, rectangle.Top));
			return new RectangleF(topLeft.X, topLeft.Y, rectangle.Width * _zoom, rectangle.Height * _zoom);
		}

		private static float ClampZoom(float value)
		{
			return Math.Max(0.02f, Math.Min(50f, value));
		}

		private static bool ApproximatelyEqual(RectangleF left, RectangleF right)
		{
			if (Math.Abs(left.X - right.X) < 0.01f && Math.Abs(left.Y - right.Y) < 0.01f && Math.Abs(left.Width - right.Width) < 0.01f)
			{
				return Math.Abs(left.Height - right.Height) < 0.01f;
			}
			return false;
		}

		private static List<List<PointF>> CloneContours(IEnumerable<List<PointF>> contours)
		{
			if (contours != null)
			{
				return contours.Select((List<PointF> contour) => new List<PointF>(contour ?? new List<PointF>())).ToList();
			}
			return new List<List<PointF>>();
		}
	}
}

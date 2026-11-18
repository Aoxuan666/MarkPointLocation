using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Seprays.MarkVision
{
	public class MarkSetupControl : UserControl
	{
		private readonly HalconMarkLocator _locator = new HalconMarkLocator();

		private readonly MarkLocatorSdk _sdk = new MarkLocatorSdk();

		private Bitmap _sourceImage;

		private Bitmap _testImage;

		private bool _showingTestImage;

		private MarkLocateJob _job = new MarkLocateJob();

		private List<PointF> _rawFeaturePoints = new List<PointF>();

		private bool _loading;

		private bool _busy;

		private IContainer components;

		private ToolStrip toolMain;

		private ToolStripDropDownButton btnTemplateShape;

		private ToolStripDropDownButton btnMaskShape;

		private ToolStripButton btnTemplateRoi;

		private ToolStripButton btnSearchRoi;

		private ToolStripSeparator toolSeparator1;

		private ToolStripButton btnMaskExclude;

		private ToolStripButton btnMaskRestore;

		private ToolStripButton btnUndoMask;

		private ToolStripButton btnClearMask;

		private ToolStripSeparator toolSeparator2;

		private ToolStripButton btnFit;

		private ToolStripButton btnActual;

		private ToolStripLabel lblZoom;

		private ToolStripButton btnLoadTeachImage;

		private ToolStripButton btnLoadTestImage;

		private ToolStripButton btnShowTeachImage;

		private ToolStripButton btnShowTestImage;

		private ToolStripLabel lblImageState;

		private SplitContainer splitMain;

		private MaskImageCanvas canvas;

		private Panel panelSettings;

		private Label lblTitle;

		private Label lblSubtitle;

		private Label lblModelStateTitle;

		private Label lblModelState;

		private Panel panelParameters;

		private GroupBox grpFeature;

		private Label lblTemplateMean;

		private Label lblEdgeSigma;

		private Label lblEdgeLow;

		private Label lblEdgeHigh;

		private Label lblFeatureDistance;

		private Label lblMaxFeaturePoints;

		private Label lblBrushRadius;

		private NumericUpDown numTemplateMean;

		private NumericUpDown numEdgeSigma;

		private NumericUpDown numEdgeLow;

		private NumericUpDown numEdgeHigh;

		private NumericUpDown numFeatureDistance;

		private NumericUpDown numMaxFeaturePoints;

		private NumericUpDown numBrushRadius;

		private Label lblFeatureCount;

		private Button btnExtract;

		private Button btnResetFeatures;

		private Button btnBrushMask;

		private GroupBox grpModel;

		private Label lblLevels;

		private Label lblMinContrast;

		private NumericUpDown numLevels;

		private NumericUpDown numMinContrast;

		private CheckBox chkUsePolarity;

		private GroupBox grpSearch;

		private Label lblSearchMean;

		private Label lblMinScore;

		private Label lblAngleStart;

		private Label lblAngleExtent;

		private Label lblScaleMin;

		private Label lblScaleMax;

		private Label lblMaxOverlap;

		private Label lblGreediness;

		private CheckBox chkSearchRoiEnabled;

		private NumericUpDown numSearchMean;

		private NumericUpDown numMinScore;

		private NumericUpDown numAngleStart;

		private NumericUpDown numAngleExtent;

		private NumericUpDown numScaleMin;

		private NumericUpDown numScaleMax;

		private NumericUpDown numMaxOverlap;

		private NumericUpDown numGreediness;

		private GroupBox grpCircle;

		private Label lblCircleRadius;

		private CheckBox chkCircleRefinement;

		private CheckBox chkRequireCircle;

		private NumericUpDown numCircleRadius;

		private Panel panelActions;

		private Button btnTeach;

		private Button btnTest;

		private Label lblStatus;

		private ToolTip toolTip;

		[Browsable(false)]
		public MarkLocateJob Job
		{
			get
			{
				PullControlsToJob();
				UpdateVisibleFeaturePoints();
				return _job.DeepClone();
			}
		}

		[Category("Mark 定位")]
		public event EventHandler JobChanged;

		[Category("Mark 定位")]
		public event EventHandler TeachCompleted;

		[Category("Mark 定位")]
		public event EventHandler<MarkLocateCompletedEventArgs> TestCompleted;

		[Category("Mark 定位")]
		public event EventHandler StatusChanged;

		public MarkSetupControl()
		{
			InitializeComponent();
			SetInteractionMode(CanvasInteractionMode.Pan);
			UpdateModelState();
			UpdateSearchRoiState();
		}

		public void LoadConfiguration(Bitmap sourceImage, MarkLocateJob currentJob)
		{
			if (_busy)
			{
				throw new InvalidOperationException("建模或测试期间不能加载新配置。");
			}
			if (sourceImage == null)
			{
				throw new ArgumentNullException("sourceImage");
			}
			_loading = true;
			try
			{
				_sourceImage?.Dispose();
				_sourceImage = new Bitmap(sourceImage);
				_testImage?.Dispose();
				_testImage = null;
				_showingTestImage = false;
				_job = currentJob?.DeepClone() ?? new MarkLocateJob();
				_job.Normalize(_sourceImage.Size);
				_rawFeaturePoints = ((_job.TeachData.FeaturePoints == null) ? new List<PointF>() : new List<PointF>(_job.TeachData.FeaturePoints));
				LoadJobToControls();
				canvas.SourceImage = _sourceImage;
				canvas.TemplateRoiShape = _job.TemplateRoiShape;
				canvas.TemplateRoi = _job.TemplateRoi;
				canvas.SearchRoi = _job.SearchRoi;
				canvas.SearchRoiEnabled = _job.SearchRoiEnabled;
				canvas.MaskStrokes = _job.MaskStrokes;
				canvas.BrushRadius = (float)_job.Parameters.BrushRadius;
				canvas.MaskShape = RegionShape.Circle;
				btnTemplateShape.Text = "模板形状: " + GetShapeText(_job.TemplateRoiShape);
				btnMaskShape.Text = "掩膜形状: 圆形";
				canvas.FeaturePoints = _rawFeaturePoints;
				canvas.ModelContours = new List<List<PointF>>();
				canvas.MatchedContours = new List<List<PointF>>();
				UpdateFeatureCount();
				UpdateImageState();
				UpdateSearchRoiState();
				UpdateModelState();
			}
			finally
			{
				_loading = false;
			}
			ExtractFeaturePoints(invalidateModel: false, showMessage: false);
		}

		public bool ValidateConfiguration(out string message)
		{
			if (_busy)
			{
				message = "请等待建模或测试完成。";
				return false;
			}
			PullControlsToJob();
			UpdateVisibleFeaturePoints();
			if (_sourceImage == null)
			{
				message = "源图像为空。";
				return false;
			}
			if (_job.TemplateRoi.Width < 16f || _job.TemplateRoi.Height < 16f)
			{
				message = "模板区域过小。";
				return false;
			}
			if (_job.EnableTemplateMatching && (!_job.TeachData.HasModel || _job.TeachData.ModelBytes == null || _job.TeachData.ModelBytes.Length == 0))
			{
				message = "请先创建外轮廓模板。";
				return false;
			}
			if (!_job.EnableTemplateMatching && !_job.EnableCircleDetection)
			{
				message = "上位机未启用模板匹配或找圆。";
				return false;
			}
			message = string.Empty;
			return true;
		}

		private void templateShape_Click(object sender, EventArgs e)
		{
			ToolStripItem item = sender as ToolStripItem;
			if (item == null || item.Tag == null)
			{
				return;
			}
			RegionShape shape = (RegionShape)item.Tag;
			SetInteractionMode(CanvasInteractionMode.EditTemplateRoi);
			canvas.TemplateRoiShape = shape;
			_job.TemplateRoiShape = shape;
			btnTemplateShape.Text = "模板形状: " + GetShapeText(shape);
			ExtractFeaturePoints(invalidateModel: false, showMessage: false);
		}

		public void LoadTeachingImage(Bitmap image)
		{
			if (_busy)
			{
				throw new InvalidOperationException("建模或测试期间不能加载新图像。");
			}
			if (image == null)
			{
				throw new ArgumentNullException("image");
			}
			_sourceImage?.Dispose();
			_sourceImage = new Bitmap(image);
			_showingTestImage = false;
			_job.Normalize(_sourceImage.Size);
			_rawFeaturePoints.Clear();
			canvas.SourceImage = _sourceImage;
			canvas.FeaturePoints = _rawFeaturePoints;
			canvas.MatchedContours = new List<List<PointF>>();
			InvalidateTeachModel("示教图已更新，请重新创建模型。", notifyChanged: false);
			UpdateImageState();
			ExtractFeaturePoints(invalidateModel: false, showMessage: false);
		}

		public void LoadTestImage(Bitmap image)
		{
			if (_busy)
			{
				throw new InvalidOperationException("建模或测试期间不能加载新图像。");
			}
			if (image == null)
			{
				throw new ArgumentNullException("image");
			}
			_testImage?.Dispose();
			_testImage = new Bitmap(image);
			ShowTestImage();
			SetStatus("测试图已加载，可执行测试定位。");
		}

		public void ShowTeachingImage()
		{
			if (_sourceImage == null)
			{
				return;
			}
			_showingTestImage = false;
			canvas.SourceImage = _sourceImage;
			canvas.FeaturePoints = _rawFeaturePoints;
			UpdateImageState();
		}

		public void ShowTestImage()
		{
			if (_testImage == null)
			{
				SetStatus("请先加载测试图。");
				return;
			}
			_showingTestImage = true;
			canvas.SourceImage = _testImage;
			canvas.FeaturePoints = new List<PointF>();
			UpdateImageState();
		}

		private void maskShape_Click(object sender, EventArgs e)
		{
			ToolStripItem item = sender as ToolStripItem;
			if (item == null || item.Tag == null)
			{
				return;
			}
			RegionShape shape = (RegionShape)item.Tag;
			canvas.MaskShape = shape;
			btnMaskShape.Text = "掩膜形状: " + GetShapeText(shape);
			if (canvas.InteractionMode != CanvasInteractionMode.ExcludeMask && canvas.InteractionMode != CanvasInteractionMode.RestoreMask)
			{
				SetInteractionMode(CanvasInteractionMode.ExcludeMask);
			}
		}

		private void btnTemplateRoi_Click(object sender, EventArgs e)
		{
			SetInteractionMode(CanvasInteractionMode.EditTemplateRoi);
		}

		private void btnSearchRoi_Click(object sender, EventArgs e)
		{
			SetInteractionMode(CanvasInteractionMode.EditSearchRoi);
		}

		private void btnMaskExclude_Click(object sender, EventArgs e)
		{
			SetInteractionMode(CanvasInteractionMode.ExcludeMask);
		}

		private void btnMaskRestore_Click(object sender, EventArgs e)
		{
			SetInteractionMode(CanvasInteractionMode.RestoreMask);
		}

		private void btnUndoMask_Click(object sender, EventArgs e)
		{
			canvas.UndoMaskStroke();
		}

		private void btnClearMask_Click(object sender, EventArgs e)
		{
			canvas.ClearMask();
		}

		private void btnBrushMask_Click(object sender, EventArgs e)
		{
			SetInteractionMode(canvas.InteractionMode == CanvasInteractionMode.BrushExclude
				? CanvasInteractionMode.Pan : CanvasInteractionMode.BrushExclude);
		}

		private void btnFit_Click(object sender, EventArgs e)
		{
			canvas.FitToWindow();
		}

		private void btnActual_Click(object sender, EventArgs e)
		{
			canvas.ActualSize();
		}

		private void btnLoadTeachImage_Click(object sender, EventArgs e)
		{
			using (var dialog = new OpenFileDialog())
			{
				dialog.Filter = "图像文件|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*";
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				using (var image = new Bitmap(dialog.FileName)) LoadTeachingImage(image);
			}
		}

		private void btnLoadTestImage_Click(object sender, EventArgs e)
		{
			using (var dialog = new OpenFileDialog())
			{
				dialog.Filter = "图像文件|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*";
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				using (var image = new Bitmap(dialog.FileName)) LoadTestImage(image);
			}
		}

		private void btnShowTeachImage_Click(object sender, EventArgs e)
		{
			ShowTeachingImage();
		}

		private void btnShowTestImage_Click(object sender, EventArgs e)
		{
			ShowTestImage();
		}

		private void btnExtract_Click(object sender, EventArgs e)
		{
			ExtractFeaturePoints(invalidateModel: true, showMessage: true);
		}

		private void btnResetFeatures_Click(object sender, EventArgs e)
		{
			_loading = true;
			try
			{
				_job.MaskStrokes.Clear();
				canvas.MaskStrokes = _job.MaskStrokes;
			}
			finally
			{
				_loading = false;
			}
			ExtractFeaturePoints(invalidateModel: true, showMessage: true);
		}

		private async void btnTeach_Click(object sender, EventArgs e)
		{
			if (_busy || _sourceImage == null)
			{
				return;
			}
			ShowTeachingImage();
			PullControlsToJob();
			UpdateVisibleFeaturePoints();
			if (CountRealPoints(_job.TeachData.FeaturePoints) < 12)
			{
				MessageBox.Show(this, "有效特征点不足，请调整边缘参数或恢复部分擦除点。", "创建模型", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			SetBusy(busy: true, "正在从外轮廓特征点创建模型...");
			try
			{
				MarkLocateJob workJob = _job.DeepClone();
				Bitmap workImage = new Bitmap(_sourceImage);
				MarkTeachData teachData;
				try
				{
					teachData = await Task.Run(() => _locator.Teach(workImage, workJob));
				}
				finally
				{
					if (workImage != null)
					{
						((IDisposable)workImage).Dispose();
					}
				}
				if (IsDisposed || Disposing)
				{
					return;
				}
				_job.TeachData = teachData;
				canvas.MatchedContours = new List<List<PointF>>();
				UpdateModelState();
				SetStatus($"模型创建完成，使用 {CountRealPoints(teachData.FeaturePoints)} 个外轮廓特征点。");
				this.TeachCompleted?.Invoke(this, EventArgs.Empty);
				this.JobChanged?.Invoke(this, EventArgs.Empty);
			}
			catch (Exception ex)
			{
				if (IsDisposed || Disposing)
				{
					return;
				}
				SetStatus("模型创建失败：" + ex.Message);
				MessageBox.Show(this, ex.Message, "模型创建失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}
			finally
			{
				if (!IsDisposed && !Disposing)
				{
					SetBusy(busy: false, null);
				}
			}
		}

		private async void btnTest_Click(object sender, EventArgs e)
		{
			if (_busy)
			{
				return;
			}
			if (_testImage == null)
			{
				SetStatus("请先载入测试图，再执行测试定位。");
				return;
			}
			Bitmap inspectionImage = _testImage;
			PullControlsToJob();
			if (_job.EnableTemplateMatching && !_job.TeachData.HasModel)
			{
				MessageBox.Show(this, "请先创建外轮廓模板。", "测试定位", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			ShowTestImage();
			SetBusy(busy: true, "正在测试定位...");
			try
			{
				MarkLocateJob workJob = _job.DeepClone();
				Bitmap workImage = new Bitmap(inspectionImage);
				MarkLocateResult result;
				try
				{
					result = await Task.Run(() => _sdk.RunInspection(workImage, workJob));
				}
				finally
				{
					if (workImage != null)
					{
						((IDisposable)workImage).Dispose();
					}
				}
				if (IsDisposed || Disposing)
				{
					return;
				}
				canvas.MatchedContours = result.MatchedContours;
				SetStatus(result.Message);
				this.TestCompleted?.Invoke(this, new MarkLocateCompletedEventArgs(result));
			}
			finally
			{
				if (!IsDisposed && !Disposing)
				{
					SetBusy(busy: false, null);
				}
			}
		}

		private void canvas_TemplateRoiChanged(object sender, EventArgs e)
		{
			if (!_loading)
			{
				_job.TemplateRoi = canvas.TemplateRoi;
				_job.TemplateRoiShape = canvas.TemplateRoiShape;
				_rawFeaturePoints.Clear();
				canvas.FeaturePoints = _rawFeaturePoints;
				InvalidateTeachModel("模板 ROI 已修改，松开鼠标后自动提取特征点。", notifyChanged: false);
			}
		}

		private void canvas_TemplateRoiEditCompleted(object sender, EventArgs e)
		{
			if (!_loading)
			{
				ExtractFeaturePoints(invalidateModel: false, showMessage: false);
			}
		}

		private void canvas_SearchRoiChanged(object sender, EventArgs e)
		{
			if (!_loading)
			{
				_job.SearchRoi = canvas.SearchRoi;
				SetStatus("搜索区域已更新。");
				this.JobChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		private void canvas_MaskChanged(object sender, EventArgs e)
		{
			if (!_loading)
			{
				_job.MaskStrokes = new List<MaskStroke>(canvas.MaskStrokes);
				UpdateVisibleFeaturePoints();
				InvalidateTeachModel("特征点掩膜已修改，请重新创建模型。", notifyChanged: true);
			}
		}

		private void canvas_ZoomChanged(object sender, EventArgs e)
		{
			lblZoom.Text = $"{canvas.ZoomFactor * 100f:F0}%";
		}

		private void canvas_MaskEditStarted(object sender, EventArgs e)
		{
			if (!_loading)
			{
				InvalidateTeachModel("特征点掩膜已修改，请重新创建模型。", notifyChanged: false);
			}
		}

		private void featureParameter_ValueChanged(object sender, EventArgs e)
		{
			if (!_loading)
			{
				PullControlsToJob();
				canvas.BrushRadius = (float)_job.Parameters.BrushRadius;
				ExtractFeaturePoints(invalidateModel: true, showMessage: false);
			}
		}

		private void brushRadius_ValueChanged(object sender, EventArgs e)
		{
			if (!_loading)
			{
				PullControlsToJob();
				canvas.BrushRadius = (float)_job.Parameters.BrushRadius;
				SetStatus("掩膜默认半径已更新。");
			}
		}

		private void modelParameter_ValueChanged(object sender, EventArgs e)
		{
			if (!_loading)
			{
				PullControlsToJob();
				InvalidateTeachModel("建模参数已修改，请重新创建模型。", notifyChanged: true);
			}
		}

		private void runtimeParameter_ValueChanged(object sender, EventArgs e)
		{
			if (!_loading)
			{
				PullControlsToJob();
				SetStatus("定位参数已更新。");
				this.JobChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		private void chkSearchRoiEnabled_CheckedChanged(object sender, EventArgs e)
		{
			if (!_loading)
			{
				_job.SearchRoiEnabled = chkSearchRoiEnabled.Checked;
				canvas.SearchRoiEnabled = _job.SearchRoiEnabled;
				UpdateSearchRoiState();
				SetStatus(_job.SearchRoiEnabled ? "已启用搜索区域限制。" : "已关闭搜索区域限制，将在全图搜索。");
				this.JobChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		private void circleParameter_ValueChanged(object sender, EventArgs e)
		{
			if (!_loading)
			{
				PullControlsToJob();
				UpdateCircleParameterState();
				SetStatus("圆心精修参数已更新。");
				this.JobChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		private void ExtractFeaturePoints(bool invalidateModel, bool showMessage)
		{
			if (_sourceImage == null || _busy)
			{
				return;
			}
			try
			{
				PullControlsToJob();
				if (invalidateModel)
				{
					InvalidateTeachModel("正在重新提取特征点...", notifyChanged: false);
				}
				_rawFeaturePoints = _locator.ExtractFeaturePoints(_sourceImage, _job);
				canvas.FeaturePoints = _rawFeaturePoints;
				UpdateVisibleFeaturePoints();
				int pointCount = CountRealPoints(_job.TeachData.FeaturePoints);
				string status = ((pointCount > 0) ? $"已自动提取 {pointCount} 个外轮廓特征点，可绘制区域掩膜排除不稳定区域。" : "没有提取到特征点，请调整模板 ROI 或边缘参数。");
				SetStatus(status);
				if (showMessage && pointCount == 0)
				{
					MessageBox.Show(this, status, "特征提取", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				}
				this.JobChanged?.Invoke(this, EventArgs.Empty);
			}
			catch (Exception ex)
			{
				SetStatus("特征提取失败：" + ex.Message);
				if (showMessage)
				{
					MessageBox.Show(this, ex.Message, "特征提取失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				}
			}
		}

		private void LoadJobToControls()
		{
			MarkMatchParameters parameters = _job.Parameters ?? new MarkMatchParameters();
			SetNumericValue(numTemplateMean, parameters.TemplateMeanSize);
			SetNumericValue(numEdgeSigma, parameters.EdgeSigma);
			SetNumericValue(numEdgeLow, parameters.EdgeLowThreshold);
			SetNumericValue(numEdgeHigh, parameters.EdgeHighThreshold);
			SetNumericValue(numFeatureDistance, parameters.FeatureMinDistancePx);
			SetNumericValue(numMaxFeaturePoints, parameters.MaxFeaturePoints);
			SetNumericValue(numBrushRadius, parameters.BrushRadius);
			SetNumericValue(numLevels, parameters.NumLevels);
			SetNumericValue(numMinContrast, parameters.MinContrast);
			chkUsePolarity.Checked = parameters.UsePolarity;
			chkSearchRoiEnabled.Checked = _job.SearchRoiEnabled;
			SetNumericValue(numSearchMean, parameters.SearchMeanSize);
			SetNumericValue(numMinScore, parameters.MinScore);
			SetNumericValue(numAngleStart, parameters.AngleStartDegrees);
			SetNumericValue(numAngleExtent, parameters.AngleExtentDegrees);
			SetNumericValue(numScaleMin, parameters.ScaleMin);
			SetNumericValue(numScaleMax, parameters.ScaleMax);
			SetNumericValue(numMaxOverlap, parameters.MaxOverlap);
			SetNumericValue(numGreediness, parameters.Greediness);
			chkCircleRefinement.Checked = parameters.EnableCircleRefinement;
			chkRequireCircle.Checked = parameters.RequireCircleRefinement;
			SetNumericValue(numCircleRadius, parameters.ExpectedCircleRadius);
			UpdateCircleParameterState();
		}

		private void PullControlsToJob()
		{
			if (_job == null)
			{
				_job = new MarkLocateJob();
			}
			_job.Parameters = _job.Parameters ?? new MarkMatchParameters();
			_job.TemplateRoi = canvas.TemplateRoi;
			_job.TemplateRoiShape = canvas.TemplateRoiShape;
			_job.SearchRoi = canvas.SearchRoi;
			_job.SearchRoiEnabled = chkSearchRoiEnabled.Checked;
			_job.MaskStrokes = new List<MaskStroke>(canvas.MaskStrokes);
			MarkMatchParameters parameters = _job.Parameters;
			parameters.TemplateMeanSize = decimal.ToInt32(numTemplateMean.Value);
			parameters.EdgeSigma = (double)numEdgeSigma.Value;
			parameters.EdgeLowThreshold = (double)numEdgeLow.Value;
			parameters.EdgeHighThreshold = (double)numEdgeHigh.Value;
			parameters.FeatureMinDistancePx = (double)numFeatureDistance.Value;
			parameters.MaxFeaturePoints = decimal.ToInt32(numMaxFeaturePoints.Value);
			parameters.BrushRadius = (double)numBrushRadius.Value;
			parameters.NumLevels = decimal.ToInt32(numLevels.Value);
			parameters.MinContrast = decimal.ToInt32(numMinContrast.Value);
			parameters.UsePolarity = chkUsePolarity.Checked;
			parameters.SearchMeanSize = decimal.ToInt32(numSearchMean.Value);
			parameters.MinScore = (double)numMinScore.Value;
			parameters.AngleStartDegrees = (double)numAngleStart.Value;
			parameters.AngleExtentDegrees = (double)numAngleExtent.Value;
			parameters.ScaleMin = (double)numScaleMin.Value;
			parameters.ScaleMax = (double)numScaleMax.Value;
			parameters.MaxOverlap = (double)numMaxOverlap.Value;
			parameters.Greediness = (double)numGreediness.Value;
			// Circle detection is controlled by MarkLocateJob.EnableCircleDetection
			// from the host application, not by this setup page.
			parameters.Normalize();
			canvas.SearchRoiEnabled = _job.SearchRoiEnabled;
			canvas.BrushRadius = (float)parameters.BrushRadius;
			if (_sourceImage != null)
			{
				_job.Normalize(_sourceImage.Size);
			}
		}

		private void UpdateVisibleFeaturePoints()
		{
			_job.TeachData = _job.TeachData ?? new MarkTeachData();
			// Reuse the canvas's incremental filtering while editing the teaching image.
			_job.TeachData.FeaturePoints = !_showingTestImage
				? new List<PointF>(canvas.VisibleFeaturePoints)
				: HalconMarkLocator.FilterFeaturePointsInRoi(_rawFeaturePoints, _job.MaskStrokes, _job.TemplateRoi, _job.TemplateRoiShape);
			UpdateFeatureCount();
		}

		private void UpdateFeatureCount()
		{
			int count = CountRealPoints(_job?.TeachData?.FeaturePoints);
			lblFeatureCount.Text = $"特征点：{count}";
		}

		private void InvalidateTeachModel(string status, bool notifyChanged)
		{
			_job.TeachData = _job.TeachData ?? new MarkTeachData();
			_job.TeachData.HasModel = false;
			_job.TeachData.ModelBytes = null;
			_job.TeachData.ModelContours.Clear();
			_job.TeachData.TemplateSize = Size.Empty;
			_job.TeachData.CreatedAt = default(DateTime);
			canvas.ModelContours = new List<List<PointF>>();
			canvas.MatchedContours = new List<List<PointF>>();
			UpdateModelState();
			SetStatus(status);
			if (notifyChanged)
			{
				this.JobChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		private void SetInteractionMode(CanvasInteractionMode mode)
		{
			if (mode == CanvasInteractionMode.EditSearchRoi && !chkSearchRoiEnabled.Checked)
			{
				mode = CanvasInteractionMode.Pan;
			}
			canvas.InteractionMode = mode;
			btnTemplateRoi.Checked = mode == CanvasInteractionMode.EditTemplateRoi;
			btnSearchRoi.Checked = mode == CanvasInteractionMode.EditSearchRoi;
			btnMaskExclude.Checked = mode == CanvasInteractionMode.ExcludeMask;
			btnMaskRestore.Checked = mode == CanvasInteractionMode.RestoreMask;
			btnBrushMask.BackColor = mode == CanvasInteractionMode.BrushExclude
				? Color.FromArgb(208, 232, 226) : SystemColors.Control;
		}

		private void UpdateSearchRoiState()
		{
			bool enabled = chkSearchRoiEnabled.Checked;
			btnSearchRoi.Enabled = enabled && !_busy;
			canvas.SearchRoiEnabled = enabled;
			if (!enabled && canvas.InteractionMode == CanvasInteractionMode.EditSearchRoi)
			{
				SetInteractionMode(CanvasInteractionMode.Pan);
			}
		}

		private void UpdateCircleParameterState()
		{
			bool enabled = chkCircleRefinement.Checked;
			chkRequireCircle.Enabled = enabled;
			numCircleRadius.Enabled = enabled;
		}

		private void UpdateModelState()
		{
			bool ready = _job?.TeachData != null && (_job.TeachData.HasModel || (!_job.EnableTemplateMatching && _job.EnableCircleDetection));
			lblModelState.Text = (_job != null && !_job.EnableTemplateMatching && _job.EnableCircleDetection) ? "找圆（上位机启用）" : (ready ? $"已创建  {_job.TeachData.CreatedAt:yyyy-MM-dd HH:mm:ss}" : "未创建");
			lblModelState.ForeColor = (ready ? Color.FromArgb(0, 122, 105) : Color.FromArgb(190, 72, 58));
			btnTest.Enabled = ready && !_busy;
		}

		private void UpdateImageState()
		{
			if (lblImageState == null) return;
			lblImageState.Text = _showingTestImage ? "当前图：测试图" : "当前图：示教图";
			btnShowTeachImage.Checked = !_showingTestImage;
			btnShowTestImage.Checked = _showingTestImage;
			btnShowTeachImage.Enabled = _sourceImage != null && !_busy;
			btnShowTestImage.Enabled = _testImage != null && !_busy;
		}

		private void SetBusy(bool busy, string status)
		{
			_busy = busy;
			canvas.Enabled = !busy;
			toolMain.Enabled = !busy;
			panelParameters.Enabled = !busy;
			btnTeach.Enabled = !busy;
			btnTest.Enabled = !busy && (_job.TeachData.HasModel || (!_job.EnableTemplateMatching && _job.EnableCircleDetection));
			base.UseWaitCursor = busy;
			UpdateSearchRoiState();
			btnLoadTeachImage.Enabled = !busy;
			btnLoadTestImage.Enabled = !busy;
			UpdateImageState();
			if (!string.IsNullOrWhiteSpace(status))
			{
				SetStatus(status);
			}
		}

		private void SetStatus(string text)
		{
			lblStatus.Text = text ?? string.Empty;
			this.StatusChanged?.Invoke(this, EventArgs.Empty);
		}

		private static int CountRealPoints(IEnumerable<PointF> points)
		{
			return points?.Count((PointF point) => !float.IsNaN(point.X) && !float.IsNaN(point.Y)) ?? 0;
		}

		private static void SetNumericValue(NumericUpDown control, double value)
		{
			decimal decimalValue = (decimal)value;
			control.Value = Math.Max(control.Minimum, Math.Min(control.Maximum, decimalValue));
		}

		private static string GetShapeText(RegionShape shape)
		{
			switch (shape)
			{
			case RegionShape.Rectangle:
				return "矩形";
			case RegionShape.Diamond:
				return "菱形";
			default:
				return "圆形";
			}
		}

		private static ToolStripMenuItem CreateShapeMenuItem(string text, RegionShape shape, EventHandler handler)
		{
			ToolStripMenuItem item = new ToolStripMenuItem(text);
			item.Tag = shape;
			item.Click += handler;
			return item;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_sourceImage?.Dispose();
				_testImage?.Dispose();
				components?.Dispose();
			}
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.toolMain = new System.Windows.Forms.ToolStrip();
			this.btnTemplateShape = new System.Windows.Forms.ToolStripDropDownButton();
			this.btnMaskShape = new System.Windows.Forms.ToolStripDropDownButton();
			this.btnTemplateRoi = new System.Windows.Forms.ToolStripButton();
			this.btnSearchRoi = new System.Windows.Forms.ToolStripButton();
			this.toolSeparator1 = new System.Windows.Forms.ToolStripSeparator();
			this.btnMaskExclude = new System.Windows.Forms.ToolStripButton();
			this.btnMaskRestore = new System.Windows.Forms.ToolStripButton();
			this.btnUndoMask = new System.Windows.Forms.ToolStripButton();
			this.btnClearMask = new System.Windows.Forms.ToolStripButton();
			this.toolSeparator2 = new System.Windows.Forms.ToolStripSeparator();
			this.btnFit = new System.Windows.Forms.ToolStripButton();
			this.btnActual = new System.Windows.Forms.ToolStripButton();
			this.lblZoom = new System.Windows.Forms.ToolStripLabel();
			this.btnLoadTeachImage = new System.Windows.Forms.ToolStripButton();
			this.btnLoadTestImage = new System.Windows.Forms.ToolStripButton();
			this.btnShowTeachImage = new System.Windows.Forms.ToolStripButton();
			this.btnShowTestImage = new System.Windows.Forms.ToolStripButton();
			this.lblImageState = new System.Windows.Forms.ToolStripLabel();
			this.splitMain = new System.Windows.Forms.SplitContainer();
			this.canvas = new Seprays.MarkVision.MaskImageCanvas();
			this.panelSettings = new System.Windows.Forms.Panel();
			this.panelParameters = new System.Windows.Forms.Panel();
			this.grpCircle = new System.Windows.Forms.GroupBox();
			this.chkCircleRefinement = new System.Windows.Forms.CheckBox();
			this.chkRequireCircle = new System.Windows.Forms.CheckBox();
			this.lblCircleRadius = new System.Windows.Forms.Label();
			this.numCircleRadius = new System.Windows.Forms.NumericUpDown();
			this.grpSearch = new System.Windows.Forms.GroupBox();
			this.chkSearchRoiEnabled = new System.Windows.Forms.CheckBox();
			this.lblSearchMean = new System.Windows.Forms.Label();
			this.lblMinScore = new System.Windows.Forms.Label();
			this.lblAngleStart = new System.Windows.Forms.Label();
			this.lblAngleExtent = new System.Windows.Forms.Label();
			this.lblScaleMin = new System.Windows.Forms.Label();
			this.lblScaleMax = new System.Windows.Forms.Label();
			this.lblMaxOverlap = new System.Windows.Forms.Label();
			this.lblGreediness = new System.Windows.Forms.Label();
			this.numSearchMean = new System.Windows.Forms.NumericUpDown();
			this.numMinScore = new System.Windows.Forms.NumericUpDown();
			this.numAngleStart = new System.Windows.Forms.NumericUpDown();
			this.numAngleExtent = new System.Windows.Forms.NumericUpDown();
			this.numScaleMin = new System.Windows.Forms.NumericUpDown();
			this.numScaleMax = new System.Windows.Forms.NumericUpDown();
			this.numMaxOverlap = new System.Windows.Forms.NumericUpDown();
			this.numGreediness = new System.Windows.Forms.NumericUpDown();
			this.grpModel = new System.Windows.Forms.GroupBox();
			this.lblLevels = new System.Windows.Forms.Label();
			this.lblMinContrast = new System.Windows.Forms.Label();
			this.numLevels = new System.Windows.Forms.NumericUpDown();
			this.numMinContrast = new System.Windows.Forms.NumericUpDown();
			this.chkUsePolarity = new System.Windows.Forms.CheckBox();
			this.grpFeature = new System.Windows.Forms.GroupBox();
			this.lblTemplateMean = new System.Windows.Forms.Label();
			this.lblEdgeSigma = new System.Windows.Forms.Label();
			this.lblEdgeLow = new System.Windows.Forms.Label();
			this.lblEdgeHigh = new System.Windows.Forms.Label();
			this.lblFeatureDistance = new System.Windows.Forms.Label();
			this.lblMaxFeaturePoints = new System.Windows.Forms.Label();
			this.lblBrushRadius = new System.Windows.Forms.Label();
			this.numTemplateMean = new System.Windows.Forms.NumericUpDown();
			this.numEdgeSigma = new System.Windows.Forms.NumericUpDown();
			this.numEdgeLow = new System.Windows.Forms.NumericUpDown();
			this.numEdgeHigh = new System.Windows.Forms.NumericUpDown();
			this.numFeatureDistance = new System.Windows.Forms.NumericUpDown();
			this.numMaxFeaturePoints = new System.Windows.Forms.NumericUpDown();
			this.numBrushRadius = new System.Windows.Forms.NumericUpDown();
			this.lblFeatureCount = new System.Windows.Forms.Label();
			this.btnExtract = new System.Windows.Forms.Button();
			this.btnResetFeatures = new System.Windows.Forms.Button();
			this.btnBrushMask = new System.Windows.Forms.Button();
			this.panelActions = new System.Windows.Forms.Panel();
			this.btnTeach = new System.Windows.Forms.Button();
			this.btnTest = new System.Windows.Forms.Button();
			this.lblModelStateTitle = new System.Windows.Forms.Label();
			this.lblModelState = new System.Windows.Forms.Label();
			this.lblSubtitle = new System.Windows.Forms.Label();
			this.lblTitle = new System.Windows.Forms.Label();
			this.lblStatus = new System.Windows.Forms.Label();
			this.toolTip = new System.Windows.Forms.ToolTip(this.components);
			this.toolMain.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)this.splitMain).BeginInit();
			this.splitMain.Panel1.SuspendLayout();
			this.splitMain.Panel2.SuspendLayout();
			this.splitMain.SuspendLayout();
			this.panelSettings.SuspendLayout();
			this.panelParameters.SuspendLayout();
			this.grpCircle.SuspendLayout();
			this.grpSearch.SuspendLayout();
			this.grpModel.SuspendLayout();
			this.grpFeature.SuspendLayout();
			this.panelActions.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)this.numTemplateMean).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numEdgeSigma).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numEdgeLow).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numEdgeHigh).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numFeatureDistance).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numMaxFeaturePoints).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numBrushRadius).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numLevels).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numMinContrast).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numSearchMean).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numMinScore).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numAngleStart).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numAngleExtent).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numScaleMin).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numScaleMax).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numMaxOverlap).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numGreediness).BeginInit();
			((System.ComponentModel.ISupportInitialize)this.numCircleRadius).BeginInit();
			base.SuspendLayout();
			this.toolMain.BackColor = System.Drawing.Color.FromArgb(245, 247, 249);
			this.toolMain.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this.toolMain.ImageScalingSize = new System.Drawing.Size(20, 20);
			this.toolMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[14]
			{
				this.btnTemplateRoi, this.btnTemplateShape, this.btnSearchRoi, this.toolSeparator1, this.btnUndoMask, this.toolSeparator2, this.btnFit,
				this.btnActual, this.btnLoadTeachImage, this.btnLoadTestImage, this.btnShowTeachImage, this.btnShowTestImage, this.lblImageState, this.lblZoom
			});
			this.toolMain.Location = new System.Drawing.Point(0, 0);
			this.toolMain.Name = "toolMain";
			this.toolMain.Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);
			this.toolMain.Size = new System.Drawing.Size(1180, 35);
			this.toolMain.TabIndex = 0;
			this.btnTemplateShape.Name = "btnTemplateShape";
			this.btnTemplateShape.Text = "模板形状: 圆形";
			this.btnTemplateShape.ToolTipText = "选择模板 ROI 形状（默认圆形）";
			this.btnTemplateShape.DropDownItems.Add(CreateShapeMenuItem("圆形", RegionShape.Circle, templateShape_Click));
			this.btnTemplateShape.DropDownItems.Add(CreateShapeMenuItem("矩形", RegionShape.Rectangle, templateShape_Click));
			this.btnTemplateShape.DropDownItems.Add(CreateShapeMenuItem("菱形", RegionShape.Diamond, templateShape_Click));
			this.btnMaskShape.Name = "btnMaskShape";
			this.btnMaskShape.Text = "掩膜形状: 圆形";
			this.btnMaskShape.ToolTipText = "选择区域掩膜形状（默认圆形）";
			this.btnMaskShape.DropDownItems.Add(CreateShapeMenuItem("圆形", RegionShape.Circle, maskShape_Click));
			this.btnMaskShape.DropDownItems.Add(CreateShapeMenuItem("矩形", RegionShape.Rectangle, maskShape_Click));
			this.btnMaskShape.DropDownItems.Add(CreateShapeMenuItem("菱形", RegionShape.Diamond, maskShape_Click));
			this.btnTemplateRoi.CheckOnClick = true;
			this.btnTemplateRoi.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.btnTemplateRoi.Name = "btnTemplateRoi";
			this.btnTemplateRoi.Text = "模板框";
			this.btnTemplateRoi.ToolTipText = "拖拽模板 ROI，松开后自动提取特征点";
			this.btnTemplateRoi.Click += new System.EventHandler(btnTemplateRoi_Click);
			this.btnSearchRoi.CheckOnClick = true;
			this.btnSearchRoi.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.btnSearchRoi.Name = "btnSearchRoi";
			this.btnSearchRoi.Text = "搜索框";
			this.btnSearchRoi.ToolTipText = "启用搜索区域后拖拽运行 ROI";
			this.btnSearchRoi.Click += new System.EventHandler(btnSearchRoi_Click);
			this.btnMaskExclude.CheckOnClick = true;
			this.btnMaskExclude.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.btnMaskExclude.Name = "btnMaskExclude";
			this.btnMaskExclude.Text = "区域排除";
			this.btnMaskExclude.ToolTipText = "拖拽绘制区域，排除不参与建模的特征点";
			this.btnMaskExclude.Click += new System.EventHandler(btnMaskExclude_Click);
			this.btnMaskRestore.CheckOnClick = true;
			this.btnMaskRestore.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.btnMaskRestore.Name = "btnMaskRestore";
			this.btnMaskRestore.Text = "区域恢复";
			this.btnMaskRestore.ToolTipText = "拖拽绘制区域，恢复原始特征点";
			this.btnMaskRestore.Click += new System.EventHandler(btnMaskRestore_Click);
			this.btnUndoMask.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.btnUndoMask.Name = "btnUndoMask";
			this.btnUndoMask.Text = "撤销";
			this.btnUndoMask.ToolTipText = "撤销最后一笔特征点掩膜";
			this.btnUndoMask.CheckOnClick = false;
			this.btnUndoMask.Click += new System.EventHandler(btnUndoMask_Click);
			this.btnClearMask.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.btnClearMask.Name = "btnClearMask";
			this.btnClearMask.Text = "清空掩膜";
			this.btnClearMask.ToolTipText = "恢复全部特征点";
			this.btnClearMask.CheckOnClick = false;
			this.btnClearMask.Click += new System.EventHandler(btnClearMask_Click);
			this.btnFit.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.btnFit.Name = "btnFit";
			this.btnFit.Text = "适应窗口";
			this.btnFit.ToolTipText = "显示完整图像";
			this.btnFit.CheckOnClick = false;
			this.btnFit.Click += new System.EventHandler(btnFit_Click);
			this.btnActual.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.btnActual.Name = "btnActual";
			this.btnActual.Text = "1:1";
			this.btnActual.ToolTipText = "按原始像素显示";
			this.btnActual.CheckOnClick = false;
			this.btnActual.Click += new System.EventHandler(btnActual_Click);
			this.lblZoom.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
			this.lblZoom.Text = "100%";
			this.btnLoadTeachImage.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.btnLoadTeachImage.Name = "btnLoadTeachImage";
			this.btnLoadTeachImage.Text = "载入示教图";
			this.btnLoadTeachImage.ToolTipText = "载入用于提取特征和创建模型的示教图";
			this.btnLoadTeachImage.Click += new System.EventHandler(btnLoadTeachImage_Click);
			this.btnLoadTestImage.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.btnLoadTestImage.Name = "btnLoadTestImage";
			this.btnLoadTestImage.Text = "载入测试图";
			this.btnLoadTestImage.ToolTipText = "载入用于复用模型测试定位的测试图";
			this.btnLoadTestImage.Click += new System.EventHandler(btnLoadTestImage_Click);
			this.btnShowTeachImage.CheckOnClick = true;
			this.btnShowTeachImage.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.btnShowTeachImage.Name = "btnShowTeachImage";
			this.btnShowTeachImage.Text = "看示教图";
			this.btnShowTeachImage.Click += new System.EventHandler(btnShowTeachImage_Click);
			this.btnShowTestImage.CheckOnClick = true;
			this.btnShowTestImage.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.btnShowTestImage.Name = "btnShowTestImage";
			this.btnShowTestImage.Text = "看测试图";
			this.btnShowTestImage.Click += new System.EventHandler(btnShowTestImage_Click);
			this.lblImageState.Text = "当前图：示教图";
			this.lblImageState.ToolTipText = "当前画布显示的图像用途";
			this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitMain.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
			this.splitMain.Location = new System.Drawing.Point(0, 35);
			this.splitMain.Name = "splitMain";
			this.splitMain.Panel1MinSize = 420;
			this.splitMain.Panel1.Controls.Add(this.canvas);
			this.splitMain.Panel2.Controls.Add(this.panelSettings);
			this.splitMain.Panel2MinSize = 360;
			this.splitMain.Size = new System.Drawing.Size(1180, 680);
			this.splitMain.SplitterDistance = 810;
			this.splitMain.SplitterWidth = 6;
			this.splitMain.TabIndex = 1;
			this.canvas.BackColor = System.Drawing.Color.FromArgb(24, 27, 31);
			this.canvas.Dock = System.Windows.Forms.DockStyle.Fill;
			this.canvas.Name = "canvas";
			this.canvas.TabIndex = 0;
			this.canvas.TemplateRoiChanged += new System.EventHandler(canvas_TemplateRoiChanged);
			this.canvas.TemplateRoiEditCompleted += new System.EventHandler(canvas_TemplateRoiEditCompleted);
			this.canvas.SearchRoiChanged += new System.EventHandler(canvas_SearchRoiChanged);
			this.canvas.MaskChanged += new System.EventHandler(canvas_MaskChanged);
			this.canvas.MaskEditStarted += new System.EventHandler(canvas_MaskEditStarted);
			this.canvas.ZoomChanged += new System.EventHandler(canvas_ZoomChanged);
			this.panelSettings.BackColor = System.Drawing.Color.FromArgb(247, 248, 250);
			this.panelSettings.Controls.Add(this.panelParameters);
			this.panelSettings.Controls.Add(this.panelActions);
			this.panelSettings.Controls.Add(this.lblModelStateTitle);
			this.panelSettings.Controls.Add(this.lblModelState);
			this.panelSettings.Controls.Add(this.lblSubtitle);
			this.panelSettings.Controls.Add(this.lblTitle);
			this.panelSettings.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panelSettings.Name = "panelSettings";
			this.panelSettings.Padding = new System.Windows.Forms.Padding(14, 12, 14, 10);
			this.lblTitle.AutoSize = true;
			this.lblTitle.Font = new System.Drawing.Font("Microsoft YaHei UI", 14f, System.Drawing.FontStyle.Bold);
			this.lblTitle.Location = new System.Drawing.Point(16, 14);
			this.lblTitle.Text = "Mark 外轮廓模板";
			this.lblSubtitle.ForeColor = System.Drawing.Color.FromArgb(95, 102, 112);
			this.lblSubtitle.Location = new System.Drawing.Point(18, 48);
			this.lblSubtitle.Size = new System.Drawing.Size(326, 42);
			this.lblSubtitle.Text = "选择形状绘制模板 ROI 和区域掩膜，确认特征点后创建模型。";
			this.lblModelStateTitle.AutoSize = true;
			this.lblModelStateTitle.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f, System.Drawing.FontStyle.Bold);
			this.lblModelStateTitle.Location = new System.Drawing.Point(18, 94);
			this.lblModelStateTitle.Text = "模型状态";
			this.lblModelState.AutoSize = true;
			this.lblModelState.Location = new System.Drawing.Point(88, 94);
			this.lblModelState.Text = "未创建";
			this.panelParameters.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			this.panelParameters.AutoScroll = true;
			this.panelParameters.Controls.Add(this.grpCircle);
			this.panelParameters.Controls.Add(this.grpSearch);
			this.panelParameters.Controls.Add(this.grpModel);
			this.panelParameters.Controls.Add(this.grpFeature);
			this.panelParameters.Location = new System.Drawing.Point(14, 122);
			this.panelParameters.Name = "panelParameters";
			this.panelParameters.Size = new System.Drawing.Size(336, 494);
			this.grpFeature.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			this.grpFeature.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f, System.Drawing.FontStyle.Bold);
			this.grpFeature.Location = new System.Drawing.Point(0, 0);
			this.grpFeature.Name = "grpFeature";
			this.grpFeature.Size = new System.Drawing.Size(316, 348);
			this.grpFeature.Text = "外轮廓特征";
			this.grpFeature.Controls.Add(this.lblTemplateMean);
			this.grpFeature.Controls.Add(this.numTemplateMean);
			this.grpFeature.Controls.Add(this.lblEdgeSigma);
			this.grpFeature.Controls.Add(this.numEdgeSigma);
			this.grpFeature.Controls.Add(this.lblEdgeLow);
			this.grpFeature.Controls.Add(this.numEdgeLow);
			this.grpFeature.Controls.Add(this.lblEdgeHigh);
			this.grpFeature.Controls.Add(this.numEdgeHigh);
			this.grpFeature.Controls.Add(this.lblFeatureDistance);
			this.grpFeature.Controls.Add(this.numFeatureDistance);
			this.grpFeature.Controls.Add(this.lblMaxFeaturePoints);
			this.grpFeature.Controls.Add(this.numMaxFeaturePoints);
			this.grpFeature.Controls.Add(this.lblBrushRadius);
			this.grpFeature.Controls.Add(this.numBrushRadius);
			this.lblTemplateMean.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblTemplateMean.Location = new System.Drawing.Point(14, 28);
			this.lblTemplateMean.Name = "lblTemplateMean";
			this.lblTemplateMean.Size = new System.Drawing.Size(120, 26);
			this.lblTemplateMean.Text = "模板均值滤波";
			this.lblTemplateMean.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numTemplateMean.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numTemplateMean.Location = new System.Drawing.Point(146, 28);
			this.numTemplateMean.Maximum = 99m;
			this.numTemplateMean.Minimum = 1m;
			this.numTemplateMean.Name = "numTemplateMean";
			this.numTemplateMean.Size = new System.Drawing.Size(154, 26);
			this.numTemplateMean.Value = 1m;
			this.lblEdgeSigma.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblEdgeSigma.Location = new System.Drawing.Point(14, 62);
			this.lblEdgeSigma.Name = "lblEdgeSigma";
			this.lblEdgeSigma.Size = new System.Drawing.Size(120, 26);
			this.lblEdgeSigma.Text = "边缘 Sigma";
			this.lblEdgeSigma.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numEdgeSigma.DecimalPlaces = 2;
			this.numEdgeSigma.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numEdgeSigma.Increment = 0.05m;
			this.numEdgeSigma.Location = new System.Drawing.Point(146, 62);
			this.numEdgeSigma.Maximum = 10m;
			this.numEdgeSigma.Minimum = 0.2m;
			this.numEdgeSigma.Name = "numEdgeSigma";
			this.numEdgeSigma.Size = new System.Drawing.Size(154, 26);
			this.numEdgeSigma.Value = 1.2m;
			this.lblEdgeLow.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblEdgeLow.Location = new System.Drawing.Point(14, 96);
			this.lblEdgeLow.Name = "lblEdgeLow";
			this.lblEdgeLow.Size = new System.Drawing.Size(120, 26);
			this.lblEdgeLow.Text = "低阈值";
			this.lblEdgeLow.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numEdgeLow.DecimalPlaces = 1;
			this.numEdgeLow.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numEdgeLow.Increment = 0.5m;
			this.numEdgeLow.Location = new System.Drawing.Point(146, 96);
			this.numEdgeLow.Maximum = 254m;
			this.numEdgeLow.Minimum = 1m;
			this.numEdgeLow.Name = "numEdgeLow";
			this.numEdgeLow.Size = new System.Drawing.Size(154, 26);
			this.numEdgeLow.Value = 15m;
			this.lblEdgeHigh.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblEdgeHigh.Location = new System.Drawing.Point(14, 130);
			this.lblEdgeHigh.Name = "lblEdgeHigh";
			this.lblEdgeHigh.Size = new System.Drawing.Size(120, 26);
			this.lblEdgeHigh.Text = "高阈值";
			this.lblEdgeHigh.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numEdgeHigh.DecimalPlaces = 1;
			this.numEdgeHigh.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numEdgeHigh.Increment = 0.5m;
			this.numEdgeHigh.Location = new System.Drawing.Point(146, 130);
			this.numEdgeHigh.Maximum = 255m;
			this.numEdgeHigh.Minimum = 2m;
			this.numEdgeHigh.Name = "numEdgeHigh";
			this.numEdgeHigh.Size = new System.Drawing.Size(154, 26);
			this.numEdgeHigh.Value = 35m;
			this.lblFeatureDistance.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblFeatureDistance.Location = new System.Drawing.Point(14, 164);
			this.lblFeatureDistance.Name = "lblFeatureDistance";
			this.lblFeatureDistance.Size = new System.Drawing.Size(120, 26);
			this.lblFeatureDistance.Text = "点最小间距";
			this.lblFeatureDistance.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numFeatureDistance.DecimalPlaces = 1;
			this.numFeatureDistance.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numFeatureDistance.Increment = 0.5m;
			this.numFeatureDistance.Location = new System.Drawing.Point(146, 164);
			this.numFeatureDistance.Maximum = 80m;
			this.numFeatureDistance.Minimum = 0.5m;
			this.numFeatureDistance.Name = "numFeatureDistance";
			this.numFeatureDistance.Size = new System.Drawing.Size(154, 26);
			this.numFeatureDistance.Value = 6m;
			this.lblMaxFeaturePoints.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblMaxFeaturePoints.Location = new System.Drawing.Point(14, 198);
			this.lblMaxFeaturePoints.Name = "lblMaxFeaturePoints";
			this.lblMaxFeaturePoints.Size = new System.Drawing.Size(120, 26);
			this.lblMaxFeaturePoints.Text = "最大特征点数";
			this.lblMaxFeaturePoints.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numMaxFeaturePoints.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numMaxFeaturePoints.Location = new System.Drawing.Point(146, 198);
			this.numMaxFeaturePoints.Maximum = 50000m;
			this.numMaxFeaturePoints.Minimum = 100m;
			this.numMaxFeaturePoints.Name = "numMaxFeaturePoints";
			this.numMaxFeaturePoints.Size = new System.Drawing.Size(154, 26);
			this.numMaxFeaturePoints.Value = 12000m;
			this.lblBrushRadius.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblBrushRadius.Location = new System.Drawing.Point(14, 232);
			this.lblBrushRadius.Name = "lblBrushRadius";
			this.lblBrushRadius.Size = new System.Drawing.Size(120, 26);
			this.lblBrushRadius.Text = "掩膜默认半径";
			this.lblBrushRadius.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numBrushRadius.DecimalPlaces = 1;
			this.numBrushRadius.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numBrushRadius.Increment = 0.5m;
			this.numBrushRadius.Location = new System.Drawing.Point(146, 232);
			this.numBrushRadius.Maximum = 500m;
			this.numBrushRadius.Minimum = 1m;
			this.numBrushRadius.Name = "numBrushRadius";
			this.numBrushRadius.Size = new System.Drawing.Size(154, 26);
			this.numBrushRadius.Value = 18m;
			this.numTemplateMean.ValueChanged += new System.EventHandler(featureParameter_ValueChanged);
			this.numEdgeSigma.ValueChanged += new System.EventHandler(featureParameter_ValueChanged);
			this.numEdgeLow.ValueChanged += new System.EventHandler(featureParameter_ValueChanged);
			this.numEdgeHigh.ValueChanged += new System.EventHandler(featureParameter_ValueChanged);
			this.numFeatureDistance.ValueChanged += new System.EventHandler(featureParameter_ValueChanged);
			this.numMaxFeaturePoints.ValueChanged += new System.EventHandler(featureParameter_ValueChanged);
			this.numBrushRadius.ValueChanged += new System.EventHandler(brushRadius_ValueChanged);
			this.lblFeatureCount.Font = new System.Drawing.Font("Microsoft YaHei UI", 10f, System.Drawing.FontStyle.Bold);
			this.lblFeatureCount.ForeColor = System.Drawing.Color.FromArgb(36, 96, 145);
			this.lblFeatureCount.Location = new System.Drawing.Point(14, 270);
			this.lblFeatureCount.Size = new System.Drawing.Size(286, 26);
			this.lblFeatureCount.Text = "特征点：0";
			this.grpFeature.Controls.Add(this.lblFeatureCount);
			this.btnExtract.Location = new System.Drawing.Point(14, 302);
			this.btnExtract.Name = "btnExtract";
			this.btnExtract.Size = new System.Drawing.Size(137, 32);
			this.btnExtract.Text = "提取外轮廓点";
			this.btnExtract.UseVisualStyleBackColor = true;
			this.btnExtract.Click += new System.EventHandler(btnExtract_Click);
			this.btnResetFeatures.Location = new System.Drawing.Point(159, 302);
			this.btnResetFeatures.Name = "btnResetFeatures";
			this.btnResetFeatures.Size = new System.Drawing.Size(143, 32);
			this.btnResetFeatures.Text = "清空掩膜并重提取";
			this.btnResetFeatures.UseVisualStyleBackColor = true;
			this.btnResetFeatures.Click += new System.EventHandler(btnResetFeatures_Click);
			this.btnBrushMask.Location = new System.Drawing.Point(14, 200);
			this.btnBrushMask.Name = "btnBrushMask";
			this.btnBrushMask.Size = new System.Drawing.Size(288, 32);
			this.btnBrushMask.Text = "画笔掩膜";
			this.btnBrushMask.UseVisualStyleBackColor = true;
			this.btnBrushMask.Click += new System.EventHandler(btnBrushMask_Click);
			this.grpFeature.Controls.Add(this.btnExtract);
			this.grpFeature.Controls.Add(this.btnResetFeatures);
			this.grpFeature.Controls.Add(this.btnBrushMask);
			this.grpModel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			this.grpModel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f, System.Drawing.FontStyle.Bold);
			this.grpModel.Location = new System.Drawing.Point(0, 704);
			this.grpModel.Size = new System.Drawing.Size(316, 132);
			this.grpModel.Text = "模型创建";
			this.grpModel.Controls.Add(this.lblLevels);
			this.grpModel.Controls.Add(this.numLevels);
			this.grpModel.Controls.Add(this.lblMinContrast);
			this.grpModel.Controls.Add(this.numMinContrast);
			this.lblLevels.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblLevels.Location = new System.Drawing.Point(14, 28);
			this.lblLevels.Name = "lblLevels";
			this.lblLevels.Size = new System.Drawing.Size(120, 26);
			this.lblLevels.Text = "金字塔层数";
			this.lblLevels.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numLevels.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numLevels.Location = new System.Drawing.Point(146, 28);
			this.numLevels.Maximum = 12m;
			this.numLevels.Name = "numLevels";
			this.numLevels.Size = new System.Drawing.Size(154, 26);
			this.lblMinContrast.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblMinContrast.Location = new System.Drawing.Point(14, 62);
			this.lblMinContrast.Name = "lblMinContrast";
			this.lblMinContrast.Size = new System.Drawing.Size(120, 26);
			this.lblMinContrast.Text = "最小对比度";
			this.lblMinContrast.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numMinContrast.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numMinContrast.Location = new System.Drawing.Point(146, 62);
			this.numMinContrast.Maximum = 255m;
			this.numMinContrast.Minimum = 1m;
			this.numMinContrast.Name = "numMinContrast";
			this.numMinContrast.Size = new System.Drawing.Size(154, 26);
			this.numMinContrast.Value = 10m;
			this.chkUsePolarity.AutoSize = true;
			this.chkUsePolarity.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.chkUsePolarity.Location = new System.Drawing.Point(16, 99);
			this.chkUsePolarity.Text = "忽略局部极性（推荐）";
			this.chkUsePolarity.Checked = true;
			this.grpModel.Controls.Add(this.chkUsePolarity);
			this.numLevels.ValueChanged += new System.EventHandler(modelParameter_ValueChanged);
			this.numMinContrast.ValueChanged += new System.EventHandler(modelParameter_ValueChanged);
			this.chkUsePolarity.CheckedChanged += new System.EventHandler(modelParameter_ValueChanged);
			this.grpSearch.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			this.grpSearch.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f, System.Drawing.FontStyle.Bold);
			this.grpSearch.Location = new System.Drawing.Point(0, 356);
			this.grpSearch.Size = new System.Drawing.Size(316, 340);
			this.grpSearch.Text = "定位搜索";
			this.chkSearchRoiEnabled.AutoSize = true;
			this.chkSearchRoiEnabled.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.chkSearchRoiEnabled.Location = new System.Drawing.Point(16, 27);
			this.chkSearchRoiEnabled.Text = "启用搜索区域（关闭时全图搜索）";
			this.chkSearchRoiEnabled.CheckedChanged += new System.EventHandler(chkSearchRoiEnabled_CheckedChanged);
			this.grpSearch.Controls.Add(this.chkSearchRoiEnabled);
			this.grpSearch.Controls.Add(this.lblSearchMean);
			this.grpSearch.Controls.Add(this.numSearchMean);
			this.grpSearch.Controls.Add(this.lblMinScore);
			this.grpSearch.Controls.Add(this.numMinScore);
			this.grpSearch.Controls.Add(this.lblAngleStart);
			this.grpSearch.Controls.Add(this.numAngleStart);
			this.grpSearch.Controls.Add(this.lblAngleExtent);
			this.grpSearch.Controls.Add(this.numAngleExtent);
			this.grpSearch.Controls.Add(this.lblScaleMin);
			this.grpSearch.Controls.Add(this.numScaleMin);
			this.grpSearch.Controls.Add(this.lblScaleMax);
			this.grpSearch.Controls.Add(this.numScaleMax);
			this.grpSearch.Controls.Add(this.lblMaxOverlap);
			this.grpSearch.Controls.Add(this.numMaxOverlap);
			this.grpSearch.Controls.Add(this.lblGreediness);
			this.grpSearch.Controls.Add(this.numGreediness);
			this.lblSearchMean.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblSearchMean.Location = new System.Drawing.Point(14, 58);
			this.lblSearchMean.Name = "lblSearchMean";
			this.lblSearchMean.Size = new System.Drawing.Size(120, 26);
			this.lblSearchMean.Text = "运行均值滤波";
			this.lblSearchMean.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numSearchMean.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numSearchMean.Location = new System.Drawing.Point(146, 58);
			this.numSearchMean.Maximum = 99m;
			this.numSearchMean.Minimum = 1m;
			this.numSearchMean.Name = "numSearchMean";
			this.numSearchMean.Size = new System.Drawing.Size(154, 26);
			this.numSearchMean.Value = 1m;
			this.lblMinScore.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblMinScore.Location = new System.Drawing.Point(14, 92);
			this.lblMinScore.Name = "lblMinScore";
			this.lblMinScore.Size = new System.Drawing.Size(120, 26);
			this.lblMinScore.Text = "最小分数";
			this.lblMinScore.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numMinScore.DecimalPlaces = 2;
			this.numMinScore.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numMinScore.Increment = 0.05m;
			this.numMinScore.Location = new System.Drawing.Point(146, 92);
			this.numMinScore.Maximum = 1m;
			this.numMinScore.Minimum = 0.01m;
			this.numMinScore.Name = "numMinScore";
			this.numMinScore.Size = new System.Drawing.Size(154, 26);
			this.numMinScore.Value = 0.75m;
			this.lblAngleStart.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblAngleStart.Location = new System.Drawing.Point(14, 126);
			this.lblAngleStart.Name = "lblAngleStart";
			this.lblAngleStart.Size = new System.Drawing.Size(120, 26);
			this.lblAngleStart.Text = "起始角度(度)";
			this.lblAngleStart.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numAngleStart.DecimalPlaces = 1;
			this.numAngleStart.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numAngleStart.Increment = 0.5m;
			this.numAngleStart.Location = new System.Drawing.Point(146, 126);
			this.numAngleStart.Maximum = 360m;
			this.numAngleStart.Minimum = -360m;
			this.numAngleStart.Name = "numAngleStart";
			this.numAngleStart.Size = new System.Drawing.Size(154, 26);
			this.numAngleStart.Value = -180m;
			this.lblAngleExtent.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblAngleExtent.Location = new System.Drawing.Point(14, 160);
			this.lblAngleExtent.Name = "lblAngleExtent";
			this.lblAngleExtent.Size = new System.Drawing.Size(120, 26);
			this.lblAngleExtent.Text = "角度范围(度)";
			this.lblAngleExtent.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numAngleExtent.DecimalPlaces = 1;
			this.numAngleExtent.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numAngleExtent.Increment = 0.5m;
			this.numAngleExtent.Location = new System.Drawing.Point(146, 160);
			this.numAngleExtent.Maximum = 360m;
			this.numAngleExtent.Minimum = 0.1m;
			this.numAngleExtent.Name = "numAngleExtent";
			this.numAngleExtent.Size = new System.Drawing.Size(154, 26);
			this.numAngleExtent.Value = 360m;
			this.lblScaleMin.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblScaleMin.Location = new System.Drawing.Point(14, 194);
			this.lblScaleMin.Name = "lblScaleMin";
			this.lblScaleMin.Size = new System.Drawing.Size(120, 26);
			this.lblScaleMin.Text = "最小缩放";
			this.lblScaleMin.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numScaleMin.DecimalPlaces = 2;
			this.numScaleMin.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numScaleMin.Increment = 0.05m;
			this.numScaleMin.Location = new System.Drawing.Point(146, 194);
			this.numScaleMin.Maximum = 4m;
			this.numScaleMin.Minimum = 0.1m;
			this.numScaleMin.Name = "numScaleMin";
			this.numScaleMin.Size = new System.Drawing.Size(154, 26);
			this.numScaleMin.Value = 0.8m;
			this.lblScaleMax.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblScaleMax.Location = new System.Drawing.Point(14, 228);
			this.lblScaleMax.Name = "lblScaleMax";
			this.lblScaleMax.Size = new System.Drawing.Size(120, 26);
			this.lblScaleMax.Text = "最大缩放";
			this.lblScaleMax.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numScaleMax.DecimalPlaces = 2;
			this.numScaleMax.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numScaleMax.Increment = 0.05m;
			this.numScaleMax.Location = new System.Drawing.Point(146, 228);
			this.numScaleMax.Maximum = 4m;
			this.numScaleMax.Minimum = 0.1m;
			this.numScaleMax.Name = "numScaleMax";
			this.numScaleMax.Size = new System.Drawing.Size(154, 26);
			this.numScaleMax.Value = 1.2m;
			this.lblMaxOverlap.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblMaxOverlap.Location = new System.Drawing.Point(14, 262);
			this.lblMaxOverlap.Name = "lblMaxOverlap";
			this.lblMaxOverlap.Size = new System.Drawing.Size(120, 26);
			this.lblMaxOverlap.Text = "最大重叠";
			this.lblMaxOverlap.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numMaxOverlap.DecimalPlaces = 2;
			this.numMaxOverlap.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numMaxOverlap.Increment = 0.05m;
			this.numMaxOverlap.Location = new System.Drawing.Point(146, 262);
			this.numMaxOverlap.Maximum = 1m;
			this.numMaxOverlap.Name = "numMaxOverlap";
			this.numMaxOverlap.Size = new System.Drawing.Size(154, 26);
			this.lblGreediness.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblGreediness.Location = new System.Drawing.Point(14, 296);
			this.lblGreediness.Name = "lblGreediness";
			this.lblGreediness.Size = new System.Drawing.Size(120, 26);
			this.lblGreediness.Text = "贪婪度";
			this.lblGreediness.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numGreediness.DecimalPlaces = 2;
			this.numGreediness.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numGreediness.Increment = 0.05m;
			this.numGreediness.Location = new System.Drawing.Point(146, 296);
			this.numGreediness.Maximum = 1m;
			this.numGreediness.Name = "numGreediness";
			this.numGreediness.Size = new System.Drawing.Size(154, 26);
			this.numGreediness.Value = 0.7m;
			this.numSearchMean.ValueChanged += new System.EventHandler(runtimeParameter_ValueChanged);
			this.numMinScore.ValueChanged += new System.EventHandler(runtimeParameter_ValueChanged);
			this.numAngleStart.ValueChanged += new System.EventHandler(modelParameter_ValueChanged);
			this.numAngleExtent.ValueChanged += new System.EventHandler(modelParameter_ValueChanged);
			this.numScaleMin.ValueChanged += new System.EventHandler(modelParameter_ValueChanged);
			this.numScaleMax.ValueChanged += new System.EventHandler(modelParameter_ValueChanged);
			this.numMaxOverlap.ValueChanged += new System.EventHandler(runtimeParameter_ValueChanged);
			this.numGreediness.ValueChanged += new System.EventHandler(runtimeParameter_ValueChanged);
			// These values remain in MarkMatchParameters for compatibility, but are
			// intentionally fixed/internal and are no longer editable on this page.
			this.lblEdgeSigma.Visible = false;
			this.numEdgeSigma.Visible = false;
			this.lblEdgeLow.Visible = false;
			this.numEdgeLow.Visible = false;
			this.lblEdgeHigh.Visible = false;
			this.numEdgeHigh.Visible = false;
			this.lblMaxFeaturePoints.Visible = false;
			this.numMaxFeaturePoints.Visible = false;
			this.lblMaxOverlap.Visible = false;
			this.numMaxOverlap.Visible = false;
			this.lblGreediness.Visible = false;
			this.numGreediness.Visible = false;
			this.grpCircle.Visible = false;
			this.grpFeature.Size = new System.Drawing.Size(316, 246);
			this.lblFeatureDistance.Location = new System.Drawing.Point(14, 62);
			this.numFeatureDistance.Location = new System.Drawing.Point(146, 62);
			this.lblBrushRadius.Location = new System.Drawing.Point(14, 96);
			this.numBrushRadius.Location = new System.Drawing.Point(146, 96);
			this.lblFeatureCount.Location = new System.Drawing.Point(14, 130);
			this.btnExtract.Location = new System.Drawing.Point(14, 164);
			this.btnResetFeatures.Location = new System.Drawing.Point(159, 164);
			this.grpSearch.Location = new System.Drawing.Point(0, 254);
			this.grpSearch.Size = new System.Drawing.Size(316, 270);
			this.grpModel.Location = new System.Drawing.Point(0, 532);
			this.grpCircle.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			this.grpCircle.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f, System.Drawing.FontStyle.Bold);
			this.grpCircle.Location = new System.Drawing.Point(0, 844);
			this.grpCircle.Size = new System.Drawing.Size(316, 126);
			this.grpCircle.Text = "圆形 Mark 精修（可选）";
			this.chkCircleRefinement.AutoSize = true;
			this.chkCircleRefinement.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.chkCircleRefinement.Location = new System.Drawing.Point(16, 27);
			this.chkCircleRefinement.Text = "启用圆心精修";
			this.chkCircleRefinement.CheckedChanged += new System.EventHandler(circleParameter_ValueChanged);
			this.grpCircle.Controls.Add(this.chkCircleRefinement);
			this.chkRequireCircle.AutoSize = true;
			this.chkRequireCircle.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.chkRequireCircle.Location = new System.Drawing.Point(154, 27);
			this.chkRequireCircle.Text = "精修失败则失败";
			this.chkRequireCircle.CheckedChanged += new System.EventHandler(circleParameter_ValueChanged);
			this.grpCircle.Controls.Add(this.chkRequireCircle);
			this.grpCircle.Controls.Add(this.lblCircleRadius);
			this.grpCircle.Controls.Add(this.numCircleRadius);
			this.lblCircleRadius.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.lblCircleRadius.Location = new System.Drawing.Point(14, 62);
			this.lblCircleRadius.Name = "lblCircleRadius";
			this.lblCircleRadius.Size = new System.Drawing.Size(120, 26);
			this.lblCircleRadius.Text = "示教圆半径";
			this.lblCircleRadius.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.numCircleRadius.DecimalPlaces = 1;
			this.numCircleRadius.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.numCircleRadius.Increment = 0.5m;
			this.numCircleRadius.Location = new System.Drawing.Point(146, 62);
			this.numCircleRadius.Maximum = 10000m;
			this.numCircleRadius.Name = "numCircleRadius";
			this.numCircleRadius.Size = new System.Drawing.Size(154, 26);
			this.numCircleRadius.ValueChanged += new System.EventHandler(circleParameter_ValueChanged);
			this.panelActions.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			this.panelActions.Location = new System.Drawing.Point(14, 624);
			this.panelActions.Size = new System.Drawing.Size(336, 46);
			this.panelActions.Controls.Add(this.btnTeach);
			this.panelActions.Controls.Add(this.btnTest);
			this.btnTeach.Location = new System.Drawing.Point(0, 4);
			this.btnTeach.Name = "btnTeach";
			this.btnTeach.Size = new System.Drawing.Size(204, 38);
			this.btnTeach.Text = "创建外轮廓模型";
			this.btnTeach.UseVisualStyleBackColor = false;
			this.btnTeach.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			this.btnTeach.BackColor = System.Drawing.Color.FromArgb(0, 139, 117);
			this.btnTeach.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.btnTeach.ForeColor = System.Drawing.Color.White;
			this.btnTeach.Click += new System.EventHandler(btnTeach_Click);
			this.btnTest.Location = new System.Drawing.Point(212, 4);
			this.btnTest.Name = "btnTest";
			this.btnTest.Size = new System.Drawing.Size(124, 38);
			this.btnTest.Text = "测试定位";
			this.btnTest.UseVisualStyleBackColor = true;
			this.btnTest.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
			this.btnTest.Click += new System.EventHandler(btnTest_Click);
			this.lblStatus.BackColor = System.Drawing.Color.FromArgb(238, 241, 244);
			this.lblStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.lblStatus.Location = new System.Drawing.Point(0, 715);
			this.lblStatus.Padding = new System.Windows.Forms.Padding(12, 0, 8, 0);
			this.lblStatus.Size = new System.Drawing.Size(1180, 35);
			this.lblStatus.Text = "默认使用圆形模板 ROI；滚轮缩放，中键拖动视图。";
			this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			base.AutoScaleDimensions = new System.Drawing.SizeF(7f, 17f);
			base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.White;
			base.Controls.Add(this.splitMain);
			base.Controls.Add(this.lblStatus);
			base.Controls.Add(this.toolMain);
			this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			base.Name = "MarkSetupControl";
			base.Size = new System.Drawing.Size(1180, 750);
			this.toolMain.ResumeLayout(false);
			this.toolMain.PerformLayout();
			this.splitMain.Panel1.ResumeLayout(false);
			this.splitMain.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)this.splitMain).EndInit();
			this.splitMain.ResumeLayout(false);
			this.panelSettings.ResumeLayout(false);
			this.panelSettings.PerformLayout();
			this.panelParameters.ResumeLayout(false);
			this.grpCircle.ResumeLayout(false);
			this.grpCircle.PerformLayout();
			this.grpSearch.ResumeLayout(false);
			this.grpSearch.PerformLayout();
			this.grpModel.ResumeLayout(false);
			this.grpModel.PerformLayout();
			this.grpFeature.ResumeLayout(false);
			this.panelActions.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)this.numTemplateMean).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numEdgeSigma).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numEdgeLow).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numEdgeHigh).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numFeatureDistance).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numMaxFeaturePoints).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numBrushRadius).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numLevels).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numMinContrast).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numSearchMean).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numMinScore).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numAngleStart).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numAngleExtent).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numScaleMin).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numScaleMax).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numMaxOverlap).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numGreediness).EndInit();
			((System.ComponentModel.ISupportInitialize)this.numCircleRadius).EndInit();
			base.ResumeLayout(false);
			base.PerformLayout();
		}
	}
}

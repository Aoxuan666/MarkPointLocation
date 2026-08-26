using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Seprays.MarkVision.Demo
{
	public class MainForm : Form
	{
		private readonly MarkLocatorSdk _sdk = new MarkLocatorSdk();

		private Bitmap _sourceImage;

		private Bitmap _displayImage;

		private MarkLocateJob _job;

		private MarkLocateResult _lastResult;

		private string _imageName = "演示图（内置）";

		private IContainer components;

		private Panel panelToolbar;

		private Button btnLoadImage;

		private Button btnSetup;

		private Button btnRun;

		private Button btnSaveJob;

		private Button btnLoadJob;

		private Label lblStatus;

		private PictureBox pictureResult;

		private HudOverlayControl hudOverlay;

		public MainForm()
		{
			InitializeComponent();
			_sourceImage = CreateDemoImage();
			ShowImage(_sourceImage);
			UpdateHud("演示图已加载，请打开 Mark 设置。", null);
		}

		private void btnLoadImage_Click(object sender, EventArgs e)
		{
			using (OpenFileDialog dialog = new OpenFileDialog())
			{
				dialog.Filter = "图像文件|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|所有文件|*.*";
				if (dialog.ShowDialog(this) == DialogResult.OK)
				{
					try
					{
						using (Bitmap loaded = new Bitmap(dialog.FileName))
						{
							_sourceImage?.Dispose();
							_sourceImage = new Bitmap(loaded);
						}
						_imageName = Path.GetFileName(dialog.FileName);
						_job = null;
						_lastResult = null;
						ShowImage(_sourceImage);
						lblStatus.Text = "图片已加载，旧 Job 已清除，请重新设置。";
						UpdateHud(lblStatus.Text, null);
					}
					catch (Exception ex)
					{
						MessageBox.Show(this, "无法加载图片：" + ex.Message, "加载图片", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					}
				}
			}
		}

		private void btnSetup_Click(object sender, EventArgs e)
		{
			MarkLocateJob edited = _sdk.OpenSetupDialog(this, _sourceImage, _job);
			if (edited != null)
			{
				_job = edited;
				_lastResult = null;
				lblStatus.Text = "Job 已更新。";
				UpdateHud(lblStatus.Text, null);
			}
		}

		private void btnRun_Click(object sender, EventArgs e)
		{
			if (_job == null)
			{
				MessageBox.Show(this, "请先配置并创建模板。", "运行定位", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			MarkLocateResult result = _sdk.RunInspection(_sourceImage, _job);
			ShowResult(result);
			lblStatus.Text = result.Message;
			UpdateHud(lblStatus.Text, result);
		}

		private void btnSaveJob_Click(object sender, EventArgs e)
		{
			if (_job == null)
			{
				MessageBox.Show(this, "当前没有可保存的 Job。", "保存 Job", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			using (SaveFileDialog dialog = new SaveFileDialog
			{
				Filter = "Mark Job|*.markjob",
				DefaultExt = "markjob"
			})
			{
				if (dialog.ShowDialog(this) == DialogResult.OK)
				{
					_sdk.SaveJob(dialog.FileName, _job);
					lblStatus.Text = "Job 已保存。";
					UpdateHud(lblStatus.Text, _lastResult);
				}
			}
		}

		private void btnLoadJob_Click(object sender, EventArgs e)
		{
			using (OpenFileDialog dialog = new OpenFileDialog
			{
				Filter = "Mark Job|*.markjob|所有文件|*.*"
			})
			{
				if (dialog.ShowDialog(this) == DialogResult.OK)
				{
					try
					{
						_job = _sdk.LoadJob(dialog.FileName);
						_lastResult = null;
						lblStatus.Text = "Job 已加载，请确认模板与当前图片匹配。";
						UpdateHud(lblStatus.Text, null);
					}
					catch (Exception ex)
					{
						MessageBox.Show(this, "无法加载 Job：" + ex.Message, "加载 Job", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					}
				}
			}
		}

		private void ShowResult(MarkLocateResult result)
		{
			_lastResult = result;
			Bitmap rendered = new Bitmap(_sourceImage);
			using (Graphics graphics = Graphics.FromImage(rendered))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				using (Pen contourPen = new Pen(Color.DeepSkyBlue, 2f))
				{
					foreach (List<PointF> contour in (result?.MatchedContours ?? new List<List<PointF>>()).Where((List<PointF> item) => item != null && item.Count > 1))
					{
						graphics.DrawLines(contourPen, contour.ToArray());
					}
				}
				if (result != null && result.CircleFound && result.CircleRadius > 0f)
				{
					using (Pen circlePen = new Pen(Color.Gold, 2f))
					{
						graphics.DrawEllipse(circlePen, result.CirclePoint.X - result.CircleRadius, result.CirclePoint.Y - result.CircleRadius, result.CircleRadius * 2f, result.CircleRadius * 2f);
					}
				}
				if (result != null && result.Success)
				{
					PointF center = result.ModelPoint;
					using (Pen centerPen = new Pen(Color.Lime, 2f))
					{
						graphics.DrawLine(centerPen, center.X - 12f, center.Y, center.X + 12f, center.Y);
						graphics.DrawLine(centerPen, center.X, center.Y - 12f, center.X, center.Y + 12f);
					}
				}
			}
			ShowImage(rendered);
			rendered.Dispose();
			UpdateHud(result?.Message ?? "未返回定位结果。", result);
		}

		private void ShowImage(Bitmap image)
		{
			pictureResult.Image = null;
			_displayImage?.Dispose();
			_displayImage = ((image == null) ? null : new Bitmap(image));
			pictureResult.Image = _displayImage;
			pictureResult.Invalidate();
		}

		private void UpdateHud(string status, MarkLocateResult result)
		{
			if (hudOverlay == null)
			{
				return;
			}
			hudOverlay.UpdateState(_imageName, _sourceImage?.Size ?? Size.Empty, _job, result, status);
		}

		private sealed class HudOverlayControl : Control
		{
			private string _imageName = "未加载";

			private Size _imageSize;

			private MarkLocateJob _job;

			private MarkLocateResult _result;

			private string _status = "等待操作";

			public HudOverlayControl()
			{
				SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
				BackColor = Color.Transparent;
				Enabled = false;
				TabStop = false;
			}

			public void UpdateState(string imageName, Size imageSize, MarkLocateJob job, MarkLocateResult result, string status)
			{
				_imageName = string.IsNullOrWhiteSpace(imageName) ? "未加载" : imageName;
				_imageSize = imageSize;
				_job = job;
				_result = result;
				_status = string.IsNullOrWhiteSpace(status) ? "等待操作" : status;
				Invalidate();
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				base.OnPaint(e);
				if (_imageSize.IsEmpty)
				{
					return;
				}

				Graphics graphics = e.Graphics;
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
				int lineHeight = 21;
				int lineCount = 5 + (_result == null ? 0 : 4) + ((_result != null && _result.CircleFound) ? 1 : 0);
				int panelWidth = Math.Min(Math.Max(280, Width - 24), 360);
				int panelHeight = 24 + lineCount * lineHeight + 14;
				Rectangle panel = new Rectangle(12, 12, panelWidth, panelHeight);

				using (SolidBrush background = new SolidBrush(Color.FromArgb(214, 12, 18, 24)))
				using (Pen border = new Pen(Color.FromArgb(150, 106, 221, 190), 1f))
				{
					graphics.FillRectangle(background, panel);
					graphics.DrawRectangle(border, panel);
				}

				using (Font titleFont = new Font(Font, FontStyle.Bold))
				using (Font bodyFont = new Font(Font.FontFamily, 9f, FontStyle.Regular))
				{
					TextRenderer.DrawText(graphics, "MARK VISION  ·  实时状态", titleFont, new Point(panel.Left + 12, panel.Top + 8), Color.FromArgb(143, 240, 207), TextFormatFlags.NoPadding);
					int y = panel.Top + 32;
					DrawLine(graphics, bodyFont, panel.Left + 12, ref y, "图片", _imageName, Color.WhiteSmoke);
					DrawLine(graphics, bodyFont, panel.Left + 12, ref y, "尺寸", _imageSize.Width + " x " + _imageSize.Height + " px", Color.WhiteSmoke);
					DrawLine(graphics, bodyFont, panel.Left + 12, ref y, "Job", GetJobState(_job), Color.WhiteSmoke);
					bool success = _result != null && _result.Success;
					Color stateColor = _result == null ? Color.LightGray : (success ? Color.FromArgb(126, 238, 156) : Color.FromArgb(255, 145, 145));
					DrawLine(graphics, bodyFont, panel.Left + 12, ref y, "定位", _result == null ? "等待运行" : (success ? "成功" : "失败"), stateColor);
					DrawLine(graphics, bodyFont, panel.Left + 12, ref y, "状态", _status, Color.FromArgb(220, 225, 230));
					if (_result != null)
					{
						DrawLine(graphics, bodyFont, panel.Left + 12, ref y, "分数", _result.Score.ToString("F3"), Color.WhiteSmoke);
						DrawLine(graphics, bodyFont, panel.Left + 12, ref y, "模板中心", "X " + _result.ModelPoint.X.ToString("F1") + "  Y " + _result.ModelPoint.Y.ToString("F1"), Color.WhiteSmoke);
						DrawLine(graphics, bodyFont, panel.Left + 12, ref y, "角度", (RadiansToDegrees(_result.AngleRadians)).ToString("F2") + " 度", Color.WhiteSmoke);
						DrawLine(graphics, bodyFont, panel.Left + 12, ref y, "缩放", _result.Scale.ToString("F3") + " x", Color.WhiteSmoke);
						if (_result.CircleFound)
						{
							DrawLine(graphics, bodyFont, panel.Left + 12, ref y, "找圆", "X " + _result.CirclePoint.X.ToString("F1") + "  Y " + _result.CirclePoint.Y.ToString("F1") + "  R " + _result.CircleRadius.ToString("F1"), Color.Gold);
						}
					}
				}
			}

			private static void DrawLine(Graphics graphics, Font font, int x, ref int y, string label, string value, Color color)
			{
				TextRenderer.DrawText(graphics, label.PadRight(5) + value, font, new Point(x, y), color, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
				y += 21;
			}

			private static string GetJobState(MarkLocateJob job)
			{
				if (job == null)
				{
					return "未配置";
				}
				if (!job.EnableTemplateMatching && job.EnableCircleDetection)
				{
					return "找圆（上位机启用）";
				}
				return job.TeachData != null && job.TeachData.HasModel ? "模板已创建" : "未创建模板";
			}

			private static double RadiansToDegrees(double radians)
			{
				return radians * 180.0 / Math.PI;
			}
		}

		private static Bitmap CreateDemoImage()
		{
			Bitmap image = new Bitmap(900, 620);
			using (Graphics graphics = Graphics.FromImage(image))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.Clear(Color.FromArgb(28, 30, 32));
				using (SolidBrush markBrush = new SolidBrush(Color.FromArgb(235, 238, 240)))
				{
					using (SolidBrush dirtBrush = new SolidBrush(Color.FromArgb(40, 42, 44)))
					{
						graphics.FillEllipse(markBrush, 345, 205, 210, 210);
						graphics.FillEllipse(dirtBrush, 395, 248, 48, 56);
						graphics.FillEllipse(dirtBrush, 465, 315, 38, 44);
						return image;
					}
				}
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_sourceImage?.Dispose();
				_displayImage?.Dispose();
				components?.Dispose();
			}
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.panelToolbar = new System.Windows.Forms.Panel();
			this.btnLoadImage = new System.Windows.Forms.Button();
			this.btnSetup = new System.Windows.Forms.Button();
			this.btnRun = new System.Windows.Forms.Button();
			this.btnSaveJob = new System.Windows.Forms.Button();
			this.btnLoadJob = new System.Windows.Forms.Button();
			this.lblStatus = new System.Windows.Forms.Label();
			this.pictureResult = new System.Windows.Forms.PictureBox();
			this.hudOverlay = new HudOverlayControl();
			this.panelToolbar.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)this.pictureResult).BeginInit();
			base.SuspendLayout();
			this.panelToolbar.BackColor = System.Drawing.Color.FromArgb(247, 249, 251);
			this.panelToolbar.Controls.Add(this.btnLoadImage);
			this.panelToolbar.Controls.Add(this.btnSetup);
			this.panelToolbar.Controls.Add(this.btnRun);
			this.panelToolbar.Controls.Add(this.btnSaveJob);
			this.panelToolbar.Controls.Add(this.btnLoadJob);
			this.panelToolbar.Controls.Add(this.lblStatus);
			this.panelToolbar.Dock = System.Windows.Forms.DockStyle.Top;
			this.panelToolbar.Location = new System.Drawing.Point(0, 0);
			this.panelToolbar.Name = "panelToolbar";
			this.panelToolbar.Size = new System.Drawing.Size(1080, 68);
			this.panelToolbar.TabIndex = 0;
			this.btnLoadImage.Location = new System.Drawing.Point(14, 16);
			this.btnLoadImage.Name = "btnLoadImage";
			this.btnLoadImage.Size = new System.Drawing.Size(94, 34);
			this.btnLoadImage.Text = "加载图片";
			this.btnLoadImage.UseVisualStyleBackColor = true;
			this.btnLoadImage.Click += new System.EventHandler(btnLoadImage_Click);
			this.btnSetup.Location = new System.Drawing.Point(116, 16);
			this.btnSetup.Name = "btnSetup";
			this.btnSetup.Size = new System.Drawing.Size(108, 34);
			this.btnSetup.Text = "Mark 设置";
			this.btnSetup.UseVisualStyleBackColor = true;
			this.btnSetup.Click += new System.EventHandler(btnSetup_Click);
			this.btnRun.BackColor = System.Drawing.Color.FromArgb(0, 122, 105);
			this.btnRun.FlatAppearance.BorderSize = 0;
			this.btnRun.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.btnRun.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f, System.Drawing.FontStyle.Bold);
			this.btnRun.ForeColor = System.Drawing.Color.White;
			this.btnRun.Location = new System.Drawing.Point(232, 16);
			this.btnRun.Name = "btnRun";
			this.btnRun.Size = new System.Drawing.Size(94, 34);
			this.btnRun.Text = "运行定位";
			this.btnRun.UseVisualStyleBackColor = false;
			this.btnRun.Click += new System.EventHandler(btnRun_Click);
			this.btnSaveJob.Location = new System.Drawing.Point(342, 16);
			this.btnSaveJob.Name = "btnSaveJob";
			this.btnSaveJob.Size = new System.Drawing.Size(94, 34);
			this.btnSaveJob.Text = "保存 Job";
			this.btnSaveJob.UseVisualStyleBackColor = true;
			this.btnSaveJob.Click += new System.EventHandler(btnSaveJob_Click);
			this.btnLoadJob.Location = new System.Drawing.Point(444, 16);
			this.btnLoadJob.Name = "btnLoadJob";
			this.btnLoadJob.Size = new System.Drawing.Size(94, 34);
			this.btnLoadJob.Text = "加载 Job";
			this.btnLoadJob.UseVisualStyleBackColor = true;
			this.btnLoadJob.Click += new System.EventHandler(btnLoadJob_Click);
			this.lblStatus.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(65, 72, 82);
			this.lblStatus.Location = new System.Drawing.Point(558, 16);
			this.lblStatus.Name = "lblStatus";
			this.lblStatus.Size = new System.Drawing.Size(506, 34);
			this.lblStatus.Text = "演示图已加载，请打开 Mark 设置。";
			this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			this.pictureResult.BackColor = System.Drawing.Color.FromArgb(24, 27, 31);
			this.pictureResult.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pictureResult.Location = new System.Drawing.Point(0, 68);
			this.pictureResult.Name = "pictureResult";
			this.pictureResult.Size = new System.Drawing.Size(1080, 632);
			this.pictureResult.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this.pictureResult.TabStop = false;
			this.hudOverlay.Dock = System.Windows.Forms.DockStyle.Fill;
			this.hudOverlay.Location = new System.Drawing.Point(0, 0);
			this.hudOverlay.Name = "hudOverlay";
			this.hudOverlay.Size = new System.Drawing.Size(1080, 632);
			this.hudOverlay.TabIndex = 0;
			this.pictureResult.Controls.Add(this.hudOverlay);
			base.AutoScaleDimensions = new System.Drawing.SizeF(7f, 17f);
			base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			base.ClientSize = new System.Drawing.Size(1080, 700);
			base.Controls.Add(this.pictureResult);
			base.Controls.Add(this.panelToolbar);
			this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.MinimumSize = new System.Drawing.Size(900, 620);
			base.Name = "MainForm";
			base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Mark 定位上位机接入演示";
			this.panelToolbar.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)this.pictureResult).EndInit();
			base.ResumeLayout(false);
		}
	}
}

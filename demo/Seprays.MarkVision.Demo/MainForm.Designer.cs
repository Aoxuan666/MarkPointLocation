using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Seprays.MarkVision.Demo
{
	public partial class MainForm
	{
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
			this.lblStatus = new System.Windows.Forms.Label();
			this.pictureResult = new System.Windows.Forms.PictureBox();
			this.panelToolbar.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)this.pictureResult).BeginInit();
			base.SuspendLayout();
			this.panelToolbar.BackColor = System.Drawing.Color.FromArgb(247, 249, 251);
			this.panelToolbar.Controls.Add(this.btnLoadImage);
			this.panelToolbar.Controls.Add(this.btnSetup);
			this.panelToolbar.Controls.Add(this.btnRun);
			this.panelToolbar.Controls.Add(this.lblStatus);
			this.panelToolbar.Dock = System.Windows.Forms.DockStyle.Top;
			this.panelToolbar.Location = new System.Drawing.Point(0, 0);
			this.panelToolbar.Name = "panelToolbar";
			this.panelToolbar.Size = new System.Drawing.Size(1080, 68);
			this.panelToolbar.TabIndex = 0;
			this.btnLoadImage.Location = new System.Drawing.Point(14, 16);
			this.btnLoadImage.Name = "btnLoadImage";
			this.btnLoadImage.Size = new System.Drawing.Size(94, 34);
			this.btnLoadImage.Text = "加载图像";
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
			this.lblStatus.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
			this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(65, 72, 82);
			this.lblStatus.Location = new System.Drawing.Point(342, 16);
			this.lblStatus.Name = "lblStatus";
			this.lblStatus.Size = new System.Drawing.Size(722, 34);
			this.lblStatus.Text = "演示图已加载，请打开 Mark 设置。";
			this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			this.pictureResult.BackColor = System.Drawing.Color.FromArgb(24, 27, 31);
			this.pictureResult.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pictureResult.Location = new System.Drawing.Point(0, 68);
			this.pictureResult.Name = "pictureResult";
			this.pictureResult.Size = new System.Drawing.Size(1080, 632);
			this.pictureResult.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this.pictureResult.TabStop = false;
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

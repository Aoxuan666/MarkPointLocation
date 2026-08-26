using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Seprays.MarkVision
{
	public class MarkSetupForm : Form
	{
		private IContainer components;

		private MarkSetupControl setupControl;

		private Panel panelFooter;

		private Button btnConfirm;

		private Button btnCancel;

		public MarkLocateJob Job { get; private set; }

		public MarkSetupForm()
		{
			InitializeComponent();
		}

		public MarkSetupForm(Bitmap image, MarkLocateJob currentJob)
			: this()
		{
			setupControl.LoadConfiguration(image, currentJob);
		}

		private void btnConfirm_Click(object sender, EventArgs e)
		{
			if (!setupControl.ValidateConfiguration(out var message))
			{
				MessageBox.Show(this, message, "Mark 定位设置", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			Job = setupControl.Job;
			base.DialogResult = DialogResult.OK;
			Close();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && components != null)
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.setupControl = new Seprays.MarkVision.MarkSetupControl();
			this.panelFooter = new System.Windows.Forms.Panel();
			this.btnConfirm = new System.Windows.Forms.Button();
			this.btnCancel = new System.Windows.Forms.Button();
			this.panelFooter.SuspendLayout();
			base.SuspendLayout();
			this.setupControl.Dock = System.Windows.Forms.DockStyle.Fill;
			this.setupControl.Location = new System.Drawing.Point(0, 0);
			this.setupControl.Name = "setupControl";
			this.setupControl.Size = new System.Drawing.Size(1180, 720);
			this.setupControl.TabIndex = 0;
			this.panelFooter.BackColor = System.Drawing.Color.White;
			this.panelFooter.Controls.Add(this.btnConfirm);
			this.panelFooter.Controls.Add(this.btnCancel);
			this.panelFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.panelFooter.Location = new System.Drawing.Point(0, 720);
			this.panelFooter.Name = "panelFooter";
			this.panelFooter.Padding = new System.Windows.Forms.Padding(12);
			this.panelFooter.Size = new System.Drawing.Size(1180, 58);
			this.panelFooter.TabIndex = 1;
			this.btnConfirm.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
			this.btnConfirm.BackColor = System.Drawing.Color.FromArgb(0, 122, 105);
			this.btnConfirm.FlatAppearance.BorderSize = 0;
			this.btnConfirm.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.btnConfirm.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f, System.Drawing.FontStyle.Bold);
			this.btnConfirm.ForeColor = System.Drawing.Color.White;
			this.btnConfirm.Location = new System.Drawing.Point(968, 11);
			this.btnConfirm.Name = "btnConfirm";
			this.btnConfirm.Size = new System.Drawing.Size(92, 36);
			this.btnConfirm.TabIndex = 0;
			this.btnConfirm.Text = "确认";
			this.btnConfirm.UseVisualStyleBackColor = false;
			this.btnConfirm.Click += new System.EventHandler(btnConfirm_Click);
			this.btnCancel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
			this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.btnCancel.Location = new System.Drawing.Point(1068, 11);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new System.Drawing.Size(92, 36);
			this.btnCancel.TabIndex = 1;
			this.btnCancel.Text = "取消";
			this.btnCancel.UseVisualStyleBackColor = true;
			base.AcceptButton = this.btnConfirm;
			base.AutoScaleDimensions = new System.Drawing.SizeF(7f, 17f);
			base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			base.CancelButton = this.btnCancel;
			base.ClientSize = new System.Drawing.Size(1180, 778);
			base.Controls.Add(this.setupControl);
			base.Controls.Add(this.panelFooter);
			this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f);
			this.MinimumSize = new System.Drawing.Size(980, 700);
			base.Name = "MarkSetupForm";
			base.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Mark 定位设置";
			this.panelFooter.ResumeLayout(false);
			base.ResumeLayout(false);
		}
	}
}

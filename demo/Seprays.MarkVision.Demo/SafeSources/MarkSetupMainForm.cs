using System.Drawing;
using System.Windows.Forms;
using Seprays.MarkVision;

namespace Seprays.MarkVision.Demo
{
	public sealed class MainForm : MarkSetupForm
	{
		public MainForm()
		{
			Text = "Mark 点设置";
			StartPosition = FormStartPosition.CenterScreen;
			WindowState = FormWindowState.Normal;
			ClientSize = new Size(1180, 808);
			MinimumSize = new Size(980, 700);
		}
	}
}

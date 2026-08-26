using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Forms;

namespace Seprays.MarkVision
{
	public sealed class MarkLocatorSdk
	{
		public MarkLocateJob OpenSetupDialog(Bitmap image, MarkLocateJob currentJob)
		{
			if (image == null)
			{
				throw new ArgumentNullException("image");
			}
			using (MarkSetupForm form = new MarkSetupForm(image, currentJob))
			{
				return (form.ShowDialog() != DialogResult.OK) ? null : form.Job?.DeepClone();
			}
		}

		public MarkLocateJob OpenSetupDialog(IWin32Window owner, Bitmap image, MarkLocateJob currentJob)
		{
			if (image == null)
			{
				throw new ArgumentNullException("image");
			}
			using (MarkSetupForm form = new MarkSetupForm(image, currentJob))
			{
				return (form.ShowDialog(owner) != DialogResult.OK) ? null : form.Job?.DeepClone();
			}
		}

		public MarkSetupControl CreateSetupControl(Bitmap image, MarkLocateJob currentJob)
		{
			if (image == null)
			{
				throw new ArgumentNullException("image");
			}
			MarkSetupControl markSetupControl = new MarkSetupControl();
			markSetupControl.Dock = DockStyle.Fill;
			markSetupControl.LoadConfiguration(image, currentJob);
			return markSetupControl;
		}

		public MarkLocateResult RunInspection(Bitmap image, MarkLocateJob job)
		{
			if (image == null)
			{
				return MarkLocateResult.Failed("待检测图片为空。");
			}
			if (job == null)
			{
				return MarkLocateResult.Failed("Mark 定位 Job 为空。");
			}
			return new HalconMarkLocator().Locate(image, job);
		}

		public MarkLocateJob Teach(Bitmap image, RectangleF templateRoi, RectangleF searchRoi, MarkMatchParameters parameters, IEnumerable<MaskStroke> maskStrokes)
		{
			if (image == null)
			{
				throw new ArgumentNullException("image");
			}
			MarkLocateJob job = new MarkLocateJob
			{
				TemplateRoi = templateRoi,
				SearchRoi = searchRoi,
				SearchRoiEnabled = !searchRoi.IsEmpty,
				Parameters = (parameters?.DeepClone() ?? new MarkMatchParameters()),
				MaskStrokes = ((maskStrokes == null) ? new List<MaskStroke>() : new List<MaskStroke>(maskStrokes.Select((MaskStroke stroke) => stroke?.DeepClone() ?? new MaskStroke())))
			};
			job.Normalize(image.Size);
			job.TeachData = new HalconMarkLocator().Teach(image, job);
			return job;
		}

		public void SaveJob(string path, MarkLocateJob job)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				throw new ArgumentException("保存路径为空。", "path");
			}
			if (job == null)
			{
				throw new ArgumentNullException("job");
			}
			using (FileStream stream = File.Create(path))
			{
				new BinaryFormatter().Serialize(stream, job);
			}
		}

		public MarkLocateJob LoadJob(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				throw new ArgumentException("配置路径为空。", "path");
			}
			using (FileStream stream = File.OpenRead(path))
			{
				return ((MarkLocateJob)new BinaryFormatter().Deserialize(stream))?.DeepClone();
			}
		}
	}
}

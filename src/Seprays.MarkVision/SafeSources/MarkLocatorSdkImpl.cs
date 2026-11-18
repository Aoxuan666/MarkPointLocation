using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
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
			double elapsedMilliseconds;
			return RunInspectionCore(image, job, returnResultImage, "上位机SDK", out elapsedMilliseconds);
		}

		internal MarkLocateResult RunInspectionCore(Bitmap image, MarkLocateJob job, bool returnResultImage, string callerSource, out double elapsedMilliseconds)
		{
			Stopwatch timer = Stopwatch.StartNew();
			MarkLocateResult result;
			if (image == null)
			{
				result = MarkLocateResult.Failed("待检测图片为空。");
			}
			else if (job == null)
			{
				result = MarkLocateResult.Failed("Mark 定位 Job 为空。");
			}
			else
			{
				result = new HalconMarkLocator().Locate(image, job);
			}
			if (returnResultImage && image != null)
			{
				result.ResultImage = HalconMarkLocator.RenderResultImageWithHalcon(image, result);
			}
			timer.Stop();
			elapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
			MarkInspectionLogger.LogInspection(callerSource, image, job, result, elapsedMilliseconds);
			return result;
		}

		private static MarkLocateJob ShowSetupDialog(Bitmap image, MarkLocateJob currentJob)
		{
			using (MarkSetupForm form = new MarkSetupForm(image, currentJob))
				return form.ShowDialog() == DialogResult.OK ? form.Job?.DeepClone() : null;
		}
	}

	internal static class MarkInspectionLogger
	{
		private static readonly object SyncRoot = new object();
		private static readonly Encoding Utf8WithBom = new UTF8Encoding(true);
		private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

		internal static string GetLogFilePath(DateTime timestamp)
		{
			return Path.Combine(@"D:\", string.Format(CultureInfo.InvariantCulture, "MarkVision_RunLog_{0:yyyyMMdd}.log", timestamp));
		}

		internal static void LogInspection(string callerSource, Bitmap image, MarkLocateJob job, MarkLocateResult result, double elapsedMilliseconds)
		{
			DateTime now = DateTime.Now;
			string imageInfo = image == null ? "null" : $"{image.Width}x{image.Height}({image.PixelFormat})";
			MarkMatchParameters p = job?.Parameters;
			string paramInfo = job == null
				? "Job=null"
				: string.Format(
					CultureInfo.InvariantCulture,
					"Tpl={0} Circle={1} SearchRoi={2} AutoThresh={3}(Low={4:F1},High={5:F1}) Polarity={6} MinScore={7:F2} Greed={8:F2}",
					job.EnableTemplateMatching,
					job.EnableCircleDetection,
					job.SearchRoiEnabled,
					p?.AutoEdgeThreshold ?? false,
					p?.EdgeLowThreshold ?? 0.0,
					p?.EdgeHighThreshold ?? 0.0,
					p == null ? "-" : (p.UsePolarity ? "IgnoreLocal" : "IgnoreGlobal"),
					p?.MinScore ?? 0.0,
					p?.Greediness ?? 0.0);

			bool success = result != null && result.Success;
			double score = result?.Score ?? 0.0;
			double x = result?.ModelPoint.X ?? 0.0;
			double y = result?.ModelPoint.Y ?? 0.0;
			double angleDeg = result != null ? result.AngleRadians * 180.0 / Math.PI : 0.0;
			double scale = result?.Scale ?? 1.0;
			string circleInfo = result != null && result.CircleFound
				? string.Format(CultureInfo.InvariantCulture, "True({0:F3},{1:F3},R={2:F2})", result.CirclePoint.X, result.CirclePoint.Y, result.CircleRadius)
				: "False";
			string message = (result?.Message ?? string.Empty).Replace("\r", " ").Replace("\n", " ");

			string line = string.Format(
				CultureInfo.InvariantCulture,
				"[{0:yyyy-MM-dd HH:mm:ss.fff}] [定位] 来源={1} TID={2} 图像={3} 结果={4} 分数={5:F4} 坐标=({6:F3},{7:F3}) 角度={8:F3}° 缩放={9:F4} 找圆={10} 耗时={11:F2}ms {12} 说明={13}",
				now,
				string.IsNullOrEmpty(callerSource) ? "未知" : callerSource,
				Thread.CurrentThread.ManagedThreadId,
				imageInfo,
				success ? "成功" : "失败",
				score,
				x,
				y,
				angleDeg,
				scale,
				circleInfo,
				elapsedMilliseconds,
				paramInfo,
				message);
			WriteLine(now, line);
		}

		internal static void LogTeach(string callerSource, Bitmap image, MarkLocateJob job, int featureCount, bool success, double elapsedMilliseconds, string message)
		{
			DateTime now = DateTime.Now;
			string imageInfo = image == null ? "null" : $"{image.Width}x{image.Height}({image.PixelFormat})";
			MarkMatchParameters p = job?.Parameters;
			string cleanMessage = (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
			string line = string.Format(
				CultureInfo.InvariantCulture,
				"[{0:yyyy-MM-dd HH:mm:ss.fff}] [建模] 来源={1} TID={2} 图像={3} 结果={4} 特征点={5} AutoThresh={6}(Low={7:F1},High={8:F1}) MinContrast={9} 耗时={10:F2}ms 说明={11}",
				now,
				string.IsNullOrEmpty(callerSource) ? "未知" : callerSource,
				Thread.CurrentThread.ManagedThreadId,
				imageInfo,
				success ? "成功" : "失败",
				featureCount,
				p?.AutoEdgeThreshold ?? false,
				p?.EdgeLowThreshold ?? 0.0,
				p?.EdgeHighThreshold ?? 0.0,
				p?.MinContrast ?? 0,
				elapsedMilliseconds,
				cleanMessage);
			WriteLine(now, line);
		}

		private static void WriteLine(DateTime timestamp, string line)
		{
			try
			{
				if (!Directory.Exists(@"D:\"))
				{
					return;
				}
				string path = GetLogFilePath(timestamp);
				lock (SyncRoot)
				{
					bool isNew = !File.Exists(path);
					using (StreamWriter writer = new StreamWriter(path, append: true, encoding: isNew ? Utf8WithBom : Utf8NoBom))
					{
						writer.WriteLine(line);
					}
				}
			}
			catch
			{
				// Never break production inspection if disk logging is unavailable.
			}
		}
	}
}

using System;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

namespace Seprays.MarkVision
{
	/// <summary>
	/// Mark 定位 SDK 入口。
	/// </summary>
	public sealed class MarkLocatorSdk
	{
		/// <summary>
		/// 在 STA UI 线程打开模板设置页面。
		/// </summary>
		/// <param name="image">上位机提供的示教图片。SDK 只读取图片，不会释放它。</param>
		/// <param name="currentJob">已有定位参数；取消对话框时返回 null。</param>
		/// <returns>用户确认后的定位参数，取消时返回 null。</returns>
		public MarkLocateJob OpenSetupDialog(Bitmap image, MarkLocateJob currentJob)
		{
			if (image == null)
			{
				throw new ArgumentNullException("image");
			}

			Func<MarkLocateJob> showDialog = () =>
			{
				using (MarkSetupForm form = new MarkSetupForm(image, currentJob))
				{
					return form.ShowDialog() == DialogResult.OK ? form.Job?.DeepClone() : null;
				}
			};

			return Thread.CurrentThread.GetApartmentState() == ApartmentState.STA
				? showDialog()
				: RunOnStaThread(showDialog);
		}

		/// <summary>
		/// 使用已创建的模板执行一次定位。
		/// </summary>
		/// <param name="image">待检测图片。SDK 只读取图片，不会释放它。</param>
		/// <param name="job">由 <see cref="OpenSetupDialog"/> 返回的定位参数。</param>
		/// <param name="returnResultImage">是否在结果中返回绘制了定位结果的效果图；返回图片由上位机负责释放。</param>
		/// <returns>包含成功状态、匹配点、分数及可选效果图的定位结果。</returns>
		public MarkLocateResult RunInspection(Bitmap image, MarkLocateJob job, bool returnResultImage)
		{
			if (image == null)
			{
				return MarkLocateResult.Failed("待检测图片为空。");
			}
			if (job == null)
			{
				return MarkLocateResult.Failed("Mark 定位 Job 为空。");
			}

			MarkLocateResult result = new HalconMarkLocator().Locate(image, job);
			if (returnResultImage)
			{
				result.ResultImage = RenderResultImage(image, result);
			}
			return result;
		}

		private static MarkLocateJob RunOnStaThread(Func<MarkLocateJob> action)
		{
			MarkLocateJob result = null;
			Exception error = null;
			using (ManualResetEvent completed = new ManualResetEvent(false))
			{
				Thread thread = new Thread(() =>
				{
					try
					{
						result = action();
					}
					catch (Exception ex)
					{
						error = ex;
					}
					finally
					{
						completed.Set();
					}
				});
				thread.IsBackground = false;
				thread.SetApartmentState(ApartmentState.STA);
				thread.Start();
				completed.WaitOne();
				thread.Join();
			}

			if (error != null)
			{
				ExceptionDispatchInfo.Capture(error).Throw();
			}
			return result;
		}

		private static Bitmap RenderResultImage(Bitmap source, MarkLocateResult result)
		{
			Bitmap rendered = new Bitmap(source);
			if (!result.Success || float.IsNaN(result.ModelPoint.X) || float.IsNaN(result.ModelPoint.Y) ||
				float.IsInfinity(result.ModelPoint.X) || float.IsInfinity(result.ModelPoint.Y))
			{
				return rendered;
			}
			using (Graphics graphics = Graphics.FromImage(rendered))
			{
				graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				using (Pen pen = new Pen(result.Success ? Color.LimeGreen : Color.Red, 2f))
				{
					PointF point = result.ModelPoint;
					graphics.DrawLine(pen, point.X - 12f, point.Y, point.X + 12f, point.Y);
					graphics.DrawLine(pen, point.X, point.Y - 12f, point.X, point.Y + 12f);
				}
			}
			return rendered;
		}
	}
}

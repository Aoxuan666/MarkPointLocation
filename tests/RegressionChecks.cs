using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using Seprays.MarkVision;
using System.Windows.Forms;

public static class RegressionChecks
{
    private static readonly PointF[] Vertices = {
        new PointF(-82, -66), new PointF(58, -66), new PointF(58, -24),
        new PointF(91, -24), new PointF(91, 61), new PointF(12, 61),
        new PointF(12, 31), new PointF(-82, 31)
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static string SdkContract(string screenshotPath)
    {
        var methods = typeof(MarkLocatorSdk).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Require(methods.Length == 2, "SDK must expose exactly two methods.");
        using (var image = Image(Polygon(320, 240, 0, 1)))
        {
            var job = new MarkLocateJob { TemplateRoiShape = RegionShape.Rectangle,
                TemplateRoi = new RectangleF(210, 130, 220, 220), SearchRoiEnabled = false };
            job.TeachData = new HalconMarkLocator().Teach(image, job);
            var originalScore = job.Parameters.MinScore;
            var sdk = new MarkLocatorSdk();
            using (var timer = new Timer { Interval = 100 })
            {
                timer.Tick += (sender, args) => {
                    var form = Application.OpenForms[Application.OpenForms.Count - 1] as MarkSetupForm;
                    if (form == null) return;
                    timer.Stop();
                    var control = (MarkSetupControl)form.Controls[0];
                    var score = (NumericUpDown)typeof(MarkSetupControl).GetField("numMinScore",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(control);
                    score.Value = 0.42m;
                    using (var screenshot = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                        screenshot.Save(screenshotPath, ImageFormat.Png);
                    }
                    ((Button)form.AcceptButton).PerformClick();
                };
                timer.Start();
                var edited = sdk.OpenSetupDialog(image, job);
                Require(edited != null && edited.Parameters.MinScore == 0.42,
                    "Confirm did not return the edited job.");
                Require(job.Parameters.MinScore == originalScore && !object.ReferenceEquals(edited, job) &&
                    !object.ReferenceEquals(edited.TeachData.ModelBytes, job.TeachData.ModelBytes),
                    "Setup returned shared job state or modified the input job.");
            }
            using (var timer = new Timer { Interval = 100 })
            {
                timer.Tick += (sender, args) => {
                    var form = Application.OpenForms[Application.OpenForms.Count - 1] as MarkSetupForm;
                    if (form == null) return;
                    timer.Stop();
                    ((Button)form.CancelButton).PerformClick();
                };
                timer.Start();
                Require(sdk.OpenSetupDialog(image, job) == null, "Cancel must return null.");
            }
            using (var failureImage = sdk.RunInspection(image, null, true).ResultImage)
                Require(failureImage != null && failureImage.Size == image.Size, "Failure must still return a requested image.");
            Require(sdk.RunInspection(null, job, true).ResultImage == null, "Null input must not allocate an image.");
        }
        using (var indexed = new Bitmap(80, 60, PixelFormat.Format8bppIndexed))
        using (var rendered = new MarkLocatorSdk().RunInspection(indexed, null, true).ResultImage)
            Require(rendered != null && rendered.PixelFormat == PixelFormat.Format24bppRgb,
                "Indexed camera images must produce a drawable result bitmap.");
        return "PASS: two SDK methods, confirmation job clone, cancellation, result images and indexed input";
    }

    public static string BusyState()
    {
        using (var control = new MarkSetupControl())
        using (var image = new Bitmap(640, 480))
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(MarkSetupControl);
            var canvas = (MaskImageCanvas)type.GetField("canvas", flags).GetValue(control);
            var setBusy = type.GetMethod("SetBusy", flags);
            setBusy.Invoke(control, new object[] { true, null });
            Require(!canvas.Enabled, "Canvas must be disabled during work.");
            string message;
            Require(!control.ValidateConfiguration(out message) && !string.IsNullOrEmpty(message),
                "Busy configuration must not be accepted.");
            bool rejected = false;
            try { control.LoadConfiguration(image, null); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Busy configuration replacement must be rejected.");
            setBusy.Invoke(control, new object[] { false, null });
            Require(canvas.Enabled, "Canvas must be re-enabled after work.");
            return "PASS: busy editing, configuration replacement, confirmation and recovery";
        }
    }

    public static string SetupDefaults()
    {
        using (var control = new MarkSetupControl())
        using (var image = new Bitmap(320, 240))
        {
            var parameters = new MarkMatchParameters();
            Require(Math.Abs(parameters.AngleStartDegrees - (-5.0)) < 0.001,
                "Default angle start must be -5 degrees.");
            Require(Math.Abs(parameters.AngleExtentDegrees - 10.0) < 0.001,
                "Default angle extent must be 10 degrees.");
            Require(Math.Abs(parameters.BrushRadius - 6.0) < 0.001,
                "Default brush radius must be 6 pixels.");

            control.LoadConfiguration(image, null);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(MarkSetupControl);
            var canvas = (MaskImageCanvas)type.GetField("canvas", flags).GetValue(control);
            var toolMain = (ToolStrip)type.GetField("toolMain", flags).GetValue(control);
            var brushButton = (Button)type.GetField("btnBrushMask", flags).GetValue(control);
            var angleStart = (NumericUpDown)type.GetField("numAngleStart", flags).GetValue(control);
            var angleExtent = (NumericUpDown)type.GetField("numAngleExtent", flags).GetValue(control);
            var brushRadius = (NumericUpDown)type.GetField("numBrushRadius", flags).GetValue(control);

            Require(angleStart.Value == -5m, "Setup page angle start default must be -5 degrees.");
            Require(angleExtent.Value == 10m, "Setup page angle extent default must be 10 degrees.");
            Require(brushRadius.Value == 6m, "Setup page brush radius default must be 6 pixels.");

            Require(canvas.TemplateRoi.IsEmpty, "New configuration must not display a template ROI.");
            Require(canvas.SearchRoi.IsEmpty && !canvas.SearchRoiEnabled,
                "New configuration must not display or enable a search ROI.");
            foreach (ToolStripItem item in toolMain.Items)
            {
                Require(item.Text != "区域排除" && item.Text != "区域恢复" &&
                    item.Text != "画笔排除" && item.Text != "画笔恢复" &&
                    item.Text != "清空掩膜",
                    "Obsolete mask controls must not be present in the top toolbar.");
            }

            brushButton.PerformClick();
            Require(canvas.InteractionMode == CanvasInteractionMode.BrushExclude,
                "Settings brush button did not enter brush mask mode.");
            brushButton.PerformClick();
            Require(canvas.InteractionMode == CanvasInteractionMode.Pan,
                "Settings brush button did not exit brush mask mode.");
            return "PASS: new ROI defaults, toolbar cleanup and settings brush toggle";
        }
    }

    public static string ReuseTeachModelForTestImage()
    {
        var teachPolygon = Polygon(320, 240, 0, 1);
        var testPolygon = Polygon(363, 213, 12, 1.02);
        using (var teachImage = Image(teachPolygon))
        using (var testImage = Image(testPolygon))
        {
            var job = new MarkLocateJob
            {
                TemplateRoiShape = RegionShape.Rectangle,
                TemplateRoi = new RectangleF(210, 130, 220, 220),
                SearchRoiEnabled = false
            };
            job.Parameters.AngleStartDegrees = -30;
            job.Parameters.AngleExtentDegrees = 60;
            job.Parameters.ScaleMin = 0.9;
            job.Parameters.ScaleMax = 1.1;
            job.Parameters.MinScore = 0.3;
            job.Parameters.MinContrast = 5;
            job.Parameters.FeatureMinDistancePx = 1;
            var sdk = new MarkLocatorSdk();
            job.TeachData = new HalconMarkLocator().Teach(teachImage, job);
            Require(job.TeachData.HasModel && job.TeachData.ModelBytes != null && job.TeachData.ModelBytes.Length > 0,
                "Teaching did not create a reusable model.");
            var modelBytes = job.TeachData.ModelBytes;
            var result = sdk.RunInspection(testImage, job, false);
            Require(result.Success, "Test image did not use the existing taught model: " + result.Message);
            Require(result.ResultImage == null, "Result image must not be allocated when disabled.");
            var sourcePixel = testImage.GetPixel(363, 213);
            var rendered = sdk.RunInspection(testImage, job, true);
            using (var overlay = rendered.ResultImage)
            {
                Require(rendered.Success && overlay != null && overlay.Size == testImage.Size,
                    "Requested inspection image must be returned at source size.");
                Require(!object.ReferenceEquals(testImage, overlay), "Result image must own a separate bitmap.");
                bool different = false;
                for (int y = 0; y < overlay.Height && !different; y++)
                    for (int x = 0; x < overlay.Width && !different; x++)
                        different = overlay.GetPixel(x, y) != testImage.GetPixel(x, y);
                Require(different, "Successful result image did not draw any overlays.");
            }
            Require(testImage.GetPixel(363, 213) == sourcePixel, "Rendering changed the source image.");
            Require(object.ReferenceEquals(modelBytes, job.TeachData.ModelBytes),
                "Test inspection replaced the taught model bytes.");
            return "PASS: second image is matched directly with the existing taught model";
        }
    }

    public static string SetupControlSingleImageWorkflow()
    {
        using (var teachImage = Image(Polygon(320, 240, 0, 1)))
        using (var testImage = Image(Polygon(363, 213, 12, 1.02)))
        using (var control = new MarkSetupControl())
        {
            var job = new MarkLocateJob
            {
                TemplateRoiShape = RegionShape.Rectangle,
                TemplateRoi = new RectangleF(210, 130, 220, 220),
                SearchRoiEnabled = false
            };
            job.Parameters.AngleStartDegrees = -30;
            job.Parameters.AngleExtentDegrees = 60;
            job.Parameters.ScaleMin = 0.9;
            job.Parameters.ScaleMax = 1.1;
            job.Parameters.MinScore = 0.3;
            job.Parameters.MinContrast = 5;
            job.Parameters.FeatureMinDistancePx = 1;
            control.LoadConfiguration(teachImage, job);

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(MarkSetupControl);
            var teach = type.GetMethod("btnTeach_Click", flags);
            var test = type.GetMethod("btnTest_Click", flags);
            var taught = false;
            var tested = false;
            MarkLocateResult testResult = null;
            control.TeachCompleted += (sender, args) => taught = true;
            control.TestCompleted += (sender, args) => { tested = true; testResult = args.Result; };

            teach.Invoke(control, new object[] { null, EventArgs.Empty });
            WaitFor(() => taught, "MarkSetupControl did not complete teaching.");
            var modelBytes = (byte[])control.Job.TeachData.ModelBytes.Clone();
            Require(modelBytes != null && modelBytes.Length > 0, "Page teaching did not create a model.");

            control.LoadImage(testImage);
            Require(BytesEqual(modelBytes, control.Job.TeachData.ModelBytes),
                "Loading a test image changed the taught model bytes.");
            var canvas = (MaskImageCanvas)type.GetField("canvas", flags).GetValue(control);
            var addSearch = (ToolStripButton)type.GetField("btnSearchRoi", flags).GetValue(control);
            Require(addSearch.Enabled, "Add search ROI must work before enabling the checkbox.");
            addSearch.PerformClick();
            Require(canvas.SearchRoi.IsEmpty, "Adding search ROI must wait for a mouse drag.");
            canvas.SearchRoi = new RectangleF(250, 100, 250, 250);
            Require(BytesEqual(modelBytes, control.Job.TeachData.ModelBytes),
                "Changing search ROI invalidated the model.");
            test.Invoke(control, new object[] { null, EventArgs.Empty });
            WaitFor(() => tested, "MarkSetupControl did not complete test inspection.");
            Require(testResult != null && testResult.Success,
                "MarkSetupControl test image did not locate the existing model: " + (testResult == null ? "no result" : testResult.Message));
            Require(BytesEqual(modelBytes, control.Job.TeachData.ModelBytes),
                "Page test inspection changed the taught model bytes.");
            using (var smaller = new Bitmap(100, 80))
            {
                var originalRoi = control.Job.TemplateRoi;
                control.LoadImage(smaller);
                Require(control.Job.TemplateRoi == originalRoi, "A different image size clipped the template ROI.");
                Require(BytesEqual(modelBytes, control.Job.TeachData.ModelBytes), "Different image size cleared model.");
            }
            control.LoadImage(testImage);
            var contrast = (NumericUpDown)type.GetField("numMinContrast", flags).GetValue(control);
            contrast.Value = 80;
            Require(control.Job.Parameters.MinContrast == 80, "MinContrast was silently capped at hidden Contrast=30.");
            var low = (NumericUpDown)type.GetField("numEdgeLow", flags).GetValue(control);
            var high = (NumericUpDown)type.GetField("numEdgeHigh", flags).GetValue(control);
            low.Value = 60;
            Require(high.Value > low.Value && control.Job.Parameters.EdgeHighThreshold == (double)high.Value,
                "Threshold UI and algorithm values disagree.");
            Require(!control.Job.TeachData.HasModel, "Changing template extraction parameters must invalidate the model.");
            return "PASS: single image entry, second-image matching, search ROI/cache preservation, different image sizes and parameter editing";
        }
    }

    public static string SetupControlTestsCurrentImage(string screenshotPath, Type demoType)
    {
        var teachPolygon = Polygon(320, 240, 0, 1);
        using (var teachImage = Image(teachPolygon))
        using (var form = (Form)Activator.CreateInstance(demoType))
        {
            var control = (MarkSetupControl)form.Controls[0];
            form.Show();
            Application.DoEvents();
            var job = new MarkLocateJob
            {
                TemplateRoiShape = RegionShape.Rectangle,
                TemplateRoi = new RectangleF(210, 130, 220, 220),
                SearchRoiEnabled = false
            };
            job.Parameters.AngleStartDegrees = -30;
            job.Parameters.AngleExtentDegrees = 60;
            job.Parameters.ScaleMin = 0.9;
            job.Parameters.ScaleMax = 1.1;
            job.Parameters.MinScore = 0.3;
            job.Parameters.MinContrast = 5;
            job.Parameters.FeatureMinDistancePx = 1;
            job.TeachData = new HalconMarkLocator().Teach(teachImage, job);
            control.LoadConfiguration(teachImage, job);

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(MarkSetupControl);
            var test = type.GetMethod("btnTest_Click", flags);
            var busy = type.GetField("_busy", flags);
            var status = (Label)type.GetField("lblStatus", flags).GetValue(control);
            var canvas = (MaskImageCanvas)type.GetField("canvas", flags).GetValue(control);
            var hud = typeof(MaskImageCanvas).GetField("_inspectionHudText", flags);
            bool tested = false;
            control.TestCompleted += (sender, args) => tested = true;

            test.Invoke(control, new object[] { null, EventArgs.Empty });
            WaitFor(() => tested, "Current image was not tested without loading a second image.");
            Require(!(bool)busy.GetValue(control), "Test did not restore editing state.");
            Require(status.Text.Contains("定位成功"), "Current-image matching failed: " + status.Text);
            string successText = (string)hud.GetValue(canvas);
            foreach (string label in new[] { "定位成功", "分数", "X ", "Y ", "角度", "缩放", "耗时" })
                Require(successText.Contains(label), "HUD is missing " + label);
            RequireHudAccent(canvas, Color.FromArgb(94, 234, 170));
            typeof(Control).GetMethod("OnMouseWheel", flags).Invoke(canvas,
                new object[] { new MouseEventArgs(MouseButtons.None, 0, 300, 200, 120) });
            canvas.InteractionMode = CanvasInteractionMode.Pan;
            Drag(canvas, new PointF(300, 220), new PointF(330, 240));
            RequireHudAccent(canvas, Color.FromArgb(94, 234, 170));
            Require((string)hud.GetValue(canvas) == successText, "Zoom/pan changed HUD contents.");
            canvas.FitToWindow();
            Application.DoEvents();
            using (var screenshot = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                screenshot.Save(screenshotPath, ImageFormat.Png);
            }
            using (var blank = new Bitmap(640, 480))
            {
                control.LoadImage(blank);
                Require(hud.GetValue(canvas) == null, "Loading an image left a stale HUD.");
                tested = false;
                test.Invoke(control, new object[] { null, EventArgs.Empty });
                WaitFor(() => tested, "Blank-image inspection did not complete.");
                string failureText = (string)hud.GetValue(canvas);
                Require(failureText.Contains("定位失败") && failureText.Contains("耗时") && !failureText.Contains("定位成功"),
                    "Failed inspection did not replace the successful HUD.");
                RequireHudAccent(canvas, Color.FromArgb(255, 120, 120));
                tested = false;
                test.Invoke(control, new object[] { null, EventArgs.Empty });
                Require(hud.GetValue(canvas) == null, "Starting another test left the previous result visible.");
                WaitFor(() => tested, "Repeated inspection did not complete.");
                ((NumericUpDown)type.GetField("numEdgeLow", flags).GetValue(control)).Value += 1;
                Require(hud.GetValue(canvas) == null, "Invalidating the template left a stale HUD.");
            }
            form.Close();
            return "PASS: current-image test, success/failure HUD, fixed zoom/pan position and stale-result clearing";
        }
    }

    private static void RequireHudAccent(MaskImageCanvas canvas, Color expected)
    {
        using (var surface = new Bitmap(canvas.Width, canvas.Height))
        {
            canvas.DrawToBitmap(surface, canvas.ClientRectangle);
            Require(surface.GetPixel(13, 13).ToArgb() == expected.ToArgb(),
                "HUD status accent was not rendered at the fixed upper-left position.");
        }
    }

    private static void WaitFor(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Application.DoEvents();
            if (DateTime.UtcNow >= deadline) throw new InvalidOperationException(message);
            System.Threading.Thread.Sleep(20);
        }
    }

    public static string MaskedModelAndPolarity()
    {
        using (var image = Image(Polygon(320, 240, 0, 1)))
        {
            var locator = new HalconMarkLocator();
            var job = new MarkLocateJob { TemplateRoiShape = RegionShape.Rectangle,
                TemplateRoi = new RectangleF(210, 130, 220, 220) };
            job.Parameters.FeatureMinDistancePx = 1;
            job.Parameters.MinContrast = 5;
            job.Parameters.MinScore = 0.3;
            job.Parameters.UsePolarity = false;
            job.TeachData.FeaturePoints = locator.ExtractFeaturePoints(image, job);
            int count = job.TeachData.FeaturePoints.Count;
            job.MaskStrokes.Add(new MaskStroke { Shape = RegionShape.Rectangle,
                Center = new PointF(280, 174), Width = 60, Height = 20, Mode = MaskStrokeMode.Exclude });
            job.TeachData = locator.Teach(image, job);
            Require(job.TeachData.FeaturePoints.Count < count, "Teaching ignored mask on cached unfiltered features.");
            foreach (var point in job.TeachData.FeaturePoints)
                Require(float.IsNaN(point.X) || !new RectangleF(250, 164, 60, 20).Contains(point),
                    "Excluded feature survived in taught model.");
            var result = locator.Locate(image, job);
            Require(result.Success, "Global-polarity XLD matching failed: " + result.Message);
            Require(Math.Abs(result.ModelPoint.X - 320) < 1 && Math.Abs(result.ModelPoint.Y - 240) < 1,
                "Masked model origin shifted.");
            return "PASS: masks applied to cached features, global polarity recovered from source, stable masked origin";
        }
    }

    public static string LayoutAndRoiInteraction(string screenshotPath, Type demoType)
    {
        using (var form = (Form)Activator.CreateInstance(demoType))
        using (var image = Image(Polygon(320, 240, 0, 1)))
        {
            Require(form.WindowState == FormWindowState.Normal, "Demo must open as a normal window.");
            var control = (MarkSetupControl)form.Controls[0];
            form.Show();
            Application.DoEvents();
            control.LoadImage(image);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(MarkSetupControl);
            var canvas = (MaskImageCanvas)type.GetField("canvas", flags).GetValue(control);
            var toolbar = (ToolStrip)type.GetField("toolMain", flags).GetValue(control);
            int imageEntries = 0;
            foreach (ToolStripItem item in toolbar.Items)
                if (item.Text.Contains("加载") || item.Text.Contains("载入")) imageEntries++;
            Require(imageEntries == 1, "Toolbar must contain exactly one image-loading entry.");
            foreach (string field in new[] { "numEdgeLow", "numEdgeHigh", "numEdgeSigma", "numMaxFeaturePoints", "numGreediness" })
                Require(((Control)type.GetField(field, flags).GetValue(control)).Visible, field + " is hidden.");
            ((ToolStripButton)type.GetField("btnTemplateRoi", flags).GetValue(control)).PerformClick();
            Require(canvas.TemplateRoi.IsEmpty, "Template ROI appeared before mouse drawing.");
            canvas.TemplateRoiShape = RegionShape.Rectangle;
            canvas.ActualSize();
            Drag(canvas, new PointF(210, 130), new PointF(430, 350));
            Require(canvas.TemplateRoi.Width == 220 && canvas.TemplateRoi.Height == 220, "Hand-drawn template geometry incorrect.");
            typeof(Control).GetMethod("OnMouseWheel", flags).Invoke(canvas,
                new object[] { new MouseEventArgs(MouseButtons.None, 0, 320, 240, 120) });
            var templateZoom = canvas.ZoomFactor;
            var viewportOrigin = canvas.ScreenToImage(Point.Empty);
            Require(Math.Abs(templateZoom - 1f) > 0.01f, "Regression must resize a template at non-1:1 zoom.");
            Drag(canvas, new PointF(430, 350), new PointF(445, 365));
            Require(canvas.TemplateRoi.Width > 230, "Template resize did not reach feature refresh.");
            Require(canvas.ZoomFactor == templateZoom && canvas.ScreenToImage(Point.Empty) == viewportOrigin,
                "Template ROI resize reset the image zoom or pan.");
            control.ShowTeachingImage();
            Require(canvas.ZoomFactor == templateZoom && canvas.ScreenToImage(Point.Empty) == viewportOrigin,
                "Refreshing the same template image reset the viewport.");
            ((ToolStripButton)type.GetField("btnSearchRoi", flags).GetValue(control)).PerformClick();
            canvas.ActualSize();
            Require(canvas.SearchRoi.IsEmpty, "Search ROI appeared before mouse drawing.");
            Drag(canvas, new PointF(100, 70), new PointF(500, 390));
            var before = canvas.SearchRoi;
            Drag(canvas, new PointF(300, 220), new PointF(320, 230));
            Require(canvas.SearchRoi.X == before.X + 20 && canvas.SearchRoi.Y == before.Y + 10,
                "Search ROI translation failed.");
            before = canvas.SearchRoi;
            Drag(canvas, new PointF(before.Right, before.Bottom), new PointF(before.Right + 30, before.Bottom + 20));
            Require(canvas.SearchRoi.Width == before.Width + 30 && canvas.SearchRoi.Height == before.Height + 20,
                "Search ROI handle resize failed.");
            var roi = canvas.SearchRoi;
            var zoom = canvas.ZoomFactor;
            typeof(Control).GetMethod("OnMouseWheel", flags).Invoke(canvas,
                new object[] { new MouseEventArgs(MouseButtons.None, 0, 300, 200, 120) });
            Require(canvas.ZoomFactor > zoom && canvas.SearchRoi == roi, "View zoom changed ROI image coordinates.");
            canvas.FitToWindow();
            Application.DoEvents();
            using (var screenshot = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                screenshot.Save(screenshotPath, ImageFormat.Png);
            }
            form.Close();
            return "PASS: normal window, single image button, visible thresholds, hand-drawn ROIs, translation, resize and preserved template zoom/pan";
        }
    }

    private static void Drag(MaskImageCanvas canvas, PointF start, PointF end)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var pan = (PointF)typeof(MaskImageCanvas).GetField("_pan", flags).GetValue(canvas);
        var a = Point.Round(new PointF(start.X * canvas.ZoomFactor + pan.X, start.Y * canvas.ZoomFactor + pan.Y));
        var b = Point.Round(new PointF(end.X * canvas.ZoomFactor + pan.X, end.Y * canvas.ZoomFactor + pan.Y));
        typeof(Control).GetMethod("OnMouseDown", flags).Invoke(canvas,
            new object[] { new MouseEventArgs(MouseButtons.Left, 1, a.X, a.Y, 0) });
        typeof(Control).GetMethod("OnMouseMove", flags).Invoke(canvas,
            new object[] { new MouseEventArgs(MouseButtons.Left, 0, b.X, b.Y, 0) });
        typeof(Control).GetMethod("OnMouseUp", flags).Invoke(canvas,
            new object[] { new MouseEventArgs(MouseButtons.Left, 1, b.X, b.Y, 0) });
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
            if (left[i] != right[i]) return false;
        return true;
    }

    public static string BrushMask()
    {
        using (var canvas = new MaskImageCanvas())
        using (var image = new Bitmap(240, 180))
        {
            canvas.Size = image.Size;
            canvas.SourceImage = image;
            canvas.TemplateRoiShape = RegionShape.Rectangle;
            canvas.TemplateRoi = new RectangleF(20, 20, 200, 140);
            var points = new List<PointF>();
            for (int x = 30; x <= 210; x += 10) points.Add(new PointF(x, 90));
            canvas.FeaturePoints = points;
            canvas.BrushRadius = 12;
            canvas.InteractionMode = CanvasInteractionMode.BrushExclude;
            canvas.CreateControl();
            canvas.PerformLayout();
            MethodInfo mouseDown = typeof(Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo mouseMove = typeof(Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo mouseUp = typeof(Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);
            mouseDown.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 35, 90, 0) });
            mouseMove.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 205, 90, 0) });
            mouseUp.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 205, 90, 0) });
            Require(canvas.MaskStrokes.Count > 3, "Continuous brush path was not sampled.");
            Require(canvas.VisibleFeaturePoints.Count == 0, "Brush did not cover the continuous path.");
            int count = canvas.MaskStrokes.Count;
            canvas.UndoMaskStroke();
            Require(canvas.MaskStrokes.Count == 0 && canvas.VisibleFeaturePoints.Count == points.Count,
                "Undo did not remove the complete brush stroke and restore points.");

            canvas.InteractionMode = CanvasInteractionMode.BrushExclude;
            mouseDown.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 35, 90, 0) });
            int partialCount = canvas.MaskStrokes.Count;
            canvas.InteractionMode = CanvasInteractionMode.Pan;
            mouseUp.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 205, 90, 0) });
            Require(!canvas.Capture && canvas.MaskStrokes.Count == partialCount,
                "Tool change left a captured partial stroke: captured=" + canvas.Capture + ", before=" + partialCount + ", after=" + canvas.MaskStrokes.Count);
            return "PASS: continuous brush sampling, point filtering, single-step undo and tool-change cancellation (" + count + " dabs)";
        }
    }

    public static string BrushRealtime(string screenshotPath)
    {
        using (var form = new Form())
        using (var control = new MarkSetupControl())
        using (var image = new Bitmap(800, 600))
        {
            using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.White);
            form.Size = new Size(1180, 808);
            control.Dock = DockStyle.Fill;
            form.Controls.Add(control);
            var points = new List<PointF>();
            for (int y = 50; y < 550; y += 5)
                for (int x = 50; x < 650; x += 5) points.Add(new PointF(x, y));
            points.Insert(120, new PointF(float.NaN, float.NaN));
            var job = new MarkLocateJob { TemplateRoiShape = RegionShape.Rectangle,
                TemplateRoi = new RectangleF(20, 20, 740, 560) };
            job.TeachData.HasModel = true;
            job.TeachData.FeaturePoints = points;
            for (int i = 0; i < 120; i++) job.MaskStrokes.Add(new MaskStroke {
                Center = new PointF(50 + i * 5, 70), Radius = 6, GroupId = i + 1 });
            control.LoadConfiguration(image, job);
            form.Show();
            Application.DoEvents();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var canvas = (MaskImageCanvas)typeof(MarkSetupControl).GetField("canvas", flags).GetValue(control);
            var internalJob = (MarkLocateJob)typeof(MarkSetupControl).GetField("_job", flags).GetValue(control);
            var down = typeof(Control).GetMethod("OnMouseDown", flags);
            var move = typeof(Control).GetMethod("OnMouseMove", flags);
            var up = typeof(Control).GetMethod("OnMouseUp", flags);
            canvas.ActualSize();
            canvas.InteractionMode = CanvasInteractionMode.BrushExclude;
            Application.DoEvents();
            var pan = (PointF)typeof(MaskImageCanvas).GetField("_pan", flags).GetValue(canvas);
            int ox = (int)pan.X, oy = (int)pan.Y;
            int changes = 0, paints = 0;
            control.JobChanged += (sender, args) => changes++;
            canvas.Paint += (sender, args) => paints++;
            down.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, ox + 100, oy + 300, 0) });
            Require(!internalJob.TeachData.HasModel, "Brush must invalidate the model before mouse-up.");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            double maxMilliseconds = 0;
            for (int i = 1; i <= 40; i++)
            {
                int previousPaints = paints;
                var step = System.Diagnostics.Stopwatch.StartNew();
                move.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 0, ox + 100 + i * 10, oy + 300, 0) });
                step.Stop();
                maxMilliseconds = Math.Max(maxMilliseconds, step.Elapsed.TotalMilliseconds);
                Require(paints > previousPaints, "Brush did not repaint while mouse messages were arriving.");
            }
            timer.Stop();
            Require(changes == 0, "Full configuration notifications must wait until the brush gesture ends.");
            Require(canvas.Capture && canvas.VisibleFeaturePoints.Count < points.Count - 100,
                "Feature exclusion was not visible before mouse-up.");
            using (var surface = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(surface, canvas.ClientRectangle);
                Color pixel = surface.GetPixel(ox + 252, oy + 303);
                Require(pixel.R > pixel.G + 30, "Brush path was not visible before mouse-up: " + pixel);
            }
            using (var screenshot = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                screenshot.Save(screenshotPath, ImageFormat.Png);
            }
            up.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, ox + 500, oy + 300, 0) });
            Require(changes == 1 && !canvas.Capture, "Brush completion must synchronize exactly once.");
            AssertBrushJob(control, points);

            canvas.InteractionMode = CanvasInteractionMode.BrushRestore;
            Drag(canvas, new PointF(100, 300), new PointF(500, 300));
            AssertBrushJob(control, points);
            control.ShowTestImage();
            Require(control.Job.TeachData.FeaturePoints.Count > 0,
                "Inspection preview must not clear the configured template features.");
            control.ShowTeachingImage();
            canvas.InteractionMode = CanvasInteractionMode.BrushExclude;
            down.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, ox + 200, oy + 350, 0) });
            int beforeLoss = changes;
            canvas.Capture = false;
            Require(changes == beforeLoss + 1, "Lost capture did not commit the partial brush gesture.");
            AssertBrushJob(control, points);
            down.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, ox + 250, oy + 350, 0) });
            int beforeSwitch = changes;
            canvas.InteractionMode = CanvasInteractionMode.Pan;
            Require(changes == beforeSwitch + 1, "Tool switch did not commit the partial brush gesture.");
            AssertBrushJob(control, points);
            canvas.UndoMaskStroke();
            AssertBrushJob(control, points);
            canvas.ClearMask();
            Require(control.Job.MaskStrokes.Count == 0, "Clear did not synchronize the configuration.");
            AssertBrushJob(control, points);
            Require(timer.Elapsed.TotalMilliseconds / 40 < 100,
                "Brush moves still block the UI: " + timer.Elapsed.TotalMilliseconds / 40 + " ms/move.");
            form.Close();
            return string.Format("PASS: live brush painting, restore, capture loss, tool switch, undo, clear and job sync; 12000 points + 120 historical dabs: avg {0:F2} ms/move, max {1:F2} ms",
                timer.Elapsed.TotalMilliseconds / 40, maxMilliseconds);
        }
    }

    private static void AssertBrushJob(MarkSetupControl control, List<PointF> rawPoints)
    {
        var job = control.Job;
        var filter = typeof(HalconMarkLocator).GetMethod("FilterFeaturePointsInRoi", BindingFlags.Static | BindingFlags.NonPublic);
        var expected = (List<PointF>)filter.Invoke(null, new object[] {
            rawPoints, job.MaskStrokes, job.TemplateRoi, job.TemplateRoiShape });
        var actual = job.TeachData.FeaturePoints;
        Require(actual.Count == expected.Count, "Incremental brush filtering differs from model filtering.");
        for (int i = 0; i < actual.Count; i++)
            Require((float.IsNaN(actual[i].X) && float.IsNaN(expected[i].X)) || actual[i] == expected[i],
                "Brush filtering changed point order or contour separators.");
    }

    public static string CursorRepaint()
    {
        using (var canvas = new MaskImageCanvas())
        using (var image = new Bitmap(100, 80))
        using (var surface = new Bitmap(150, 120, PixelFormat.Format32bppArgb))
        {
            canvas.Size = new Size(150, 120);
            canvas.SourceImage = image;
            canvas.TemplateRoiShape = RegionShape.Rectangle;
            canvas.TemplateRoi = new RectangleF(10, 10, 80, 60);
            canvas.MaskShape = RegionShape.Rectangle;
            canvas.BrushRadius = 18;
            canvas.InteractionMode = CanvasInteractionMode.RestoreMask;
            canvas.CreateControl();
            MethodInfo move = typeof(Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo paint = typeof(Control).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
            move.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.None, 0, 0, 0, 0) });
            using (var g = Graphics.FromImage(surface))
            using (var e = new PaintEventArgs(g, canvas.ClientRectangle)) paint.Invoke(canvas, new object[] { e });
            Color initial = surface.GetPixel(0, 0);
            Require(initial.G > initial.R, "Restore cursor did not render at the image boundary.");
            move.Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.None, 0, 75, 60, 0) });
            using (var g = Graphics.FromImage(surface))
            using (var e = new PaintEventArgs(g, new Rectangle(0, 0, 100, 80))) paint.Invoke(canvas, new object[] { e });
            Require(surface.GetPixel(0, 0).G == initial.G && surface.GetPixel(0, 0).R == initial.R,
                "Old cursor pixels were not repainted.");
            return "PASS: cursor refresh covers its old boundary area";
        }
    }

    public static string CircleRois()
    {
        var cases = new[] { new RectangleF(100, 100, 120, 60),
            new RectangleF(600, 430, 40, 50), new RectangleF(0, 0, 640, 480), RectangleF.Empty };
        using (var canvas = new MaskImageCanvas())
        using (var image = new Bitmap(640, 480))
        {
            canvas.SourceImage = image;
            foreach (var roi in cases)
            {
                var job = new MarkLocateJob { TemplateRoi = roi };
                job.Normalize(image.Size);
                var normalized = job.TemplateRoi;
                Require(normalized.Width == normalized.Height, "Circle is not square.");
                Require(normalized.Left >= 0 && normalized.Top >= 0 &&
                    normalized.Right <= image.Width && normalized.Bottom <= image.Height, "Circle exceeds image.");
                job.Normalize(image.Size);
                Require(job.TemplateRoi == normalized, "Normalization must be idempotent.");
                canvas.TemplateRoi = normalized;
                Require(canvas.TemplateRoi == normalized, "Canvas and normalized job disagree.");
            }
            canvas.TemplateRoi = new RectangleF(0, 0, 640, 480);
            Require(canvas.TemplateRoi.Width == canvas.TemplateRoi.Height, "Oversized canvas circle became an ellipse.");
        }
        var rectangular = new MarkLocateJob { TemplateRoiShape = RegionShape.Rectangle,
            TemplateRoi = new RectangleF(100, 100, 120, 60) };
        rectangular.Normalize(new Size(640, 480));
        Require(rectangular.TemplateRoi == new RectangleF(100, 100, 120, 60), "Rectangle changed unexpectedly.");
        var circular = new MarkLocateJob { TemplateRoi = rectangular.TemplateRoi };
        circular.Normalize(new Size(640, 480));
        var geometry = typeof(MarkLocateJob).Assembly.GetType("Seprays.MarkVision.RegionShapeGeometry");
        Require((bool)geometry.GetMethod("Contains").Invoke(null, new object[] {
            new PointF(160, 80), circular.TemplateRoi, RegionShape.Circle }), "Valid circle feature was excluded.");
        return "PASS: circle feature inclusion, image boundaries, idempotence and rectangle preservation";
    }

    private static PointF[] Polygon(double cx, double cy, double degrees, double scale)
    {
        double angle = degrees * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        var points = new PointF[Vertices.Length];
        for (int i = 0; i < points.Length; i++)
        {
            double x = Vertices[i].X * scale, y = Vertices[i].Y * scale;
            points[i] = new PointF((float)(cx + x * c - y * s), (float)(cy + x * s + y * c));
        }
        return points;
    }

    private static Bitmap Image(PointF[] polygon)
    {
        var image = new Bitmap(640, 480);
        using (var g = Graphics.FromImage(image))
        using (var pen = new Pen(Color.Black, 4))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(225, 225, 225));
            g.FillPolygon(Brushes.White, polygon);
            g.DrawPolygon(pen, polygon);
        }
        return image;
    }

    private static double EdgeDistance(IEnumerable<List<PointF>> contours, PointF[] polygon)
    {
        double sum = 0; int count = 0;
        foreach (var contour in contours) foreach (var p in contour)
        {
            double nearest = double.PositiveInfinity;
            for (int i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy)));
                double ex = p.X - a.X - t * dx, ey = p.Y - a.Y - t * dy;
                nearest = Math.Min(nearest, Math.Sqrt(ex * ex + ey * ey));
            }
            sum += nearest; count++;
        }
        Require(count > 0, "No matched contours.");
        return sum / count;
    }

    public static string Rotation(double degrees, double scale)
    {
        var polygon = Polygon(363, 213, degrees, scale);
        using (var teach = Image(Polygon(320, 240, 0, 1)))
        using (var runtime = Image(polygon))
        {
            var locator = new HalconMarkLocator();
            var job = new MarkLocateJob { TemplateRoi = new RectangleF(210, 130, 220, 220),
                TemplateRoiShape = RegionShape.Rectangle, SearchRoiEnabled = false };
            job.Parameters.AngleStartDegrees = -30;
            job.Parameters.AngleExtentDegrees = 60;
            job.Parameters.ScaleMin = 0.9; job.Parameters.ScaleMax = 1.1;
            job.Parameters.MinScore = 0.3; job.Parameters.MinContrast = 5;
            job.Parameters.FeatureMinDistancePx = 1;
            job.TeachData = locator.Teach(teach, job);
            var result = locator.Locate(runtime, job);
            Require(result.Success, "Matching failed: " + result.Message);
            double dx = result.ModelPoint.X - 363, dy = result.ModelPoint.Y - 213;
            double positionError = Math.Sqrt(dx * dx + dy * dy);
            double distance = EdgeDistance(result.MatchedContours, polygon);
            Require(positionError < 1, "Center error exceeds 1px: " + positionError);
            Require(distance < 4, "Contour edge distance exceeds 4px: " + distance);
            Require(Math.Abs(result.AngleRadians * 180 / Math.PI + degrees) < 0.2, "Returned angle changed convention.");
            return string.Format("PASS: rotation {0} deg, scale {1}, center error {2:F3}px, contour distance {3:F3}px",
                degrees, scale, positionError, distance);
        }
    }
}

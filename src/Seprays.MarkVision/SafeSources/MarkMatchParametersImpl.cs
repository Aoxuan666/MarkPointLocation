using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace Seprays.MarkVision
{
	[Serializable]
	[TypeConverter(typeof(ExpandableObjectConverter))]
	public sealed class MarkMatchParameters
	{
		[Category("模板预处理")]
		[DisplayName("模板均值滤波")]
		[DefaultValue(1)]
		public int TemplateMeanSize { get; set; } = 1;

		[Category("模板预处理")]
		[DisplayName("运行均值滤波")]
		[DefaultValue(1)]
		public int SearchMeanSize { get; set; } = 1;

		[Category("特征提取")]
		[DisplayName("边缘 Sigma")]
		[DefaultValue(1.2)]
		[field: OptionalField(VersionAdded = 2)]
		public double EdgeSigma { get; set; } = 1.2;

		[Category("特征提取")]
		[DisplayName("自动阈值")]
		[DefaultValue(true)]
		[field: OptionalField(VersionAdded = 4)]
		public bool AutoEdgeThreshold { get; set; } = true;

		[Category("特征提取")]
		[DisplayName("低阈值")]
		[DefaultValue(15.0)]
		[field: OptionalField(VersionAdded = 2)]
		public double EdgeLowThreshold { get; set; } = 15.0;

		[Category("特征提取")]
		[DisplayName("高阈值")]
		[DefaultValue(35.0)]
		[field: OptionalField(VersionAdded = 2)]
		public double EdgeHighThreshold { get; set; } = 35.0;

		[Category("特征提取")]
		[DisplayName("点最小间距(px)")]
		[DefaultValue(2.0)]
		[field: OptionalField(VersionAdded = 2)]
		public double FeatureMinDistancePx { get; set; } = 2.0;

		[Category("特征提取")]
		[DisplayName("最大特征点数")]
		[DefaultValue(12000)]
		[field: OptionalField(VersionAdded = 2)]
		public int MaxFeaturePoints { get; set; } = 12000;

		[Category("模型创建")]
		[DisplayName("金字塔层数")]
		[DefaultValue(0)]
		[Description("0 表示由 HALCON 自动确定。")]
		public int NumLevels { get; set; }

		[Category("模型创建")]
		[DisplayName("对比度")]
		[DefaultValue(30)]
		public int Contrast { get; set; } = 30;

		[Category("模型创建")]
		[DisplayName("最小对比度")]
		[DefaultValue(10)]
		public int MinContrast { get; set; } = 10;

		[Category("模型创建")]
		[DisplayName("使用极性")]
		[DefaultValue(true)]
		public bool UsePolarity { get; set; } = true;

		[Category("搜索范围")]
		[DisplayName("起始角度(度)")]
		[DefaultValue(-5.0)]
		public double AngleStartDegrees { get; set; } = -5.0;

		[Category("搜索范围")]
		[DisplayName("角度范围(度)")]
		[DefaultValue(10.0)]
		public double AngleExtentDegrees { get; set; } = 10.0;

		[Category("搜索范围")]
		[DisplayName("最小缩放")]
		[DefaultValue(0.95)]
		public double ScaleMin { get; set; } = 0.95;

		[Category("搜索范围")]
		[DisplayName("最大缩放")]
		[DefaultValue(1.05)]
		public double ScaleMax { get; set; } = 1.05;

		[Category("匹配判定")]
		[DisplayName("最小分数")]
		[DefaultValue(0.70)]
		public double MinScore { get; set; } = 0.70;

		[Category("匹配判定")]
		[DisplayName("最大重叠")]
		[DefaultValue(0.0)]
		public double MaxOverlap { get; set; }

		[Category("匹配判定")]
		[DisplayName("贪婪度")]
		[DefaultValue(0.5)]
		public double Greediness { get; set; } = 0.5;

		[Category("掩膜工具")]
		[DisplayName("画笔半径(px)")]
		[DefaultValue(6.0)]
		public double BrushRadius { get; set; } = 6.0;

		[Category("圆心精修")]
		[DisplayName("启用圆心精修")]
		[DefaultValue(false)]
		[Description("对应原 Template_Circle 流程，在形状匹配后通过 HALCON 测量稳健拟合圆。")]
		public bool EnableCircleRefinement { get; set; }

		[Category("圆心精修")]
		[DisplayName("精修失败则定位失败")]
		[DefaultValue(false)]
		public bool RequireCircleRefinement { get; set; }

		[Category("圆心精修")]
		[DisplayName("示教圆半径(px)")]
		[DefaultValue(0.0)]
		public double ExpectedCircleRadius { get; set; }

		[Category("圆心精修")]
		[DisplayName("径向搜索比例")]
		[DefaultValue(0.3)]
		public double CircleRadialTolerance { get; set; } = 0.3;

		[Category("圆心精修")]
		[DisplayName("最小边缘梯度")]
		[DefaultValue(8.0)]
		public double CircleEdgeThreshold { get; set; } = 8.0;

		[Category("圆心精修")]
		[DisplayName("射线数量")]
		[DefaultValue(96)]
		public int CircleSpokeCount { get; set; } = 96;

		[Category("圆心精修")]
		[DisplayName("最大圆心偏移比例")]
		[DefaultValue(0.6)]
		public double CircleMaxCenterShiftRatio { get; set; } = 0.6;

		public MarkMatchParameters DeepClone()
		{
			return (MarkMatchParameters)MemberwiseClone();
		}

		public void Normalize()
		{
			TemplateMeanSize = Clamp(TemplateMeanSize, 1, 99);
			SearchMeanSize = Clamp(SearchMeanSize, 1, 99);
			EdgeSigma = Clamp(EdgeSigma, 0.2, 10.0);
			EdgeLowThreshold = Clamp(EdgeLowThreshold, 1.0, 254.0);
			EdgeHighThreshold = Clamp(EdgeHighThreshold, EdgeLowThreshold + 1.0, 255.0);
			FeatureMinDistancePx = Clamp(FeatureMinDistancePx, 0.5, 80.0);
			MaxFeaturePoints = Clamp(MaxFeaturePoints, 100, 50000);
			NumLevels = Clamp(NumLevels, 0, 12);
			Contrast = Clamp(Contrast, 1, 255);
			MinContrast = Clamp(MinContrast, 1, 255);
			AngleStartDegrees = Clamp(AngleStartDegrees, -360.0, 360.0);
			AngleExtentDegrees = Clamp(AngleExtentDegrees, 0.1, 360.0);
			ScaleMin = Clamp(ScaleMin, 0.1, 4.0);
			ScaleMax = Clamp(ScaleMax, ScaleMin, 4.0);
			MinScore = Clamp(MinScore, 0.01, 1.0);
			MaxOverlap = Clamp(MaxOverlap, 0.0, 1.0);
			Greediness = Clamp(Greediness, 0.0, 1.0);
			BrushRadius = Clamp(BrushRadius, 1.0, 500.0);
			ExpectedCircleRadius = Clamp(ExpectedCircleRadius, 0.0, 10000.0);
			CircleRadialTolerance = Clamp(CircleRadialTolerance, 0.05, 0.8);
			CircleEdgeThreshold = Clamp(CircleEdgeThreshold, 1.0, 255.0);
			CircleSpokeCount = Clamp(CircleSpokeCount, 24, 720);
			CircleMaxCenterShiftRatio = Clamp(CircleMaxCenterShiftRatio, 0.05, 1.5);
		}

		public override string ToString()
		{
			return $"Score {MinScore:F2}, Scale {ScaleMin:F2}-{ScaleMax:F2}";
		}

		private static int Clamp(int value, int min, int max)
		{
			return Math.Max(min, Math.Min(max, value));
		}

		private static double Clamp(double value, double min, double max)
		{
			return Math.Max(min, Math.Min(max, value));
		}
	}
}

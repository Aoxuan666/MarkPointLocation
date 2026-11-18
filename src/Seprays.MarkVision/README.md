# Seprays.MarkVision

`Seprays.MarkVision` 是供上位机引用的 .NET Framework 4.7.2 x64 类库。它保留原有 HALCON 缩放形状匹配流程，并增加可编辑的外轮廓特征点模板。

## 检测流程

1. 模板图均值滤波，在模板 ROI 内执行 `EdgesSubPix`。
2. 将边缘轮廓采样为离散特征点并立即显示。
3. 按笔刷顺序擦除或恢复特征点。
4. 使用有效特征点调用 `CreateScaledShapeModelXld`。
5. 将 Shape Model 和特征点保存到 `MarkLocateJob.TeachData`。
6. 运行图均值滤波；搜索 ROI 关闭时使用全图，开启时限制到指定区域。
7. 反序列化模型后调用 `FindScaledShapeModel`。

掩膜只编辑模板特征点。运行时不需要在待测图上重新绘制掩膜，因为被擦除点没有写入模型。

## 对话框接入

```csharp
using Seprays.MarkVision;

MarkLocatorSdk sdk = new MarkLocatorSdk();
MarkLocateJob edited = sdk.OpenSetupDialog(cameraBitmap, savedJob);
if (edited != null)
{
    savedJob = edited;
}

MarkLocateResult result = sdk.RunInspection(cameraBitmap, savedJob, returnResultImage: true);
try
{
    if (result.ResultImage != null)
    {
        result.ResultImage.Save(resultPath);
    }
}
finally
{
    result.ResultImage?.Dispose();
    result.ResultImage = null;
}
```

`MarkLocatorSdk` 只公开 `OpenSetupDialog` 和 `RunInspection` 两个无重载方法。SDK 不释放上位机传入的图像；返回的非空 `ResultImage` 由上位机负责释放。

`RunInspection` 的 `returnResultImage` 默认值为 `false`，设为 `true` 时在独立图像上绘制匹配轮廓、中心点及找圆结果。设置窗口底部的“确认”按钮返回完成 Job 的深克隆；“取消”或关闭窗口返回 `null`。

`OpenSetupDialog` 会自动在 STA UI 线程创建设置窗口；上位机从后台线程调用时无需额外切换线程。

## 页面操作

- 鼠标滚轮：以光标位置为中心缩放；中键拖动：任何模式下平移图像。页面不再提供单独的“平移”按钮。
- 模板框：可选择矩形、圆形或菱形（默认矩形）。矩形使用八个控制点；圆形从圆心向外拖拽创建并使用四个半径控制点等比缩放；菱形使用四个顶点；拖动区域内部可整体移动。
- 搜索框默认按模板 ROI 外扩固定 `100px`，页面不提供边间距参数。
- 区域掩膜：可选择圆形、矩形或菱形（默认圆形），拖拽绘制排除/恢复区域；允许从模板外开始拖入，最终只对模板 ROI 内的特征点生效。
- 模板中心：保持为模板 ROI 的几何中心，示教时使用占满 ROI 的十字线显示。
- 擦除点/恢复点：直接编辑橙色外轮廓特征点，默认笔刷半径为 `18px`。
- 橙色点：当前写入 Shape Model 的模板特征点。
- 蓝色点：测试定位后的匹配特征点。

模板匹配和找圆由上位机通过 `MarkLocateJob.EnableTemplateMatching`、`MarkLocateJob.EnableCircleDetection` 控制，设置页不再提供开关。

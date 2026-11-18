# MarkPointLocation

本解决方案为上位机提供带可编辑掩膜的 Mark 模板定位流程。

- `src/Seprays.MarkVision`：可交付类库、HALCON 算法、可嵌入设置控件和 SDK。
- `demo/Seprays.MarkVision.Demo`：启动带“确认/取消”按钮的 `MarkSetupForm` 页面入口。
- `docs/上位机接入说明.md`：新旧流程映射与部署说明。
- `docs/掩膜匹配算法核对.md`：与 OpenCV LINEMOD、shape_based_matching 的算法核对。

页面以普通窗口启动。加载图片 → 添加模板 ROI 并手绘 → 调整特征参数/掩膜 → 创建模型；之后通过同一个“加载图片”入口换图即可测试，搜索 ROI 可独立添加、移动和缩放，保留模板缓存。

上位机 SDK 提供 `OpenSetupDialog(image, job)` 和 `RunInspection(image, job, returnResultImage)` 两个入口。设置完成后点击“确认”返回 Job；检测时传 `true` 返回独立效果图 `ResultImage`，用完由上位机释放。

当前实际编译源码位于 `src/Seprays.MarkVision/SafeSources`，以 `.csproj` 的 Compile 项为准。

构建：

```powershell
dotnet build MarkPointLocation.sln -c Release -p:Platform=x64
```

Mark 设置页面：

```powershell
.\demo\Seprays.MarkVision.Demo\bin\Release\Seprays.MarkVision.Demo.exe
```

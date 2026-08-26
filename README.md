# MarkPointLocation

本解决方案为上位机提供带可编辑掩膜的 Mark 模板定位流程。

- `src/Seprays.MarkVision`：可交付类库、HALCON 算法、可嵌入设置控件和 SDK。
- `demo/Seprays.MarkVision.Demo`：模拟上位机调用的 WinForms 测试宿主。
- `docs/上位机接入说明.md`：新旧流程映射与部署说明。

构建：

```powershell
dotnet build MarkPointLocation.sln -c Release -p:Platform=x64
```

测试宿主：

```powershell
.\demo\Seprays.MarkVision.Demo\bin\Release\Seprays.MarkVision.Demo.exe
```

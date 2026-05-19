# 桌面图标显示隐藏器

一个 Windows 后台小程序。启动后常驻系统托盘，在桌面任意位置双击鼠标左键即可切换桌面图标的显示和隐藏。

## 功能

- 双击桌面隐藏桌面图标
- 再次双击桌面恢复显示桌面图标
- 当前用户开机自动启动
- 系统托盘右键菜单支持退出
- 自定义托盘图标和程序图标

## 文件说明

- `切换桌面图标.exe`：主程序，已配置为开机自启动目标
- `桌面双击隐藏图标.exe`：同功能备用程序名
- `ToggleDesktopIcons.cs`：主程序源码
- `DesktopToggle.ico`：自定义图标
- `GenerateIcon.cs`：图标生成源码
- `使用说明.txt`：中文使用说明

## 编译

使用 Windows 自带 .NET Framework C# 编译器：

```powershell
$folder = "C:\Users\KK19312\Desktop\桌面图标显示隐藏器"
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

& $csc /nologo /target:winexe /platform:x64 /optimize+ `
  /reference:System.Windows.Forms.dll `
  /reference:System.Drawing.dll `
  "/win32icon:$folder\DesktopToggle.ico" `
  "/out:$folder\切换桌面图标.exe" `
  "$folder\ToggleDesktopIcons.cs"
```

## 开机自启动

当前用户启动项快捷方式：

```text
C:\Users\KK19312\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\桌面双击隐藏图标.lnk
```

该快捷方式指向：

```text
C:\Users\KK19312\Desktop\桌面图标显示隐藏器\切换桌面图标.exe
```

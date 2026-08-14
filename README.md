# Auto Color

一个适用于 Windows 11 的轻量级自动明暗主题切换器。程序仅保留通知区域图标，并使用一次性计时器在下一次切换时才唤醒，避免轮询和联网请求。

## 功能

- 按自定义的日间、夜间开始时间切换 Windows 的应用和系统主题。
- 可跟随本地离线计算的日出/日落：填写经纬度即可，无需定位权限或网络连接。
- 可选开机自启动（仅当前用户，写入 `HKCU\\...\\Run`）。
- 托盘菜单支持立即切换、打开设置与退出；双击图标也可打开设置。

## 构建与运行

在 PowerShell 执行：

```powershell
.\build.ps1
.\dist\AutoColor.exe
```

程序只使用 Windows 自带的 .NET Framework WinForms 组件，无第三方依赖。配置保存于 `%LOCALAPPDATA%\\AutoColor\\settings.ini`；删除该文件即可恢复默认设置（上海经纬度以及 07:00 / 19:00）。

## 开发说明

主题由 `AppsUseLightTheme` 和 `SystemUsesLightTheme` 两个当前用户注册表值控制。日出日落使用 NOAA 近似太阳位置算法；极昼、极夜时回退为 06:00/18:00。

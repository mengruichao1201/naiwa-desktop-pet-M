# 奶娃桌宠（音效版）

一个适用于 Windows 的透明桌面宠物。点击肚子会播放捧腹动画和同目录的笑声音频；其余部位各有简单互动。

## 运行

双击 `启动奶娃桌宠.bat` 或 `NaiwaPet.exe`。程序启动后会在通知区域显示图标。

- 点击头部：歪头
- 点击肚子：播放捧腹动画和笑声
- 点击手部：摇摆
- 点击脚部：弹一下
- 鼠标滚轮：调整大小
- 按住并拖动：移动桌宠

关闭窗口会将桌宠隐藏到通知区域；在通知图标菜单中可退出程序。

## 从源码编译

需要 64 位 Windows 和 .NET Framework 4.x（包含 WPF）。在此目录运行 PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

脚本会将 `src\NaiwaPetSprite.cs` 与 `src\laugh_frames\` 下的 92 张动画帧编译为根目录的 `NaiwaPet.exe`。笑声音频由程序从 EXE 同目录加载，因此运行时需要保留 `laugh.mp3`。

## 项目文件

- `NaiwaPet.exe`：可直接运行的音效版
- `laugh.mp3`：肚子互动时播放的音频
- `奶娃透明立绘.png`：待机角色图片
- `src\NaiwaPetSprite.cs`：桌宠源码
- `src\laugh_frames\`：捧腹动画帧
- `build.ps1`：源码编译脚本
- `启动奶娃桌宠.bat`：启动脚本


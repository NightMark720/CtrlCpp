# CtrlC++ 剪贴板历史

一款运行在 Windows 10 / Windows 11（x64）的轻量级剪贴板历史工具。后台常驻托盘，随时找回近期复制的文字、链接、图片和文件路径。

## 下载与安装

在仓库的 **Releases** 页面下载 `CtrlCpp-Setup-1.0.0.exe`，双击后按中文安装向导操作即可。

- 无需另外安装 .NET 运行时
- 安装到当前用户目录，无需管理员权限
- 默认创建桌面快捷方式并启用开机启动（可在设置中关闭）
- 首次运行若出现 SmartScreen 提示，选择“更多信息”→“仍要运行”

## 使用方法

- 默认按 **Ctrl + 空格** 打开或关闭历史窗口，可在设置中录制新的快捷键
- 单击一条记录或按 **Enter**：将该内容写回剪贴板并自动粘贴到之前使用的程序
- 顶部搜索框：筛选文字和链接
- **Delete** 或右键菜单：删除选中的记录
- **Esc** 或点击窗口外部：关闭历史窗口
- 托盘图标：左键打开历史，右键选择 **设置 / 历史记录 / 退出**

## 功能

- 记录文字、URL、图片及文件路径，相同内容去重并置顶
- 默认保留 3 天，可在 1～30 天范围内调整
- 默认缓存上限 3 GB，超限后从最旧记录开始清理；支持一键清空
- 支持排除指定应用，不记录这些应用的复制内容
- 可自定义全局快捷键（普通键需搭配 Ctrl 或 Alt，F1～F12 可单独使用）
- 深色卡片式 UI、图片缩略图、文件类型图标、来源与相对时间显示
- 单实例运行、开机自启动、原子设置保存、日志记录

## 数据与隐私

剪贴板历史仅保存在本机：

```text
%LOCALAPPDATA%\CtrlCpp\
```

程序不上传剪贴板内容。复制的文件只记录路径，不复制文件本体。

## 已知限制

- 在管理员权限的程序窗口中，快捷键可能无法触发（Windows UIPI 安全机制）
- 若快捷键与其他软件或输入法冲突，本程序会优先占用该组合键
- 未进行商业代码签名，个别安全软件可能提示未知发布者

## 从源码构建

环境要求：Windows 10/11 x64、.NET 10 SDK、PowerShell。

```powershell
dotnet publish src/CtrlCpp/CtrlCpp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
```

也可以使用项目中的脚本（SDK 位于用户目录时会自动查找）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
```

### 生成安装包

安装 [Inno Setup](https://jrsoftware.org/isinfo.php) 后运行：

```powershell
ISCC.exe installer/setup.iss
```

安装包输出到 `installer/CtrlCpp-Setup-1.0.0.exe`。仓库不提交大型二进制构建产物，正式安装包随 GitHub Release 分发。

## 技术栈

- C# / .NET 10 / WPF
- WPF-UI（Fluent 控件）
- H.NotifyIcon.Wpf（托盘）
- Microsoft.Data.Sqlite（历史索引）
- Win32 API（剪贴板监听、低级键盘钩子、窗口焦点与 SendInput）

## 主要目录

```text
src/CtrlCpp/          应用源码、XAML 视图、图标资源
installer/setup.iss   Inno Setup 安装包脚本
scripts/build.ps1     发布脚本
scripts/make-icon.ps1 图标生成脚本
scripts/dev/          本地自动化测试辅助脚本
README-使用说明.txt    面向最终用户的中文说明
```

本仓库不包含开放源代码许可证；未另行授权前，保留所有权利。

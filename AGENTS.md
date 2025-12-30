# 开发环境提示
* Windows 环境，默认使用 GBK 编码（可使用 chcp 65001 切换至 UTF-8）
* 可以通过 `"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"` 找到 VS 安装路径，然后调用 `call "安装路径\VC\Auxiliary\Build\vcvarsall.bat" x64` 设置环境变量
* 设置环境变量后可以使用 msbuild 来构建工程
* 项目使用的是 .net framework 而不是 .net core
* Mile.Xaml 是基于 XAML Island 的，而不是 Windows App SDK
# 项目说明
* 当前项目是一个叫做 MicroWinUI 的脚手架工程，MainPage 是从 UWP Gallery 拷贝过来的，可以删除。
* 创建 PLANS.md 文件来记录项目计划和进度
# 需求
* 实现打开 FP16 HDR JXR 图片，并可以和 InkCanvas 上修改并支持另存为 FP16 HDR JXR 图片
# 限制
* 尽可能使用 UWP API，不要使用 Windows API，确保其可移植性
* 禁止使用 Windows App SDK 或者 WinUI 3
* 已经引入 Microsoft.Graphics.WinUI 1.0.1 版本，可以 Microsoft.Graphics.Canvas 命名空间来使用 Win2D
# Agent 重要事项
* **所有回答和计划必须使用简体中文编写**
* **如果可能，将思维链语言也设置为简体中文**
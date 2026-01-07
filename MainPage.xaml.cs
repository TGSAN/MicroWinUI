using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas;
using MicroWinUICore;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;
using Windows.Graphics.Display;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using System.Numerics;
using Windows.UI.Xaml.Media;
using Windows.Storage.Streams;

namespace MicroWinUI
{
    [ComImport]
    [Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithWindow
    {
        void Initialize(IntPtr hwnd);
    }

    public sealed partial class MainPage : Page
    {
        private IslandWindow coreWindowHost;
        private CanvasBitmap rawBitmap; // 用于保存的原始数据
        private bool _isHandMode = false;
        private Windows.Foundation.Point? _lastDragPoint;
        // 惯性滚动相关字段
        private Vector2 _velocity;
        private DateTime _lastMoveTime;
        private bool _isInertiaRendering;
        private const double Friction = 0.88;
        private const double VelocityThreshold = 0.1;

        public MainPage(IslandWindow coreWindowHost)
        {
            this.coreWindowHost = coreWindowHost;
            coreWindowHost.Backdrop = IslandWindow.SystemBackdrop.Tabbed;
            this.InitializeComponent();

            // 初始化 InkCanvas 支持的输入类型
            inkCanvas.InkPresenter.InputDeviceTypes = CoreInputDeviceTypes.Mouse | CoreInputDeviceTypes.Pen | CoreInputDeviceTypes.Touch;

            // 默认启用抓手模式
            Loaded += (s, e) => EnableHandMode();
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenImageAsync();
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveImageAsync();
        }

        private async Task OpenImageAsync()
        {
            try
            {
                var picker = new FileOpenPicker();

                // 初始化窗口句柄
                ((IInitializeWithWindow)(object)picker).Initialize(coreWindowHost.Handle);

                picker.ViewMode = PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".jxr");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".png");

                StorageFile file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    // 1. 用于显示的 BitmapImage (系统自动处理 HDR -> SDR 映射或直接 HDR 显示)
                    using (var stream = await file.OpenReadAsync())
                    {
                        var bitmapImage = new BitmapImage();
                        // 关键：忽略缓存，避免重复打开同一文件时属性不刷新
                        bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        await bitmapImage.SetSourceAsync(stream);

                        DisplayImage.Source = bitmapImage;

                        // 调整 InkCanvas 尺寸以匹配图片像素尺寸 (Image Stretch=None)
                        // 注意：如果 DisplayImage 进行了缩放(Zoom)，InkCanvas 应该放在 ScrollViewer 内部随之缩放，
                        // 这里我们设置 InkCanvas 的实际大小等于图片像素大小。
                        var displayInfo = DisplayInformation.GetForCurrentView();
                        double scaleFactor = displayInfo.RawPixelsPerViewPixel;

                        DisplayImage.Width = bitmapImage.PixelWidth / scaleFactor;
                        DisplayImage.Height = bitmapImage.PixelHeight / scaleFactor;
                        inkCanvas.Width = bitmapImage.PixelWidth / scaleFactor;
                        inkCanvas.Height = bitmapImage.PixelHeight / scaleFactor;
                    }

                    // 2. 用于保存的 CanvasBitmap (保留原始 FP16 数据)
                    // 需要重新打开流，因为之前的流已被 BitmapImage 占用
                    using (var stream = await file.OpenReadAsync())
                    {
                        var device = CanvasDevice.GetSharedDevice();
                        rawBitmap = await CanvasBitmap.LoadAsync(device, stream);

                        System.Diagnostics.Debug.WriteLine($"Image Loaded. Format: {rawBitmap.Format}, Size: {rawBitmap.SizeInPixels.Width}x{rawBitmap.SizeInPixels.Height}");

                        // 打开新图片后默认切换回抓手模式
                        SaveButton.IsEnabled = true;
                        MainInkToolbar.IsEnabled = true;
                        EnableHandMode();

                        // 清除旧笔迹
                        inkCanvas.InkPresenter.StrokeContainer.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OpenImageAsync Error: {ex.Message}");
            }
        }

        private async Task SaveImageAsync()
        {
            if (rawBitmap == null) return;

            try
            {
                var picker = new FileSavePicker();

                // 初始化窗口句柄
                ((IInitializeWithWindow)(object)picker).Initialize(coreWindowHost.Handle);

                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                picker.FileTypeChoices.Add("JPEG XR", new List<string>() { ".jxr" });
                picker.SuggestedFileName = "EditedImage";

                StorageFile file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                    {
                        var device = CanvasDevice.GetSharedDevice();

                        // 创建 RenderTarget，尺寸和 DPI 与原始图片一致
                        // 这样保存出来的图片像素尺寸才会和原图一样
                        using (var renderTarget = new CanvasRenderTarget(
                            device,
                            (float)rawBitmap.SizeInPixels.Width,
                            (float)rawBitmap.SizeInPixels.Height,
                            rawBitmap.Dpi,
                            DirectXPixelFormat.R16G16B16A16Float,
                            CanvasAlphaMode.Premultiplied))
                        {
                            using (var ds = renderTarget.CreateDrawingSession())
                            {
                                ds.Clear(Windows.UI.Colors.Transparent);
                                // 绘制原图
                                ds.DrawImage(rawBitmap);

                                // 绘制笔迹
                                // 注意：InkCanvas 也是设置为 PixelWidth/Height，所以坐标系应当是一致的 (DIPs = Pixels at 96 DPI logic)
                                // 如果 rawBitmap.Dpi 不是 96，但 InkCanvas 是按 96 布局的，可能需要 Transform。
                                // 我们的逻辑是：InkCanvas.Width = BitmapImage.PixelWidth.
                                // 假设 BitmapImage.PixelWidth == rawBitmap.SizeInPixels.Width.
                                // InkCanvas 的笔迹坐标是基于其尺寸的。
                                // RenderTarget 的尺寸也是 SizeInPixels (单位是 DIPs, 如果 DPI=96)。
                                // 实际上 CanvasRenderTarget(w, h, dpi) -> 物理像素 = w * dpi/96.
                                // 我们传入了 (SizeInPixels.W, SizeInPixels.H, rawBitmap.Dpi)。
                                // 物理像素 = SizeInPixels * (Dpi/96)。这可能会导致输出尺寸变大如果 Dpi != 96。
                                // 为了确保 1:1 输出，我们可以强制 RenderTarget DPI = 96。
                                // 这样 RenderTarget 的逻辑尺寸(DIPs) = 物理像素尺寸。

                                // 重新创建以确保 1:1
                            }
                        }

                        // 重新修正 Save 逻辑以确保绝对 1:1 (忽略原始 DPI)
                        // 重新修正 Save 逻辑以确保绝对 1:1 (忽略原始 DPI)
                        using (var renderTarget = new CanvasRenderTarget(
                            device,
                            (float)rawBitmap.SizeInPixels.Width,
                            (float)rawBitmap.SizeInPixels.Height,
                            96.0f, // 强制 96 DPI
                            DirectXPixelFormat.R16G16B16A16Float,
                            CanvasAlphaMode.Premultiplied))
                        {
                            // 1. 先将笔迹绘制到一个临时的 sRGB RenderTarget 上
                            // 这样我们明确了笔迹是在 sRGB 空间中定义的
                            using (var inkRenderTarget = new CanvasRenderTarget(
                                device,
                                (float)rawBitmap.SizeInPixels.Width,
                                (float)rawBitmap.SizeInPixels.Height,
                                96.0f,
                                DirectXPixelFormat.B8G8R8A8UIntNormalized, // 标准 sRGB 格式
                                CanvasAlphaMode.Premultiplied))
                            {
                                using (var dsInk = inkRenderTarget.CreateDrawingSession())
                                {
                                    dsInk.Clear(Windows.UI.Colors.Transparent);

                                    // 计算缩放并应用
                                    float scaleX = (float)(rawBitmap.SizeInPixels.Width / inkCanvas.Width);
                                    float scaleY = (float)(rawBitmap.SizeInPixels.Height / inkCanvas.Height);
                                    if (!float.IsNaN(scaleX) && !float.IsNaN(scaleY) && !float.IsInfinity(scaleX) && !float.IsInfinity(scaleY))
                                    {
                                        dsInk.Transform = Matrix3x2.CreateScale(scaleX, scaleY);
                                    }

                                    dsInk.DrawInk(inkCanvas.InkPresenter.StrokeContainer.GetStrokes());
                                }

                                // 2. 合成最终图片
                                using (var ds = renderTarget.CreateDrawingSession())
                                {
                                    ds.Clear(Windows.UI.Colors.Transparent);

                                    // 绘制 HDR 原图 (直接保留 rawBitmap 数据)
                                    ds.DrawImage(rawBitmap, new Windows.Foundation.Rect(0, 0, renderTarget.Size.Width, renderTarget.Size.Height));

                                    // 绘制笔迹层
                                    // 由于 inkRenderTarget 是 sRGB 的，而目标 renderTarget 是 ScRGB (Linear) 的
                                    // 我们需要进行 sRGB -> Linear 的转换，即 Gamma 2.2 扩展
                                    // 这样 0.9 sRGB 才会变成正确的 ~0.79 Linear，而不是被当做 0.9 Linear (过亮)
                                    // 3. 计算 SDR 白点增益
                                    // scRGB 标准定义 1.0 = 80 nits。但屏幕的 SDR 白点通常高于 80 nits (e.g. 200 nits)。
                                    // 为了让 sRGB 笔迹看起来和屏幕上显示的一致 (屏幕上 sRGB White 被映射到了 SdrWhiteLevel)，
                                    // 我们需要对笔迹应用增益：Gain = SdrWhiteLevel / 80。
                                    float sdrWhiteGain = 1.0f;
                                    try
                                    {
                                        var mainDisplayInfo = DisplayInformation.GetForCurrentView();
                                        var colorInfo = mainDisplayInfo.GetAdvancedColorInfo();
                                        if (colorInfo != null)
                                        {
                                            sdrWhiteGain = (float)colorInfo.SdrWhiteLevelInNits / 80.0f;
                                        }
                                    }
                                    catch { /* Fallback to 1.0 */ }

                                    // 4. 生成 sRGB -> Linear 的查找表 (Look-Up Table)
                                    // 这比简单的 Gamma 2.2 更精确，因为它遵循 sRGB 的分段函数定义
                                    float[] srgbToLinearTable = new float[256];
                                    for (int i = 0; i < 256; i++)
                                    {
                                        float u = i / 255.0f; // 归一化输入
                                        float val;
                                        if (u <= 0.04045f)
                                        {
                                            val = u / 12.92f;
                                        }
                                        else
                                        {
                                            val = (float)Math.Pow((u + 0.055) / 1.055, 2.4);
                                        }
                                        // 同时应用白点增益
                                        srgbToLinearTable[i] = val * sdrWhiteGain;
                                    }

                                    // 构建渲染链：
                                    // Ink(Premul) -> UnPremul -> Table(Linearize) -> Premul -> Draw
                                    // 必须先 UnPremultiply，否则对于半透明像素 (边缘抗锯齿)，
                                    // R_premul = R * A。直接查表会导致非线性误差 (LUT(R*A) != LUT(R)*A)。
                                    using (var unpremulEffect = new UnPremultiplyEffect { Source = inkRenderTarget })
                                    using (var tableEffect = new TableTransferEffect
                                    {
                                        Source = unpremulEffect,
                                        RedTable = srgbToLinearTable,
                                        GreenTable = srgbToLinearTable,
                                        BlueTable = srgbToLinearTable,
                                        // AlphaTable 留空，默认为 Identity
                                        ClampOutput = false
                                    })
                                    using (var premulEffect = new PremultiplyEffect { Source = tableEffect })
                                    {
                                        ds.DrawImage(premulEffect);
                                    }
                                }
                            }

                            await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.JpegXR);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveImageAsync Error: {ex.Message}");
            }
        }

        private void CropButton_Click(object sender, RoutedEventArgs e)
        {
            if (rawBitmap == null) return;

            // 界面状态: 开启裁剪，关闭抓手
            CropButton.IsChecked = true;
            HandToolButton.IsChecked = false;
            MainInkToolbar.ActiveTool = null;

            // 初始化并显示裁剪控件（工具栏由 SelectionChanged 事件控制）
            cropControl.Visibility = Visibility.Visible;
            cropControl.Initialize(DisplayImage.ActualWidth, DisplayImage.ActualHeight);
        }

        private void CropControl_SelectionChanged(object sender, bool hasSelection)
        {
            CropButtonPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CropControl_CropCancelled(object sender, EventArgs e)
        {
            cropControl.Visibility = Visibility.Collapsed;
            CropButtonPanel.Visibility = Visibility.Collapsed;

            // 退出裁剪
            CropButton.IsChecked = false;
            // 恢复抓手? 由调用方决定
        }

        private async void CropControl_CropConfirmed(object sender, Windows.Foundation.Rect uiCropRect)
        {
            try
            {
                cropControl.Visibility = Visibility.Collapsed;
                CropButtonPanel.Visibility = Visibility.Collapsed;

                CropButton.IsChecked = false;
                EnableHandMode();

                // 1. 计算图片空间的裁剪区域
                double scaleFactor = rawBitmap.SizeInPixels.Width / DisplayImage.ActualWidth;
                Windows.Foundation.Rect pixelRect = new Windows.Foundation.Rect(
                    Math.Round(uiCropRect.X * scaleFactor),
                    Math.Round(uiCropRect.Y * scaleFactor),
                    Math.Round(uiCropRect.Width * scaleFactor),
                    Math.Round(uiCropRect.Height * scaleFactor));

                var device = CanvasDevice.GetSharedDevice();

                // 2. 裁剪图片 (创建新的 rawBitmap)
                // 使用 RenderTarget 绘制原图的指定区域到新画布
                var newBitmap = new CanvasRenderTarget(
                    device,
                    (float)pixelRect.Width,
                    (float)pixelRect.Height,
                    rawBitmap.Dpi,
                    rawBitmap.Format,
                    CanvasAlphaMode.Premultiplied);

                using (var ds = newBitmap.CreateDrawingSession())
                {
                    ds.Clear(Windows.UI.Colors.Transparent);
                    // 将原图向左上移动，相当于截取 cropRect 区域
                    ds.DrawImage(rawBitmap, (float)-pixelRect.X, (float)-pixelRect.Y);
                }

                // 更新 rawBitmap 引用
                rawBitmap = newBitmap;

                // 3. 更新笔迹位置
                // 笔迹使用的是 UI 坐标系 (DisplayImage.ActualWidth x ActualHeight)
                // 裁剪掉左上角 (X, Y)，相当于所有笔迹向左上平移 (-X, -Y)
                var container = inkCanvas.InkPresenter.StrokeContainer;
                var strokes = container.GetStrokes();
                if (strokes.Count > 0)
                {
                    // 创建平移矩阵
                    var translation = Matrix3x2.CreateTranslation((float)-uiCropRect.X, (float)-uiCropRect.Y);

                    foreach (var stroke in strokes)
                    {
                        var transform = stroke.PointTransform;
                        stroke.PointTransform = Matrix3x2.Multiply(transform, translation);
                    }

                    // 必须重新赋值 strokes 吗？InkStroke 是引用对象，修改属性应即时生效。
                    // 但为了触发重绘，可能需要一点操作。MoveSelected 是官方推荐。
                    // 但 InkStroke.PointTransform 文档说 "This property is read/write".
                }

                // 4. 更新显示的 Image
                // 将新的 rawBitmap 转回 BitmapImage 以显示 (保留 HDR 能力)
                using (var stream = new InMemoryRandomAccessStream())
                {
                    await rawBitmap.SaveAsync(stream, CanvasBitmapFileFormat.JpegXR);
                    stream.Seek(0);

                    var newImg = new BitmapImage();
                    newImg.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    await newImg.SetSourceAsync(stream);
                    DisplayImage.Source = newImg;
                }

                // 5. 更新控件尺寸
                DisplayImage.Width = uiCropRect.Width;
                DisplayImage.Height = uiCropRect.Height;
                inkCanvas.Width = uiCropRect.Width;
                inkCanvas.Height = uiCropRect.Height;

                // 清除裁剪控件的状态
                // (Optional: 可以在 CropControl.Initialize 里重置)
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Crop Error: {ex.Message}");
            }
        }

        private void ConfirmCrop_Click(object sender, RoutedEventArgs e)
        {
            cropControl.Confirm();
        }

        private void CancelCrop_Click(object sender, RoutedEventArgs e)
        {
            cropControl.Cancel();
            EnableHandMode();
        }

        private void EnableHandMode()
        {
            // 如果裁剪处于活动状态，取消它 (仅隐藏 UI，不触发状态循环，因下文会设置状态)
            if (cropControl.Visibility == Visibility.Visible)
            {
                cropControl.Cancel();
            }

            _isHandMode = true;
            inkCanvas.InkPresenter.IsInputEnabled = false;

            HandToolButton.IsChecked = true;
            CropButton.IsChecked = false;
            MainInkToolbar.ActiveTool = null;
        }

        private void EnableInkMode()
        {
            // 如果裁剪处于活动状态，取消它
            if (cropControl.Visibility == Visibility.Visible)
            {
                cropControl.Cancel();
            }

            _isHandMode = false;
            inkCanvas.InkPresenter.IsInputEnabled = true;

            HandToolButton.IsChecked = false;
            CropButton.IsChecked = false;
        }

        private void HandToolButton_Click(object sender, RoutedEventArgs e)
        {
            // 模拟 RadioButton 行为：点击即选中，不允许点击取消
            HandToolButton.IsChecked = true;
            EnableHandMode();
        }

        private void InkToolbar_ActiveToolChanged(InkToolbar sender, object args)
        {
            // 如果切换到了画笔
            if (sender.ActiveTool != null)
            {
                EnableInkMode();
            }
        }

        private void MainScrollViewer_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isHandMode)
            {
                // 停止惯性滚动
                StopInertia();

                _lastDragPoint = e.GetCurrentPoint(MainScrollViewer).Position;
                _velocity = Vector2.Zero;
                _lastMoveTime = DateTime.Now;

                (sender as UIElement).CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }



        private void MainScrollViewer_PointerMoved(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isHandMode && _lastDragPoint.HasValue)
            {
                var currentPoint = e.GetCurrentPoint(MainScrollViewer).Position;
                var currentTime = DateTime.Now;

                double deltaX = currentPoint.X - _lastDragPoint.Value.X;
                double deltaY = currentPoint.Y - _lastDragPoint.Value.Y;

                // 计算瞬时速度 (pixels / ms)
                double dt = (currentTime - _lastMoveTime).TotalMilliseconds;
                if (dt > 0)
                {
                    _velocity = new Vector2((float)(deltaX / dt), (float)(deltaY / dt));
                }

                MainScrollViewer.ChangeView(MainScrollViewer.HorizontalOffset - deltaX, MainScrollViewer.VerticalOffset - deltaY, null, true);

                _lastDragPoint = currentPoint;
                _lastMoveTime = currentTime;
                e.Handled = true;
            }
        }

        private void MainScrollViewer_PointerReleased(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isHandMode)
            {
                _lastDragPoint = null;
                (sender as UIElement).ReleasePointerCapture(e.Pointer);
                e.Handled = true;

                // 如果用户在释放前停留了超过 50ms，认为是有意停止，不进行惯性
                if ((DateTime.Now - _lastMoveTime).TotalMilliseconds > 50)
                {
                    _velocity = Vector2.Zero;
                }

                // 如果速度足够大，启动惯性滚动
                if (_velocity.LengthSquared() > VelocityThreshold * VelocityThreshold)
                {
                    StartInertia();
                }
            }
        }

        private void StartInertia()
        {
            if (!_isInertiaRendering)
            {
                CompositionTarget.Rendering += OnCompositionTargetRendering;
                _isInertiaRendering = true;
            }
        }

        private void StopInertia()
        {
            if (_isInertiaRendering)
            {
                CompositionTarget.Rendering -= OnCompositionTargetRendering;
                _isInertiaRendering = false;
            }
        }

        private void OnCompositionTargetRendering(object sender, object e)
        {
            // 简单物理模拟: 速度衰减与位移更新
            // 假设帧率为 60fps, dt ~ 16.6ms
            double dt = 16.6;

            double dX = _velocity.X * dt;
            double dY = _velocity.Y * dt;

            MainScrollViewer.ChangeView(MainScrollViewer.HorizontalOffset - dX, MainScrollViewer.VerticalOffset - dY, null, true);

            _velocity *= (float)Friction;

            // 当速度极小时停止
            if (_velocity.LengthSquared() < 0.001)
            {
                StopInertia();
            }
        }
        public bool IsCheckedNegation(bool? value) => !(value == true);
    }
}

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
        private bool isHdrImage = false; // 源图像是否为 HDR（浮点格式，scRGB 线性空间）
        private bool _isHandMode = false;
        private Windows.Foundation.Point? _lastDragPoint;
        // 惯性滚动相关字段
        private Vector2 _velocity;
        private DateTime _lastMoveTime;
        private bool _isInertiaRendering;
        private const double Friction = 0.88;
        private const double VelocityThreshold = 0.1;
        private Windows.Foundation.Point _lastPointerInView = new Windows.Foundation.Point(double.NaN, double.NaN);

        // 笔迹撤销/还原
        private readonly List<List<Windows.UI.Input.Inking.InkStroke>> _inkHistory = new List<List<Windows.UI.Input.Inking.InkStroke>>();
        private int _inkHistoryIndex = -1;
        private bool _isUndoRedoing;

        public MainPage(IslandWindow coreWindowHost)
        {
            this.coreWindowHost = coreWindowHost;
            coreWindowHost.Backdrop = IslandWindow.SystemBackdrop.Tabbed;
            this.InitializeComponent();

            // 初始化 InkCanvas 支持的输入类型
            inkCanvas.InkPresenter.InputDeviceTypes = CoreInputDeviceTypes.Mouse | CoreInputDeviceTypes.Pen | CoreInputDeviceTypes.Touch;

            // 默认启用抓手模式
            Loaded += (s, e) => EnableHandMode();

            // Ctrl+滚轮缩放：在内容层拦截 PointerWheelChanged，阻止 ScrollViewer 做内置缩放
            ImageGrid.AddHandler(UIElement.PointerWheelChangedEvent,
                new Windows.UI.Xaml.Input.PointerEventHandler(OnContentPointerWheelChanged), true);

            // 笔迹撤销/还原：监听绘制和擦除事件
            inkCanvas.InkPresenter.StrokesCollected += (s2, _) => { if (!_isUndoRedoing) SaveInkState(); };
            inkCanvas.InkPresenter.StrokesErased += (s2, _) => { if (!_isUndoRedoing) SaveInkState(); };

            // 键盘快捷键（XAML Islands 不转发 Ctrl+Key 到 XAML 层，需在消息泵层拦截）
            System.Windows.Forms.Application.AddMessageFilter(new ShortcutMessageFilter(this));
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
                        rawBitmap?.Dispose();
                        rawBitmap = await CanvasBitmap.LoadAsync(device, stream);

                        isHdrImage = rawBitmap.Format == DirectXPixelFormat.R16G16B16A16Float ||
                                     rawBitmap.Format == DirectXPixelFormat.R32G32B32A32Float ||
                                     rawBitmap.Format == DirectXPixelFormat.R32G32B32Float ||
                                     rawBitmap.Format == DirectXPixelFormat.R11G11B10Float;

                        System.Diagnostics.Debug.WriteLine($"Image Loaded. Format: {rawBitmap.Format}, HDR: {isHdrImage}, Size: {rawBitmap.SizeInPixels.Width}x{rawBitmap.SizeInPixels.Height}");

                        // 打开新图片后默认切换回抓手模式
                        SaveButton.IsEnabled = true;
                        MainInkToolbar.IsEnabled = true;
                        EnableHandMode();

                        // 清除旧笔迹并重置撤销历史
                        inkCanvas.InkPresenter.StrokeContainer.Clear();
                        ResetInkHistory();
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

                ((IInitializeWithWindow)(object)picker).Initialize(coreWindowHost.Handle);

                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

                if (isHdrImage)
                {
                    picker.FileTypeChoices.Add("JPEG XR", new List<string>() { ".jxr" });
                }
                else
                {
                    picker.FileTypeChoices.Add("PNG", new List<string>() { ".png" });
                    picker.FileTypeChoices.Add("JPEG", new List<string>() { ".jpg" });
                    picker.FileTypeChoices.Add("JPEG XR", new List<string>() { ".jxr" });
                }

                picker.SuggestedFileName = "EditedImage";

                StorageFile file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                    {
                        var device = CanvasDevice.GetSharedDevice();

                        CanvasBitmapFileFormat saveFormat;
                        string ext = file.FileType.ToLowerInvariant();
                        switch (ext)
                        {
                            case ".png": saveFormat = CanvasBitmapFileFormat.Png; break;
                            case ".jpg":
                            case ".jpeg": saveFormat = CanvasBitmapFileFormat.Jpeg; break;
                            default: saveFormat = CanvasBitmapFileFormat.JpegXR; break;
                        }

                        var strokes = inkCanvas.InkPresenter.StrokeContainer.GetStrokes();
                        bool hasInk = strokes.Count > 0;

                        if (!hasInk)
                        {
                            // 无笔迹快速路径：跳过渲染管线，直接保存原始位图
                            await rawBitmap.SaveAsync(stream, saveFormat);
                        }
                        else
                        {
                            DirectXPixelFormat renderFormat = isHdrImage
                                ? DirectXPixelFormat.R16G16B16A16Float
                                : DirectXPixelFormat.B8G8R8A8UIntNormalized;

                            float scaleX = (float)(rawBitmap.SizeInPixels.Width / inkCanvas.Width);
                            float scaleY = (float)(rawBitmap.SizeInPixels.Height / inkCanvas.Height);
                            bool validScale = !float.IsNaN(scaleX) && !float.IsNaN(scaleY)
                                           && !float.IsInfinity(scaleX) && !float.IsInfinity(scaleY);

                            using (var renderTarget = new CanvasRenderTarget(
                                device,
                                (float)rawBitmap.SizeInPixels.Width,
                                (float)rawBitmap.SizeInPixels.Height,
                                96.0f,
                                renderFormat,
                                CanvasAlphaMode.Premultiplied))
                            {
                                if (isHdrImage)
                                {
                                    // HDR 管线：用 CanvasCommandList 捕获笔迹指令，避免分配全分辨率像素缓冲区
                                    using (var inkCommands = new CanvasCommandList(device))
                                    {
                                        using (var dsInk = inkCommands.CreateDrawingSession())
                                        {
                                            if (validScale)
                                                dsInk.Transform = Matrix3x2.CreateScale(scaleX, scaleY);
                                            dsInk.DrawInk(strokes);
                                        }

                                        using (var ds = renderTarget.CreateDrawingSession())
                                        {
                                            ds.Clear(Windows.UI.Colors.Transparent);
                                            ds.DrawImage(rawBitmap, new Windows.Foundation.Rect(0, 0, renderTarget.Size.Width, renderTarget.Size.Height));

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
                                            catch { }

                                            float[] srgbToLinearTable = new float[256];
                                            for (int i = 0; i < 256; i++)
                                            {
                                                float u = i / 255.0f;
                                                float val;
                                                if (u <= 0.04045f)
                                                    val = u / 12.92f;
                                                else
                                                    val = (float)Math.Pow((u + 0.055) / 1.055, 2.4);
                                                srgbToLinearTable[i] = val * sdrWhiteGain;
                                            }

                                            using (var unpremulEffect = new UnPremultiplyEffect { Source = inkCommands })
                                            using (var tableEffect = new TableTransferEffect
                                            {
                                                Source = unpremulEffect,
                                                RedTable = srgbToLinearTable,
                                                GreenTable = srgbToLinearTable,
                                                BlueTable = srgbToLinearTable,
                                                ClampOutput = false
                                            })
                                            using (var premulEffect = new PremultiplyEffect { Source = tableEffect })
                                            {
                                                ds.DrawImage(premulEffect);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    // SDR 管线：源图和笔迹都在 sRGB 空间，无需色彩空间转换
                                    using (var ds = renderTarget.CreateDrawingSession())
                                    {
                                        ds.Clear(Windows.UI.Colors.Transparent);
                                        ds.DrawImage(rawBitmap, new Windows.Foundation.Rect(0, 0, renderTarget.Size.Width, renderTarget.Size.Height));

                                        if (validScale)
                                            ds.Transform = Matrix3x2.CreateScale(scaleX, scaleY);
                                        ds.DrawInk(strokes);
                                    }
                                }

                                await renderTarget.SaveAsync(stream, saveFormat);
                            }
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

                rawBitmap?.Dispose();
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
                    var intermediateFormat = isHdrImage ? CanvasBitmapFileFormat.JpegXR : CanvasBitmapFileFormat.Png;
                    await rawBitmap.SaveAsync(stream, intermediateFormat);
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

                // 裁剪后笔迹已变换，重置撤销历史
                ResetInkHistory();

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
            _lastPointerInView = e.GetCurrentPoint(MainScrollViewer).Position;

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
        private void SaveInkState()
        {
            if (_inkHistoryIndex < _inkHistory.Count - 1)
                _inkHistory.RemoveRange(_inkHistoryIndex + 1, _inkHistory.Count - _inkHistoryIndex - 1);

            var snapshot = new List<Windows.UI.Input.Inking.InkStroke>();
            foreach (var s in inkCanvas.InkPresenter.StrokeContainer.GetStrokes())
                snapshot.Add(s.Clone());
            _inkHistory.Add(snapshot);
            _inkHistoryIndex++;
        }

        private void ResetInkHistory()
        {
            _inkHistory.Clear();
            _inkHistoryIndex = -1;
            SaveInkState();
        }

        internal void InkUndo()
        {
            if (_inkHistoryIndex <= 0) return;
            _isUndoRedoing = true;
            _inkHistoryIndex--;
            inkCanvas.InkPresenter.StrokeContainer.Clear();
            foreach (var s in _inkHistory[_inkHistoryIndex])
                inkCanvas.InkPresenter.StrokeContainer.AddStroke(s.Clone());
            _isUndoRedoing = false;
        }

        internal void InkRedo()
        {
            if (_inkHistoryIndex >= _inkHistory.Count - 1) return;
            _isUndoRedoing = true;
            _inkHistoryIndex++;
            inkCanvas.InkPresenter.StrokeContainer.Clear();
            foreach (var s in _inkHistory[_inkHistoryIndex])
                inkCanvas.InkPresenter.StrokeContainer.AddStroke(s.Clone());
            _isUndoRedoing = false;
        }

        private void OnContentPointerWheelChanged(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse) return;
            var props = e.GetCurrentPoint(MainScrollViewer).Properties;
            if (!props.IsHorizontalMouseWheel && e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
            {
                int delta = props.MouseWheelDelta;
                // 更新鼠标位置用于锚点计算
                _lastPointerInView = e.GetCurrentPoint(MainScrollViewer).Position;

                const float zoomStep = 1.25f;
                float factor = (float)Math.Pow(zoomStep, delta / 120.0);
                ZoomByFactor(factor);

                e.Handled = true;
            }
        }

        internal void ZoomByFactor(float factor)
        {
            float newZoom = MainScrollViewer.ZoomFactor * factor;
            newZoom = Math.Max(MainScrollViewer.MinZoomFactor, Math.Min(MainScrollViewer.MaxZoomFactor, newZoom));
            ZoomToFactor(newZoom);
        }

        internal void ZoomToFactor(float targetZoom)
        {
            targetZoom = Math.Max(MainScrollViewer.MinZoomFactor, Math.Min(MainScrollViewer.MaxZoomFactor, targetZoom));
            float currentZoom = MainScrollViewer.ZoomFactor;

            // 锚点：鼠标在视口中的位置（如无有效位置则退回视口中心）
            double anchorX, anchorY;
            if (!double.IsNaN(_lastPointerInView.X))
            {
                anchorX = _lastPointerInView.X;
                anchorY = _lastPointerInView.Y;
            }
            else
            {
                anchorX = MainScrollViewer.ViewportWidth / 2;
                anchorY = MainScrollViewer.ViewportHeight / 2;
            }

            // 当内容小于视口时，ScrollViewer 将内容居中，产生额外的内边距
            double padX = Math.Max(0, (MainScrollViewer.ViewportWidth - MainScrollViewer.ExtentWidth) / 2);
            double padY = Math.Max(0, (MainScrollViewer.ViewportHeight - MainScrollViewer.ExtentHeight) / 2);

            // 锚点对应的内容坐标（减去居中内边距）
            double contentX = (MainScrollViewer.HorizontalOffset + anchorX - padX) / currentZoom;
            double contentY = (MainScrollViewer.VerticalOffset + anchorY - padY) / currentZoom;

            // 缩放后保持锚点在视口中相同位置
            double newOffsetX = contentX * targetZoom - anchorX;
            double newOffsetY = contentY * targetZoom - anchorY;

            MainScrollViewer.ChangeView(newOffsetX, newOffsetY, targetZoom, false);
        }

        public bool IsCheckedNegation(bool? value) => !(value == true);
    }

    internal class ShortcutMessageFilter : System.Windows.Forms.IMessageFilter
    {
        private const int WM_KEYDOWN = 0x0100;
        private const float ZoomStep = 1.25f;
        private readonly MainPage _page;

        public ShortcutMessageFilter(MainPage page) { _page = page; }

        public bool PreFilterMessage(ref System.Windows.Forms.Message m)
        {
            if (m.Msg != WM_KEYDOWN) return false;
            if ((System.Windows.Forms.Control.ModifierKeys & System.Windows.Forms.Keys.Control) == 0) return false;

            bool shift = (System.Windows.Forms.Control.ModifierKeys & System.Windows.Forms.Keys.Shift) != 0;
            var key = (System.Windows.Forms.Keys)(int)m.WParam;
            switch (key)
            {
                case System.Windows.Forms.Keys.Oemplus:
                case System.Windows.Forms.Keys.Add:
                    _page.ZoomByFactor(ZoomStep);
                    return true;
                case System.Windows.Forms.Keys.OemMinus:
                case System.Windows.Forms.Keys.Subtract:
                    _page.ZoomByFactor(1f / ZoomStep);
                    return true;
                case System.Windows.Forms.Keys.D0:
                case System.Windows.Forms.Keys.NumPad0:
                    _page.ZoomToFactor(1.0f);
                    return true;
                case System.Windows.Forms.Keys.Z:
                    if (shift) _page.InkRedo();
                    else _page.InkUndo();
                    return true;
                case System.Windows.Forms.Keys.Y:
                    _page.InkRedo();
                    return true;
                default:
                    return false;
            }
        }
    }
}

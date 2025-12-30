using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
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
        private IslandWindow coreWindow;
        private CanvasBitmap rawBitmap; // 用于保存的原始数据
        
        public MainPage(IslandWindow coreWindow)
        {
            this.coreWindow = coreWindow;
            this.InitializeComponent();
            
            // 初始化 InkCanvas 支持的输入类型
            inkCanvas.InkPresenter.InputDeviceTypes = CoreInputDeviceTypes.Mouse | CoreInputDeviceTypes.Pen | CoreInputDeviceTypes.Touch;
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
                ((IInitializeWithWindow)(object)picker).Initialize(coreWindow.Handle);

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
                ((IInitializeWithWindow)(object)picker).Initialize(coreWindow.Handle);

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
                         using (var renderTarget = new CanvasRenderTarget(
                            device,
                            (float)rawBitmap.SizeInPixels.Width,
                            (float)rawBitmap.SizeInPixels.Height,
                            96.0f, // 强制 96 DPI
                            DirectXPixelFormat.R16G16B16A16Float,
                            CanvasAlphaMode.Premultiplied))
                        {
                            using (var ds = renderTarget.CreateDrawingSession())
                            {
                                ds.Clear(Windows.UI.Colors.Transparent);
                                // 绘制原图 (覆盖整个 Target)
                                ds.DrawImage(rawBitmap, new Windows.Foundation.Rect(0, 0, renderTarget.Size.Width, renderTarget.Size.Height));
                                
                                // 绘制笔迹
                                // InkCanvas 的大小(DIPs) = Pixels / ScaleFactor
                                // RenderTarget 的大小(DIPs at 96 DPI) = Pixels
                                // 所以笔迹需要放大 ScaleFactor 倍才能匹配 RenderTarget
                                float scaleX = (float)(rawBitmap.SizeInPixels.Width / inkCanvas.Width);
                                float scaleY = (float)(rawBitmap.SizeInPixels.Height / inkCanvas.Height);
                                
                                // 如果 inkCanvas.Width 是 NaN (未初始化) 或者 0，这里会出错，但在 OpenImageAsync 里我们设置了它。
                                if (!float.IsNaN(scaleX) && !float.IsNaN(scaleY) && !float.IsInfinity(scaleX) && !float.IsInfinity(scaleY))
                                {
                                    ds.Transform = Matrix3x2.CreateScale(scaleX, scaleY);
                                }

                                ds.DrawInk(inkCanvas.InkPresenter.StrokeContainer.GetStrokes());
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
    }
}

using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace MicroWinUI
{
    public sealed partial class CropControl : UserControl
    {
        public event EventHandler<Rect> CropConfirmed;
        public event EventHandler CropCancelled;

        private double _imageWidth;
        private double _imageHeight;
        private Rect _currentRect;
        private const double MinSize = 50;

        public CropControl()
        {
            this.InitializeComponent();
        }

        public void Initialize(double width, double height)
        {
            _imageWidth = width;
            _imageHeight = height;

            // 初始裁剪框为图片中心 80% 大小
            double cropW = width * 0.8;
            double cropH = height * 0.8;
            double x = (width - cropW) / 2;
            double y = (height - cropH) / 2;

            _currentRect = new Rect(x, y, cropW, cropH);
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            // 更新裁剪框位置和大小
            Canvas.SetLeft(SelectionBorder, _currentRect.X);
            Canvas.SetTop(SelectionBorder, _currentRect.Y);
            SelectionBorder.Width = _currentRect.Width;
            SelectionBorder.Height = _currentRect.Height;

            // 更新 4 个遮罩矩形
            // Top Mask
            TopMask.Width = Math.Max(0, _imageWidth);
            TopMask.Height = Math.Max(0, _currentRect.Top);
            Canvas.SetLeft(TopMask, 0);
            Canvas.SetTop(TopMask, 0);

            // Bottom Mask
            BottomMask.Width = Math.Max(0, _imageWidth);
            BottomMask.Height = Math.Max(0, _imageHeight - _currentRect.Bottom);
            Canvas.SetLeft(BottomMask, 0);
            Canvas.SetTop(BottomMask, _currentRect.Bottom);

            // Left Mask
            LeftMask.Width = Math.Max(0, _currentRect.Left);
            LeftMask.Height = Math.Max(0, _currentRect.Height);
            Canvas.SetLeft(LeftMask, 0);
            Canvas.SetTop(LeftMask, _currentRect.Top);

            // Right Mask
            RightMask.Width = Math.Max(0, _imageWidth - _currentRect.Right);
            RightMask.Height = Math.Max(0, _currentRect.Height);
            Canvas.SetLeft(RightMask, _currentRect.Right);
            Canvas.SetTop(RightMask, _currentRect.Top);

            // 更新手柄位置
            UpdateThumbPosition("TL", _currentRect.Left, _currentRect.Top);
            UpdateThumbPosition("T", _currentRect.Left + _currentRect.Width / 2, _currentRect.Top);
            UpdateThumbPosition("TR", _currentRect.Right, _currentRect.Top);
            UpdateThumbPosition("R", _currentRect.Right, _currentRect.Top + _currentRect.Height / 2);
            UpdateThumbPosition("BR", _currentRect.Right, _currentRect.Bottom);
            UpdateThumbPosition("B", _currentRect.Left + _currentRect.Width / 2, _currentRect.Bottom);
            UpdateThumbPosition("BL", _currentRect.Left, _currentRect.Bottom);
            UpdateThumbPosition("L", _currentRect.Left, _currentRect.Top + _currentRect.Height / 2);
        }

        private void UpdateThumbPosition(string tag, double x, double y)
        {
            foreach (var child in ((Canvas)SelectionBorder.Parent).Children)
            {
                if (child is Thumb thumb && (string)thumb.Tag == tag)
                {
                    Canvas.SetLeft(thumb, x - thumb.Width / 2);
                    Canvas.SetTop(thumb, y - thumb.Height / 2);
                    break;
                }
            }
        }

        private void Selection_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            double newX = _currentRect.X + e.Delta.Translation.X;
            double newY = _currentRect.Y + e.Delta.Translation.Y;

            // 边界检查
            newX = Math.Max(0, Math.Min(newX, _imageWidth - _currentRect.Width));
            newY = Math.Max(0, Math.Min(newY, _imageHeight - _currentRect.Height));

            _currentRect.X = newX;
            _currentRect.Y = newY;
            UpdateVisuals();
            e.Handled = true;
        }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var thumb = sender as Thumb;
            string tag = (string)thumb.Tag;

            double dX = e.HorizontalChange;
            double dY = e.VerticalChange;

            double newLeft = _currentRect.Left;
            double newTop = _currentRect.Top;
            double newWidth = _currentRect.Width;
            double newHeight = _currentRect.Height;

            if (tag.Contains("L"))
            {
                double maxDX = newWidth - MinSize;
                dX = Math.Min(dX, maxDX);
                newLeft += dX;
                newWidth -= dX;
            }
            if (tag.Contains("R"))
            {
                double minWidth = MinSize;
                if (newWidth + dX < minWidth) dX = minWidth - newWidth;
                newWidth += dX;
            }
            if (tag.Contains("T"))
            {
                double maxDY = newHeight - MinSize;
                dY = Math.Min(dY, maxDY);
                newTop += dY;
                newHeight -= dY;
            }
            if (tag.Contains("B"))
            {
                double minHeight = MinSize;
                if (newHeight + dY < minHeight) dY = minHeight - newHeight;
                newHeight += dY;
            }

            // 边界约束
            if (newLeft < 0) { newWidth += newLeft; newLeft = 0; }
            if (newTop < 0) { newHeight += newTop; newTop = 0; }
            if (newLeft + newWidth > _imageWidth) newWidth = _imageWidth - newLeft;
            if (newTop + newHeight > _imageHeight) newHeight = _imageHeight - newTop;

            _currentRect = new Rect(newLeft, newTop, newWidth, newHeight);
            UpdateVisuals();
        }

        private void LayoutRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // 阻止事件冒泡防止 ScrollViewer 捕获
            e.Handled = true;
        }

        public void Confirm()
        {
            CropConfirmed?.Invoke(this, _currentRect);
        }

        public void Cancel()
        {
            CropCancelled?.Invoke(this, EventArgs.Empty);
        }
    }
}

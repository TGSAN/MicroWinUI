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
        private bool _isDragging = false;
        private Point _lastDragPoint;


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

            // 更新手柄位置 (直接使用命名控件)
            SetThumb(ThumbTL, _currentRect.Left, _currentRect.Top);
            SetThumb(ThumbT, _currentRect.Left + _currentRect.Width / 2, _currentRect.Top);
            SetThumb(ThumbTR, _currentRect.Right, _currentRect.Top);
            SetThumb(ThumbR, _currentRect.Right, _currentRect.Top + _currentRect.Height / 2);
            SetThumb(ThumbBR, _currentRect.Right, _currentRect.Bottom);
            SetThumb(ThumbB, _currentRect.Left + _currentRect.Width / 2, _currentRect.Bottom);
            SetThumb(ThumbBL, _currentRect.Left, _currentRect.Bottom);
            SetThumb(ThumbL, _currentRect.Left, _currentRect.Top + _currentRect.Height / 2);
        }

        private void SetThumb(Thumb thumb, double x, double y)
        {
            // 如果 Width 未设置 (NaN)，默认 20
            double w = double.IsNaN(thumb.Width) ? 20 : thumb.Width;
            double h = double.IsNaN(thumb.Height) ? 20 : thumb.Height;
            Canvas.SetLeft(thumb, x - w / 2);
            Canvas.SetTop(thumb, y - h / 2);
        }

        private void SelectionBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = true;
            // 捕获相对于 LayoutRoot (或 Canvas) 的位置
            _lastDragPoint = e.GetCurrentPoint(this.Content as UIElement).Position;
            (sender as UIElement).CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void SelectionBorder_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                var cur = e.GetCurrentPoint(this.Content as UIElement).Position;
                double dx = cur.X - _lastDragPoint.X;
                double dy = cur.Y - _lastDragPoint.Y;

                double newX = _currentRect.X + dx;
                double newY = _currentRect.Y + dy;

                newX = Math.Max(0, Math.Min(newX, _imageWidth - _currentRect.Width));
                newY = Math.Max(0, Math.Min(newY, _imageHeight - _currentRect.Height));

                _currentRect.X = newX;
                _currentRect.Y = newY;
                UpdateVisuals();

                _lastDragPoint = cur;
                e.Handled = true;
            }
        }

        private void SelectionBorder_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                (sender as UIElement).ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            }
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

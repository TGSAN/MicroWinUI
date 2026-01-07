using System;
using Windows.Foundation;
using Windows.UI.Core;
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
        public event EventHandler<bool> SelectionChanged; // 选区状态变化事件，参数为是否有选区

        private double _imageWidth;
        private double _imageHeight;
        private Rect _currentRect;
        private const double MinSize = 10; // 最小裁剪框大小
        private const double ClickThreshold = 5; // 点击判定阈值（拖动距离小于此值视为点击）
        
        // 裁剪框拖动状态
        private bool _isDragging = false;
        private Point _lastDragPoint;
        
        // 新增状态字段
        private bool _hasSelection = false;           // 是否有有效裁剪框
        private bool _isCreatingSelection = false;    // 是否正在拖动创建新裁剪框
        private Point _creationStartPoint;            // 创建裁剪框的起始点
        
        // 手柄拖动锚点 - 用于支持翻转
        private double _thumbAnchorX;                 // 手柄拖动时X方向锚点（不移动的那条边）
        private double _thumbAnchorY;                 // 手柄拖动时Y方向锚点（不移动的那条边）
        private double _thumbStartActiveX;            // 拖动开始时的X方向活动边初始位置
        private double _thumbStartActiveY;            // 拖动开始时的Y方向活动边初始位置
        private double _thumbTotalDeltaX;             // 累计X方向增量
        private double _thumbTotalDeltaY;             // 累计Y方向增量
        private bool _thumbMovesX;                    // 当前手柄是否移动X
        private bool _thumbMovesY;                    // 当前手柄是否移动Y

        public CropControl()
        {
            this.InitializeComponent();
        }

        public void Initialize(double width, double height)
        {
            _imageWidth = width;
            _imageHeight = height;

            // 初始化时不创建裁剪框，将整个画布调暗
            _hasSelection = false;
            _currentRect = new Rect(0, 0, 0, 0);
            UpdateVisuals();
            UpdateSelectionVisibility();
        }

        /// <summary>
        /// 根据是否有选区控制裁剪框和手柄的可见性
        /// </summary>
        private void UpdateSelectionVisibility()
        {
            var visibility = _hasSelection ? Visibility.Visible : Visibility.Collapsed;
            
            SelectionBorder.Visibility = visibility;
            ThumbTL.Visibility = visibility;
            ThumbT.Visibility = visibility;
            ThumbTR.Visibility = visibility;
            ThumbR.Visibility = visibility;
            ThumbBR.Visibility = visibility;
            ThumbB.Visibility = visibility;
            ThumbBL.Visibility = visibility;
            ThumbL.Visibility = visibility;
            
            // 更新SelectionCanvas的hit test状态
            SelectionCanvas.IsHitTestVisible = _hasSelection;
            
            // 通知外部选区状态变化
            SelectionChanged?.Invoke(this, _hasSelection);
        }

        private void UpdateVisuals()
        {
            if (_hasSelection)
            {
                // 有裁剪框时，正常更新位置和遮罩
                Canvas.SetLeft(SelectionBorder, _currentRect.X);
                Canvas.SetTop(SelectionBorder, _currentRect.Y);
                SelectionBorder.Width = Math.Max(0, _currentRect.Width);
                SelectionBorder.Height = Math.Max(0, _currentRect.Height);

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
                SetThumb(ThumbTL, _currentRect.Left, _currentRect.Top);
                SetThumb(ThumbT, _currentRect.Left + _currentRect.Width / 2, _currentRect.Top);
                SetThumb(ThumbTR, _currentRect.Right, _currentRect.Top);
                SetThumb(ThumbR, _currentRect.Right, _currentRect.Top + _currentRect.Height / 2);
                SetThumb(ThumbBR, _currentRect.Right, _currentRect.Bottom);
                SetThumb(ThumbB, _currentRect.Left + _currentRect.Width / 2, _currentRect.Bottom);
                SetThumb(ThumbBL, _currentRect.Left, _currentRect.Bottom);
                SetThumb(ThumbL, _currentRect.Left, _currentRect.Top + _currentRect.Height / 2);
            }
            else
            {
                // 无裁剪框时，整个画布调暗
                TopMask.Width = _imageWidth;
                TopMask.Height = _imageHeight;
                Canvas.SetLeft(TopMask, 0);
                Canvas.SetTop(TopMask, 0);

                BottomMask.Width = 0;
                BottomMask.Height = 0;
                LeftMask.Width = 0;
                LeftMask.Height = 0;
                RightMask.Width = 0;
                RightMask.Height = 0;
            }
        }

        private void SetThumb(Thumb thumb, double x, double y)
        {
            double w = double.IsNaN(thumb.Width) ? 20 : thumb.Width;
            double h = double.IsNaN(thumb.Height) ? 20 : thumb.Height;
            Canvas.SetLeft(thumb, x - w / 2);
            Canvas.SetTop(thumb, y - h / 2);
        }

        #region 遮罩层交互 - 创建新裁剪框

        private void MaskCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isCreatingSelection = true;
            _creationStartPoint = e.GetCurrentPoint(MaskCanvas).Position;
            MaskCanvas.CapturePointer(e.Pointer);
            
            // 设置十字光标
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Cross, 0);
            
            e.Handled = true;
        }

        private void MaskCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isCreatingSelection)
            {
                var rawPoint = e.GetCurrentPoint(MaskCanvas).Position;
                
                // 先约束当前点到有效范围，防止超出边界时反向扩大选区
                double clampedX = Math.Max(0, Math.Min(rawPoint.X, _imageWidth));
                double clampedY = Math.Max(0, Math.Min(rawPoint.Y, _imageHeight));
                var currentPoint = new Point(clampedX, clampedY);
                
                // 计算裁剪框的位置和大小
                double x = Math.Min(_creationStartPoint.X, currentPoint.X);
                double y = Math.Min(_creationStartPoint.Y, currentPoint.Y);
                double width = Math.Abs(currentPoint.X - _creationStartPoint.X);
                double height = Math.Abs(currentPoint.Y - _creationStartPoint.Y);

                _currentRect = new Rect(x, y, width, height);
                
                // 拖动过程中显示裁剪框
                if (width > ClickThreshold || height > ClickThreshold)
                {
                    _hasSelection = true;
                    UpdateSelectionVisibility();
                }
                
                UpdateVisuals();
                e.Handled = true;
            }
        }

        private void MaskCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isCreatingSelection)
            {
                _isCreatingSelection = false;
                MaskCanvas.ReleasePointerCapture(e.Pointer);
                
                // 恢复十字光标（仍在遮罩区域）
                Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Cross, 0);

                var currentPoint = e.GetCurrentPoint(MaskCanvas).Position;
                double dragDistance = Math.Max(
                    Math.Abs(currentPoint.X - _creationStartPoint.X),
                    Math.Abs(currentPoint.Y - _creationStartPoint.Y));

                if (dragDistance < ClickThreshold)
                {
                    // 点击：撤回裁剪框
                    _hasSelection = false;
                    _currentRect = new Rect(0, 0, 0, 0);
                    UpdateVisuals();
                    UpdateSelectionVisibility();
                }
                else
                {
                    // 拖动完成：确保裁剪框有效
                    if (_currentRect.Width >= MinSize && _currentRect.Height >= MinSize)
                    {
                        _hasSelection = true;
                    }
                    else
                    {
                        // 太小的裁剪框视为无效
                        _hasSelection = false;
                        _currentRect = new Rect(0, 0, 0, 0);
                    }
                    UpdateVisuals();
                    UpdateSelectionVisibility();
                }

                e.Handled = true;
            }
        }

        private void MaskCanvas_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            // 进入遮罩区域时设置十字光标
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Cross, 0);
        }

        private void MaskCanvas_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            // 离开遮罩区域时恢复默认光标（如果不是在创建选区过程中）
            if (!_isCreatingSelection)
            {
                Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Arrow, 0);
            }
        }

        #endregion

        #region 裁剪框拖动

        private void SelectionBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = true;
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

                double oldX = _currentRect.X;
                double oldY = _currentRect.Y;

                double newX = _currentRect.X + dx;
                double newY = _currentRect.Y + dy;

                newX = Math.Max(0, Math.Min(newX, _imageWidth - _currentRect.Width));
                newY = Math.Max(0, Math.Min(newY, _imageHeight - _currentRect.Height));

                _currentRect.X = newX;
                _currentRect.Y = newY;
                UpdateVisuals();

                double actualDx = _currentRect.X - oldX;
                double actualDy = _currentRect.Y - oldY;
                _lastDragPoint = new Point(_lastDragPoint.X + actualDx, _lastDragPoint.Y + actualDy);

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

        private void SelectionBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.SizeAll, 0);
        }

        private void SelectionBorder_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging)
            {
                Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Arrow, 0);
            }
        }

        #endregion

        #region 手柄拖动

        private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            var thumb = sender as Thumb;
            string tag = (string)thumb.Tag;

            // 根据手柄类型确定锚点和活动边
            _thumbMovesX = tag.Contains("L") || tag.Contains("R");
            _thumbMovesY = tag.Contains("T") || tag.Contains("B");
            
            // 初始化累计增量
            _thumbTotalDeltaX = 0;
            _thumbTotalDeltaY = 0;
            
            if (tag.Contains("L"))
            {
                _thumbAnchorX = _currentRect.Right;
                _thumbStartActiveX = _currentRect.Left;
            }
            else if (tag.Contains("R"))
            {
                _thumbAnchorX = _currentRect.Left;
                _thumbStartActiveX = _currentRect.Right;
            }
            
            if (tag.Contains("T"))
            {
                _thumbAnchorY = _currentRect.Bottom;
                _thumbStartActiveY = _currentRect.Top;
            }
            else if (tag.Contains("B"))
            {
                _thumbAnchorY = _currentRect.Top;
                _thumbStartActiveY = _currentRect.Bottom;
            }
        }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            // 累加增量（不约束）
            _thumbTotalDeltaX += e.HorizontalChange;
            _thumbTotalDeltaY += e.VerticalChange;
            
            // 使用初始位置 + 累计增量计算活动边，然后约束
            double activeX = _thumbStartActiveX + _thumbTotalDeltaX;
            double activeY = _thumbStartActiveY + _thumbTotalDeltaY;
            
            activeX = Math.Max(0, Math.Min(activeX, _imageWidth));
            activeY = Math.Max(0, Math.Min(activeY, _imageHeight));

            // 使用锚点和活动边计算新的 rect
            double left, right, top, bottom;
            
            if (_thumbMovesX)
            {
                left = Math.Min(_thumbAnchorX, activeX);
                right = Math.Max(_thumbAnchorX, activeX);
            }
            else
            {
                left = _currentRect.Left;
                right = _currentRect.Right;
            }
            
            if (_thumbMovesY)
            {
                top = Math.Min(_thumbAnchorY, activeY);
                bottom = Math.Max(_thumbAnchorY, activeY);
            }
            else
            {
                top = _currentRect.Top;
                bottom = _currentRect.Bottom;
            }

            _currentRect = new Rect(left, top, right - left, bottom - top);
            UpdateVisuals();
        }

        private void Thumb_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            var thumb = sender as Thumb;
            string tag = (string)thumb.Tag;
            
            CoreCursorType cursorType = CoreCursorType.Arrow;
            
            switch (tag)
            {
                case "TL":
                case "BR":
                    cursorType = CoreCursorType.SizeNorthwestSoutheast;
                    break;
                case "TR":
                case "BL":
                    cursorType = CoreCursorType.SizeNortheastSouthwest;
                    break;
                case "T":
                case "B":
                    cursorType = CoreCursorType.SizeNorthSouth;
                    break;
                case "L":
                case "R":
                    cursorType = CoreCursorType.SizeWestEast;
                    break;
            }
            
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(cursorType, 0);
        }

        private void Thumb_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Arrow, 0);
        }

        #endregion

        private void LayoutRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;
        }

        public void Confirm()
        {
            if (_hasSelection)
            {
                CropConfirmed?.Invoke(this, _currentRect);
            }
        }

        public void Cancel()
        {
            CropCancelled?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// 获取当前是否有有效的裁剪选区
        /// </summary>
        public bool HasSelection => _hasSelection;
    }
}


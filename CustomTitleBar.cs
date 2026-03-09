using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MicroWinUI.Tests")]

namespace MicroWinUICore
{
    /// <summary>
    /// 独立的自定义标题栏管理类，封装所有标题栏相关的 Win32 消息处理和 DWM API 调用。
    /// 仅依赖 System.Windows.Forms.Form，可在任意 WinForms 窗口中复用。
    /// </summary>
    public class CustomTitleBar
    {
        // --- 内部字段 ---
        private readonly Form _hostForm;
        private bool _extendsContentIntoTitleBar;
        private int _titleBarHeight = 48;
        private Color _backgroundColor;
        private Color _foregroundColor;
        private Rectangle[] _dragRectangles;
        private Panel _titleBarHostPanel;
        private object _titleBarContent;
        private int _currentDpi = 96;
        private bool _isMaximized;
        private bool _initialized;

        // --- 事件 ---
        public event EventHandler DragRegionInvalidated;
        public event EventHandler ThemeChanged;

        // --- 构造函数 ---
        public CustomTitleBar(Form hostForm)
        {
            _hostForm = hostForm ?? throw new ArgumentNullException(nameof(hostForm));
        }

        // --- 属性 ---
        public bool ExtendsContentIntoTitleBar
        {
            get => _extendsContentIntoTitleBar;
            set => _extendsContentIntoTitleBar = value;
        }

        public int TitleBarHeight
        {
            get => _titleBarHeight;
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "TitleBarHeight must be greater than 0.");
                _titleBarHeight = value;
            }
        }

        /// <summary>
        /// 标题栏背景色。设置为 Color.Transparent 时标题栏透明（使用 DWMWA_COLOR_NONE）。
        /// 通过 DWM API（DWMWA_CAPTION_COLOR）设置。
        /// </summary>
        public Color TitleBarBackgroundColor
        {
            get => _backgroundColor;
            set
            {
                _backgroundColor = value;
                if (_hostForm.IsHandleCreated)
                {
                    uint colorRef = value == Color.Transparent
                        ? Win32API.DWMWA_COLOR_NONE
                        : ColorToColorRef(value);
                    SetDwmAttribute((uint)Win32API.DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, colorRef);
                }
            }
        }

        /// <summary>
        /// 标题栏前景色（标题文字颜色）。
        /// 通过 DWM API（DWMWA_TEXT_COLOR）设置。
        /// </summary>
        public Color TitleBarForegroundColor
        {
            get => _foregroundColor;
            set
            {
                _foregroundColor = value;
                if (_hostForm.IsHandleCreated)
                {
                    uint colorRef = ColorToColorRef(value);
                    SetDwmAttribute((uint)Win32API.DWMWINDOWATTRIBUTE.DWMWA_TEXT_COLOR, colorRef);
                }
            }
        }

        /// <summary>
        /// 标题栏区域的自定义内容。使用 object 类型以避免 XAML 依赖。
        /// 设置后会创建一个 Panel 宿主控件并添加到宿主窗口，定位在标题栏区域。
        /// 实际 XAML 内容承载（WindowsXamlHost）在 IslandForm 集成时完成（Task 8）。
        /// </summary>
        public object TitleBarContent
        {
            get => _titleBarContent;
            set
            {
                _titleBarContent = value;

                if (value != null)
                {
                    EnsureTitleBarHostPanel();
                }
                else
                {
                    RemoveTitleBarHostPanel();
                }
            }
        }

        /// <summary>
        /// 获取标题栏内容宿主 Panel 控件（供 IslandForm 集成使用）。
        /// </summary>
        internal Panel TitleBarHostPanel => _titleBarHostPanel;

        public Rectangle CaptionButtonsBounds { get; internal set; }

        public int ScaledTitleBarHeight => ScaleDimension(_titleBarHeight, _currentDpi);

        /// <summary>
        /// 获取当前 DPI 值（供测试使用）。
        /// </summary>
        internal int CurrentDpi => _currentDpi;

        /// <summary>
        /// 获取当前是否最大化状态（供测试使用）。
        /// </summary>
        internal bool IsMaximized => _isMaximized;

        /// <summary>
        /// 获取是否已初始化（供测试使用）。
        /// </summary>
        internal bool Initialized => _initialized;

        // --- 纯计算方法 ---

        /// <summary>
        /// DPI 缩放计算：将逻辑像素转换为物理像素。
        /// </summary>
        internal static int ScaleDimension(int logicalPixels, int dpi)
        {
            return logicalPixels * dpi / 96;
        }

        /// <summary>
        /// 命中测试纯函数：根据客户区坐标返回命中测试结果。
        /// </summary>
        internal static int HitTest(
            Point clientPoint,
            int scaledTitleBarHeight,
            bool isMaximized,
            Rectangle[] dragRects,
            Rectangle captionButtonBounds,
            int windowWidth,
            int windowHeight,
            int resizeBorderThickness)
        {
            int x = clientPoint.X;
            int y = clientPoint.Y;

            // 1. 检查窗口边缘（仅非最大化时）
            if (!isMaximized)
            {
                bool left = x < resizeBorderThickness;
                bool right = x >= windowWidth - resizeBorderThickness;
                bool top = y < resizeBorderThickness;
                bool bottom = y >= windowHeight - resizeBorderThickness;

                if (top && left) return Win32API.HTTOPLEFT;
                if (top && right) return Win32API.HTTOPRIGHT;
                if (bottom && left) return Win32API.HTBOTTOMLEFT;
                if (bottom && right) return Win32API.HTBOTTOMRIGHT;
                if (bottom) return Win32API.HTBOTTOM;
                if (left) return Win32API.HTLEFT;

                if (right)
                {
                    bool inCaptionButtons = y < scaledTitleBarHeight &&
                        captionButtonBounds.Width > 0 && captionButtonBounds.Height > 0 &&
                        x >= captionButtonBounds.X && x < captionButtonBounds.Right &&
                        y >= captionButtonBounds.Y && y < captionButtonBounds.Bottom;
                    if (!inCaptionButtons)
                        return Win32API.HTRIGHT;
                }

                if (top) return Win32API.HTTOP;
            }

            // 2. 检查标题栏区域
            if (y < scaledTitleBarHeight)
            {
                // 3. 标题栏按钮子区域：返回 HTCAPTION 而非具体按钮值。
                //    DwmDefWindowProc 在调用 HitTest 之前已优先处理按钮 hover/点击，
                //    此处仅作为 DWM 未处理时的回退（例如按钮右侧间隙），返回 HTCAPTION 使其可拖拽。
                if (captionButtonBounds.Width > 0 && captionButtonBounds.Height > 0 &&
                    x >= captionButtonBounds.X && x < captionButtonBounds.Right &&
                    y >= captionButtonBounds.Y && y < captionButtonBounds.Bottom)
                {
                    return Win32API.HTCAPTION;
                }

                // 4. 检查拖拽区域
                if (dragRects == null)
                    return Win32API.HTCAPTION;

                foreach (var rect in dragRects)
                {
                    if (x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom)
                        return Win32API.HTCAPTION;
                }

                return Win32API.HTCLIENT;
            }

            return Win32API.HTCLIENT;
        }

        /// <summary>
        /// 计算最大化时的内边距补偿：对 RECT 四边各缩进 borderPadding 像素。
        /// 还原时不调用此方法（即不添加补偿），RECT 保持原始值。
        /// </summary>
        internal static Win32API.RECT ApplyMaximizePadding(Win32API.RECT rect, int borderPadding)
        {
            return new Win32API.RECT
            {
                Left = rect.Left + borderPadding,
                Top = rect.Top + borderPadding,
                Right = rect.Right - borderPadding,
                Bottom = rect.Bottom - borderPadding
            };
        }

        /// <summary>
        /// 计算标题栏内容宿主控件的边界。
        /// </summary>
        internal static Rectangle CalculateTitleBarHostBounds(
            int clientWidth,
            int captionButtonsWidth,
            int scaledTitleBarHeight)
        {
            return new Rectangle(0, 0, clientWidth - captionButtonsWidth, scaledTitleBarHeight);
        }

        // --- 公共方法 ---

        /// <summary>
        /// 初始化自定义标题栏。调用 DwmExtendFrameIntoClientArea 并触发首次布局。
        /// 应在窗口 Load 事件后调用。
        /// </summary>
        public void Initialize()
        {
            if (!_hostForm.IsHandleCreated)
            {
                // 窗口句柄未就绪，延迟到 HandleCreated 事件
                _hostForm.HandleCreated += (s, e) => Initialize();
                return;
            }

            // 扩展客户区到整个窗口帧（MARGINS(-1) 表示全部扩展）
            var margins = new Win32API.MARGINS(-1);
            Win32API.DwmExtendFrameIntoClientArea(_hostForm.Handle, in margins);

            // 获取初始 DPI
            try
            {
                int dpi = Win32API.GetDpiForWindow(_hostForm.Handle);
                if (dpi > 0)
                    _currentDpi = dpi;
            }
            catch
            {
                // GetDpiForWindow 不可用时回退到 Graphics.DpiX
                using (var g = _hostForm.CreateGraphics())
                {
                    _currentDpi = (int)g.DpiX;
                }
            }

            // 计算初始标题栏按钮区域
            // DWM 绘制的按钮区域从窗口右边缘向左延伸，包含系统边框。
            // 使用 SM_CXSIZE * 3 + SM_CXPADDEDBORDER 来匹配 DWM 实际按钮宽度，
            // 并让 CaptionButtonsBounds 延伸到窗口右边缘，消除间隙。
            int captionButtonWidth = ScaleDimension(46, _currentDpi);
            int totalButtonsWidth = captionButtonWidth * 3;
            int clientWidth = _hostForm.ClientSize.Width;
            CaptionButtonsBounds = new Rectangle(
                clientWidth - totalButtonsWidth, 0,
                totalButtonsWidth, ScaledTitleBarHeight);

            _initialized = true;

            // 触发 WM_NCCALCSIZE 重新计算
            Win32API.SetWindowPos(
                _hostForm.Handle, IntPtr.Zero,
                0, 0, 0, 0,
                Win32API.SWP_NOMOVE | Win32API.SWP_NOSIZE | Win32API.SWP_NOZORDER | Win32API.SWP_FRAMECHANGED);
        }

        /// <summary>
        /// 处理窗口消息。IslandWindow 应在 WndProc 中调用此方法。
        /// </summary>
        /// <param name="m">窗口消息</param>
        /// <returns>true 表示消息已处理，IslandWindow 不应调用 base.WndProc</returns>
        public bool ProcessMessage(ref Message m)
        {
            if (!_initialized || !_extendsContentIntoTitleBar)
                return false;

            switch (m.Msg)
            {
                case Win32API.WM_NCCALCSIZE:
                    return HandleNcCalcSize(ref m);
                case Win32API.WM_NCHITTEST:
                    return HandleNcHitTest(ref m);
                case Win32API.WM_SIZE:
                    HandleSize(ref m);
                    return false;
                case Win32API.WM_DPICHANGED:
                    HandleDpiChanged(ref m);
                    return false;
                case Win32API.WM_SETTINGCHANGE:
                    HandleSettingChange(ref m);
                    return false;
                case Win32API.WM_GETMINMAXINFO:
                    HandleGetMinMaxInfo(ref m);
                    return false;
                default:
                    return false;
            }
        }

        // --- 消息处理内部方法 ---

        /// <summary>
        /// 处理 WM_NCCALCSIZE：移除系统标题栏和边框，使客户区覆盖整个窗口。
        /// 最大化时补偿系统边框以避免内容溢出屏幕边缘。
        /// </summary>
        private bool HandleNcCalcSize(ref Message m)
        {
            // wParam == 1 表示需要计算有效客户区
            if (m.WParam != IntPtr.Zero)
            {
                // 最大化时补偿系统边框
                if (_isMaximized)
                {
                    var nccsp = Marshal.PtrToStructure<Win32API.NCCALCSIZE_PARAMS>(m.LParam);
                    int borderPadding = Win32API.GetSystemMetrics(Win32API.SM_CXFRAME)
                                      + Win32API.GetSystemMetrics(Win32API.SM_CXPADDEDBORDER);
                    nccsp.rgrc0 = ApplyMaximizePadding(nccsp.rgrc0, borderPadding);
                    Marshal.StructureToPtr(nccsp, m.LParam, false);
                }
            }

            m.Result = IntPtr.Zero;
            return true;
        }

        /// <summary>
        /// 处理 WM_NCHITTEST：根据鼠标位置返回命中测试结果。
        /// </summary>
        private bool HandleNcHitTest(ref Message m)
        {
            // 从 lParam 提取鼠标屏幕坐标
            long lParam = m.LParam.ToInt64();
            int screenX = (short)(lParam & 0xFFFF);
            int screenY = (short)((lParam >> 16) & 0xFFFF);

            // 转换为客户区坐标
            Point clientPoint = _hostForm.PointToClient(new Point(screenX, screenY));

            // 计算调整大小边框厚度
            int resizeBorder = ScaleDimension(4, _currentDpi);

            // 调用纯函数进行命中测试
            int hitResult = HitTest(
                clientPoint,
                ScaledTitleBarHeight,
                _isMaximized,
                _dragRectangles,
                CaptionButtonsBounds,
                _hostForm.ClientSize.Width,
                _hostForm.ClientSize.Height,
                resizeBorder);

            // 先让 DWM 处理所有 WM_NCHITTEST，以获得标题栏按钮的 hover 效果
            IntPtr dwmResult;
            int dwmHandled = Win32API.DwmDefWindowProc(
                _hostForm.Handle, m.Msg, m.WParam, m.LParam, out dwmResult);
            if (dwmHandled != 0)
            {
                m.Result = dwmResult;
                return true;
            }

            m.Result = new IntPtr(hitResult);
            return true;
        }

        /// <summary>
        /// 处理 WM_SIZE：更新最大化状态、标题栏布局，触发拖拽区域失效事件。
        /// </summary>
        private void HandleSize(ref Message m)
        {
            // wParam: 0=SIZE_RESTORED, 1=SIZE_MINIMIZED, 2=SIZE_MAXIMIZED
            int wParam = m.WParam.ToInt32();
            _isMaximized = (wParam == 2);

            // 更新标题栏按钮区域
            UpdateCaptionButtonsBounds();

            // 更新标题栏宿主 Panel 布局
            UpdateTitleBarHostPanelBounds();

            // 触发拖拽区域失效事件
            DragRegionInvalidated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 处理 WM_DPICHANGED：更新 DPI 并重新计算所有缩放尺寸。
        /// </summary>
        private void HandleDpiChanged(ref Message m)
        {
            // wParam 低字 = 新 X DPI
            int newDpi = (short)(m.WParam.ToInt64() & 0xFFFF);
            if (newDpi > 0)
                _currentDpi = newDpi;

            // 重新计算标题栏按钮区域
            UpdateCaptionButtonsBounds();

            // 更新标题栏宿主 Panel 布局
            UpdateTitleBarHostPanelBounds();
        }

        /// <summary>
        /// 处理 WM_SETTINGCHANGE：检测系统主题变化。
        /// </summary>
        private void HandleSettingChange(ref Message m)
        {
            // 检查 lParam 是否指向 "ImmersiveColorSet" 字符串
            if (m.LParam != IntPtr.Zero)
            {
                string setting = Marshal.PtrToStringUni(m.LParam);
                if (string.Equals(setting, "ImmersiveColorSet", StringComparison.OrdinalIgnoreCase))
                {
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// 处理 WM_GETMINMAXINFO：确保最大化时窗口不超出工作区域。
        /// 通过设置 ptMaxSize 和 ptMaxPosition 约束最大化尺寸和位置。
        /// </summary>
        private void HandleGetMinMaxInfo(ref Message m)
        {
            var screen = System.Windows.Forms.Screen.FromHandle(_hostForm.Handle);
            var workArea = screen.WorkingArea;

            var mmi = Marshal.PtrToStructure<Win32API.MINMAXINFO>(m.LParam);
            mmi.ptMaxPosition = new Win32API.POINT { X = workArea.X, Y = workArea.Y };
            mmi.ptMaxSize = new Win32API.POINT { X = workArea.Width, Y = workArea.Height };
            Marshal.StructureToPtr(mmi, m.LParam, false);
        }


        // --- 内部辅助方法 ---

        /// <summary>
        /// 将 Color 转换为 Win32 COLORREF 格式（0x00BBGGRR）。
        /// </summary>
        internal static uint ColorToColorRef(Color color)
        {
            return (uint)(color.R | (color.G << 8) | (color.B << 16));
        }

        /// <summary>
        /// 调用 DwmSetWindowAttribute 设置 uint 类型的窗口属性。
        /// </summary>
        private void SetDwmAttribute(uint attribute, uint value)
        {
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(uint));
            try
            {
                Marshal.WriteInt32(ptr, (int)value);
                Win32API.DwmSetWindowAttribute(_hostForm.Handle, attribute, ptr, sizeof(uint));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// 确保标题栏宿主 Panel 已创建并添加到宿主窗口。
        /// </summary>
        private void EnsureTitleBarHostPanel()
        {
            if (_titleBarHostPanel != null)
                return;

            _titleBarHostPanel = new Panel();
            UpdateTitleBarHostPanelBounds();
            _hostForm.Controls.Add(_titleBarHostPanel);
            _titleBarHostPanel.BringToFront();
        }

        /// <summary>
        /// 移除标题栏宿主 Panel。
        /// </summary>
        private void RemoveTitleBarHostPanel()
        {
            if (_titleBarHostPanel == null)
                return;

            _hostForm.Controls.Remove(_titleBarHostPanel);
            _titleBarHostPanel.Dispose();
            _titleBarHostPanel = null;
        }

        /// <summary>
        /// 更新标题栏宿主 Panel 的位置和大小。
        /// </summary>
        private void UpdateTitleBarHostPanelBounds()
        {
            if (_titleBarHostPanel == null)
                return;

            var bounds = CalculateTitleBarHostBounds(
                _hostForm.ClientSize.Width,
                CaptionButtonsBounds.Width,
                ScaledTitleBarHeight);
            _titleBarHostPanel.Bounds = bounds;
        }

        /// <summary>
        /// 更新标题栏按钮区域（基于当前 DPI 和窗口宽度）。
        /// </summary>
        private void UpdateCaptionButtonsBounds()
        {
            int captionButtonWidth = ScaleDimension(46, _currentDpi);
            int totalButtonsWidth = captionButtonWidth * 3;
            int clientWidth = _hostForm.ClientSize.Width;
            CaptionButtonsBounds = new Rectangle(
                clientWidth - totalButtonsWidth, 0,
                totalButtonsWidth, ScaledTitleBarHeight);
        }

        /// <summary>
        /// 设置标题栏中的可拖拽区域。
        /// 传入 null 等同于 ResetDragRectangles()（整个标题栏可拖拽）。
        /// 传入空数组表示无可拖拽区域。
        /// </summary>
        /// <param name="rects">可拖拽矩形区域数组（客户区坐标），null 表示恢复默认</param>
        public void SetDragRectangles(Rectangle[] rects)
        {
            if (rects == null)
            {
                ResetDragRectangles();
                return;
            }
            _dragRectangles = rects;
        }


        /// <summary>
        /// 清除自定义拖拽区域，恢复默认行为（整个标题栏除按钮外均可拖拽）。
        /// </summary>
        public void ResetDragRectangles()
        {
            _dragRectangles = null;
        }
    }
}

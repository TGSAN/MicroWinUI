using HelloXbox;
using MicroWinUICore;
using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace MicroWinUI
{
    internal class CodePage : Page
    {
        public SwapChainPanel SwapChainPanel { get; private set; }

        public ComboBox GpuComboBox { get; private set; }

        public Button ToggleBtn { get; private set; }

        public CheckBox AutoRotateCheck { get; private set; }

        public TextBlock FpsText { get; private set; }

        private void InitializeComponent()
        {
            // Root grid
            var rootGrid = new Grid();
            rootGrid.Background = new SolidColorBrush(Windows.UI.Colors.Black);

            // Viewbox with SwapChainPanel
            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform
            };

            SwapChainPanel = new SwapChainPanel
            {
                Name = "SwapChainPanel",
                Width = 1024,
                Height = 1024
            };

            viewbox.Child = SwapChainPanel;
            rootGrid.Children.Add(viewbox);

            // Left panel with controls
            var leftPanel = new StackPanel
            {
                Padding = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0)
            };

            GpuComboBox = new ComboBox
            {
                Name = "GpuComboBox",
                Width = 320,
                Margin = new Thickness(0, 0, 0, 10)
            };
            GpuComboBox.SelectionChanged += GpuComboBox_SelectionChanged;
            leftPanel.Children.Add(GpuComboBox);

            ToggleBtn = new Button
            {
                Name = "ToggleBtn",
                Content = "Start"
            };
            ToggleBtn.Click += ToggleBtn_Click;
            leftPanel.Children.Add(ToggleBtn);

            AutoRotateCheck = new CheckBox
            {
                Name = "AutoRotateCheck",
                Content = "Auto Rotate",
                IsChecked = true,
                Margin = new Thickness(0, 10, 0, 0)
            };
            leftPanel.Children.Add(AutoRotateCheck);

            rootGrid.Children.Add(leftPanel);

            // Right panel with FPS text
            var rightPanel = new StackPanel
            {
                Padding = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0)
            };

            FpsText = new TextBlock
            {
                Name = "FpsText",
                Text = "FPS: 0",
                FontSize = 20
            };
            rightPanel.Children.Add(FpsText);

            rootGrid.Children.Add(rightPanel);

            Content = rootGrid;
        }

        // Designer End

        public CodePage(IslandWindow coreWindowHost)
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
            this.Unloaded += MainPage_Unloaded;
            this.RequestedTheme = ElementTheme.Dark;
        }

        private D3D12Renderer _renderer;
        private bool _isRendering = false;

        // Input State
        private bool _isLeftPressed = false;
        private bool _isRightPressed = false;
        private Point _lastMousePos;

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Input Events
            SwapChainPanel.PointerPressed += SwapChainPanel_PointerPressed;
            SwapChainPanel.PointerReleased += SwapChainPanel_PointerReleased;
            SwapChainPanel.PointerMoved += SwapChainPanel_PointerMoved;
            SwapChainPanel.PointerWheelChanged += SwapChainPanel_PointerWheelChanged;

            // Initialize GPU list
            var adapters = D3D12Renderer.GetHardwareAdapters();
            GpuComboBox.ItemsSource = adapters;
            if (adapters.Count > 0)
            {
                GpuComboBox.SelectedIndex = 0; // This will trigger SelectionChanged and create the renderer
            }
        }

        private void GpuComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GpuComboBox.SelectedIndex < 0) return;

            // Preserve state
            float len = 1.6f, ang1 = 2.8f, ang2 = 0.4f;
            float cenx = 0, ceny = 0, cenz = 0;

            if (_renderer != null)
            {
                len = _renderer.Len;
                ang1 = _renderer.Ang1;
                ang2 = _renderer.Ang2;
                cenx = _renderer.CenX;
                ceny = _renderer.CenY;
                cenz = _renderer.CenZ;

                _renderer.Dispose();
                _renderer = null;
            }

            try
            {
                _renderer = new D3D12Renderer(SwapChainPanel, GpuComboBox.SelectedIndex);

                // Restore state
                _renderer.Len = len;
                _renderer.Ang1 = ang1;
                _renderer.Ang2 = ang2;
                _renderer.CenX = cenx;
                _renderer.CenY = ceny;
                _renderer.CenZ = cenz;
            }
            catch (Exception ex)
            {
                // Fallback or error logging could go here
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        private void SwapChainPanel_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(SwapChainPanel).Properties;
            if (props.IsLeftButtonPressed) _isLeftPressed = true;
            if (props.IsRightButtonPressed) _isRightPressed = true;
            _lastMousePos = e.GetCurrentPoint(SwapChainPanel).Position;
            SwapChainPanel.CapturePointer(e.Pointer);
        }

        private void SwapChainPanel_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(SwapChainPanel).Properties;
            if (!props.IsLeftButtonPressed) _isLeftPressed = false;
            if (!props.IsRightButtonPressed) _isRightPressed = false;
            SwapChainPanel.ReleasePointerCapture(e.Pointer);
        }

        private void SwapChainPanel_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_renderer == null) return;

            var currentPos = e.GetCurrentPoint(SwapChainPanel).Position;
            double dx = currentPos.X - _lastMousePos.X;
            double dy = currentPos.Y - _lastMousePos.Y;

            if (_isLeftPressed)
            {
                _renderer.Ang1 += (float)(dx * 0.002);
                _renderer.Ang2 += (float)(dy * 0.002);
            }

            if (_isRightPressed)
            {
                float cx = (float)SwapChainPanel.ActualWidth;
                float cy = (float)SwapChainPanel.ActualHeight;
                float l = _renderer.Len * 4.0f / (cx + cy);

                _renderer.CenX += l * (-(float)dx * (float)Math.Sin(_renderer.Ang1) - (float)dy * (float)Math.Sin(_renderer.Ang2) * (float)Math.Cos(_renderer.Ang1));
                _renderer.CenY += l * ((float)dy * (float)Math.Cos(_renderer.Ang2));
                _renderer.CenZ += l * ((float)dx * (float)Math.Cos(_renderer.Ang1) - (float)dy * (float)Math.Sin(_renderer.Ang2) * (float)Math.Sin(_renderer.Ang1));
            }

            _lastMousePos = currentPos;
        }

        private void SwapChainPanel_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (_renderer == null) return;
            var delta = e.GetCurrentPoint(SwapChainPanel).Properties.MouseWheelDelta;
            _renderer.Len *= (float)Math.Exp(-0.001 * delta);
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _renderer?.Dispose();
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            SwapChainPanel.PointerPressed -= SwapChainPanel_PointerPressed;
            SwapChainPanel.PointerReleased -= SwapChainPanel_PointerReleased;
            SwapChainPanel.PointerMoved -= SwapChainPanel_PointerMoved;
            SwapChainPanel.PointerWheelChanged -= SwapChainPanel_PointerWheelChanged;
        }

        private void ToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            _isRendering = !_isRendering;
            ToggleBtn.Content = _isRendering ? "Stop" : "Start";
            FpsText.Text = "FPS: 0";

            if (_isRendering)
            {
                CompositionTarget.Rendering += CompositionTarget_Rendering;
            }
            else
            {
                CompositionTarget.Rendering -= CompositionTarget_Rendering;
            }
        }

        private DateTime _lastFpsUpdate = DateTime.Now;
        private int _frameCount = 0;

        private void CompositionTarget_Rendering(object sender, object e)
        {
            if (_isRendering && _renderer != null)
            {
                if (AutoRotateCheck.IsChecked == true)
                {
                    _renderer.Ang1 += 0.01f;
                }
                _renderer.Render();
            }

            // FPS Calculation
            _frameCount++;
            var now = DateTime.Now;
            if ((now - _lastFpsUpdate).TotalSeconds >= 0.5)
            {
                var fps = _frameCount / (now - _lastFpsUpdate).TotalSeconds;
                FpsText.Text = $"FPS: {fps:F1}";
                _frameCount = 0;
                _lastFpsUpdate = now;
            }
        }
    }
}

using MicroWinUICore;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

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
        private float _splitterPosition = 0.5f;
        private bool _isDraggingSplitter = false;
        private bool _isSeeking = false;
        private bool _isPlaying = false; // Manual control flag for UI stability
        private bool _isUpdatingSlider = false;
        private DispatcherTimer _timer;
        private TimeSpan _frameStep = TimeSpan.FromMilliseconds(33); // Dynamic frame step

        private MediaPlayer _mp1;
        private MediaPlayer _mp2;
        private MediaTimelineController _timelineController;
        private IslandWindow _host;

        public MainPage(IslandWindow coreWindowHost)
        {
            this.InitializeComponent();
            _host = coreWindowHost;
            _host.Backdrop = IslandWindow.SystemBackdrop.Mica;
            InitializePlayers();
            InitializeTimer();

            // Use AddHandler to capture handled events (crucial for Slider)
            TimeSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(TimeSlider_PointerPressed), true);
            TimeSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(TimeSlider_PointerReleased), true);
        }

        private void InitializePlayers()
        {
            // Create a single TimelineController for sync
            _timelineController = new MediaTimelineController();

            // Initialize Player 1
            Player1.SetMediaPlayer(new MediaPlayer());
            _mp1 = Player1.MediaPlayer;
            _mp1.CommandManager.IsEnabled = false; // Disable individual control
            _mp1.TimelineController = _timelineController;
            _mp1.MediaOpened += OnMediaOpened;

            // Initialize Player 2
            Player2.SetMediaPlayer(new MediaPlayer());
            _mp2 = Player2.MediaPlayer;
            _mp2.CommandManager.IsEnabled = false;
            _mp2.TimelineController = _timelineController;
            _mp2.MediaOpened += OnMediaOpened;
        }

        private void InitializeTimer()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(16); // Default to 60fps for smoother UI
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        private TimeSpan _lastThrottledPos = TimeSpan.Zero;
        private DateTime _lastThrottleTime = DateTime.MinValue;

        private void TimeSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!_isUpdatingSlider)
            {
                // User is seeking
                _isPlaying = false;
                _timelineController.Pause();

                var newPos = TimeSpan.FromSeconds(e.NewValue);
                
                // Throttle updates to prevent flooding the controller (dead loop/freeze)
                var now = DateTime.UtcNow;
                if ((now - _lastThrottleTime).TotalMilliseconds > 50 || Math.Abs((newPos - _lastThrottledPos).TotalMilliseconds) > 500)
                {
                    _timelineController.Position = newPos;
                    UpdateDisplayTime();
                    
                    _lastThrottledPos = newPos;
                    _lastThrottleTime = now;
                }
            }
        }

        private void OnTimerTick(object sender, object e)
        {
            // Only rely on manual flag. Engine state might be volatile during seeks.
            if (_isPlaying && !_isSeeking) 
            {
                var d1 = _mp1.PlaybackSession.NaturalDuration;
                var d2 = _mp2.PlaybackSession.NaturalDuration;
                var maxDuration = (d1 > d2) ? d1 : d2;

                if (maxDuration.TotalSeconds > 0)
                {
                    if (_timelineController.Position >= maxDuration)
                    {
                        // Video ended
                        _isPlaying = false;
                        _timelineController.Pause();
                        _timelineController.Position = maxDuration; // Clamp to end
                        
                        _isUpdatingSlider = true;
                        TimeSlider.Value = maxDuration.TotalSeconds;
                        _isUpdatingSlider = false;
                        UpdateDisplayTime();
                    }
                    else
                    {
                        _isUpdatingSlider = true;
                        TimeSlider.Value = _timelineController.Position.TotalSeconds;
                        _isUpdatingSlider = false;
                        UpdateDisplayTime();
                    }
                }
            }
        }

        private async void LoadLeftVideo_Click(object sender, RoutedEventArgs e)
        {
            var file = await PickVideoFileAsync();
            if (file != null)
            {
                LeftFileText.Text = file.Name;
                ResetTimeline();
                // Wrap in MediaPlaybackItem to access track properties
                var source = MediaSource.CreateFromStorageFile(file);
                var item = new MediaPlaybackItem(source);
                _mp1.Source = item;
            }
        }

        private async void LoadRightVideo_Click(object sender, RoutedEventArgs e)
        {
            var file = await PickVideoFileAsync();
            if (file != null)
            {
                RightFileText.Text = file.Name;
                ResetTimeline();
                var source = MediaSource.CreateFromStorageFile(file);
                var item = new MediaPlaybackItem(source);
                _mp2.Source = item;
            }
        }
        
        private void ResetTimeline()
        {
            _isPlaying = false;
            _timelineController.Pause();
            _timelineController.Position = TimeSpan.Zero;
            _isUpdatingSlider = true;
            TimeSlider.Value = 0;
            _isUpdatingSlider = false;
            UpdateDisplayTime();
        }

        private async Task<StorageFile> PickVideoFileAsync()
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            picker.FileTypeFilter.Add(".mp4");
            picker.FileTypeFilter.Add(".mkv");
            picker.FileTypeFilter.Add(".avi");
            picker.FileTypeFilter.Add(".mov");
            picker.FileTypeFilter.Add(".wmv");
            
            if (_host != null)
            {
                var init = (IInitializeWithWindow)(object)picker;
                init.Initialize(_host.Handle);
            }
            
            return await picker.PickSingleFileAsync();
        }

        private void OnMediaOpened(MediaPlayer sender, object args)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Ensure controller is attached when media opens
                sender.CommandManager.IsEnabled = false;
                sender.TimelineController = _timelineController;

                // Attempt to get real frame rate from specific player
                if (sender.Source is MediaPlaybackItem item && item.VideoTracks.Count > 0)
                {
                    var props = item.VideoTracks[0].GetEncodingProperties();
                    if (props.FrameRate.Numerator > 0 && props.FrameRate.Denominator > 0)
                    {
                        double fps = (double)props.FrameRate.Numerator / props.FrameRate.Denominator;
                        if (fps > 0)
                        {
                            _frameStep = TimeSpan.FromSeconds(1.0 / fps);
                            _timer.Interval = _frameStep;
                        }
                    }
                }

                UpdateTimelineDuration();
            });
        }
        
        private void UpdateTimelineDuration()
        {
             var d1 = _mp1.PlaybackSession.NaturalDuration;
             var d2 = _mp2.PlaybackSession.NaturalDuration;
             var maxDuration = (d1 > d2) ? d1 : d2;
             
             TimeSlider.Maximum = maxDuration.TotalSeconds;
             UpdateDisplayTime();
        }
        
        private void UpdateDisplayTime()
        {
            var current = _timelineController.Position;
            var d1 = _mp1.PlaybackSession.NaturalDuration;
            var d2 = _mp2.PlaybackSession.NaturalDuration;
            var maxDuration = (d1 > d2) ? d1 : d2;
            
            TimeText.Text = $"{current:hh\\:mm\\:ss\\.fff} / {maxDuration:hh\\:mm\\:ss\\.fff}";
        }

        #region Playback Controls

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying)
            {
                StepFrame(true);
                StepFrame(false);
            }
            else
            {
                _isPlaying = true;
                _timelineController.Resume();
            }
        }

        private void StepForward_Click(object sender, RoutedEventArgs e)
        {
            StepFrame(true);
            if (_isPlaying)
            {
                StepFrame(true);
            }
        }

        private void StepBack_Click(object sender, RoutedEventArgs e)
        {
            StepFrame(false);
            if (_isPlaying)
            {
                StepFrame(false);
            }
        }

        private void StepFrame(bool forward)
        {
            _isPlaying = false;
            _timelineController.Pause();

            TimeSpan step = _frameStep; 
            if (!forward) step = -step;

            // Use Slider value as current reference to prevent jump-back
            var currentPos = TimeSpan.FromSeconds(TimeSlider.Value);
            var newPos = currentPos + step;
            
            // Clamp
            if (newPos < TimeSpan.Zero) newPos = TimeSpan.Zero;
            var duration = TimeSpan.FromSeconds(TimeSlider.Maximum); 
            if (newPos > duration) newPos = duration;

            // 1. Update Slider visually (guarded to prevent triggering ValueChanged)
            _isUpdatingSlider = true;
            TimeSlider.Value = newPos.TotalSeconds;
            _isUpdatingSlider = false;

            // 2. Explicitly update Controller to match new Slider value
            _timelineController.Position = newPos;
            UpdateDisplayTime();
        }

        private void TimeSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isSeeking = true;
            _isPlaying = false; // Pause when seeking starts
            _timelineController.Pause();
        }

        private void TimeSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isSeeking = false;
            // Explicitly commit the final position to be safe
            var newPos = TimeSpan.FromSeconds(TimeSlider.Value);
            _timelineController.Position = newPos;
        }

        #endregion

        #region Splitter Logic

        private void UpdateSplitterVisuals()
        {
            if (OverlayCanvas.ActualWidth == 0) return;

            var w = OverlayCanvas.ActualWidth;
            var h = OverlayCanvas.ActualHeight;
            var x = w * _splitterPosition;

            // Update Line
            SplitLine.Height = h;
            Canvas.SetLeft(SplitLine, x - 1); 
            Canvas.SetTop(SplitLine, 0);

            // Update Thumb
            double thumbSize = SplitThumb.Width;
            Canvas.SetLeft(SplitThumb, x - thumbSize / 2);
            Canvas.SetTop(SplitThumb, h / 2 - thumbSize / 2);
        }

        private void VideoContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateClipRects();
            UpdateSplitterVisuals();
        }

        private void UpdateClipRects()
        {
            if (VideoContainer.ActualWidth == 0) return;

            double width = VideoContainer.ActualWidth;
            double height = VideoContainer.ActualHeight;
            double splitX = width * _splitterPosition;

            var leftClip = new RectangleGeometry();
            leftClip.Rect = new Windows.Foundation.Rect(0, 0, splitX, height);
            LeftPlayerContainer.Clip = leftClip;

            var rightClip = new RectangleGeometry();
            rightClip.Rect = new Windows.Foundation.Rect(splitX, 0, width - splitX, height);
            RightPlayerContainer.Clip = rightClip;
        }

        private void OverlayCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingSplitter = true;
            OverlayCanvas.CapturePointer(e.Pointer);
        }

        private void OverlayCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingSplitter = false;
            OverlayCanvas.ReleasePointerCapture(e.Pointer);
        }

        private void OverlayCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDraggingSplitter)
            {
                var pt = e.GetCurrentPoint(OverlayCanvas).Position;
                var w = OverlayCanvas.ActualWidth;
                
                _splitterPosition = (float)(pt.X / w);
                
                // Clamp
                if (_splitterPosition < 0) _splitterPosition = 0;
                if (_splitterPosition > 1) _splitterPosition = 1;

                UpdateClipRects();
                UpdateSplitterVisuals();
            }
        }
        
        #endregion
    }
}
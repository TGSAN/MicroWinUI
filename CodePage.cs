using MicroWinUICore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Graphics.Display;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MicroWinUI
{
    internal class CodePage : Page
    {
        IslandWindow coreWindow;
        DisplayInformation displayInfo;
        StackPanel mainStackPanel;
        TextBlock displayInfoTextBlock;

        public CodePage(IslandWindow coreWindow)
        {
            this.coreWindow = coreWindow;
            displayInfo = DisplayInformation.GetForCurrentView();
            displayInfo.AdvancedColorInfoChanged += DisplayInfo_AdvancedColorInfoChanged;
            mainStackPanel = new StackPanel();
            mainStackPanel.Orientation = Orientation.Vertical;
            mainStackPanel.Spacing = 32;
            mainStackPanel.HorizontalAlignment = HorizontalAlignment.Center;
            mainStackPanel.VerticalAlignment = VerticalAlignment.Center;
            displayInfoTextBlock = new TextBlock();
            mainStackPanel.Children.Add(displayInfoTextBlock);
            var restartButton = new Button();
            if (Application.Current.Resources.TryGetValue("ButtonRevealStyle", out var style))
            {
                restartButton.Style = style as Style;
            }
            restartButton.Content = "切换数据到窗口所在显示器";
            restartButton.Click += RestartButton_Click;
            mainStackPanel.Children.Add(restartButton);
            Content = mainStackPanel;
            InvalidateArrange();
            coreWindow.Backdrop = IslandWindow.SystemBackdrop.Tabbed;
            UpdateDisplayInfo();
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            coreWindow.Content = new CodePage(coreWindow);
        }

        private void UpdateDisplayInfo()
        {
            var colorInfo = displayInfo.GetAdvancedColorInfo();
            var advancedColor = colorInfo.CurrentAdvancedColorKind;
            var advancedColorStr = "SDR";
            switch (advancedColor)
            {
                case AdvancedColorKind.StandardDynamicRange:
                    advancedColorStr = "Standard Dynamic Range";
                    break;
                case AdvancedColorKind.WideColorGamut:
                    advancedColorStr = "Wide Color Gamut";
                    break;
                case AdvancedColorKind.HighDynamicRange:
                    advancedColorStr = "High Dynamic Range";
                    break;
                default:
                    advancedColorStr = advancedColor.ToString();
                    break;
            }
            displayInfoTextBlock.Text = $"最高亮度（峰值）：{colorInfo.MaxLuminanceInNits} 尼特";
            displayInfoTextBlock.Text += $"\r\n最高亮度（全屏）：{colorInfo.MaxAverageFullFrameLuminanceInNits} 尼特";
            displayInfoTextBlock.Text += $"\r\n最低亮度：{colorInfo.MinLuminanceInNits} 尼特";
            displayInfoTextBlock.Text += $"\r\nSDR亮度：{colorInfo.SdrWhiteLevelInNits} 尼特";
            displayInfoTextBlock.Text += $"\r\n";
            displayInfoTextBlock.Text += $"\r\n色彩模式：{advancedColorStr}";
            displayInfoTextBlock.Text += $"\r\n";
            displayInfoTextBlock.Text += $"\r\n红：{colorInfo.RedPrimary}";
            displayInfoTextBlock.Text += $"\r\n绿：{colorInfo.GreenPrimary}";
            displayInfoTextBlock.Text += $"\r\n蓝：{colorInfo.BluePrimary}";
            displayInfoTextBlock.Text += $"\r\n";
            displayInfoTextBlock.Text += $"\r\n白点：{colorInfo.WhitePoint}";
        }

        private void DisplayInfo_AdvancedColorInfoChanged(DisplayInformation sender, object args)
        {
            UpdateDisplayInfo();
        }
    }
}

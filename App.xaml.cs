using Mile.Xaml;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Markup;

namespace MicroWinUI
{
    sealed partial class App : Application
    {
        public App()
        {
            this.ThreadInitialize();

            // 从嵌入资源加载 SunValley 控件样式，替代原先依赖 resources.pri 的 XBF 资源。
            this.Resources = LoadEmbeddedStyles();
        }

        public void Close()
        {
            this.Exit();
            this.ThreadUninitialize();
        }

        private static ResourceDictionary LoadEmbeddedStyles()
        {
            string xaml = XamlPageLoader.ReadEmbeddedText("SunValley.xaml");
            if (xaml == null)
            {
                return new ResourceDictionary();
            }

            var styles = (ResourceDictionary)XamlReader.Load(xaml);

            var root = new ResourceDictionary();
            root.MergedDictionaries.Add(styles);
            return root;
        }
    }
}

using MicroWinUI;
using System;

namespace MicroWinUICore
{
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            App app = new();

            var window = new IslandWindow();
            window.XamlIslandContent = new MainPage(window);
            window.Width = 1280;
            window.Height = 720;
            window.Title = "App";

            window.ShowDialog();

            app.Close();
        }
    }
}

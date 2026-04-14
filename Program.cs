using MicroWinUI;
using System;
using System.Windows.Forms;

namespace MicroWinUICore
{
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

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

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
            window.Content = new CodePage(window);
            window.ClientSize = new System.Drawing.Size(1280, 720);
            window.Text = "App";
            window.ShowIcon = false;

            Application.Run(window);

            app.Close();
        }
    }
}

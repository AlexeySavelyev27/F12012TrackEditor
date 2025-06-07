// App.xaml.cs
using System.Windows;

namespace PSSGEditor
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            if (e.Args.Length > 0)
            {
                string file = e.Args[0];
                if (System.IO.File.Exists(file))
                {
                    await mainWindow.LoadFileAsync(file);
                }
            }

            mainWindow.Show();
        }
    }
}

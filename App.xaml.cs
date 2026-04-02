using System.Windows;
using ToiseApp.Model;
using ToiseApp.View;
using ToiseApp.ViewModel;

namespace ToiseApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Composition root : câblage manuel des dépendances
            var service    = new ToiseService();
            var viewModel  = new ToiseViewModel(service);
            var mainWindow = new MainWindow { DataContext = viewModel };

            MainWindow = mainWindow;
            mainWindow.Show();
        }
    }
}

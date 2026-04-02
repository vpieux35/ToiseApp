using System.Windows;
using ToiseApp.Model;
using ToiseApp.View;
using ToiseApp.ViewModel;
using WpfActiback.Model.Metier;

namespace ToiseApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── Composition root ─────────────────────────────────────────────
            // C'est ici que toutes les dépendances sont instanciées.
            // Adaptez selon votre configuration réelle.

            // 1. ACTValise :
            //    - Option A (sans capteur de force) : utilisez DefaultValise.Create()
            //    - Option B (avec capteur réel)      : instanciez votre vraie ACTValise
            ACTValise valise = DefaultValise.Create(hauteurMaxiCm: 210);

            // 2. Service qui encapsule le vérin Linak
            var service = new ToiseService(valise);

            // 3. ViewModel
            var viewModel = new ToiseViewModel(service);

            // 4. Vue
            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            MainWindow = mainWindow;
            mainWindow.Show();
        }
    }
}

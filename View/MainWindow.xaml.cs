using System.Windows;
using ToiseApp.ViewModel;

namespace ToiseApp.View
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Libère le ViewModel (qui ferme le vérin) à la fermeture de la fenêtre.
        /// </summary>
        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            (DataContext as ToiseViewModel)?.Dispose();
        }
    }
}

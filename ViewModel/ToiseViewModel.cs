using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ToiseApp.Helpers;
using ToiseApp.Model;

namespace ToiseApp.ViewModel
{
    /// <summary>
    /// ViewModel principal : lie la vue aux opérations du ToiseService.
    /// Actualise la position toutes les 500 ms via un timer.
    /// </summary>
    public class ToiseViewModel : INotifyPropertyChanged, IDisposable
    {
        // ── Service (Model) ───────────────────────────────────────────────────
        private readonly ToiseService _service;

        // ── Timer de rafraîchissement de position ─────────────────────────────
        private readonly DispatcherTimer _refreshTimer;

        // ── Champs de backing ─────────────────────────────────────────────────
        private float _currentHeightMm;
        private float _targetHeightMm = 1530f;   // valeur par défaut : 153 cm
        private float _stepMm = 10f;      // pas de déplacement en mm
        private bool _isConnected;
        private string _statusMessage = "Initialisation…";
        private bool _isBusy;

        // ── Constructeur ──────────────────────────────────────────────────────
        public ToiseViewModel(ToiseService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));

            // Abonnement à la déconnexion
            _service.Disconnected += OnVerinDisconnected;

            // Initialisation de l'état
            IsConnected = _service.IsConnected;
            StatusMessage = IsConnected ? "Connecté" : "Vérin non détecté";

            // Commandes
            MoveUpCommand = new RelayCommand(_ => ExecuteMoveUp(), _ => CanOperate);
            MoveDownCommand = new RelayCommand(_ => ExecuteMoveDown(), _ => CanOperate);
            StopCommand = new RelayCommand(_ => ExecuteStop(), _ => CanOperate);
            MoveToCommand = new RelayCommand(_ => ExecuteMoveTo(), _ => CanOperate);
            StepUpCommand = new RelayCommand(_ => ExecuteStepUp(), _ => CanOperate);
            StepDownCommand = new RelayCommand(_ => ExecuteStepDown(), _ => CanOperate);
            ReconnectCommand = new RelayCommand(_ => ExecuteReconnect(), _ => !IsBusy);

            // Timer de rafraîchissement (toutes les 500 ms)
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _refreshTimer.Tick += OnRefreshTick;

            if (IsConnected)
                _refreshTimer.Start();
        }

        // ── Propriétés liées ──────────────────────────────────────────────────

        /// <summary>Position courante de la toise en mm.</summary>
        public float CurrentHeightMm
        {
            get => _currentHeightMm;
            private set { if (Set(ref _currentHeightMm, value)) OnPropertyChanged(nameof(CurrentHeightCm)); }
        }

        /// <summary>Position courante affichée en cm (lecture seule).</summary>
        public string CurrentHeightCm => $"{_currentHeightMm / 10f:F1} cm";

        /// <summary>Hauteur cible saisie par l'utilisateur, en mm.</summary>
        public float TargetHeightMm
        {
            get => _targetHeightMm;
            set => Set(ref _targetHeightMm, value);
        }

        /// <summary>Pas de déplacement pour les boutons Haut/Bas en mm.</summary>
        public float StepMm
        {
            get => _stepMm;
            set => Set(ref _stepMm, Math.Max(1f, value));
        }

        /// <summary>État de la connexion USB/LIN.</summary>
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (Set(ref _isConnected, value))
                    OnPropertyChanged(nameof(CanOperate));
            }
        }

        /// <summary>Message d'état affiché dans la barre de statut.</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => Set(ref _statusMessage, value);
        }

        /// <summary>True pendant une opération longue (déplacement cible).</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (Set(ref _isBusy, value))
                    OnPropertyChanged(nameof(CanOperate));
            }
        }

        /// <summary>True si les commandes de mouvement sont autorisées.</summary>
        public bool CanOperate => IsConnected && !IsBusy;

        // ── Commandes ─────────────────────────────────────────────────────────

        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand MoveToCommand { get; }
        public ICommand StepUpCommand { get; }
        public ICommand StepDownCommand { get; }
        public ICommand ReconnectCommand { get; }

        // ── Handlers de commandes ─────────────────────────────────────────────

        private void ExecuteMoveUp()
        {
            SafeExecute(() =>
            {
                _service.MoveUp();
                StatusMessage = "Montée en cours…";
            });
        }

        private void ExecuteMoveDown()
        {
            SafeExecute(() =>
            {
                _service.MoveDown();
                StatusMessage = "Descente en cours…";
            });
        }

        private void ExecuteStop()
        {
            SafeExecute(() =>
            {
                _service.Stop();
                StatusMessage = "Arrêt";
            });
        }

        private void ExecuteStepUp()
        {
            SafeExecute(() =>
            {
                float target = CurrentHeightMm + StepMm;
                _service.MoveToHeight(target);
                StatusMessage = $"Déplacement vers {target / 10f:F1} cm";
            });
        }

        private void ExecuteStepDown()
        {
            SafeExecute(() =>
            {
                float target = CurrentHeightMm - StepMm;
                _service.MoveToHeight(target);
                StatusMessage = $"Déplacement vers {target / 10f:F1} cm";
            });
        }

        /// <summary>Déplace vers la hauteur cible saisie — tourne en tâche de fond.</summary>
        private void ExecuteMoveTo()
        {
            IsBusy = true;
            StatusMessage = $"Déplacement vers {TargetHeightMm / 10f:F1} cm…";

            Task.Run(() =>
            {
                try
                {
                    _service.MoveToHeight(TargetHeightMm);
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        StatusMessage = $"Erreur : {ex.Message}");
                }
                finally
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsBusy = false;
                        StatusMessage = "Prêt";
                    });
                }
            });
        }

        private void ExecuteReconnect()
        {
            IsBusy = true;
            StatusMessage = "Reconnexion…";

            Task.Run(() =>
            {
                bool ok = _service.Reconnect();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsBusy = false;
                    IsConnected = ok;
                    StatusMessage = ok ? "Reconnecté" : "Reconnexion échouée";
                    if (ok) _refreshTimer.Start();
                });
            });
        }

        // ── Timer : rafraîchissement de la position ───────────────────────────

        private void OnRefreshTick(object sender, EventArgs e)
        {
            if (!IsConnected || IsBusy) return;
            try
            {
                CurrentHeightMm = _service.ReadCurrentHeightMm();
            }
            catch
            {
                // La déconnexion sera signalée via l'événement VerinDisconnected
            }
        }

        // ── Déconnexion ───────────────────────────────────────────────────────

        private void OnVerinDisconnected(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _refreshTimer.Stop();
                IsConnected = false;
                StatusMessage = "⚠ Vérin déconnecté — cliquez Reconnecter";
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SafeExecute(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur : {ex.Message}";
            }
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            _refreshTimer.Stop();
            _service.Dispose();
        }
    }
}

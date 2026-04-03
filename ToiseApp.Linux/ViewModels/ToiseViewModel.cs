using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ToiseApp.Linux.Helpers;
using ToiseApp.Linux.Models;

namespace ToiseApp.Linux.ViewModels
{
    public class ToiseViewModel : INotifyPropertyChanged, IDisposable
    {
        // ── Service ───────────────────────────────────────────────────────────
        private readonly ToiseService _service;
        private readonly DispatcherTimer _refreshTimer;
        private readonly RelayCommand[] _operationCommands;

        // ── Brushes statiques pour l'indicateur de connexion ──────────────────
        private static readonly IBrush ConnectedBrush    = new SolidColorBrush(Color.Parse("#16A34A"));
        private static readonly IBrush DisconnectedBrush = new SolidColorBrush(Color.Parse("#DC2626"));

        // ── Champs de backing ─────────────────────────────────────────────────
        private float  _currentHeightMm;
        private float  _targetHeightMm = 1530f;
        private float  _stepMm = 10f;
        private bool   _isConnected;
        private string _statusMessage = "Initialisation…";
        private bool   _isBusy;

        // ── Constructeur ──────────────────────────────────────────────────────
        public ToiseViewModel(ToiseService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _service.Disconnected += OnVerinDisconnected;

            IsConnected   = _service.IsConnected;
            StatusMessage = IsConnected ? "Connecté" : "Vérin non détecté";

            var moveUp   = new RelayCommand(_ => ExecuteMoveUp(),   _ => CanOperate);
            var moveDown = new RelayCommand(_ => ExecuteMoveDown(), _ => CanOperate);
            var stop     = new RelayCommand(_ => ExecuteStop(),     _ => CanOperate);
            var moveTo   = new RelayCommand(_ => ExecuteMoveTo(),   _ => CanOperate);
            var stepUp   = new RelayCommand(_ => ExecuteStepUp(),   _ => CanOperate);
            var stepDown = new RelayCommand(_ => ExecuteStepDown(), _ => CanOperate);

            MoveUpCommand    = moveUp;
            MoveDownCommand  = moveDown;
            StopCommand      = stop;
            MoveToCommand    = moveTo;
            StepUpCommand    = stepUp;
            StepDownCommand  = stepDown;
            ReconnectCommand = new RelayCommand(_ => ExecuteReconnect(), _ => !IsBusy);

            _operationCommands = new[] { moveUp, moveDown, stop, moveTo, stepUp, stepDown };

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _refreshTimer.Tick += OnRefreshTick;

            if (IsConnected)
                _refreshTimer.Start();
        }

        // ── Propriétés bindables ──────────────────────────────────────────────

        public float CurrentHeightMm
        {
            get => _currentHeightMm;
            private set
            {
                if (Set(ref _currentHeightMm, value))
                    OnPropertyChanged(nameof(CurrentHeightCm));
            }
        }

        public string CurrentHeightCm => $"{_currentHeightMm / 10f:F1} cm";

        public float TargetHeightMm
        {
            get => _targetHeightMm;
            set => Set(ref _targetHeightMm, value);
        }

        public float StepMm
        {
            get => _stepMm;
            set => Set(ref _stepMm, Math.Max(1f, value));
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (Set(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(CanOperate));
                    OnPropertyChanged(nameof(ConnectionBrush));
                    NotifyCommandsCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => Set(ref _statusMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (Set(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(CanOperate));
                    NotifyCommandsCanExecuteChanged();
                }
            }
        }

        public bool CanOperate => IsConnected && !IsBusy;

        /// <summary>Couleur de l'indicateur USB : vert = connecté, rouge = déconnecté.</summary>
        public IBrush ConnectionBrush => IsConnected ? ConnectedBrush : DisconnectedBrush;

        // ── Commandes ─────────────────────────────────────────────────────────

        public ICommand MoveUpCommand    { get; }
        public ICommand MoveDownCommand  { get; }
        public ICommand StopCommand      { get; }
        public ICommand MoveToCommand    { get; }
        public ICommand StepUpCommand    { get; }
        public ICommand StepDownCommand  { get; }
        public ICommand ReconnectCommand { get; }

        // ── Handlers de commandes ─────────────────────────────────────────────

        private void ExecuteMoveUp() => SafeExecute(() =>
        {
            _service.MoveUp();
            StatusMessage = "Montée en cours…";
        });

        private void ExecuteMoveDown() => SafeExecute(() =>
        {
            _service.MoveDown();
            StatusMessage = "Descente en cours…";
        });

        private void ExecuteStop() => SafeExecute(() =>
        {
            _service.Stop();
            StatusMessage = "Arrêt";
        });

        private void ExecuteStepUp() => SafeExecute(() =>
        {
            float target = CurrentHeightMm + StepMm;
            _service.MoveToHeight(target);
            StatusMessage = $"Déplacement vers {target / 10f:F1} cm";
        });

        private void ExecuteStepDown() => SafeExecute(() =>
        {
            float target = CurrentHeightMm - StepMm;
            _service.MoveToHeight(target);
            StatusMessage = $"Déplacement vers {target / 10f:F1} cm";
        });

        private void ExecuteMoveTo()
        {
            IsBusy = true;
            StatusMessage = $"Déplacement vers {TargetHeightMm / 10f:F1} cm…";

            Task.Run(() =>
            {
                try   { _service.MoveToHeight(TargetHeightMm); }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                        StatusMessage = $"Erreur : {ex.Message}");
                }
                finally
                {
                    Dispatcher.UIThread.Post(() =>
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
                Dispatcher.UIThread.Post(() =>
                {
                    IsBusy = false;
                    IsConnected = ok;
                    StatusMessage = ok ? "Reconnecté" : "Reconnexion échouée";
                    if (ok) _refreshTimer.Start();
                });
            });
        }

        // ── Timer ─────────────────────────────────────────────────────────────

        private void OnRefreshTick(object? sender, EventArgs e)
        {
            if (!IsConnected || IsBusy) return;
            try   { CurrentHeightMm = _service.ReadCurrentHeightMm(); }
            catch { }
        }

        // ── Déconnexion ───────────────────────────────────────────────────────

        private void OnVerinDisconnected(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _refreshTimer.Stop();
                IsConnected   = false;
                StatusMessage = "⚠ Vérin déconnecté — cliquez Reconnecter";
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SafeExecute(Action action)
        {
            try   { action(); }
            catch (Exception ex) { StatusMessage = $"Erreur : {ex.Message}"; }
        }

        private void NotifyCommandsCanExecuteChanged()
        {
            foreach (var cmd in _operationCommands)
                cmd.RaiseCanExecuteChanged();
            ((RelayCommand)ReconnectCommand).RaiseCanExecuteChanged();
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
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

using SitStand;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ToiseApp.Model
{
    /// <summary>
    /// Pilotage du vérin Linak DL14 via USB-to-LIN (SDK SitStand/Usb2Lin).
    /// Aucune dépendance externe hors SitStand.dll.
    /// Unité interne : cm (float). La conversion mm↔cm est faite dans ToiseService.
    /// </summary>
    public class VerinDL14 : IDisposable
    {
        // ── Constante matérielle ──────────────────────────────────────────────
        // Point zéro physique en unités internes (1/100 de cm) = 153 cm plancher
        private const int HauteurAZero = 15300;

        // ── Fields ────────────────────────────────────────────────────────────
        private int _positionVerin;
        private float _hauteurToise;
        private SafeHandle _handler;
        private Usb2Lin _cbd6;
        private byte[] _bufferEmission;
        private byte[] _bufferReception;
        private bool _isOk;
        private int _closingFlag; // guard anti-réentrance pour CloseDevice

        // ── Événement de déconnexion ──────────────────────────────────────────
        /// <summary>
        /// Déclenché une seule fois à la première détection d'erreur USB/LIN.
        /// Abonné par ToiseService pour propager vers le ViewModel.
        /// </summary>
        public event EventHandler VerinDisconnected;

        // ── Propriétés publiques ──────────────────────────────────────────────
        public bool IsOK => _isOk;
        public float HauteurToise => _hauteurToise;

        // ── Constructeur ──────────────────────────────────────────────────────
        public VerinDL14()
        {
            _hauteurToise = 0;
            _handler = null;
            _isOk = OpenVerin();
        }

        // ── Connexion ─────────────────────────────────────────────────────────

        /// <summary>
        /// Ouvre la connexion avec le vérin Linak.
        /// Retourne true si le device est trouvé et initialisé correctement.
        /// Peut être rappelé après une déconnexion pour reconnecter sans redémarrage.
        /// </summary>
        public bool OpenVerin()
        {
            try
            {
                // Ne pas réouvrir si le handle est déjà valide
                if (_handler != null && !_handler.IsInvalid && !_handler.IsClosed)
                {
                    _isOk = true;
                    return true;
                }

                _hauteurToise = 0;
                _cbd6 = new Usb2Lin();
                _bufferEmission = new byte[64];
                _bufferReception = new byte[64];

                string[] listDevice = _cbd6.FindAllLinakDevices();
                if (listDevice.Length == 0)
                    listDevice = _cbd6.FindAllLinakDevices_old();

                if (listDevice.Length >= 1)
                {
                    _handler = _cbd6.OpenDevice(listDevice[0]);

                    // Initialisation du protocole LIN
                    _bufferEmission[0] = 3;
                    _bufferEmission[1] = 4;
                    _bufferEmission[2] = 0;
                    _bufferEmission[3] = 251;
                    _cbd6.SetFeature(_handler, _bufferEmission);
                    Thread.Sleep(100);
                    _bufferReception = _cbd6.GetFeature(_handler);

                    // Code 10 = interruption de service → device non opérationnel
                    if (_bufferReception[34] == 10)
                    {
                        _isOk = false;
                        return false;
                    }
                    _isOk = true;
                }
                else
                {
                    _isOk = false;
                }
            }
            catch (ConnectionException) { SetDisconnected(); }
            catch (Exception) { SetDisconnected(); }

            return _isOk;
        }

        // ── Commandes de mouvement ────────────────────────────────────────────

        /// <summary>Monte le vérin en continu.</summary>
        public void MoveUp()
        {
            try
            {
                if (!IsHandleValid()) return;
                _bufferEmission = new byte[64];
                _bufferEmission[0] = 5;
                _bufferEmission[1] = 0;
                _bufferEmission[2] = 0x80;
                _cbd6.SetFeature(_handler, _bufferEmission);
                Thread.Sleep(100);
                GetPositionToise();
            }
            catch (ConnectionException) { SetDisconnected(); }
            catch (SEHException) { SetDisconnected(); SafeCloseHandle(); }
            catch (Exception) { /* autres erreurs non fatales */ }
        }

        /// <summary>Descend le vérin en continu.</summary>
        public void MoveDown()
        {
            try
            {
                if (!IsHandleValid()) return;
                _bufferEmission = new byte[64];
                _bufferEmission[0] = 5;
                _bufferEmission[1] = 0xFF;
                _bufferEmission[2] = 0x7F;
                _cbd6.SetFeature(_handler, _bufferEmission);
                Thread.Sleep(100);
                GetPositionToise();
            }
            catch (ConnectionException) { SetDisconnected(); }
            catch (SEHException) { SetDisconnected(); SafeCloseHandle(); }
            catch (Exception) { }
        }

        /// <summary>Arrête immédiatement tout mouvement (commande 0x80).</summary>
        public void Stop()
        {
            try
            {
                if (!IsHandleValid()) return;
                _bufferEmission = new byte[64];
                _bufferEmission[0] = 5;
                _bufferEmission[1] = 1;
                _bufferEmission[2] = 0x80;
                _cbd6.SetFeature(_handler, _bufferEmission);
                Thread.Sleep(100);
                GetPositionToise();
            }
            catch (ConnectionException) { SetDisconnected(); }
            catch (SEHException) { SetDisconnected(); SafeCloseHandle(); }
            catch (Exception) { }
        }

        /// <summary>
        /// Déplace le vérin vers une hauteur cible en cm.
        /// Boucle jusqu'à ±0.01 cm de la cible.
        /// Méthode bloquante — à appeler depuis un thread de fond (Task.Run).
        /// </summary>
        /// <param name="hauteurDesireCm">Hauteur cible en cm.</param>
        public void MoveToToise(float hauteurDesireCm)
        {
            try
            {
                if (!IsHandleValid()) return;

                // Sécurité plancher : ne pas descendre sous le zéro physique
                if (hauteurDesireCm * 100f < HauteurAZero) return;

                int hauteur = (int)((hauteurDesireCm * 100f) - HauteurAZero);

                while (IsHandleValid()
                    && ((GetPositionToise() - hauteurDesireCm) > 0.01f
                     || (GetPositionToise() - hauteurDesireCm) < -0.01f))
                {
                    _bufferEmission = new byte[64];
                    _bufferEmission[0] = 5;
                    _bufferEmission[1] = (byte)hauteur;
                    _bufferEmission[2] = (byte)(hauteur >> 8);
                    _cbd6.SetFeature(_handler, _bufferEmission);
                    Thread.Sleep(100);
                    _bufferReception = _cbd6.GetFeature(_handler);
                }
            }
            catch (ConnectionException) { SetDisconnected(); }
            catch (SEHException) { SetDisconnected(); SafeCloseHandle(); }
            catch (ObjectDisposedException) { SetDisconnected(); _handler = null; }
            catch (Exception) { SetDisconnected(); }
        }

        // ── Lecture de position ───────────────────────────────────────────────

        /// <summary>
        /// Lit la position courante du vérin.
        /// Retourne la hauteur en cm, ou 0 en cas d'erreur/déconnexion.
        /// </summary>
        public float GetPositionToise()
        {
            try
            {
                if (!IsHandleValid()) return _hauteurToise = 0;

                _bufferReception = _cbd6.GetFeature(_handler);
                _positionVerin = _bufferReception[5];
                _positionVerin = _positionVerin << 8;
                _positionVerin += _bufferReception[4];
                Thread.Sleep(50);

                // 384 et 385 = valeurs parasites à ignorer
                if (_positionVerin != 384 && _positionVerin != 385)
                {
                    _hauteurToise = ((float)_positionVerin + HauteurAZero) / 100f;
                    string s = _hauteurToise.ToString(".#");
                    if (!string.IsNullOrEmpty(s))
                        _hauteurToise = float.Parse(s);
                }
                return _hauteurToise;
            }
            catch (ConnectionException) { SetDisconnected(); return _hauteurToise = 0; }
            catch (SEHException) { SetDisconnected(); SafeCloseHandle(); return _hauteurToise = 0; }
            catch (ObjectDisposedException) { SetDisconnected(); _handler = null; return _hauteurToise = 0; }
            catch (Exception) { SetDisconnected(); return _hauteurToise = 0; }
        }

        // ── Fermeture ─────────────────────────────────────────────────────────

        /// <summary>
        /// Ferme proprement la connexion.
        /// Guard Interlocked pour éviter la double libération du SafeHandle
        /// (source de SEHException dans le finalizer thread du CLR).
        /// </summary>
        public void CloseDevice()
        {
            if (Interlocked.CompareExchange(ref _closingFlag, 1, 0) != 0)
                return;
            try
            {
                if (_handler == null) return;

                // Supprimer le finalizer CLR AVANT l'appel SDK
                // pour éviter la double libération du handle natif
                GC.SuppressFinalize(_handler);

                try { _cbd6?.CloseDevice(_handler); } catch { }

                _handler = null;
                _isOk = false;
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _closingFlag, 0);
            }
        }

        public void Dispose() => CloseDevice();

        // ── Helpers privés ────────────────────────────────────────────────────

        /// <summary>
        /// Passe IsOK à false et lève VerinDisconnected UNE SEULE FOIS.
        /// </summary>
        private void SetDisconnected()
        {
            if (_isOk)
            {
                _isOk = false;
                VerinDisconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool IsHandleValid() =>
            _handler != null && !_handler.IsInvalid && !_handler.IsClosed && _isOk;

        /// <summary>
        /// Invalide le handle sans appeler CloseDevice().
        /// Utilisé uniquement après SEHException ou ObjectDisposedException,
        /// quand le handle natif est déjà dans un état indéfini.
        /// </summary>
        private void SafeCloseHandle()
        {
            try
            {
                if (_handler != null)
                {
                    GC.SuppressFinalize(_handler);
                    _handler = null;
                }
            }
            catch { _handler = null; }
        }
    }
}

using System;
using WpfActiback.Model.Metier;

namespace ToiseApp.Model
{
    /// <summary>
    /// Couche Model/Service : encapsule ACTVerinDL14 et expose
    /// uniquement les opérations métier utiles à l'application.
    /// Toutes les hauteurs échangées avec l'extérieur sont en mm.
    /// En interne, ACTVerinDL14 travaille en cm (float).
    /// </summary>
    public class ToiseService : IDisposable
    {
        // ── Dépendances ───────────────────────────────────────────────────────
        private readonly ACTVerinDL14 _verin;
        private readonly ACTValise    _valise;

        private bool _disposed;

        // ── Événement de déconnexion (relayé depuis ACTVerinDL14) ─────────────
        public event EventHandler Disconnected;

        // ── Constructeur ──────────────────────────────────────────────────────
        /// <param name="valise">
        /// Instance ACTValise configurée.
        /// Elle fournit : le seuil de force (ForceToise), la hauteur maxi
        /// (HauteurToise) et les lectures Arduino (MyF).
        /// Passez une <see cref="DefaultValise"/> si vous n'avez pas de capteur
        /// de force et que vous voulez désactiver la vérification de charge.
        /// </param>
        public ToiseService(ACTValise valise)
        {
            _valise = valise ?? throw new ArgumentNullException(nameof(valise));
            _verin  = new ACTVerinDL14(_valise);

            // On relaie l'événement de déconnexion vers les abonnés du service
            _verin.VerinDisconnected += (s, e) => Disconnected?.Invoke(this, EventArgs.Empty);
        }

        // ── Propriétés publiques ──────────────────────────────────────────────

        /// <summary>Position actuelle de la toise en mm.</summary>
        public float CurrentHeightMm => CmToMm(_verin.HauteurToise);

        /// <summary>True si la connexion USB/LIN est opérationnelle.</summary>
        public bool IsConnected => _verin.IsOK;

        // ── Opérations ────────────────────────────────────────────────────────

        /// <summary>Monte la toise (mouvement continu).</summary>
        public void MoveUp()
        {
            ThrowIfDisposed();
            _verin.MoveUp();
        }

        /// <summary>
        /// Descend la toise (mouvement continu).
        /// La vérification de charge est gérée par ACTVerinDL14 via ACTValise.
        /// </summary>
        public void MoveDown()
        {
            ThrowIfDisposed();
            _verin.MoveDown(_valise);
        }

        /// <summary>Arrête tout mouvement.</summary>
        public void Stop()
        {
            ThrowIfDisposed();
            _verin.Stop();
        }

        /// <summary>
        /// Déplace la toise vers une hauteur cible.
        /// </summary>
        /// <param name="targetMm">Hauteur cible en mm.</param>
        public void MoveToHeight(float targetMm)
        {
            ThrowIfDisposed();
            float targetCm = MmToCm(targetMm);
            _verin.MoveToToise(targetCm, _valise);
        }

        /// <summary>Lit la position courante et la retourne en mm.</summary>
        public float ReadCurrentHeightMm()
        {
            ThrowIfDisposed();
            return CmToMm(_verin.GetPositionToise());
        }

        /// <summary>
        /// Réinitialise la connexion au vérin.
        /// Retourne true si la reconnexion a réussi.
        /// </summary>
        public bool Reconnect()
        {
            ThrowIfDisposed();
            return _verin.OpenVerin();
        }

        // ── Conversions d'unités ──────────────────────────────────────────────

        private static float CmToMm(float cm) => cm * 10f;
        private static float MmToCm(float mm) => mm / 10f;

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _verin.CloseDevice();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ToiseService));
        }
    }
}

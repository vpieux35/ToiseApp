using System;

namespace ToiseApp.Model
{
    /// <summary>
    /// Façade sur VerinDL14.
    /// Expose les opérations métier en mm (conversion cm↔mm transparente).
    /// </summary>
    public class ToiseService : IDisposable
    {
        private readonly VerinDL14 _verin;
        private bool _disposed;

        public event EventHandler Disconnected;

        public ToiseService()
        {
            _verin = new VerinDL14();
            _verin.VerinDisconnected += (s, e) => Disconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Position actuelle de la toise en mm.</summary>
        public float CurrentHeightMm => CmToMm(_verin.HauteurToise);

        /// <summary>True si la connexion USB/LIN est opérationnelle.</summary>
        public bool IsConnected => _verin.IsOK;

        /// <summary>Monte la toise (mouvement continu).</summary>
        public void MoveUp()
        {
            ThrowIfDisposed();
            _verin.MoveUp();
        }

        /// <summary>Descend la toise (mouvement continu).</summary>
        public void MoveDown()
        {
            ThrowIfDisposed();
            _verin.MoveDown();
        }

        /// <summary>Arrête tout mouvement.</summary>
        public void Stop()
        {
            ThrowIfDisposed();
            _verin.Stop();
        }

        /// <summary>Déplace la toise vers une hauteur cible en mm.</summary>
        public void MoveToHeight(float targetMm)
        {
            ThrowIfDisposed();
            _verin.MoveToToise(MmToCm(targetMm));
        }

        /// <summary>Lit la position courante et la retourne en mm.</summary>
        public float ReadCurrentHeightMm()
        {
            ThrowIfDisposed();
            return CmToMm(_verin.GetPositionToise());
        }

        /// <summary>Tente une reconnexion au vérin. Retourne true si réussi.</summary>
        public bool Reconnect()
        {
            ThrowIfDisposed();
            return _verin.OpenVerin();
        }

        private static float CmToMm(float cm) => cm * 10f;
        private static float MmToCm(float mm) => mm / 10f;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _verin.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ToiseService));
        }
    }
}

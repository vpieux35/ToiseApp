using System;
using ToiseApp.Model;

namespace ToiseApp.Linux.Models
{
    /// <summary>
    /// Façade Linux sur VerinDL14Linux.
    /// Même contrat que le ToiseService Windows, mais wraps HidSharp au lieu de SitStand SDK.
    /// Unités exposées : mm (conversion cm↔mm transparente).
    /// </summary>
    public class ToiseService : IDisposable
    {
        private readonly VerinDL14Linux _verin;
        private bool _disposed;

        public event EventHandler? Disconnected;

        public ToiseService()
        {
            _verin = new VerinDL14Linux();
            _verin.VerinDisconnected += (s, e) => Disconnected?.Invoke(this, EventArgs.Empty);
        }

        public bool IsConnected => _verin.IsOK;

        public void MoveUp()       { ThrowIfDisposed(); _verin.MoveUp(); }
        public void MoveDown()     { ThrowIfDisposed(); _verin.MoveDown(); }
        public void Stop()         { ThrowIfDisposed(); _verin.Stop(); }

        public void MoveToHeight(float targetMm)
        {
            ThrowIfDisposed();
            _verin.MoveToToise(targetMm / 10f);
        }

        public float ReadCurrentHeightMm()
        {
            ThrowIfDisposed();
            return _verin.GetPositionToise() * 10f;
        }

        public bool Reconnect()
        {
            ThrowIfDisposed();
            return _verin.OpenVerin();
        }

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

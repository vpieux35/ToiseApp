using HidSharp;
using System;
using System.Linq;
using System.Threading;

namespace ToiseApp.Model
{
    /// <summary>
    /// Pilotage du vérin Linak DL14 via USB HID direct (HidSharp).
    /// Implémentation Linux — remplace VerinDL14 (Windows/SitStand SDK).
    /// Même API publique, même protocole de bytes LIN.
    /// Unité interne : cm (float). La conversion mm↔cm est faite dans ToiseService.
    /// </summary>
    public class VerinDL14Linux : IDisposable
    {
        // ── Constantes matérielles ────────────────────────────────────────────
        // Linak DeskLine CBD Control Box — confirmé par lsusb : ID 12d3:0002
        private const int LinakVendorId  = 0x12D3;
        private const int LinakProductId = 0x0002;

        // Report IDs du protocole Linak DeskLine CBD :
        //   0x03 = initialisation / configuration
        //   0x05 = commande moteur + lecture position
        private const byte ReportIdInit   = 0x03;
        private const byte ReportIdMoteur = 0x05;

        // Point zéro physique en unités internes (1/100 de cm) = 153 cm plancher
        private const int HauteurAZero = 15300;

        // ── Fields ────────────────────────────────────────────────────────────
        private HidStream? _stream;
        private float _hauteurToise;
        private bool _isOk;
        private int _closingFlag; // guard anti-réentrance pour CloseDevice

        // ── Événement de déconnexion ──────────────────────────────────────────
        public event EventHandler? VerinDisconnected;

        // ── Propriétés publiques ──────────────────────────────────────────────
        public bool IsOK => _isOk;
        public float HauteurToise => _hauteurToise;

        // ── Constructeur ──────────────────────────────────────────────────────
        public VerinDL14Linux()
        {
            _isOk = OpenVerin();
        }

        // ── Connexion ─────────────────────────────────────────────────────────

        /// <summary>
        /// Ouvre la connexion HID avec le vérin Linak.
        /// Peut être rappelé après une déconnexion pour reconnecter sans redémarrage.
        ///
        /// Prérequis Linux : le compte utilisateur doit avoir accès aux devices hidraw.
        /// Ajouter une règle udev :
        ///   echo 'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="12d3", MODE="0666"' \
        ///        | sudo tee /etc/udev/rules.d/99-linak.rules
        ///   sudo udevadm control --reload-rules && sudo udevadm trigger
        /// </summary>
        public bool OpenVerin()
        {
            try
            {
                if (_stream != null) { _isOk = true; return true; }

                var device = DeviceList.Local
                    .GetHidDevices(LinakVendorId, LinakProductId)
                    .FirstOrDefault();

                if (device == null) { _isOk = false; return false; }
                if (!device.TryOpen(out _stream)) { _isOk = false; return false; }

                _stream.ReadTimeout  = 500;
                _stream.WriteTimeout = 500;

                // Initialisation du protocole LIN (même séquence que Windows)
                var buf = new byte[64];
                buf[0] = ReportIdInit; // 0x03
                buf[1] = 4;
                buf[2] = 0;
                buf[3] = 251;
                HidSetFeature(buf);
                Thread.Sleep(100);
                var recv = HidGetFeature(ReportIdInit);

                // Code 10 = interruption de service → device non opérationnel
                if (recv != null && recv.Length > 34 && recv[34] == 10)
                {
                    _isOk = false;
                    return false;
                }

                _isOk = true;
                return true;
            }
            catch (Exception) { SetDisconnected(); return false; }
        }

        // ── Commandes de mouvement ────────────────────────────────────────────

        public void MoveUp()
        {
            try
            {
                if (!_isOk) return;
                var buf = new byte[64];
                buf[0] = 5;
                buf[1] = 0;
                buf[2] = 0x80;
                HidSetFeature(buf);
                Thread.Sleep(100);
                GetPositionToise();
            }
            catch (Exception) { SetDisconnected(); }
        }

        public void MoveDown()
        {
            try
            {
                if (!_isOk) return;
                var buf = new byte[64];
                buf[0] = 5;
                buf[1] = 0xFF;
                buf[2] = 0x7F;
                HidSetFeature(buf);
                Thread.Sleep(100);
                GetPositionToise();
            }
            catch (Exception) { SetDisconnected(); }
        }

        public void Stop()
        {
            try
            {
                if (!_isOk) return;
                var buf = new byte[64];
                buf[0] = 5;
                buf[1] = 1;
                buf[2] = 0x80;
                HidSetFeature(buf);
                Thread.Sleep(100);
                GetPositionToise();
            }
            catch (Exception) { SetDisconnected(); }
        }

        /// <summary>
        /// Déplace le vérin vers une hauteur cible en cm.
        /// Boucle jusqu'à ±0.01 cm de la cible.
        /// Méthode bloquante — à appeler depuis un thread de fond (Task.Run).
        /// </summary>
        public void MoveToToise(float hauteurDesireCm)
        {
            try
            {
                if (!_isOk) return;
                if (hauteurDesireCm * 100f < HauteurAZero) return;

                int hauteur = (int)((hauteurDesireCm * 100f) - HauteurAZero);

                while (_isOk
                    && ((GetPositionToise() - hauteurDesireCm) > 0.01f
                     || (GetPositionToise() - hauteurDesireCm) < -0.01f))
                {
                    var buf = new byte[64];
                    buf[0] = 5;
                    buf[1] = (byte)hauteur;
                    buf[2] = (byte)(hauteur >> 8);
                    HidSetFeature(buf);
                    Thread.Sleep(100);
                    HidGetFeature();
                }
            }
            catch (Exception) { SetDisconnected(); }
        }

        // ── Lecture de position ───────────────────────────────────────────────

        public float GetPositionToise()
        {
            try
            {
                if (!_isOk) return _hauteurToise = 0;

                var recv = HidGetFeature();
                if (recv == null || recv.Length < 6) return _hauteurToise = 0;

                int pos = recv[5];
                pos = pos << 8;
                pos += recv[4];
                Thread.Sleep(50);

                // 384 et 385 = valeurs parasites à ignorer
                if (pos != 384 && pos != 385)
                {
                    _hauteurToise = ((float)pos + HauteurAZero) / 100f;
                    string s = _hauteurToise.ToString(".#");
                    if (!string.IsNullOrEmpty(s))
                        _hauteurToise = float.Parse(s);
                }
                return _hauteurToise;
            }
            catch (Exception) { SetDisconnected(); return _hauteurToise = 0; }
        }

        // ── Fermeture ─────────────────────────────────────────────────────────

        public void CloseDevice()
        {
            if (Interlocked.CompareExchange(ref _closingFlag, 1, 0) != 0) return;
            try
            {
                _stream?.Close();
                _stream = null;
                _isOk = false;
            }
            catch { }
            finally { Interlocked.Exchange(ref _closingFlag, 0); }
        }

        public void Dispose() => CloseDevice();

        // ── Helpers HID privés ────────────────────────────────────────────────

        /// <summary>
        /// Envoie un HID Feature Report.
        /// payload[0] EST le Report ID (3 ou 5 selon le protocole Linak) —
        /// identique au comportement de Usb2Lin.SetFeature() sous Windows.
        /// </summary>
        private void HidSetFeature(byte[] payload)
        {
            // Pas de préfixe à ajouter : payload[0] est déjà le Report ID
            _stream!.SetFeature(payload);
        }

        /// <summary>
        /// Lit un HID Feature Report de 64 octets.
        /// report[0] = Report ID demandé ; le buffer retourné inclut le Report ID en [0],
        /// identique au tableau retourné par Usb2Lin.GetFeature() sous Windows.
        /// </summary>
        private byte[] HidGetFeature(byte reportId = ReportIdMoteur)
        {
            var report = new byte[64];
            report[0] = reportId;
            _stream!.GetFeature(report);
            return report;
        }

        private void SetDisconnected()
        {
            if (_isOk)
            {
                _isOk = false;
                VerinDisconnected?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}

using WpfActiback.Model.Metier;

namespace ToiseApp.Model
{
    /// <summary>
    /// ACTValise minimale pour une utilisation sans capteur de force ni Arduino.
    ///
    /// Elle désactive la vérification de charge en déclarant :
    ///   - MyF = 0 (force nulle mesurée)
    ///   - ForceToise = "9999" (seuil très élevé → condition toujours vraie)
    ///   - Port = null / IsArduino = false → pas de capteur Arduino
    ///
    /// Remplacez cette classe par une vraie ACTValise configurée dès que vous
    /// intégrez un capteur de force ou un Arduino.
    /// </summary>
    public static class DefaultValise
    {
        /// <summary>
        /// Crée et retourne une ACTValise préconfigurée pour usage autonome
        /// (sans capteur de force).
        /// Adaptez les paramètres (hauteur maxi, chemin log…) selon votre matériel.
        /// </summary>
        /// <param name="hauteurMaxiCm">Hauteur maximale autorisée en cm (ex : 210).</param>
        public static ACTValise Create(int hauteurMaxiCm = 210)
        {
            // ACTValise est la classe de configuration centrale de WpfActiback.
            // Adaptez les propriétés ci-dessous selon votre fichier XML produit.
            var valise = new ACTValise();

            // -- Paramètres de force : désactivation de la vérification de charge --
            // Si votre ACTValise expose ces propriétés autrement, ajustez ici.
            if (valise.ActibackXML?.MonProduit != null)
            {
                valise.ActibackXML.MonProduit.ForceToise      = "9999";
                valise.ActibackXML.MonProduit.HauteurToise    = new HauteurToiseWrapper(hauteurMaxiCm);
            }

            // -- Pas d'Arduino : Port = null, IsArduino = false --
            // MonManagerThread est normalement instancié par ACTValise.
            // Vérifiez que MyF est initialisé à 0 (valeur par défaut).

            return valise;
        }
    }

    /// <summary>
    /// Wrapper léger si HauteurToise de MonProduit est un type complexe.
    /// À remplacer ou supprimer selon la structure réelle de votre MonProduit.
    /// </summary>
    public class HauteurToiseWrapper
    {
        public int Value { get; }
        public HauteurToiseWrapper(int value) { Value = value; }
    }
}

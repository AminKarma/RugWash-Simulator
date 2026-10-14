using System;
using System.IO;
using RugWash.Save;
using UnityEngine;

namespace RugWash.Tests
{
    /// <summary>Redirige la sauvegarde vers un dossier temporaire propre : les tests ne touchent jamais la vraie sauvegarde.</summary>
    public static class TestSaveSandbox
    {
        public static string Directory { get; private set; }

        /// <summary>
        /// Dossier de sauvegarde neuf. Par défaut, il contient une sauvegarde « outils de l'atelier déjà acquis » : les outils
        /// qui étaient gratuits avant la v0.1.2 (aspirateur, lance, extracteur, canon à mousse, balai) sont débloqués, comme pour
        /// une partie v1 migrée — les tests d'outils restent indépendants du kit de départ. <paramref name="legacyTools"/> = faux :
        /// vraie nouvelle partie (kit de départ seul, aucune sauvegarde).
        /// </summary>
        public static string Reset(bool legacyTools = true)
        {
            Directory = Path.Combine(Application.temporaryCachePath, "RugWashTests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            SaveManager.DirectoryOverride = Directory;
            if (legacyTools)
            {
                var data = new SaveData();
                data.upgrades.AddRange(SaveMigration.LegacyToolUnlocks);
                data.upgrades.AddRange(SaveMigration.LegacyWorkshopUnlocks);
                data.upgrades.AddRange(SaveMigration.LegacyDecorUnlocks);
                new SaveFileStore(Directory, SaveManager.DefaultFileName).Save(data, out _);
            }
            return Directory;
        }

        public static string SavePath => Path.Combine(Directory, SaveManager.DefaultFileName);
    }

    /// <summary>
    /// Pose un tapis sur l'aire de lavage pour les tests qui exercent les outils sans commande. Le jeu n'a plus de tapis
    /// d'entraînement (v0.1.2 : un tapis n'apparaît qu'avec une commande) ; le chargement générique reste utilisé ici.
    /// </summary>
    public static class TestRugs
    {
        public static void LoadDefault(RugWash.Core.GameBootstrap game) => game.Rug.LoadRug(game.AvailableRugs[0]);
    }
}

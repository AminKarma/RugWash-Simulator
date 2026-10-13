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

        public static string Reset()
        {
            Directory = Path.Combine(Application.temporaryCachePath, "RugWashTests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            SaveManager.DirectoryOverride = Directory;
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

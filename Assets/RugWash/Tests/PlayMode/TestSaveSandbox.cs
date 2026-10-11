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
}

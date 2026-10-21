using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RugWash.Editor
{
    /// <summary>
    /// Génère des sons TEMPORAIRES par synthèse (bruit filtré, oscillateurs) et les écrit en WAV
    /// dans Audio/Generated. Ce sont des substituts fonctionnels et identifiables (préfixe « tmp_»),
    /// à remplacer par des enregistrements définitifs : il suffit de changer le clip assigné.
    /// </summary>
    public static class RugWashAudioGenerator
    {
        const int SampleRate = 44100;
        const string Folder = RugWashPaths.Root + "/Audio/Generated";

        public const string VacuumLoop = Folder + "/tmp_vacuum_loop.wav";
        public const string SoapLoop = Folder + "/tmp_soap_spray_loop.wav";
        public const string BrushLoop = Folder + "/tmp_brush_loop.wav";
        public const string RinseLoop = Folder + "/tmp_rinse_loop.wav";
        public const string ExtractorLoop = Folder + "/tmp_extractor_loop.wav";
        public const string UIClick = Folder + "/tmp_ui_click.wav";
        public const string UIConfirm = Folder + "/tmp_ui_confirm.wav";
        public const string UIDeny = Folder + "/tmp_ui_deny.wav";
        public const string Refill = Folder + "/tmp_refill.wav";
        public const string Drain = Folder + "/tmp_drain.wav";
        public const string CleanSparkle = Folder + "/tmp_clean_sparkle.wav";
        public const string Ambient = Folder + "/tmp_workshop_ambient_loop.wav";
        public const string Objective = Folder + "/tmp_objective_reached.wav";
        public const string VacuumContactLoop = Folder + "/tmp_vacuum_contact_loop.wav";
        public const string FoamContactLoop = Folder + "/tmp_foam_contact_loop.wav";
        public const string BrushContactLoop = Folder + "/tmp_brush_contact_loop.wav";
        public const string RinseContactLoop = Folder + "/tmp_rinse_contact_loop.wav";
        public const string ExtractorContactLoop = Folder + "/tmp_extractor_contact_loop.wav";
        public const string ToolEquip = Folder + "/tmp_tool_equip.wav";
        public const string ScanPulse = Folder + "/tmp_scan_pulse.wav";
        public const string ScanDenied = Folder + "/tmp_scan_denied.wav";
        public const string Milestone = Folder + "/tmp_milestone.wav";
        public const string Reward = Folder + "/tmp_reward.wav";
        public const string FoamCannonLoop = Folder + "/tmp_foam_cannon_loop.wav";
        public const string BroomLoop = Folder + "/tmp_broom_loop.wav";
        public const string BroomContactLoop = Folder + "/tmp_broom_contact_loop.wav";
        public const string SqueegeeLoop = Folder + "/tmp_squeegee_loop.wav";
        public const string RotaryLoop = Folder + "/tmp_rotary_loop.wav";
        public const string RotaryContactLoop = Folder + "/tmp_rotary_contact_loop.wav";
        /// <summary>Musiques de fond (v0.1.2), composées et synthétisées ici : aucune licence tierce.</summary>
        public const string MusicMenu = Folder + "/tmp_music_menu_loop.wav";
        /// <summary>Machines de l'atelier (v0.1.2 jalon 5).</summary>
        public const string BlowerLoop = Folder + "/tmp_blower_loop.wav";
        public const string PlateLoop = Folder + "/tmp_plate_loop.wav";
        public const string MusicWorkshop = Folder + "/tmp_music_workshop_loop.wav";
        public const string RobotHumLoop = Folder + "/tmp_robot_hum_loop.wav";
        // Farces (v0.1.2 jalon 6) : sons de dessin animé.
        public const string PrankStream = Folder + "/tmp_prank_stream.wav";
        public const string BatSwing = Folder + "/tmp_bat_swing.wav";
        public const string BatBonk = Folder + "/tmp_bat_bonk.wav";
        public const string BlasterShot = Folder + "/tmp_blaster_shot.wav";
        public const string DynamiteFuse = Folder + "/tmp_dynamite_fuse.wav";
        public const string DynamiteBoom = Folder + "/tmp_dynamite_boom.wav";
        public const string PrankSplat = Folder + "/tmp_prank_splat.wav";
        public const string RobotBump = Folder + "/tmp_robot_bump.wav";
        public const string RobotStart = Folder + "/tmp_robot_start.wav";
        public const string RobotStop = Folder + "/tmp_robot_stop.wav";
        public const string RobotDocked = Folder + "/tmp_robot_docked.wav";
        public static readonly string[] Footsteps =
        {
            Folder + "/tmp_footstep_1.wav", Folder + "/tmp_footstep_2.wav", Folder + "/tmp_footstep_3.wav",
        };

        [MenuItem("RugWash/Setup/Regénérer les Sons Temporaires", priority = 41)]
        static void RegenerateMenu() => GenerateAll(force: true);

        public static void GenerateAll(bool force)
        {
            RugWashProjectBootstrapper.EnsureFolders();
            Write(VacuumLoop, force, true, 2f, Vacuum);
            Write(SoapLoop, force, true, 1f, SoapSpray);
            Write(BrushLoop, force, true, 1f, Brush);
            Write(RinseLoop, force, true, 2f, Rinse);
            Write(ExtractorLoop, force, true, 2f, Extractor);
            Write(UIClick, force, false, 0.05f, Click);
            Write(UIConfirm, force, false, 0.45f, Confirm);
            Write(UIDeny, force, false, 0.3f, Deny);
            Write(Refill, force, false, 1.1f, Pour);
            Write(Drain, force, false, 1.2f, Gurgle);
            Write(CleanSparkle, force, false, 0.35f, Sparkle);
            Write(Ambient, force, true, 8f, AmbientRoom);
            Write(Objective, force, false, 0.9f, ObjectiveChime);
            Write(VacuumContactLoop, force, true, 2f, VacuumContact);
            Write(FoamContactLoop, force, true, 2f, FoamFizz);
            Write(BrushContactLoop, force, true, 1f, BrushScrub);
            Write(RinseContactLoop, force, true, 2f, WaterOnFabric);
            Write(ExtractorContactLoop, force, true, 2f, Slurp);
            Write(ToolEquip, force, false, 0.2f, EquipClack);
            Write(ScanPulse, force, false, 0.8f, ScanSweep);
            Write(ScanDenied, force, false, 0.26f, DeniedBeep);
            Write(Milestone, force, false, 0.55f, MilestoneChime);
            Write(Reward, force, false, 0.9f, RewardBell);
            Write(FoamCannonLoop, force, true, 2f, FoamCannon);
            Write(BroomLoop, force, true, 2f, BroomSwish);
            Write(BroomContactLoop, force, true, 2f, BroomOnSurface);
            Write(SqueegeeLoop, force, true, 2f, SqueegeeSlide);
            Write(RotaryLoop, force, true, 2f, RotaryMotor);
            Write(RotaryContactLoop, force, true, 2f, RotaryScrub);
            Write(MusicMenu, force, true, MenuLoopSeconds, MenuMusic);
            Write(BlowerLoop, force, true, 2f, BlowerHum);
            Write(PlateLoop, force, true, 2f, PlateRattle);
            Write(MusicWorkshop, force, true, WorkshopLoopSeconds, WorkshopMusic);
            Write(RobotHumLoop, force, true, 2f, RobotHum);
            Write(PrankStream, force, false, 1.25f, Trickle);
            Write(BatSwing, force, false, 0.36f, Whoosh);
            Write(BatBonk, force, false, 0.42f, Bonk);
            Write(BlasterShot, force, false, 0.6f, ToyPop);
            Write(DynamiteFuse, force, false, 1.2f, FuseHiss);
            Write(DynamiteBoom, force, false, 2.2f, Boom);
            Write(PrankSplat, force, false, 0.32f, Splat);
            Write(RobotBump, force, false, 0.18f, RobotBumpThud);
            Write(RobotStart, force, false, 0.45f, (float t, float d, ref State s) => RobotBeeps(t, 660f, 880f));
            Write(RobotStop, force, false, 0.45f, (float t, float d, ref State s) => RobotBeeps(t, 880f, 587f));
            Write(RobotDocked, force, false, 0.6f, RobotDockChime);
            for (int i = 0; i < Footsteps.Length; i++)
            {
                float pitch = 0.85f + i * 0.12f;
                Write(Footsteps[i], force, false, 0.22f, (float t, float d, ref State s) => Footstep(t, d, ref s, pitch));
            }
            ConfigureImports();
        }

        /// <summary>
        /// V0.2.5 — réglages d'import des sons. Avant : importés sans préchargement, compressés (Vorbis) et décompressés au
        /// chargement… de leur première lecture. Le premier passage de chaque outil sur un tapis chargeait et décodait son son
        /// pendant la partie (`SoundManager.LoadFMODSound`, 20 à 30 ms d'un coup, relevé par le diagnostic de premier contact).
        /// Désormais les effets sont chargés avec la scène (préchargement, décodés une fois) ; les deux musiques, longues,
        /// sont lues en continu depuis le disque (aucun décodage complet ni gros bloc en mémoire).
        /// </summary>
        public static void ConfigureImports()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;
                bool music = path == MusicMenu || path == MusicWorkshop;
                var settings = importer.defaultSampleSettings;
                var loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                bool preload = !music;
                if (settings.loadType == loadType && settings.preloadAudioData == preload && !importer.loadInBackground) continue;
                settings.loadType = loadType;
                settings.preloadAudioData = preload;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------------ Synthèses

        /// <summary>Générateur appelé échantillon par échantillon (t en secondes, état de filtre partagé).</summary>
        delegate float Synth(float t, float duration, ref State state);

        struct State
        {
            public System.Random Random;
            public float Lp1, Lp2, Hp, HpIn, Env;
            public float Noise() => (float)(Random.NextDouble() * 2.0 - 1.0);
        }

        static float LowPass(ref float memory, float input, float cutoff)
        {
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / SampleRate);
            memory += a * (input - memory);
            return memory;
        }

        static float HighPass(ref State s, float input, float cutoff)
        {
            float rc = 1f / (2f * Mathf.PI * cutoff), dt = 1f / SampleRate, a = rc / (rc + dt);
            float output = a * (s.Hp + input - s.HpIn);
            s.Hp = output;
            s.HpIn = input;
            return output;
        }

        static float Sine(float t, float frequency) => Mathf.Sin(2f * Mathf.PI * frequency * t);

        static float Vacuum(float t, float d, ref State s)
        {
            float motor = 0.22f * Sine(t, 110f) + 0.12f * Sine(t, 220f) + 0.05f * Sine(t, 330f) + 0.04f * Sine(t, 1850f);
            float air = LowPass(ref s.Lp1, s.Noise(), 2400f) * 0.55f;
            return (motor + air) * 0.7f;
        }

        static float SoapSpray(float t, float d, ref State s)
        {
            float hiss = HighPass(ref s, s.Noise(), 2500f);
            float pump = 0.75f + 0.25f * Sine(t, 6f);
            return hiss * pump * 0.5f;
        }

        static float Brush(float t, float d, ref State s)
        {
            float bristles = HighPass(ref s, s.Noise(), 1200f);
            float stroke = Mathf.Pow(Mathf.Abs(Sine(t, 3f)), 0.6f);
            return LowPass(ref s.Lp1, bristles, 5000f) * (0.25f + 0.75f * stroke) * 0.6f;
        }

        static float Rinse(float t, float d, ref State s)
        {
            float body = LowPass(ref s.Lp1, s.Noise(), 1800f) * 0.6f;
            float spray = HighPass(ref s, s.Noise(), 3000f) * 0.35f;
            float droplets = s.Random.NextDouble() < 0.0015 ? 0.6f : 0f;
            s.Env = Mathf.Max(droplets, s.Env * 0.995f);
            return body + spray + s.Env * Sine(t, 900f + 600f * s.Env) * 0.2f;
        }

        static float Extractor(float t, float d, ref State s)
        {
            float motor = 0.18f * Sine(t, 90f) + 0.1f * Sine(t, 180f) + 0.04f * Sine(t, 2200f);
            float suction = LowPass(ref s.Lp1, s.Noise(), 1500f) * 0.45f;
            if (s.Random.NextDouble() < 0.0009) s.Env = 1f;
            s.Env *= 0.9993f;
            float gurgle = s.Env * Sine(t, 180f + 260f * s.Env) * 0.35f;
            return (motor + suction + gurgle) * 0.75f;
        }

        static float Click(float t, float d, ref State s) => Sine(t, 1300f) * Mathf.Exp(-t * 90f) * 0.6f;

        /// <summary>Refus : deux bips graves descendants.</summary>
        static float Deny(float t, float d, ref State s)
        {
            float first = t < 0.12f ? Sine(t, 330f) * Mathf.Exp(-t * 18f) : 0f;
            float second = t >= 0.13f ? Sine(t, 247f) * Mathf.Exp(-(t - 0.13f) * 16f) : 0f;
            return (first + second) * 0.5f;
        }

        static float Confirm(float t, float d, ref State s)
        {
            float first = t < 0.15f ? Sine(t, 660f) * Mathf.Exp(-t * 12f) : 0f;
            float second = t >= 0.12f ? Sine(t, 990f) * Mathf.Exp(-(t - 0.12f) * 8f) : 0f;
            return (first + second) * 0.45f;
        }

        static float Pour(float t, float d, ref State s)
        {
            float envelope = Mathf.Clamp01(t * 12f) * Mathf.Clamp01((d - t) * 4f);
            return LowPass(ref s.Lp1, s.Noise(), 1400f + 400f * Sine(t, 7f)) * envelope * 0.7f;
        }

        static float Gurgle(float t, float d, ref State s)
        {
            float envelope = Mathf.Clamp01(t * 10f) * Mathf.Clamp01((d - t) * 3f);
            float water = LowPass(ref s.Lp1, s.Noise(), 900f) * 0.6f;
            float bubble = Sine(t, 220f - 100f * t / d + 40f * Sine(t, 11f)) * 0.25f * (0.5f + 0.5f * Sine(t, 9f));
            return (water + bubble) * envelope;
        }

        static float AmbientRoom(float t, float d, ref State s)
        {
            // Ronflement électrique discret + souffle d'air + rumeur lointaine (périodes entières pour boucler).
            float hum = 0.05f * Sine(t, 50f) + 0.025f * Sine(t, 100f) + 0.012f * Sine(t, 150f);
            float air = LowPass(ref s.Lp1, s.Noise(), 400f) * 0.5f;
            float rumble = LowPass(ref s.Lp2, s.Noise(), 120f) * (0.6f + 0.4f * Sine(t, 0.25f));
            return (hum + air + rumble * 0.8f) * 0.6f;
        }

        static float ObjectiveChime(float t, float d, ref State s)
        {
            float[] notes = { 784f, 988f, 1175f, 1568f };
            float sum = 0f;
            for (int i = 0; i < notes.Length; i++)
            {
                float start = i * 0.09f;
                if (t < start) continue;
                float local = t - start;
                sum += Sine(local, notes[i]) * Mathf.Exp(-local * 5f) * (i == notes.Length - 1 ? 1.2f : 0.8f);
            }
            return sum * 0.2f;
        }

        static float Footstep(float t, float d, ref State s, float pitch)
        {
            float envelope = Mathf.Exp(-t * 38f) * Mathf.Clamp01(t * 400f);
            float thump = Sine(t, 85f * pitch) * Mathf.Exp(-t * 30f);
            float scuff = LowPass(ref s.Lp1, s.Noise(), 1800f * pitch);
            return (thump * 0.8f + scuff * 0.6f) * envelope;
        }

        static float Sparkle(float t, float d, ref State s)
        {
            float a = Sine(t, 1568f) * Mathf.Exp(-t * 18f);
            float b = t > 0.06f ? Sine(t, 2093f) * Mathf.Exp(-(t - 0.06f) * 16f) : 0f;
            float c = t > 0.12f ? Sine(t, 2637f) * Mathf.Exp(-(t - 0.12f) * 14f) : 0f;
            return (a + b + c) * 0.25f;
        }

        // ---- Sons de contact avec le tapis (superposés à la boucle de l'outil, dosés par le contact réel)

        static float VacuumContact(float t, float d, ref State s)
        {
            // Aspiration étouffée par les fibres : souffle grave qui « flotte » lentement.
            float flutter = 0.7f + 0.3f * Sine(t, 2f) * Sine(t, 0.5f);
            return LowPass(ref s.Lp2, LowPass(ref s.Lp1, s.Noise(), 700f), 500f) * flutter * 1.4f;
        }

        static float FoamFizz(float t, float d, ref State s)
        {
            // Bulles qui éclatent : crépitements aigus épars sur un léger souffle.
            if (s.Random.NextDouble() < 0.004) s.Env = 0.4f + 0.6f * (float)s.Random.NextDouble();
            s.Env *= 0.985f;
            float pop = HighPass(ref s, s.Noise(), 3500f) * s.Env;
            float hiss = LowPass(ref s.Lp1, s.Noise(), 6000f) * 0.06f;
            return pop * 0.9f + hiss;
        }

        static float BrushScrub(float t, float d, ref State s)
        {
            // Frottement des poils dans les fibres, rythmé par les allers-retours (6 par seconde).
            float friction = LowPass(ref s.Lp1, HighPass(ref s, s.Noise(), 500f), 2800f);
            float stroke = 0.3f + 0.7f * Mathf.Pow(Mathf.Abs(Sine(t, 3f)), 0.8f);
            float grit = s.Random.NextDouble() < 0.002 ? 0.8f : 0f;
            s.Env = Mathf.Max(grit, s.Env * 0.97f);
            return friction * stroke * 0.9f + s.Env * s.Noise() * 0.3f;
        }

        static float WaterOnFabric(float t, float d, ref State s)
        {
            // Eau qui frappe le tissu : éclaboussures sourdes et gouttes.
            float splat = LowPass(ref s.Lp1, s.Noise(), 1100f) * (0.75f + 0.25f * Sine(t, 13f));
            if (s.Random.NextDouble() < 0.003) s.Env = 0.5f + 0.5f * (float)s.Random.NextDouble();
            s.Env *= 0.996f;
            float drop = s.Env * Sine(t, 500f + 900f * s.Env) * 0.25f;
            return splat * 0.8f + drop;
        }

        static float Slurp(float t, float d, ref State s)
        {
            // Eau aspirée hors des fibres : gargouillis irrégulier.
            float water = LowPass(ref s.Lp1, s.Noise(), 800f);
            if (s.Random.NextDouble() < 0.0015) s.Env = 1f;
            s.Env *= 0.9994f;
            float bubble = s.Env * Sine(t, 140f + 200f * s.Env + 30f * Sine(t, 7f)) * 0.5f;
            return water * (0.6f + 0.4f * Sine(t, 4f)) * 0.8f + bubble;
        }

        static float FoamCannon(float t, float d, ref State s)
        {
            // Air comprimé + mousse épaisse projetée : souffle médium pulsé par la pompe.
            float air = HighPass(ref s, s.Noise(), 900f);
            float body = LowPass(ref s.Lp1, s.Noise(), 1600f);
            float pump = 0.8f + 0.2f * Sine(t, 9f);
            return (air * 0.35f + body * 0.55f) * pump * 0.7f;
        }

        static float BroomSwish(float t, float d, ref State s)
        {
            // Allers-retours du balai (1,2 par seconde) : souffle filtré qui enfle à chaque passage.
            float stroke = Mathf.Pow(Mathf.Abs(Sine(t, 1.2f)), 1.5f);
            float swish = LowPass(ref s.Lp1, HighPass(ref s, s.Noise(), 700f), 3500f);
            return swish * (0.15f + 0.85f * stroke) * 0.55f;
        }

        static float BroomOnSurface(float t, float d, ref State s)
        {
            // Poils raides qui raclent le sol mouillé : grattement rythmé et clapotis.
            float stroke = 0.3f + 0.7f * Mathf.Pow(Mathf.Abs(Sine(t, 1.2f)), 0.8f);
            float scrape = LowPass(ref s.Lp1, HighPass(ref s, s.Noise(), 1500f), 6000f);
            if (s.Random.NextDouble() < 0.0025) s.Env = 0.5f + 0.5f * (float)s.Random.NextDouble();
            s.Env *= 0.994f;
            float splash = s.Env * LowPass(ref s.Lp2, s.Noise(), 1200f);
            return scrape * stroke * 0.7f + splash * 0.5f;
        }

        static float SqueegeeSlide(float t, float d, ref State s)
        {
            // Lame en caoutchouc sur un sol mouillé : glissement doux (une poussée par seconde) et petits couinements.
            float stroke = 0.4f + 0.6f * Mathf.Pow(Mathf.Abs(Sine(t, 0.5f)), 1.2f);
            float slide = LowPass(ref s.Lp1, HighPass(ref s, s.Noise(), 400f), 2200f);
            if (s.Random.NextDouble() < 0.0008) s.Env = 0.4f + 0.4f * (float)s.Random.NextDouble();
            s.Env *= 0.9992f;
            float squeak = s.Env * Sine(t, 1100f);
            return slide * stroke * 0.45f + squeak * 0.1f;
        }

        static float RotaryMotor(float t, float d, ref State s)
        {
            // Moteur de monobrosse : ronflement grave, harmoniques et engrenages (périodes entières sur 2 s).
            float motor = 0.2f * Sine(t, 50f) + 0.14f * Sine(t, 100f) + 0.06f * Sine(t, 150f) + 0.03f * Sine(t, 1200f);
            float gear = LowPass(ref s.Lp1, s.Noise(), 900f) * 0.35f;
            return (motor + gear) * (0.92f + 0.08f * Sine(t, 12.5f)) * 0.8f;
        }

        static float RotaryScrub(float t, float d, ref State s)
        {
            // Disque qui frotte les fibres : souffle rugueux modulé par la rotation.
            float swirl = 0.7f + 0.3f * Sine(t, 12.5f);
            return LowPass(ref s.Lp1, HighPass(ref s, s.Noise(), 600f), 4000f) * swirl * 0.6f;
        }

        static float BlowerHum(float t, float d, ref State s)
        {
            // Souffleur : moteur grave et souffle d'air large (périodes entières sur 2 s).
            float motor = 0.12f * Sine(t, 75f) + 0.06f * Sine(t, 150f) + 0.03f * Sine(t, 450f);
            float air = LowPass(ref s.Lp1, s.Noise(), 1400f) * 0.7f;
            return (motor + air) * (0.92f + 0.08f * Sine(t, 6f));
        }

        static float PlateRattle(float t, float d, ref State s)
        {
            // Plaque vibrante : bourdonnement à 50 Hz et cliquetis du tapis qui tremble.
            float buzz = 0.25f * Mathf.Sign(Sine(t, 50f)) * 0.3f + 0.12f * Sine(t, 100f);
            float rattle = HighPass(ref s, s.Noise(), 1200f) * (0.5f + 0.5f * Mathf.Abs(Sine(t, 25f))) * 0.3f;
            return LowPass(ref s.Lp1, buzz, 900f) + rattle;
        }

        // ------------------------------------------------------------------ Musiques

        static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

        /// <summary>Atelier : 80 BPM, 8 mesures (24 s) — do maj7, la m7, fa maj7, sol 6 ; piano électrique, basse, batterie douce.</summary>
        const float WorkshopLoopSeconds = 24f;
        static readonly int[][] WorkshopChords =
        {
            new[] { 36, 52, 55, 59, 64 }, new[] { 33, 52, 55, 57, 60 }, new[] { 29, 52, 57, 60, 64 }, new[] { 31, 50, 55, 59, 64 },
        };

        static float WorkshopMusic(float time, float d, ref State s)
        {
            const float beat = 0.75f, chordLength = beat * 8f;
            float t = Mathf.Repeat(time, WorkshopLoopSeconds);
            int chordIndex = Mathf.Min(3, (int)(t / chordLength));
            var chord = WorkshopChords[chordIndex];
            float inChord = t - chordIndex * chordLength;
            float edges = Mathf.Clamp01(inChord / 0.04f) * Mathf.Clamp01((chordLength - inChord) / 0.06f);

            // Accords plaqués toutes les deux temps, attaque douce et longue résonance.
            float strike = Mathf.Repeat(t, beat * 2f);
            float keysEnv = (Mathf.Exp(-strike * 1.4f) * 0.65f + 0.35f) * Mathf.Clamp01(strike / 0.01f);
            float keys = 0f;
            for (int i = 1; i < chord.Length; i++)
            {
                float f = Midi(chord[i]);
                keys += Mathf.Sin(2f * Mathf.PI * f * t) + 0.22f * Mathf.Sin(4f * Mathf.PI * f * t) * Mathf.Exp(-strike * 5f);
            }
            keys *= keysEnv * edges * 0.075f;

            // Basse : fondamentale aux temps 1 et 3.
            float bassTime = Mathf.Repeat(t, beat * 2f);
            float bass = Mathf.Sin(2f * Mathf.PI * Midi(chord[0]) * t) * Mathf.Exp(-bassTime * 1.8f) * Mathf.Clamp01(bassTime / 0.01f) * 0.32f * edges;

            // Batterie feutrée : grosse caisse (1, 3), caisse claire balayée (2, 4), charleston sur les contretemps.
            float barTime = Mathf.Repeat(t, beat * 4f);
            float kick = Kick(barTime) + Kick(barTime - beat * 2f);
            float noise = HighPass(ref s, s.Noise(), 1800f);
            float snareTime = Mathf.Repeat(barTime - beat, beat * 2f);
            float snare = LowPass(ref s.Lp1, noise, 5000f) * Mathf.Exp(-snareTime * 22f) * 0.12f;
            float hatTime = Mathf.Repeat(t - beat * 0.5f, beat);
            float hat = noise * Mathf.Exp(-hatTime * 70f) * 0.035f;
            return keys + bass + kick * 0.35f + snare + hat;
        }

        static float Kick(float x)
        {
            if (x < 0f || x > 0.35f) return 0f;
            float phase = 48f * x + 70f * (1f - Mathf.Exp(-25f * x)) / 25f;
            return Mathf.Sin(2f * Mathf.PI * phase) * Mathf.Exp(-x * 10f);
        }

        /// <summary>Menu : nappes lentes (30 s) — ré m9, si♭ maj7, fa maj7, do add9 — et arpège de piano discret.</summary>
        const float MenuLoopSeconds = 30f;
        static readonly int[][] MenuChords =
        {
            new[] { 38, 53, 57, 60, 64 }, new[] { 34, 50, 53, 57, 62 }, new[] { 41, 52, 57, 60, 64 }, new[] { 36, 50, 55, 60, 64 },
        };

        static float MenuMusic(float time, float d, ref State s)
        {
            const float chordLength = 7.5f, step = 0.9375f;
            float t = Mathf.Repeat(time, MenuLoopSeconds);
            int chordIndex = Mathf.Min(3, (int)(t / chordLength));
            float inChord = t - chordIndex * chordLength;
            // Fondu enchaîné entre deux accords (la fin d'un accord se mêle au début du suivant).
            float pad = Pad(MenuChords[chordIndex], t, Mathf.Clamp01(inChord / 1.2f) * Mathf.Clamp01((chordLength - inChord) / 1.2f + 0.0f));
            int previous = (chordIndex + 3) % 4;
            pad += Pad(MenuChords[previous], t, Mathf.Clamp01(1f - inChord / 1.2f) * 0.6f);

            // Arpège : une note de l'accord par pas, longue décroissance.
            int stepIndex = (int)(inChord / step);
            float stepTime = inChord - stepIndex * step;
            var chord = MenuChords[chordIndex];
            int note = chord[1 + stepIndex % (chord.Length - 1)] + 12;
            float pluck = Mathf.Sin(2f * Mathf.PI * Midi(note) * t) * Mathf.Exp(-stepTime * 2.4f) * Mathf.Clamp01(stepTime / 0.008f) * 0.06f;
            float air = LowPass(ref s.Lp1, s.Noise(), 600f) * 0.03f;
            return pad + pluck + air;
        }

        static float Pad(int[] chord, float t, float level)
        {
            if (level <= 0f) return 0f;
            float sum = 0f;
            for (int i = 0; i < chord.Length; i++)
            {
                float f = Midi(chord[i]);
                // Léger désaccord entre deux voix : nappe vivante, sans battement marqué.
                sum += Mathf.Sin(2f * Mathf.PI * f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * f * 1.003f * t);
            }
            float tremolo = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.2f * t);
            return sum * level * tremolo * 0.045f;
        }

        static float RobotHum(float t, float d, ref State s)
        {
            // Petit moteur et turbine du robot : ronronnement aigu discret (périodes entières sur 2 s).
            float motor = 0.12f * Sine(t, 180f) + 0.06f * Sine(t, 360f) + 0.03f * Sine(t, 1440f);
            float air = LowPass(ref s.Lp1, s.Noise(), 2600f) * 0.3f;
            return (motor + air) * (0.9f + 0.1f * Sine(t, 3f));
        }

        /// <summary>Filet d'eau qui tombe (farce) : bruit filtré ondulant, entrée et sortie douces.</summary>
        static float Trickle(float t, float d, ref State s)
        {
            float envelope = Mathf.Clamp01(t * 12f) * Mathf.Clamp01((d - t) * 6f);
            float wobble = 0.6f + 0.4f * Mathf.Sin(t * 37f) * Mathf.Sin(t * 11f + 1f);
            float water = LowPass(ref s.Lp1, s.Noise(), 1400f + 700f * wobble) - LowPass(ref s.Lp2, s.Noise(), 300f) * 0.5f;
            return water * envelope * 0.7f;
        }

        /// <summary>Élan de la batte : souffle dont la hauteur monte puis retombe.</summary>
        static float Whoosh(float t, float d, ref State s)
        {
            float x = t / d;
            float envelope = Mathf.Sin(Mathf.Clamp01(x) * Mathf.PI);
            float cutoff = 400f + 2600f * Mathf.Sin(Mathf.Clamp01(x) * Mathf.PI);
            return HighPass(ref s, LowPass(ref s.Lp1, s.Noise(), cutoff), 180f) * envelope * envelope * 1.4f;
        }

        /// <summary>« Bonk » de dessin animé : note qui chute vite, petit claquement de bois.</summary>
        static float Bonk(float t, float d, ref State s)
        {
            float pitch = 620f * Mathf.Exp(-t * 7f) + 180f;
            float tone = Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-t * 9f);
            float knock = LowPass(ref s.Lp1, s.Noise(), 2500f) * Mathf.Exp(-t * 80f);
            return tone * 0.6f + knock * 0.6f;
        }

        /// <summary>Tir du fusil-jouet : « pop » d'air comprimé, ressort qui vibre, clac de la pompe.</summary>
        static float ToyPop(float t, float d, ref State s)
        {
            float pop = LowPass(ref s.Lp1, s.Noise(), 1800f) * Mathf.Exp(-t * 45f) + Sine(t, 140f) * Mathf.Exp(-t * 30f) * 0.6f;
            float spring = t > 0.04f ? Mathf.Sin(2f * Mathf.PI * (520f + 60f * Mathf.Sin(t * 90f)) * t) * Mathf.Exp(-(t - 0.04f) * 14f) * 0.18f : 0f;
            float pump = t > 0.38f ? LowPass(ref s.Lp2, s.Noise(), 3200f) * Mathf.Exp(-(t - 0.38f) * 60f) * 0.7f : 0f;
            return pop * 0.9f + spring + pump;
        }

        /// <summary>V0.4.2 : mèche qui crépite (petits craquements irréguliers sur un sifflement).</summary>
        static float FuseHiss(float t, float d, ref State s)
        {
            float hiss = LowPass(ref s.Lp1, s.Noise(), 6000f) * 0.18f;
            float crackle = s.Random.NextDouble() < 0.015 ? s.Noise() * 0.8f : 0f;
            float fade = Mathf.Clamp01(t * 20f) * Mathf.Clamp01((d - t) * 6f);
            return (hiss + crackle) * fade;
        }

        /// <summary>V0.4.2 : « BOUM » de dessin animé : claquement bref, grondement grave qui descend, souffle qui retombe.</summary>
        static float Boom(float t, float d, ref State s)
        {
            float crack = LowPass(ref s.Lp1, s.Noise(), 5000f) * Mathf.Exp(-t * 35f) * 0.7f;
            float thump = Sine(t, 48f + 70f * Mathf.Exp(-t * 5f)) * Mathf.Exp(-t * 2.6f) * 0.9f;
            float rumble = LowPass(ref s.Lp2, s.Noise(), 420f) * Mathf.Exp(-t * 1.8f) * 1.6f;
            return Mathf.Clamp(crack + thump + rumble, -1f, 1f) * Mathf.Clamp01((d - t) * 3f);
        }

        /// <summary>« Splat » mouillé : bruit sourd très bref et petite goutte.</summary>
        static float Splat(float t, float d, ref State s)
        {
            float body = LowPass(ref s.Lp1, s.Noise(), 900f) * Mathf.Exp(-t * 28f);
            float drip = t > 0.12f ? Sine(t, 900f - 1200f * (t - 0.12f)) * Mathf.Exp(-(t - 0.12f) * 30f) * 0.25f : 0f;
            return body * 1.1f + drip;
        }

        static float RobotBumpThud(float t, float d, ref State s)
        {
            float thud = Sine(t, 120f) * Mathf.Exp(-t * 45f);
            float click = LowPass(ref s.Lp1, s.Noise(), 3000f) * Mathf.Exp(-t * 90f);
            return thud * 0.8f + click * 0.5f;
        }

        static float RobotBeeps(float t, float first, float second)
        {
            float a = t < 0.16f ? Sine(t, first) * Mathf.Clamp01((0.16f - t) * 40f) : 0f;
            float b = t > 0.2f && t < 0.38f ? Sine(t, second) * Mathf.Clamp01((0.38f - t) * 40f) : 0f;
            return (a + b) * Mathf.Clamp01(t * 200f) * 0.3f;
        }

        static float RobotDockChime(float t, float d, ref State s)
        {
            float a = Sine(t, 523.25f) * Mathf.Exp(-t * 6f);
            float b = t > 0.12f ? Sine(t, 783.99f) * Mathf.Exp(-(t - 0.12f) * 5f) : 0f;
            return (a * 0.6f + b) * 0.22f;
        }

        // ---- Sons ponctuels

        static float EquipClack(float t, float d, ref State s)
        {
            // Prise en main : petit choc plastique puis cliquet.
            float thump = Sine(t, 140f) * Mathf.Exp(-t * 40f) * 0.7f;
            float click = LowPass(ref s.Lp1, s.Noise(), 4000f) * Mathf.Exp(-t * 120f);
            float latch = t > 0.07f ? HighPass(ref s, s.Noise(), 2000f) * Mathf.Exp(-(t - 0.07f) * 150f) * 0.8f : 0f;
            return thump + click * 0.8f + latch;
        }

        static float ScanSweep(float t, float d, ref State s)
        {
            // Balayage montant puis tintement : l'onde du scan se propage.
            float k = t / d;
            float phaseFrequency = 380f + 1400f * k * k;
            s.Lp2 += 2f * Mathf.PI * phaseFrequency / SampleRate;
            float sweep = Mathf.Sin(s.Lp2) * Mathf.Clamp01(t * 20f) * (1f - k) * 0.5f;
            float air = LowPass(ref s.Lp1, s.Noise(), 1500f + 3000f * k) * Mathf.Sin(Mathf.PI * k) * 0.35f;
            float ping = t > 0.18f ? Sine(t, 1760f) * Mathf.Exp(-(t - 0.18f) * 9f) * 0.35f : 0f;
            return sweep + air + ping;
        }

        static float DeniedBeep(float t, float d, ref State s)
        {
            float first = t < 0.1f ? Sine(t, 330f) * Mathf.Clamp01((0.1f - t) * 60f) : 0f;
            float second = t > 0.13f && t < 0.24f ? Sine(t, 262f) * Mathf.Clamp01((0.24f - t) * 60f) : 0f;
            return (first + second) * Mathf.Clamp01(t * 300f) * 0.4f;
        }

        static float MilestoneChime(float t, float d, ref State s)
        {
            float a = Sine(t, 880f) * Mathf.Exp(-t * 7f);
            float b = t > 0.1f ? Sine(t, 1318.5f) * Mathf.Exp(-(t - 0.1f) * 6f) : 0f;
            return (a * 0.7f + b) * 0.25f;
        }

        static float RewardBell(float t, float d, ref State s)
        {
            // Tiroir-caisse stylisé : cliquetis métallique puis cloche brillante.
            float rattle = t < 0.12f ? HighPass(ref s, s.Noise(), 3000f) * Mathf.Exp(-t * 30f) * 0.5f : 0f;
            float bellT = Mathf.Max(0f, t - 0.1f);
            float bell = t > 0.1f ? (Sine(bellT, 2093f) + 0.6f * Sine(bellT, 3136f) + 0.3f * Sine(bellT, 4186f)) * Mathf.Exp(-bellT * 4.5f) : 0f;
            return rattle + bell * 0.22f;
        }

        // ------------------------------------------------------------------ Écriture

        static void Write(string assetPath, bool force, bool loop, float seconds, Synth synth)
        {
            if (!force && File.Exists(assetPath)) return;

            int length = Mathf.RoundToInt(seconds * SampleRate);
            int fade = loop ? Mathf.RoundToInt(0.12f * SampleRate) : 0;
            var raw = new float[length + fade];
            var state = new State { Random = new System.Random(assetPath.GetHashCode()) };
            for (int i = 0; i < raw.Length; i++)
                raw[i] = synth(i / (float)SampleRate, seconds, ref state);

            var samples = new float[length];
            Array.Copy(raw, samples, length);
            // Boucle sans couture : la queue générée au-delà de la durée est fondue dans le début.
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                samples[i] = raw[length + i] * (1f - k) + raw[i] * k;
            }

            float peak = 0.0001f;
            foreach (float v in samples) peak = Mathf.Max(peak, Mathf.Abs(v));
            float gain = Mathf.Min(1f, 0.85f / peak);

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            using (var writer = new BinaryWriter(File.Create(fullPath)))
            {
                int dataBytes = samples.Length * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataBytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataBytes);
                foreach (float v in samples)
                    writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * gain * 32767f), -32768, 32767));
            }
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}

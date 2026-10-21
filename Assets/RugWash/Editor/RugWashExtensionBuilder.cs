using RugWash.Audio;
using RugWash.Player;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using static RugWash.Editor.RugWashBuildHelpers;
using static RugWash.Editor.RugWashProceduralMeshes;

namespace RugWash.Editor
{
    /// <summary>
    /// Agrandissement de l'atelier (grande mise à jour) : mur droit percé d'une ouverture (fermée par une cloison tant que
    /// l'agrandissement n'est pas acheté), annexe avec la zone de lavage 2 (aire, bordures, caniveau, projecteur) et le
    /// modèle de la machine à laver (tambour animé, écran du temps restant, voyant d'état).
    /// </summary>
    public static class RugWashExtensionBuilder
    {
        const float HalfX = RugWashSceneBuilder.RoomHalfX, HalfZ = RugWashSceneBuilder.RoomHalfZ, Height = RugWashSceneBuilder.RoomHeight;
        const float Wall = RugWashSceneBuilder.WallThickness;

        /// <summary>Ouverture du mur droit vers l'annexe (entre le bureau et l'espace de séchage).</summary>
        internal const float OpeningMinZ = -2.15f, OpeningMaxZ = -0.75f, OpeningHeight = 2.3f;
        /// <summary>Annexe : intérieur entre le mur droit de l'atelier et ses propres murs.</summary>
        internal const float AnnexMinX = HalfX + Wall, AnnexMaxX = 12.4f, AnnexMinZ = -4.4f, AnnexMaxZ = 2.0f;
        /// <summary>Aire de lavage de la zone 2 (même taille que celle de la zone 1).</summary>
        internal static readonly Vector3 Bay2Center = new Vector3(9.3f, 0f, -1.6f);
        internal static readonly Vector3 PadSize = new Vector3(4.6f, 0.05f, 3.6f);
        /// <summary>Machine à laver : contre le mur du fond de l'annexe, face à l'aire.</summary>
        internal static readonly Vector3 WasherPosition = new Vector3(10.9f, 0f, 1.55f);

        /// <summary>
        /// Mur droit de l'atelier : deux pans et un linteau autour de l'ouverture, et la cloison qui la ferme (retournée :
        /// retirée à l'achat de l'agrandissement). Remplace le mur plein et son soubassement.
        /// </summary>
        public static GameObject RightWall(Transform room, RugWashContentBuilder.Content c, MeshStore m)
        {
            float x = HalfX + Wall / 2f;
            float frontMin = -HalfZ - Wall, backMax = HalfZ + Wall;
            CreateBox(m, "Wall_Right_Front", room, new Vector3(x, Height / 2f, (frontMin + OpeningMinZ) / 2f),
                new Vector3(Wall, Height, OpeningMinZ - frontMin), c["Plaster"]);
            CreateBox(m, "Wall_Right_Back", room, new Vector3(x, Height / 2f, (OpeningMaxZ + backMax) / 2f),
                new Vector3(Wall, Height, backMax - OpeningMaxZ), c["Plaster"]);
            float width = OpeningMaxZ - OpeningMinZ, center = (OpeningMinZ + OpeningMaxZ) / 2f;
            CreateBox(m, "Wall_Right_Lintel", room, new Vector3(x, (OpeningHeight + Height) / 2f, center),
                new Vector3(Wall, Height - OpeningHeight, width), c["Plaster"]);

            // Soubassement de part et d'autre de l'ouverture.
            const float band = 1.1f, inset = 0.012f;
            CreateBox(m, "Wainscot_Right_Front", room, new Vector3(HalfX - inset, band / 2f, (-HalfZ + OpeningMinZ) / 2f),
                new Vector3(0.02f, band, OpeningMinZ + HalfZ), c["Wainscot"], collider: false);
            CreateBox(m, "Wainscot_Right_Back", room, new Vector3(HalfX - inset, band / 2f, (OpeningMaxZ + HalfZ) / 2f),
                new Vector3(0.02f, band, HalfZ - OpeningMaxZ), c["Wainscot"], collider: false);

            // Encadrement métallique de l'ouverture (visible des deux côtés).
            CreateBox(m, "Opening_Jamb_Front", room, new Vector3(x, OpeningHeight / 2f, OpeningMinZ + 0.025f), new Vector3(Wall + 0.04f, OpeningHeight, 0.05f), c["DarkMetal"], collider: false);
            CreateBox(m, "Opening_Jamb_Back", room, new Vector3(x, OpeningHeight / 2f, OpeningMaxZ - 0.025f), new Vector3(Wall + 0.04f, OpeningHeight, 0.05f), c["DarkMetal"], collider: false);
            CreateBox(m, "Opening_Head", room, new Vector3(x, OpeningHeight - 0.025f, center), new Vector3(Wall + 0.04f, 0.05f, width), c["DarkMetal"], collider: false);

            // Cloison provisoire : même plâtre et même soubassement que le mur, panneau « bientôt ».
            var closed = new GameObject("Wall_Right_Closed").transform;
            closed.SetParent(room, false);
            CreateBox(m, "Panel", closed, new Vector3(x, OpeningHeight / 2f, center), new Vector3(Wall * 0.9f, OpeningHeight, width - 0.1f), c["Plaster"]);
            CreateBox(m, "Wainscot", closed, new Vector3(HalfX - inset, band / 2f, center), new Vector3(0.02f, band, width - 0.1f), c["Wainscot"], collider: false);
            RugWashSceneBuilder.CreateSign(closed, "Sign_Extension", "AGRANDISSEMENT", new Vector3(HalfX - 0.03f, 1.6f, center), Quaternion.Euler(0f, 90f, 0f), m, c);
            return closed.gameObject;
        }

        /// <summary>Annexe : sol, plafond, murs, soubassement, aire de lavage 2 et éclairage (inactive avant l'achat).</summary>
        public static GameObject Annex(Transform parent, RugWashContentBuilder.Content c, MeshStore m, AudioManager audio)
        {
            var annex = new GameObject("Annex").transform;
            annex.SetParent(parent, false);
            float cx = (AnnexMinX + AnnexMaxX) / 2f, cz = (AnnexMinZ + AnnexMaxZ) / 2f;
            float w = AnnexMaxX - AnnexMinX, d = AnnexMaxZ - AnnexMinZ;
            CreateBox(m, "Floor", annex, new Vector3(cx, -0.1f, cz), new Vector3(w, 0.2f, d), c["Concrete"]);
            CreateBox(m, "Ceiling", annex, new Vector3(cx, Height + 0.1f, cz), new Vector3(w, 0.2f, d), c["Ceiling"]);
            CreateBox(m, "Wall_Far", annex, new Vector3(AnnexMaxX + Wall / 2f, Height / 2f, cz), new Vector3(Wall, Height, d + Wall * 2f), c["Plaster"]);
            CreateBox(m, "Wall_Front", annex, new Vector3(cx, Height / 2f, AnnexMinZ - Wall / 2f), new Vector3(w, Height, Wall), c["Plaster"]);
            CreateBox(m, "Wall_Back", annex, new Vector3(cx, Height / 2f, AnnexMaxZ + Wall / 2f), new Vector3(w, Height, Wall), c["Plaster"]);
            const float band = 1.1f, inset = 0.012f;
            CreateBox(m, "Wainscot_Far", annex, new Vector3(AnnexMaxX - inset, band / 2f, cz), new Vector3(0.02f, band, d), c["Wainscot"], collider: false);
            CreateBox(m, "Wainscot_Front", annex, new Vector3(cx, band / 2f, AnnexMinZ + inset), new Vector3(w, band, 0.02f), c["Wainscot"], collider: false);
            CreateBox(m, "Wainscot_Back", annex, new Vector3(cx, band / 2f, AnnexMaxZ - inset), new Vector3(w, band, 0.02f), c["Wainscot"], collider: false);
            // Côté atelier : soubassement de l'autre face du mur droit (hors ouverture).
            CreateBox(m, "Wainscot_Near_Front", annex, new Vector3(AnnexMinX + inset, band / 2f, (AnnexMinZ + OpeningMinZ) / 2f),
                new Vector3(0.02f, band, OpeningMinZ - AnnexMinZ), c["Wainscot"], collider: false);
            CreateBox(m, "Wainscot_Near_Back", annex, new Vector3(AnnexMinX + inset, band / 2f, (OpeningMaxZ + AnnexMaxZ) / 2f),
                new Vector3(0.02f, band, AnnexMaxZ - OpeningMaxZ), c["Wainscot"], collider: false);

            RugWashSceneBuilder.BuildWashPad(annex, c, m, audio, Bay2Center, "WashBay_2");
            RugWashSceneBuilder.CreateSign(annex, "Sign_WashBay2", "ZONE 2 · AIRE DE LAVAGE", new Vector3(Bay2Center.x - 1.2f, 1.95f, AnnexMaxZ - 0.04f),
                Quaternion.identity, m, c);
            RugWashSceneBuilder.CreateSign(annex, "Sign_Washer", "MACHINE À LAVER", new Vector3(WasherPosition.x, 1.75f, AnnexMaxZ - 0.04f),
                Quaternion.identity, m, c);

            // Projecteur de l'aire 2 (comme celui de la zone 1) et plafonnier d'ambiance, sans ombres.
            var spotGo = new GameObject("WashBay2_Spot");
            spotGo.transform.SetParent(annex, false);
            spotGo.transform.SetPositionAndRotation(new Vector3(Bay2Center.x, Height - 0.15f, Bay2Center.z - 0.3f), Quaternion.Euler(80f, 0f, 0f));
            var spot = spotGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 115f;
            spot.innerSpotAngle = 70f;
            spot.range = 7f;
            spot.intensity = 6f;
            spot.color = new Color(1f, 0.96f, 0.9f);
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 0.75f;
            spotGo.AddComponent<UniversalAdditionalLightData>();
            var fixture = new GameObject("CeilingLamp_Annex");
            fixture.transform.SetParent(annex, false);
            fixture.transform.localPosition = new Vector3(cx + 1.6f, Height - 0.4f, AnnexMaxZ - 1.0f);
            RugWashDecor.LampFixture(fixture.transform, c, m);
            var lampLight = new GameObject("Light");
            lampLight.transform.SetParent(fixture.transform, false);
            lampLight.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            var light = lampLight.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 7f;
            light.intensity = 2f;
            light.color = new Color(1f, 0.95f, 0.88f);
            light.shadows = LightShadows.None;
            lampLight.AddComponent<UniversalAdditionalLightData>();
            AnnexDecor(annex, c);
            return annex.gameObject;
        }

        /// <summary>
        /// V0.3.0 : décor de l'annexe modélisé dans Blender (MCP), hors de l'aire de lavage 2 et du passage : sèche-linge à côté de
        /// la machine à laver (décor, sans fonction), établi et panneau d'outils contre le mur d'entrée de l'annexe avec sa
        /// suspension, accessoires (aspirateur à eau, bidons de 40 L, pompe, pulvérisateur, lance-brosse, pistolet, chiffons,
        /// éponge). Collisions : boîtes englobantes des objets au sol. Visible avec l'annexe (après l'achat de l'agrandissement).
        /// </summary>
        static void AnnexDecor(Transform annex, RugWashContentBuilder.Content c)
        {
            var root = new GameObject("AnnexDecor").transform;
            root.SetParent(annex, false);
            GameObject Put(string path, Vector3 position, float yaw, bool collider, float surface = 0f, string name = null, Vector3 tilt = default) =>
                RugWashFbxModels.PlaceDecor(path, root, c, position, new Vector3(tilt.x, yaw, tilt.z), collider, surface, name);
            const float benchTop = 0.9f;
            // Sèche-linge : même alignement que la machine à laver (façade vers -Z).
            Put("Environment/Env_Dryer", new Vector3(WasherPosition.x - 1.05f, 0f, WasherPosition.z), 180f, true, name: "Dryer");
            // Établi (dos au mur d'entrée de l'annexe), panneau d'outils au-dessus, suspension au plafond.
            Put("Environment/Env_Workbench", new Vector3(AnnexMaxX - 0.3f, 0f, AnnexMinZ + 0.72f), 0f, true, name: "Workbench");
            // Panneau d'outils : sans la plaque « trous » (pleine et noire à l'export), le panneau perforé en bois reste visible.
            var toolWall = Put("Environment/Env_ToolWall", new Vector3(AnnexMaxX - 1.2f, 1.25f, AnnexMinZ + 0.045f), 0f, false, float.NaN, "ToolWall");
            if (toolWall != null)
            {
                var holes = RugWashFbxModels.FindDeep(toolWall.transform, "Env_ToolWall_Holes");
                if (holes != null) Object.DestroyImmediate(holes.gameObject);
            }
            // Suspension au-dessus de l'établi : lumière ponctuelle douce sans ombres (le coin était sombre).
            var pendant = Put("Environment/Env_PendantLamp", new Vector3(AnnexMaxX - 1.2f, Height, AnnexMinZ + 0.45f), 0f, false, float.NaN, "PendantLamp");
            if (pendant != null)
            {
                var bulb = new GameObject("Light");
                bulb.transform.SetParent(pendant.transform, false);
                bulb.transform.localPosition = new Vector3(0f, -0.95f, 0f);
                var light = bulb.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 3.5f;
                light.intensity = 1.6f;
                light.color = new Color(1f, 0.9f, 0.75f);
                light.shadows = LightShadows.None;
                bulb.AddComponent<UniversalAdditionalLightData>();
            }
            // Sur l'établi.
            Put("Tools/LanceBrosse", new Vector3(AnnexMaxX - 1.55f, 0f, AnnexMinZ + 0.13f), 90f, false, benchTop, "Bench_BrushLance");
            Put("Tools/PulverisateurMousse", new Vector3(AnnexMaxX - 1.85f, 0f, AnnexMinZ + 0.55f), 25f, false, benchTop, "Bench_Sprayer");
            Put("Tools/Pistolet", new Vector3(AnnexMaxX - 0.95f, 0f, AnnexMinZ + 0.58f), 60f, false, benchTop, "Bench_SprayGun", new Vector3(0f, 0f, 90f));
            Put("Props/Eponge", new Vector3(AnnexMaxX - 1.2f, 0f, AnnexMinZ + 0.4f), -15f, false, benchTop, "Bench_Sponge");
            Put("Props/Chiffons", new Vector3(AnnexMaxX - 0.65f, 0f, AnnexMinZ + 0.25f), 20f, false, benchTop, "Bench_Rags");
            // Au sol, le long des murs.
            Put("Tools/AspirateurEau", new Vector3(AnnexMinX + 1.2f, 0f, AnnexMaxZ - 0.6f), 180f, true, name: "WetVacuum");
            Put("Props/Bidon40L", new Vector3(AnnexMinX + 0.8f, 0f, AnnexMinZ + 0.3f), 8f, true, name: "Jerrycan_A");
            Put("Props/Bidon40L", new Vector3(AnnexMinX + 1.15f, 0f, AnnexMinZ + 0.3f), -6f, true, name: "Jerrycan_B");
            Put("Props/Pompe", new Vector3(AnnexMinX + 1.5f, 0f, AnnexMinZ + 0.25f), 0f, true, name: "Pump");
        }

        public struct WasherParts
        {
            public Transform Root;
            public GameObject Body;
            public Transform Drum;
            public Transform Door;
            public TextMesh Display;
            public Renderer Lamp;
            public AudioSource Loop;
        }

        static GameObject Part(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// Machine à laver professionnelle à hublot : carrosserie blanche, bandeau de commande avec écran, hublot chromé
        /// (porte qui s'entrouvre hors lavage), tambour visible qui tourne pendant le cycle, voyant d'état.
        /// </summary>
        public static WasherParts WasherModel(Transform parent, RugWashContentBuilder.Content c, MeshStore m, AudioClip loop, Vector3 position, float yaw)
        {
            var root = new GameObject("Machine_Washer").transform;
            root.SetParent(parent, false);
            root.localPosition = position;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var body = new GameObject("Body").transform;
            body.SetParent(root, false);
            if (WasherFbx(root, body, c, m, loop, out var parts)) return parts;

            const float width = 0.9f, height = 1.15f, depth = 0.85f;
            Part("Cabinet", body, m.Add("Washer_Cabinet", () => RoundedBox(new Vector3(width, height, depth), 0.04f)), c["WhitePlastic"],
                new Vector3(0f, height / 2f, 0f), Quaternion.identity);
            Part("Plinth", body, m.Add("Washer_Plinth", () => RoundedBox(new Vector3(width - 0.04f, 0.08f, depth - 0.04f), 0.01f)), c["DarkMetal"],
                new Vector3(0f, 0.04f, 0f), Quaternion.identity);
            // Bandeau de commande (face avant = -Z local).
            Part("Console", body, m.Add("Washer_Console", () => RoundedBox(new Vector3(width - 0.06f, 0.18f, 0.06f), 0.015f)), c["DarkMetal"],
                new Vector3(0f, height - 0.12f, -depth / 2f - 0.01f), Quaternion.identity);
            Part("Knob", body, m.Add("Washer_Knob", () => Cylinder(0.045f, 0.04f, 20)), c["Chrome"],
                new Vector3(0.3f, height - 0.12f, -depth / 2f - 0.05f), Quaternion.Euler(90f, 0f, 0f));
            var lamp = Part("StatusLamp", body, m.Add("Washer_Lamp", () => Cylinder(0.02f, 0.02f, 12)), c["RobotLed"],
                new Vector3(0.18f, height - 0.12f, -depth / 2f - 0.045f), Quaternion.Euler(90f, 0f, 0f)).GetComponent<Renderer>();

            var display = Display(body, new Vector3(-0.15f, height - 0.12f, -depth / 2f - 0.045f));

            // Tambour (derrière le hublot) : tourne pendant le lavage, axe = Z local.
            var drum = new GameObject("Drum").transform;
            drum.SetParent(body, false);
            drum.localPosition = new Vector3(0f, 0.5f, -depth / 2f + 0.06f);
            Part("Shell", drum, m.Add("Washer_Drum", () => Cylinder(0.28f, 0.05f, 28)), c["Steel"], Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
            for (int i = 0; i < 3; i++)
                Part("Paddle", drum, m.Add("Washer_Paddle", () => RoundedBox(new Vector3(0.05f, 0.16f, 0.06f), 0.01f)), c["DarkMetal"],
                    Quaternion.Euler(0f, 0f, i * 120f) * new Vector3(0f, 0.2f, -0.03f), Quaternion.Euler(0f, 0f, i * 120f));

            // Hublot : charnière à gauche, anneau chromé et vitre.
            var hinge = new GameObject("Door_Hinge").transform;
            hinge.SetParent(body, false);
            hinge.localPosition = new Vector3(-0.32f, 0.5f, -depth / 2f - 0.02f);
            var ring = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i <= 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f;
                ring.Add(new Vector3(Mathf.Cos(a) * 0.3f, Mathf.Sin(a) * 0.3f, 0f));
            }
            Part("Ring", hinge, m.Add("Washer_DoorRing", () => Tube(ring, 0.035f, 10)), c["Chrome"], new Vector3(0.32f, 0f, 0f), Quaternion.identity);
            Part("Glass", hinge, m.Add("Washer_DoorGlass", () => Cylinder(0.28f, 0.01f, 32)), c["Glass"], new Vector3(0.32f, 0f, 0.005f), Quaternion.Euler(90f, 0f, 0f));

            // Collider sous le corps : désactivé avec lui tant que la machine n'est pas achetée.
            var collider = body.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, height / 2f, 0f);
            collider.size = new Vector3(width, height, depth);

            var source = LoopSource(root, loop);
            return new WasherParts { Root = root, Body = body.gameObject, Drum = drum, Door = hinge, Display = display, Lamp = lamp, Loop = source };
        }

        static TextMesh Display(Transform body, Vector3 position)
        {
            var displayGo = new GameObject("Display");
            displayGo.transform.SetParent(body, false);
            displayGo.transform.localPosition = position;
            var display = displayGo.AddComponent<TextMesh>();
            display.text = "LIBRE";
            display.font = UIFont;
            display.fontSize = 64;
            display.characterSize = 0.012f;
            display.anchor = TextAnchor.MiddleCenter;
            display.alignment = TextAlignment.Center;
            display.color = new Color(0.55f, 0.95f, 1f);
            DepthTestedText(display);
            return display;
        }

        static AudioSource LoopSource(Transform root, AudioClip loop)
        {
            var source = root.gameObject.AddComponent<AudioSource>();
            source.clip = loop;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 10f;
            source.volume = 0.25f;
            source.pitch = 0.7f;
            return source;
        }

        /// <summary>
        /// V0.3.0 : machine à laver modélisée dans Blender (MCP) (face avant du FBX vers +Z, tournée de 180° : façade vers -Z
        /// comme l'ancienne). Le hublot du FBX pivote dans « Door_Hinge » (charnière à gauche, même ouverture de 25°) ; tambour
        /// (disque et pales) juste derrière la vitre pour qu'on le voie tourner ; écran et voyant sur le bandeau du FBX.
        /// </summary>
        static bool WasherFbx(Transform root, Transform body, RugWashContentBuilder.Content c, MeshStore m, AudioClip loop, out WasherParts parts)
        {
            parts = default;
            var model = RugWashFbxModels.Place("Environment/Env_Washer", body, c, Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
            if (model == null) return false;
            var face = Quaternion.Euler(0f, 180f, 0f);
            var hinge = new GameObject("Door_Hinge").transform;
            hinge.SetParent(body, false);
            hinge.localPosition = face * new Vector3(0.31f, 0.68f, 0.47f);
            var door = RugWashFbxModels.FindDeep(model.transform, "Env_Washer_Door");
            if (door != null) door.SetParent(hinge, true);

            var drum = new GameObject("Drum").transform;
            drum.SetParent(body, false);
            drum.localPosition = new Vector3(0f, 0.68f, -0.456f);
            Part("Shell", drum, m.Add("Washer_DrumFace", () => Cylinder(0.22f, 0.01f, 28)), c["Steel"], Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
            for (int i = 0; i < 3; i++)
                Part("Paddle", drum, m.Add("Washer_PaddleFlat", () => RoundedBox(new Vector3(0.04f, 0.12f, 0.012f), 0.004f)), c["DarkMetal"],
                    Quaternion.Euler(0f, 0f, i * 120f) * new Vector3(0f, 0.13f, -0.008f), Quaternion.Euler(0f, 0f, i * 120f));

            var display = Display(body, new Vector3(-0.18f, 1.23f, -0.476f));
            display.characterSize = 0.009f;
            var lamp = Part("StatusLamp", body, m.Add("Washer_Lamp", () => Cylinder(0.02f, 0.02f, 12)), c["RobotLed"],
                new Vector3(0.37f, 1.23f, -0.47f), Quaternion.Euler(90f, 0f, 0f)).GetComponent<Renderer>();

            var collider = body.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.65f, 0f);
            collider.size = new Vector3(0.92f, 1.3f, 0.92f);
            parts = new WasherParts { Root = root, Body = body.gameObject, Drum = drum, Door = hinge, Display = display, Lamp = lamp, Loop = LoopSource(root, loop) };
            return true;
        }
    }
}

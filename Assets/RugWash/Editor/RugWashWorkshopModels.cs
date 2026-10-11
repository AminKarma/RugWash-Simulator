using System.Collections.Generic;
using UnityEngine;
using static RugWash.Editor.RugWashBuildHelpers;
using static RugWash.Editor.RugWashProceduralMeshes;

namespace RugWash.Editor
{
    /// <summary>Modèles du robot aspirateur et de sa base (maillages procéduraux, avant = +Z, posés sur y = 0).</summary>
    public static class RugWashWorkshopModels
    {
        public sealed class RobotParts
        {
            public Transform Root;
            public Renderer Led;
            public Transform SideBrush;
            public Transform Turret;
        }

        public sealed class DockParts
        {
            public Transform Root;
            public Renderer Led;
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

        public static RobotParts Robot(Transform parent, MeshStore store, RugWashContentBuilder.Content c)
        {
            var root = new GameObject("RobotVacuum").transform;
            root.SetParent(parent, false);

            Part("Body", root, store.Add("Robot_Body", () => Lathe(new[]
                {
                    new Vector2(0f, 0.012f), new Vector2(0.16f, 0.012f), new Vector2(0.17f, 0.022f), new Vector2(0.17f, 0.058f),
                    new Vector2(0.158f, 0.074f), new Vector2(0.13f, 0.078f), new Vector2(0f, 0.078f),
                }, 40)), c["RobotShell"], Vector3.zero, Quaternion.identity);
            Part("Skirt", root, store.Add("Robot_Skirt", () => Cylinder(0.166f, 0.014f, 40)), c["RobotDark"], new Vector3(0f, 0.005f, 0f), Quaternion.identity);
            Part("Top", root, store.Add("Robot_Top", () => Cylinder(0.125f, 0.004f, 36)), c["RobotDark"], new Vector3(0f, 0.078f, 0f), Quaternion.identity);

            // Pare-chocs avant (arc de cercle).
            var arc = new List<Vector3>();
            for (int i = 0; i <= 16; i++)
            {
                float angle = Mathf.Lerp(-80f, 80f, i / 16f) * Mathf.Deg2Rad;
                arc.Add(new Vector3(Mathf.Sin(angle) * 0.172f, 0.036f, Mathf.Cos(angle) * 0.172f));
            }
            Part("Bumper", root, store.Add("Robot_Bumper", () => Tube(arc, 0.016f, 10)), c["RobotDark"], Vector3.zero, Quaternion.identity);

            var turret = Part("Turret", root, store.Add("Robot_Turret", () => Cylinder(0.04f, 0.026f, 24)), c["RobotShell"],
                new Vector3(0f, 0.08f, -0.045f), Quaternion.identity).transform;
            Part("Turret_Band", turret, store.Add("Robot_TurretBand", () => Cylinder(0.041f, 0.008f, 24)), c["RobotDark"],
                new Vector3(0f, 0.008f, 0f), Quaternion.identity);
            var led = Part("Led", root, store.Add("Robot_Led", () => Cylinder(0.012f, 0.006f, 16)), c["RobotLed"],
                new Vector3(0f, 0.08f, 0.075f), Quaternion.identity);

            var brush = new GameObject("SideBrush").transform;
            brush.SetParent(root, false);
            brush.localPosition = new Vector3(0.11f, 0.004f, 0.11f);
            Part("Hub", brush, store.Add("Robot_BrushHub", () => Cylinder(0.012f, 0.008f, 12)), c["RobotDark"], Vector3.zero, Quaternion.identity);
            var bristle = store.Add("Robot_Bristle", () => RoundedBox(new Vector3(0.075f, 0.003f, 0.006f), 0.0015f, 1));
            for (int i = 0; i < 3; i++)
                Part($"Arm_{i}", brush, bristle, c["Bristle"], Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0.036f, 0f, 0f), Quaternion.Euler(0f, i * 120f, 0f));

            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = new Vector3(0f, 0.05f, 0f);
            collider.size = new Vector3(0.36f, 0.1f, 0.36f);
            return new RobotParts { Root = root, Led = led.GetComponent<Renderer>(), SideBrush = brush, Turret = turret };
        }

        /// <summary>Base de recharge : origine = position du robot en charge, +Z vers la pièce, borne côté -Z.</summary>
        public static DockParts Dock(Transform parent, MeshStore store, RugWashContentBuilder.Content c, Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("RobotDock").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(position, rotation);
            Part("Plate", root, store.Add("Dock_Plate", () => RoundedBox(new Vector3(0.4f, 0.012f, 0.3f), 0.004f)), c["RobotDark"],
                new Vector3(0f, 0.006f, -0.06f), Quaternion.identity);
            Part("Tower", root, store.Add("Dock_Tower", () => RoundedBox(new Vector3(0.34f, 0.13f, 0.07f), 0.02f)), c["RobotShell"],
                new Vector3(0f, 0.065f, -0.205f), Quaternion.identity);
            var led = Part("Led", root, store.Add("Dock_Led", () => Cylinder(0.01f, 0.004f, 12)), c["RobotLed"],
                new Vector3(0f, 0.105f, -0.168f), Quaternion.Euler(90f, 0f, 0f));
            var contact = store.Add("Dock_Contact", () => RoundedBox(new Vector3(0.04f, 0.004f, 0.07f), 0.0015f, 1));
            Part("Contact_L", root, contact, c["Chrome"], new Vector3(-0.07f, 0.013f, -0.08f), Quaternion.identity);
            Part("Contact_R", root, contact, c["Chrome"], new Vector3(0.07f, 0.013f, -0.08f), Quaternion.identity);

            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = new Vector3(0f, 0.07f, -0.17f);
            collider.size = new Vector3(0.36f, 0.14f, 0.14f);
            return new DockParts { Root = root, Led = led.GetComponent<Renderer>() };
        }
    }
}

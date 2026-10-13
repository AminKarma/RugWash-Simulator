using System;
using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>Outil détaillé porté par une silhouette : même modèle que la vue à la première personne.</summary>
    [Serializable]
    public sealed class AvatarTool
    {
        public GameObject root;
        [Tooltip("Extrémité active (bouche de la buse, tête).")]
        public Transform tip;
        [Tooltip("Outil de contact : tête et manche télescopique (vides pour une buse).")]
        public Transform head;
        public Transform shaft;
        public float shaftLength;
        [Tooltip("Portée de l'outil (m), comme pour le joueur local.")]
        public float range = 2.2f;
        [Tooltip("Gant refermé sur la poignée (cible de la main droite).")]
        public Transform rightHand;
        [Tooltip("Point de soutien de la main gauche (optionnel).")]
        public Transform leftHand;
        [Tooltip("Effet visible pendant l'utilisation : 0 aucun, 1 liquide (jet, brume, mousse), 2 poussière.")]
        public int effect;
    }

    /// <summary>
    /// Silhouette d'un joueur vue par les autres : position et orientation (NetworkTransform, autorité du propriétaire),
    /// inclinaison de la tête, outil en main, utilisation de l'outil et nom. Seuls ces états sont transmis (aucune
    /// particule, aucune pose d'os) ; tout le reste est reproduit localement :
    /// <list type="bullet">
    /// <item>outil : le même modèle détaillé qu'à la première personne, porté devant les yeux de la silhouette avec le même
    /// décalage ; le point visé est retrouvé par le même rayon (position, lacet, tangage) et l'outil y est posé avec la même
    /// règle (<see cref="ToolPose"/> : manche télescopique, orientation) ;</item>
    /// <item>effets : jet, brume ou mousse partent de la bouche de l'outil vers le point visé, poussière à la tête ;</item>
    /// <item>corps : bras à deux segments refermés sur la poignée (et sur le manche quand c'est possible), jambes animées
    /// selon la vitesse. Silhouette procédurale (placeholder propre, pas un asset définitif).</item>
    /// </list>
    /// Le propriétaire suit son propre joueur (caméra et entrées restent locales) et ne voit pas sa silhouette.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPlayerAvatar : NetworkBehaviour
    {
        [SerializeField] GameObject visuals;
        [Tooltip("Pivot de la tête (tangage).")]
        [SerializeField] Transform head;
        [Tooltip("Point de vue (yeux), enfant de la tête : support des outils, comme la caméra du joueur local.")]
        [SerializeField] Transform view;
        [Tooltip("Outils, un par emplacement (1 à 8).")]
        [SerializeField] List<AvatarTool> tools = new List<AvatarTool>();
        [SerializeField] TextMesh nameLabel;
        [SerializeField] ParticleSystem liquidEffect;
        [SerializeField] ParticleSystem dustEffect;
        [SerializeField] AudioSource toolAudio;
        [Tooltip("Boucle sonore par emplacement d'outil (1 à 8).")]
        [SerializeField] List<AudioClip> toolLoops = new List<AudioClip>();
        [Tooltip("Surfaces visées par le rayon de la silhouette (comme la visée du joueur local).")]
        [SerializeField] LayerMask aimMask = ~0;

        [Header("Corps")]
        [SerializeField] Transform legLeft;
        [SerializeField] Transform legRight;
        [SerializeField] Transform shoulderLeft;
        [SerializeField] Transform shoulderRight;
        [SerializeField] Transform upperArmLeft;
        [SerializeField] Transform upperArmRight;
        [SerializeField] Transform foreArmLeft;
        [SerializeField] Transform foreArmRight;
        [Tooltip("Gant propre à la silhouette, masqué quand le gant de l'outil prend sa place.")]
        [SerializeField] GameObject handRight;
        [SerializeField] float upperArmLength = 0.3f;
        [SerializeField] float foreArmLength = 0.3f;
        [SerializeField] float strideDegrees = 26f;

        readonly NetworkVariable<FixedString64Bytes> displayName = new NetworkVariable<FixedString64Bytes>();
        readonly NetworkVariable<byte> toolSlot = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> usingTool = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<sbyte> headPitch = new NetworkVariable<sbyte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        string initialName = string.Empty;
        FirstPersonController localPlayer;
        PlayerToolController localTools;
        Transform viewer;
        ToolPose[] poses;
        int shownSlot = -1;
        bool shownUsing, shownNear = true, snapPose;
        CleaningTuning rugTuning;
        float pitch, stridePhase, speed;
        Vector3 lastPosition;
        bool hasLastPosition;
        bool hasPreview, previewUsing;
        int previewSlot;
        float previewPitch;

        int CurrentSlot => hasPreview ? previewSlot : toolSlot.Value;
        bool CurrentUsing => hasPreview ? previewUsing : usingTool.Value;
        float CurrentPitch => hasPreview ? previewPitch : headPitch.Value;

        /// <summary>
        /// Hors réseau (tests, aperçus) : présente la silhouette avec cet état, comme si elle le recevait d'un joueur distant.
        /// </summary>
        public void SetPreviewState(int slot, bool active, float pitchDegrees)
        {
            hasPreview = true;
            previewSlot = slot;
            previewUsing = active;
            previewPitch = pitchDegrees;
            if (visuals != null) visuals.SetActive(true);
        }

        public string DisplayName => displayName.Value.ToString();
        public int ToolSlot => CurrentSlot;
        public bool UsingTool => CurrentUsing;
        /// <summary>Outil détaillé affiché (diagnostic, tests), ou null.</summary>
        public AvatarTool ActiveTool => shownSlot >= 1 && shownSlot <= tools.Count ? tools[shownSlot - 1] : null;
        /// <summary>Point visé reconstitué localement pour l'outil affiché.</summary>
        public Vector3 AimTarget { get; private set; }
        /// <summary>Distance (m) entre l'extrémité active de l'outil affiché et le point visé (-1 sans outil).</summary>
        public float TipToTargetMeters { get; private set; } = -1f;
        public Transform View => view;

        /// <summary>Hôte, avant l'apparition : nom affiché du joueur.</summary>
        public void SetInitialName(string name) => initialName = SessionRules.CleanName(name);

        void Awake()
        {
            EffectBudget.Configure(liquidEffect);
            EffectBudget.Configure(dustEffect);
            poses = new ToolPose[tools.Count];
            for (int i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                if (tool?.root == null || tool.tip == null) continue;
                poses[i] = new ToolPose(tool.root.transform, tool.tip, tool.head, tool.shaft, tool.shaftLength, tool.range);
                tool.root.SetActive(false);
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && !string.IsNullOrEmpty(initialName)) displayName.Value = new FixedString64Bytes(initialName);
            if (IsOwner)
            {
                localPlayer = FindAnyObjectByType<FirstPersonController>();
                localTools = FindAnyObjectByType<PlayerToolController>();
                if (visuals != null) visuals.SetActive(false);
            }
            else if (visuals != null) visuals.SetActive(true);
            displayName.OnValueChanged += (_, value) => RefreshName();
            RefreshName();
            var rug = FindAnyObjectByType<RugCleaningController>();
            if (rug != null) rugTuning = rug.Tuning;
        }

        void RefreshName()
        {
            if (nameLabel != null) nameLabel.text = displayName.Value.ToString();
        }

        void Update()
        {
            if (!IsSpawned)
            {
                if (hasPreview) PresentRemote(Time.deltaTime);
                return;
            }
            if (IsOwner)
            {
                FollowLocalPlayer();
                return;
            }
            PresentRemote(Time.deltaTime);
        }

        void FollowLocalPlayer()
        {
            if (localPlayer == null) return;
            var source = localPlayer.transform;
            transform.SetPositionAndRotation(source.position, Quaternion.Euler(0f, source.eulerAngles.y, 0f));
            sbyte value = (sbyte)Mathf.Clamp(Mathf.RoundToInt(localPlayer.Pitch), -89, 89);
            if (headPitch.Value != value) headPitch.Value = value;
            var tool = localTools != null ? localTools.ActiveTool : null;
            byte slot = tool != null ? (byte)Mathf.Clamp(tool.Definition.slot, 0, 255) : (byte)0;
            if (toolSlot.Value != slot) toolSlot.Value = slot;
            bool active = tool != null && tool.IsUsing;
            if (usingTool.Value != active) usingTool.Value = active;
        }

        /// <summary>Présentation locale d'un joueur distant (exposée pour les tests : un pas de temps donné).</summary>
        public void PresentRemote(float deltaTime)
        {
            // Tangage lissé (le réseau l'envoie au degré près) : la tête et l'outil ne saccadent pas.
            pitch = Mathf.LerpAngle(pitch, CurrentPitch, deltaTime > 0f ? 1f - Mathf.Exp(-18f * deltaTime) : 1f);
            if (head != null) head.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            int slot = CurrentSlot;
            if (slot != shownSlot)
            {
                shownSlot = slot;
                for (int i = 0; i < tools.Count; i++)
                    if (tools[i]?.root != null) tools[i].root.SetActive(i == slot - 1);
                if (handRight != null) handRight.SetActive(ActiveTool == null);
                shownUsing = !CurrentUsing;
                snapPose = true;
                if (slot >= 1 && slot <= poses.Length) poses[slot - 1]?.ResetState();
            }

            var tool = ActiveTool;
            var pose = tool != null ? poses[shownSlot - 1] : null;
            bool active = CurrentUsing;
            if (pose != null && view != null)
            {
                // Même rayon que le réticule du joueur distant (sa position, son lacet et son tangage), même règle de pose.
                var ray = new Ray(view.position, view.forward);
                bool hit = Physics.Raycast(ray, out RaycastHit info, 8f, aimMask, QueryTriggerInteraction.Ignore);
                AimTarget = ToolReach.Target(tool.head != null, hit, info.point, info.distance, ray, tool.range);
                pose.ResetToRest();
                pose.Solve(AimTarget, deltaTime, snapPose);
                snapPose = false;
                // Geste d'utilisation : va-et-vient de la tête d'un outil de contact (brossage, aspiration).
                if (active && tool.head != null)
                    tool.root.transform.localPosition += tool.root.transform.localRotation * Vector3.forward * (Mathf.Sin(Time.time * 14f) * 0.025f);
                pose.Apply();
                TipToTargetMeters = Vector3.Distance(tool.tip.position, AimTarget);
            }
            else TipToTargetMeters = -1f;

            // Effets d'un joueur éloigné : non affichés (son conservé), la silhouette et l'outil restent visibles.
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
            float effectDistance = rugTuning != null ? rugTuning.remoteEffectDistance : 20f;
            bool near = viewer == null || (viewer.position - transform.position).sqrMagnitude <= effectDistance * effectDistance;
            if (active != shownUsing || near != shownNear)
            {
                shownUsing = active;
                shownNear = near;
                SetEffect(liquidEffect, active && near && tool != null && tool.effect == 1);
                SetEffect(dustEffect, active && near && tool != null && tool.effect == 2);
                if (toolAudio != null)
                {
                    var clip = slot >= 1 && slot <= toolLoops.Count ? toolLoops[slot - 1] : null;
                    if (active && clip != null)
                    {
                        toolAudio.clip = clip;
                        if (!toolAudio.isPlaying) toolAudio.Play();
                    }
                    else toolAudio.Stop();
                }
            }
            // Les effets partent de la bouche de l'outil (ou de sa tête) et suivent le point visé.
            if (active && tool != null && tool.tip != null)
            {
                Vector3 toTarget = AimTarget - tool.tip.position;
                var rotation = toTarget.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toTarget) : tool.tip.rotation;
                if (liquidEffect != null && tool.effect == 1) liquidEffect.transform.SetPositionAndRotation(tool.tip.position, rotation);
                if (dustEffect != null && tool.effect == 2) dustEffect.transform.SetPositionAndRotation(tool.tip.position, Quaternion.identity);
            }

            AnimateBody(deltaTime, tool);

            // Nom toujours tourné vers la caméra locale.
            if (nameLabel != null)
            {
                if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
                if (viewer != null) nameLabel.transform.rotation = Quaternion.LookRotation(nameLabel.transform.position - viewer.position);
            }
        }

        /// <summary>Jambes selon la vitesse réelle de la silhouette ; bras refermés sur l'outil (deux segments).</summary>
        void AnimateBody(float deltaTime, AvatarTool tool)
        {
            if (deltaTime > 0f)
            {
                Vector3 position = transform.position;
                float measured = hasLastPosition ? new Vector2(position.x - lastPosition.x, position.z - lastPosition.z).magnitude / deltaTime : 0f;
                lastPosition = position;
                hasLastPosition = true;
                speed = Mathf.Lerp(speed, Mathf.Min(measured, 6f), 1f - Mathf.Exp(-10f * deltaTime));
                float stride = Mathf.Clamp01(speed / 3f);
                stridePhase += speed * deltaTime * 2.6f;
                float swing = Mathf.Sin(stridePhase) * strideDegrees * stride;
                if (legLeft != null) legLeft.localRotation = Quaternion.Euler(swing, 0f, 0f);
                if (legRight != null) legRight.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            }

            Transform body = visuals != null ? visuals.transform : transform;
            Vector3 restRight = body.TransformPoint(new Vector3(0.3f, 0.86f, 0.06f));
            Vector3 restLeft = body.TransformPoint(new Vector3(-0.3f, 0.86f, 0.06f));
            Vector3 rightTarget = tool != null && tool.rightHand != null ? tool.rightHand.TransformPoint(new Vector3(0f, 0f, -0.06f)) : restRight;
            Vector3 leftTarget = restLeft;
            if (tool != null && tool.leftHand != null && shoulderLeft != null
                && Vector3.Distance(shoulderLeft.position, tool.leftHand.position) <= upperArmLength + foreArmLength + 0.06f)
                leftTarget = tool.leftHand.position;
            Vector3 outward = body.right;
            Vector3 down = -body.up;
            SolveArm(shoulderRight, upperArmRight, foreArmRight, rightTarget, (outward + down * 0.8f - body.forward * 0.3f).normalized);
            SolveArm(shoulderLeft, upperArmLeft, foreArmLeft, leftTarget, (-outward + down * 0.8f - body.forward * 0.3f).normalized);
        }

        /// <summary>Bras à deux segments (épaule → coude → poignet) dont le coude part du côté de <paramref name="pole"/>.</summary>
        void SolveArm(Transform shoulder, Transform upper, Transform fore, Vector3 target, Vector3 pole)
        {
            if (shoulder == null || upper == null || fore == null) return;
            Vector3 start = shoulder.position;
            Vector3 toTarget = target - start;
            float a = upperArmLength, b = foreArmLength;
            float distance = Mathf.Clamp(toTarget.magnitude, 0.05f, a + b - 1e-3f);
            Vector3 direction = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : -transform.up;
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(-transform.up, direction);
            bend.Normalize();
            float cosine = Mathf.Clamp((a * a + distance * distance - b * b) / (2f * a * distance), -1f, 1f);
            float angle = Mathf.Acos(cosine);
            Vector3 elbow = start + (direction * Mathf.Cos(angle) + bend * Mathf.Sin(angle)) * a;
            Vector3 wrist = start + direction * distance;
            upper.SetPositionAndRotation(start, Quaternion.LookRotation(elbow - start, bend));
            fore.SetPositionAndRotation(elbow, Quaternion.LookRotation(wrist - elbow, bend));
        }

        static void SetEffect(ParticleSystem effect, bool on)
        {
            if (effect == null) return;
            if (on && !effect.isPlaying) effect.Play();
            else if (!on && effect.isPlaying) effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}

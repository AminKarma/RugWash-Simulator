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
        [Tooltip("Couleurs de la tenue, de la casquette et des gants (personnalisation, v0.1.2).")]
        [SerializeField] CharacterAppearance appearance;
        [Tooltip("Ragdoll de la silhouette quand ce joueur chute (v0.1.2).")]
        [SerializeField] AvatarRagdoll ragdoll;
        [Tooltip("KO du fusil de farces : la tête « éclate » en confettis rouges et blancs (dessin animé), puis revient.")]
        [SerializeField] ParticleSystem headPop;
        [Tooltip("Pivot de la tête (tangage).")]
        [SerializeField] Transform head;
        [Tooltip("Point de vue (yeux), enfant de la tête : support des outils, comme la caméra du joueur local.")]
        [SerializeField] Transform view;
        [Tooltip("Outils, un par emplacement (1 à 8).")]
        [SerializeField] List<AvatarTool> tools = new List<AvatarTool>();
        [SerializeField] TextMesh nameLabel;
        [Tooltip("Voix de ce joueur (chat vocal de proximité), jouée depuis sa tête.")]
        [SerializeField] VoicePlayback voice;
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
        [Tooltip("V0.3.0 : squelettes des modèles d'ouvrier (suivent la silhouette ; tête masquée au KO du fusil).")]
        [SerializeField] List<AvatarSkeleton> skeletons = new List<AvatarSkeleton>();
        [SerializeField] float upperArmLength = 0.3f;
        [SerializeField] float foreArmLength = 0.3f;
        [SerializeField] float strideDegrees = 26f;
        [Tooltip("V0.3.0 : étirement maximal du bras (personnage aux bras courts) pour refermer la main sur l'outil tenu.")]
        [SerializeField, Range(1f, 1.6f)] float maxArmStretch = 1f;

        readonly NetworkVariable<FixedString64Bytes> displayName = new NetworkVariable<FixedString64Bytes>();
        /// <summary>Outils dorés de ce joueur (un bit par emplacement), calculés par l'hôte depuis sa carrière : non falsifiable.</summary>
        readonly NetworkVariable<int> goldenTools = new NetworkVariable<int>();
        readonly NetworkVariable<byte> toolSlot = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> usingTool = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<sbyte> headPitch = new NetworkVariable<sbyte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        /// <summary>Personnalisation du joueur (tenue, casquette, gants), lue dans SA sauvegarde et montrée aux autres.</summary>
        readonly NetworkVariable<int> look = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        /// <summary>Chute : état « à terre » et élan reçu (aucun os n'est transmis, chaque poste simule le ragdoll).</summary>
        // Déclarée avant « à terre » : reçue avec elle, elle est déjà à jour quand la chute commence chez les autres.
        readonly NetworkVariable<byte> knockCause = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> knockedDown = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<Vector3> knockImpulse = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        PlayerKnockdown localKnockdown;
        Pranks.PrankGadgetController localGadgets;

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
        /// <summary>Poignet droit de la silhouette (bout de l'avant-bras, étirement compris).</summary>
        public Vector3 RightWrist => foreArmRight != null ? foreArmRight.TransformPoint(new Vector3(0f, 0f, foreArmLength)) : transform.position;
        /// <summary>Voix de ce joueur (null pour le joueur local : on ne s'entend pas soi-même).</summary>
        public VoicePlayback Voice => voice;
        bool shownSpeaking;
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

        /// <summary>Durée de vol moyenne d'une goutte du jet distant jusqu'au point visé (s).</summary>
        const float LiquidFlightSeconds = 0.32f;
        const float SwingSeconds = 0.4f;
        float swingTimer;

        /// <summary>Hôte : outils dorés de ce joueur (niveau maximal), d'après la carrière qu'il tient.</summary>
        public void SetGoldenTools(int mask)
        {
            if (IsServer && goldenTools.Value != mask) goldenTools.Value = mask;
        }

        public int GoldenTools => goldenTools.Value;

        void ApplyGolden(int mask)
        {
            for (int i = 0; i < tools.Count; i++)
            {
                var root = tools[i]?.root;
                var skin = root != null ? root.GetComponent<Effects.GoldenToolSkin>() : null;
                if (skin != null) skin.SetGolden(i < 31 && (mask & (1 << i)) != 0);
            }
        }

        /// <summary>Coup de batte d'un joueur distant (même dans le vide) : le geste est rejoué sur sa silhouette.</summary>
        public void PlaySwing() => swingTimer = SwingSeconds;
        public bool IsSwinging => swingTimer > 0f;
        /// <summary>Jet, brume ou mousse de l'outil tenu (rattaché à sa buse).</summary>
        public ParticleSystem LiquidEffect => liquidEffect;
        Transform effectsRoot;

        ParticleSystemRenderer liquidRenderer;
        float liquidMinSpeed, liquidMaxSpeed, liquidGravity;

        void Awake()
        {
            effectsRoot = liquidEffect != null ? liquidEffect.transform.parent : null;
            if (liquidEffect != null)
            {
                liquidRenderer = liquidEffect.GetComponent<ParticleSystemRenderer>();
                var speed = liquidEffect.main.startSpeed;
                liquidMinSpeed = speed.mode == ParticleSystemCurveMode.TwoConstants ? speed.constantMin : speed.constant;
                liquidMaxSpeed = speed.mode == ParticleSystemCurveMode.TwoConstants ? speed.constantMax : speed.constant;
                liquidGravity = liquidEffect.main.gravityModifierMultiplier;
            }
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
                localGadgets = FindAnyObjectByType<Pranks.PrankGadgetController>();
                if (visuals != null) visuals.SetActive(false);
                localKnockdown = FindAnyObjectByType<PlayerKnockdown>();
                if (localKnockdown != null)
                {
                    localKnockdown.KnockedDown += OnLocalKnockedDown;
                    localKnockdown.Recovered += OnLocalRecovered;
                }
                var game = FindAnyObjectByType<Core.GameBootstrap>();
                if (game != null && game.SaveSystem != null && game.SaveSystem.Current != null)
                    look.Value = CharacterPalette.Clamp(game.SaveSystem.Profile.character).Pack();
            }
            else if (visuals != null) visuals.SetActive(true);
            look.OnValueChanged += (_, value) => ApplyLook(value);
            ApplyLook(look.Value);
            knockedDown.OnValueChanged += (_, value) => ApplyKnockdown(value);
            knockCause.OnValueChanged += (_, value) =>
            {
                if (!IsOwner && knockedDown.Value && value == (byte)KnockCause.Blaster) PopHead();
            };
            if (!IsOwner && knockedDown.Value) ApplyKnockdown(true);
            displayName.OnValueChanged += (_, value) => RefreshName();
            RefreshName();
            goldenTools.OnValueChanged += (_, value) => ApplyGolden(value);
            ApplyGolden(goldenTools.Value);
            var rug = FindAnyObjectByType<RugCleaningController>();
            if (rug != null) rugTuning = rug.Tuning;
        }

        public override void OnNetworkDespawn()
        {
            if (localKnockdown != null)
            {
                localKnockdown.KnockedDown -= OnLocalKnockedDown;
                localKnockdown.Recovered -= OnLocalRecovered;
            }
        }

        void OnLocalKnockedDown(Vector3 impulse, float seconds)
        {
            if (!IsSpawned || !IsOwner) return;
            knockCause.Value = localKnockdown != null ? (byte)localKnockdown.Cause : (byte)0;
            knockImpulse.Value = impulse;
            knockedDown.Value = true;
        }

        // Relevé forcé pendant un départ (scène déchargée) : la silhouette n'est peut-être plus sur le réseau.
        void OnLocalRecovered()
        {
            if (IsSpawned && IsOwner) knockedDown.Value = false;
        }

        void ApplyKnockdown(bool down)
        {
            if (IsOwner) return;
            ShowKnockdown(down, knockImpulse.Value, (KnockCause)knockCause.Value);
        }

        /// <summary>Tests (hors réseau) : fait chuter ou relève la silhouette.</summary>
        public void PreviewKnockdown(bool down, Vector3 impulse, KnockCause cause = KnockCause.Fall)
        {
            hasPreviewDown = down;
            ShowKnockdown(down, impulse, cause);
        }

        readonly List<Renderer> poppedHead = new List<Renderer>();
        /// <summary>La tête a « éclaté » (KO du fusil) et n'est pas encore revenue.</summary>
        public bool HeadPopped => headPopped;
        bool headPopped;

        void ShowKnockdown(bool down, Vector3 impulse, KnockCause cause)
        {
            if (down)
            {
                if (ragdoll != null) ragdoll.Begin(impulse);
                if (cause == KnockCause.Blaster) PopHead();
                return;
            }
            if (ragdoll != null) ragdoll.End();
            RestoreHead();
        }

        /// <summary>Effet cartoon exagéré, sans gore : la tête disparaît dans une gerbe de confettis rouges et blancs.</summary>
        void PopHead()
        {
            if (head == null || HeadPopped) return;
            headPopped = true;
            foreach (var skeleton in skeletons)
                if (skeleton != null) skeleton.HeadHidden = true;
            foreach (var renderer in head.GetComponentsInChildren<Renderer>())
            {
                if (view != null && renderer.transform.IsChildOf(view)) continue;
                if (!renderer.enabled) continue;
                renderer.enabled = false;
                poppedHead.Add(renderer);
            }
            if (headPop == null) return;
            headPop.transform.position = head.position + Vector3.up * 0.08f;
            if (!headPop.isPlaying) headPop.Play();
            for (int i = 0; i < 36; i++)
                headPop.Emit(new ParticleSystem.EmitParams
                {
                    position = headPop.transform.position + UnityEngine.Random.insideUnitSphere * 0.06f,
                    velocity = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(1.5f, 4f) + Vector3.up * 1.5f,
                    applyShapeToPosition = false,
                }, 1);
        }

        void RestoreHead()
        {
            foreach (var renderer in poppedHead)
                if (renderer != null) renderer.enabled = true;
            poppedHead.Clear();
            headPopped = false;
            foreach (var skeleton in skeletons)
                if (skeleton != null) skeleton.HeadHidden = false;
        }

        public AvatarRagdoll Ragdoll => ragdoll;
        /// <summary>Ce joueur est à terre (chute, coup de batte).</summary>
        public bool IsKnockedDown => hasPreviewDown || knockedDown.Value;
        bool hasPreviewDown;

        /// <summary>Apparence reçue de ce joueur (valeur compacte, -1 si aucune).</summary>
        public int PackedLook => look.Value;

        void ApplyLook(int packed)
        {
            if (appearance != null) appearance.Apply(packed < 0 ? CharacterPalette.Default : CharacterLook.Unpack(packed));
        }

        void RefreshName()
        {
            if (nameLabel == null) return;
            // Chat vocal : le nom passe au vert, suivi de « (parle) », tant que ce joueur parle.
            nameLabel.text = shownSpeaking ? $"{displayName.Value} (parle)" : displayName.Value.ToString();
            nameLabel.color = shownSpeaking ? new Color(0.55f, 1f, 0.6f) : Color.white;
        }

        void Update()
        {
            bool speaking = voice != null && IsSpawned && !IsOwner && voice.IsSpeaking;
            if (speaking != shownSpeaking)
            {
                shownSpeaking = speaking;
                RefreshName();
            }
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
            var gadget = localGadgets != null ? localGadgets.Equipped : Pranks.PrankGadget.None;
            // Gadget de farce en main : emplacement dédié (modèle de la batte ou du fusil, main libre pour le pipi).
            byte slot = gadget != Pranks.PrankGadget.None ? Pranks.PrankTuning.AvatarSlot(gadget)
                : tool != null ? (byte)Mathf.Clamp(tool.Definition.slot, 0, 255) : (byte)0;
            if (toolSlot.Value != slot) toolSlot.Value = slot;
            bool active = gadget == Pranks.PrankGadget.None && tool != null && tool.IsUsing;
            if (usingTool.Value != active) usingTool.Value = active;
        }

        /// <summary>Présentation locale d'un joueur distant (exposée pour les tests : un pas de temps donné).</summary>
        public void PresentRemote(float deltaTime)
        {
            // À terre : la physique (ou le retour à la pose) pilote la silhouette ; ni visée ni bras commandés.
            if (ragdoll != null && ragdoll.IsBusy)
            {
                ragdoll.Tick(deltaTime);
                if (ragdoll.IsActive) return;
            }
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
                // Effets rattachés à la bouche (ou à la tête) de l'outil affiché : ils en partent quel que soit le moment où
                // la silhouette est déplacée dans l'image (interpolation réseau), jamais du corps du joueur.
                Transform anchor = ActiveTool != null && ActiveTool.tip != null ? ActiveTool.tip : effectsRoot;
                if (liquidEffect != null) liquidEffect.transform.SetParent(anchor, false);
                if (dustEffect != null) dustEffect.transform.SetParent(anchor, false);
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
            if (swingTimer > 0f)
            {
                swingTimer = Mathf.Max(0f, swingTimer - deltaTime);
                if (tool != null && shownSlot == Pranks.PrankTuning.AvatarSlot(Pranks.PrankGadget.Bat))
                {
                    float t = 1f - swingTimer / SwingSeconds;
                    float swing = t < 0.38f ? Mathf.SmoothStep(0f, 1f, t / 0.38f) : Mathf.SmoothStep(1f, 0f, (t - 0.38f) / 0.62f);
                    tool.root.transform.localRotation *= Quaternion.Euler(swing * 60f, -swing * 90f, swing * 25f);
                }
            }

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
            // Les effets partent de la bouche de l'outil (ou de sa tête, à laquelle ils sont rattachés) vers le point visé ;
            // le jet est assez rapide pour l'atteindre (comme à la première personne), quelle que soit la distance.
            if (active && tool != null && tool.tip != null)
            {
                Vector3 toTarget = AimTarget - tool.tip.position;
                var rotation = toTarget.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toTarget) : tool.tip.rotation;
                if (liquidEffect != null && tool.effect == 1)
                {
                    liquidEffect.transform.SetPositionAndRotation(tool.tip.position, rotation);
                    var main = liquidEffect.main;
                    // Vitesse de départ du préfabriqué (2,5 à 4 m/s) mise à l'échelle pour atteindre la cible ; les deux bornes sont
                    // réglées (un multiplicateur ne change que la borne haute). Outil doré : jet tendu, sans retombée (V0.2.5).
                    float scale = Mathf.Clamp(toTarget.magnitude / LiquidFlightSeconds, 2.5f, 16f) / (0.5f * (liquidMinSpeed + liquidMaxSpeed));
                    bool golden = slot >= 1 && slot <= 31 && (goldenTools.Value & (1 << (slot - 1))) != 0;
                    main.startSpeed = new ParticleSystem.MinMaxCurve(liquidMinSpeed * scale, liquidMaxSpeed * scale);
                    main.gravityModifierMultiplier = golden ? 0f : liquidGravity;
                    // Gouttes étirées : émission avancée de leur traînée, le jet part de la buse et non du corps.
                    StretchedJet.AlignToNozzle(liquidEffect, liquidRenderer, liquidMinSpeed * scale);
                }
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
            // Personnage à squelette (bras courts) : bras au repos tendus le long du corps, légèrement écartés (sans étirement).
            if (maxArmStretch > 1f && shoulderLeft != null && shoulderRight != null)
            {
                float reach = (upperArmLength + foreArmLength) * 0.99f;
                restRight = shoulderRight.position + (-body.up * 0.97f + body.right * 0.24f + body.forward * 0.03f).normalized * reach;
                restLeft = shoulderLeft.position + (-body.up * 0.97f - body.right * 0.24f + body.forward * 0.03f).normalized * reach;
            }
            Vector3 rightTarget = tool != null && tool.rightHand != null ? tool.rightHand.TransformPoint(new Vector3(0f, 0f, -0.06f)) : restRight;
            Vector3 leftTarget = restLeft;
            if (tool != null && tool.leftHand != null && shoulderLeft != null
                && Vector3.Distance(shoulderLeft.position, tool.leftHand.position) <= (upperArmLength + foreArmLength) * maxArmStretch + 0.06f)
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
            // Bras étirables (V0.3.0) : au-delà de la longueur du bras, les deux segments s'allongent ensemble (borné).
            float stretch = Mathf.Clamp(toTarget.magnitude / (a + b), 1f, maxArmStretch);
            a *= stretch;
            b *= stretch;
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
            upper.localScale = fore.localScale = new Vector3(1f, 1f, stretch);
        }

        static void SetEffect(ParticleSystem effect, bool on)
        {
            if (effect == null) return;
            if (on && !effect.isPlaying) effect.Play();
            else if (!on && effect.isPlaying) effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}

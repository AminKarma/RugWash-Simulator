using System.Collections.Generic;
using RugWash.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>
    /// Silhouette d'un joueur vue par les autres : position et orientation (NetworkTransform, autorité du propriétaire),
    /// inclinaison de la tête, outil en main, utilisation de l'outil (effets et son produits localement) et nom.
    /// Le propriétaire suit son propre joueur (caméra et entrées restent locales) et ne voit pas sa silhouette.
    /// Aucune caméra ni entrée n'existe sur une silhouette : un joueur ne peut jamais piloter celle d'un autre.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPlayerAvatar : NetworkBehaviour
    {
        [SerializeField] GameObject visuals;
        [SerializeField] Transform head;
        [Tooltip("Modèles d'outil, un par emplacement (1 à 8).")]
        [SerializeField] List<GameObject> toolModels = new List<GameObject>();
        [SerializeField] TextMesh nameLabel;
        [SerializeField] ParticleSystem liquidEffect;
        [SerializeField] ParticleSystem dustEffect;
        [SerializeField] AudioSource toolAudio;
        [Tooltip("Boucle sonore par emplacement d'outil (1 à 8).")]
        [SerializeField] List<AudioClip> toolLoops = new List<AudioClip>();

        readonly NetworkVariable<FixedString64Bytes> displayName = new NetworkVariable<FixedString64Bytes>();
        readonly NetworkVariable<byte> toolSlot = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> usingTool = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<sbyte> headPitch = new NetworkVariable<sbyte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        string initialName = string.Empty;
        FirstPersonController localPlayer;
        PlayerToolController localTools;
        Transform viewer;
        int shownSlot = -1;
        bool shownUsing;

        public string DisplayName => displayName.Value.ToString();
        public int ToolSlot => toolSlot.Value;
        public bool UsingTool => usingTool.Value;

        /// <summary>Hôte, avant l'apparition : nom affiché du joueur.</summary>
        public void SetInitialName(string name) => initialName = SessionRules.CleanName(name);

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
        }

        void RefreshName()
        {
            if (nameLabel != null) nameLabel.text = displayName.Value.ToString();
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                FollowLocalPlayer();
                return;
            }
            PresentRemote();
        }

        void FollowLocalPlayer()
        {
            if (localPlayer == null) return;
            var source = localPlayer.transform;
            transform.SetPositionAndRotation(source.position, Quaternion.Euler(0f, source.eulerAngles.y, 0f));
            sbyte pitch = (sbyte)Mathf.Clamp(Mathf.RoundToInt(localPlayer.Pitch), -89, 89);
            if (headPitch.Value != pitch) headPitch.Value = pitch;
            var tool = localTools != null ? localTools.ActiveTool : null;
            byte slot = tool != null ? (byte)Mathf.Clamp(tool.Definition.slot, 0, 255) : (byte)0;
            if (toolSlot.Value != slot) toolSlot.Value = slot;
            bool active = tool != null && tool.IsUsing;
            if (usingTool.Value != active) usingTool.Value = active;
        }

        void PresentRemote()
        {
            if (head != null) head.localRotation = Quaternion.Euler(headPitch.Value, 0f, 0f);
            int slot = toolSlot.Value;
            if (slot != shownSlot)
            {
                shownSlot = slot;
                for (int i = 0; i < toolModels.Count; i++)
                    if (toolModels[i] != null) toolModels[i].SetActive(i == slot - 1);
                shownUsing = !usingTool.Value;
            }
            bool active = usingTool.Value;
            if (active != shownUsing)
            {
                shownUsing = active;
                bool liquid = slot == 2 || slot == 4 || slot == 6;
                bool dusty = slot == 1 || slot == 3 || slot == 7 || slot == 8;
                SetEffect(liquidEffect, active && liquid);
                SetEffect(dustEffect, active && dusty);
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

            // Nom toujours tourné vers la caméra locale.
            if (nameLabel != null)
            {
                if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
                if (viewer != null) nameLabel.transform.rotation = Quaternion.LookRotation(nameLabel.transform.position - viewer.position);
            }
        }

        static void SetEffect(ParticleSystem effect, bool on)
        {
            if (effect == null) return;
            if (on && !effect.isPlaying) effect.Play();
            else if (!on && effect.isPlaying) effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}

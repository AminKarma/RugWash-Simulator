using System;
using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Core;
using RugWash.Effects;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>
    /// Sélection et utilisation des outils : vise depuis le centre de la caméra, transmet l'intention
    /// d'utilisation à l'outil actif. L'outil ne nettoie que ce qui est réellement visé.
    /// </summary>
    // Après le déplacement du joueur (ordre 0) : la visée utilise la position de caméra de cette image.
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public sealed class PlayerToolController : MonoBehaviour
    {
        [SerializeField] Camera playerCamera;
        [SerializeField] PlayerInputReader input;
        [SerializeField] PlayerEquipment equipment;
        [SerializeField] GameStateManager gameState;
        [SerializeField] ToolTargetIndicator targetIndicator;
        [SerializeField] List<CleaningToolBase> tools = new List<CleaningToolBase>();
        [SerializeField] LayerMask aimMask = ~0;
        [SerializeField] float maxAimDistance = 6f;

        readonly Dictionary<Collider, RugCleaningController> rugCache = new Dictionary<Collider, RugCleaningController>();
        int activeIndex = -1;

        public IReadOnlyList<CleaningToolBase> Tools => tools;
        public CleaningToolBase ActiveTool => activeIndex >= 0 && activeIndex < tools.Count ? tools[activeIndex] : null;
        public ToolAim CurrentAim { get; private set; }
        public PlayerEquipment Equipment => equipment;

        /// <summary>Utilisation forcée (tests automatisés uniquement) ; jamais activée en jeu.</summary>
        public bool AutomationUseOverride { get; set; }

        public event Action<CleaningToolBase> ActiveToolChanged;
        /// <summary>Le joueur a demandé un outil pas encore acheté.</summary>
        public event Action<CleaningToolBase> LockedToolRequested;

        public bool IsConfigured(out string missing)
        {
            missing = playerCamera == null ? nameof(playerCamera)
                : input == null ? nameof(input)
                : equipment == null ? nameof(equipment)
                : gameState == null ? nameof(gameState)
                : targetIndicator == null ? nameof(targetIndicator)
                : tools.Count == 0 ? nameof(tools) : null;
            if (missing != null) return false;
            foreach (var tool in tools)
            {
                if (tool == null) { missing = "tools[null]"; return false; }
                if (!tool.IsConfigured(out string toolMissing)) { missing = $"{tool.name}.{toolMissing}"; return false; }
            }
            return true;
        }

        void Awake()
        {
            if (!IsConfigured(out string missing))
            {
                Debug.LogError($"[RugWash] PlayerToolController : référence manquante '{missing}'.", this);
                enabled = false;
                return;
            }
            tools.Sort((a, b) => a.Definition.slot.CompareTo(b.Definition.slot));
            foreach (var tool in tools)
            {
                tool.Initialize(equipment);
                tool.ContactStarted += OnToolContact;
            }
        }

        void Start()
        {
            if (activeIndex < 0) SelectIndex(FirstUsableIndex());
        }

        int FirstUsableIndex()
        {
            for (int i = 0; i < tools.Count; i++)
                if (tools[i].IsUnlocked) return i;
            return 0;
        }

        /// <summary>Si l'outil actif n'est pas (ou plus) utilisable, sélectionne le premier outil disponible.</summary>
        public void EnsureUsableToolSelected()
        {
            if (activeIndex >= 0 && ActiveTool != null && !ActiveTool.IsUnlocked) SelectIndex(FirstUsableIndex());
        }

        void OnToolContact(CleaningToolBase tool)
        {
            if (tool == ActiveTool && targetIndicator != null) targetIndicator.Pulse();
        }

        void OnDisable()
        {
            ActiveTool?.StopUsing();
            if (targetIndicator != null) targetIndicator.Hide();
        }

        void Update()
        {
            var tool = ActiveTool;
            if (tool == null) return;

            if (!gameState.IsGameplayActive)
            {
                tool.StopUsing();
                targetIndicator.Hide();
                return;
            }

            HandleSelection();
            tool = ActiveTool;

            var aim = ComputeAim();
            CurrentAim = aim;
            bool wantsUse = input.UseHeld || AutomationUseOverride;
            tool.Tick(wantsUse, aim, Time.deltaTime);

            if (aim.HasRug)
            {
                bool clean = aim.Rug.TrySampleCell(aim.Point, out RugCell cell) && !DirtScan.IsDirty(cell)
                             && cell.Residue < DirtScan.DefaultResidueThreshold;
                targetIndicator.Show(aim.Point, aim.Normal, tool.Radius, tool.SpreadAspect, CleaningToolBase.SpreadAxis(aim),
                    !tool.IsTargetOutOfRange, tool.IsUsing, clean);
            }
            else targetIndicator.Hide();
        }

        void HandleSelection()
        {
            int slot = input.ToolSlotPressedThisFrame();
            if (slot > 0)
            {
                SelectSlot(slot);
                return;
            }
            int step = input.ToolScrollStep;
            if (step == 0 || tools.Count <= 1) return;
            // La molette saute les outils pas encore achetés.
            int index = activeIndex;
            for (int i = 0; i < tools.Count; i++)
            {
                index = (index - step + tools.Count) % tools.Count;
                if (tools[index].IsUnlocked) break;
            }
            SelectIndex(index);
        }

        /// <summary>Sélectionne l'outil associé à une touche numérique. Faux si aucun outil n'y est associé.</summary>
        public bool SelectSlot(int slot)
        {
            for (int i = 0; i < tools.Count; i++)
                if (tools[i].Definition.slot == slot)
                {
                    if (!tools[i].IsUnlocked)
                    {
                        LockedToolRequested?.Invoke(tools[i]);
                        return false;
                    }
                    SelectIndex(i);
                    return true;
                }
            return false;
        }

        public void SelectIndex(int index)
        {
            if (index < 0 || index >= tools.Count || index == activeIndex) return;
            ActiveTool?.Unequip();
            activeIndex = index;
            ActiveTool.Equip();
            ActiveToolChanged?.Invoke(ActiveTool);
        }

        ToolAim ComputeAim()
        {
            var aim = new ToolAim { Ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) };
            if (Physics.Raycast(aim.Ray, out RaycastHit hit, maxAimDistance, aimMask, QueryTriggerInteraction.Ignore))
            {
                aim.HasHit = true;
                aim.Point = hit.point;
                aim.Normal = hit.normal;
                aim.Distance = hit.distance;
                aim.Rug = ResolveRug(hit.collider);
            }
            return aim;
        }

        RugCleaningController ResolveRug(Collider collider)
        {
            if (!rugCache.TryGetValue(collider, out var rug))
            {
                rug = collider.GetComponentInParent<RugCleaningController>();
                rugCache[collider] = rug;
            }
            return rug;
        }
    }
}

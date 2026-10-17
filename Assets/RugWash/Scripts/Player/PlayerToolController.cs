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
        [Tooltip("Son de refus quand l'outil ne peut pas agir (réserve vide…), optionnel.")]
        [SerializeField] Audio.AudioManager audioManager;
        [Tooltip("Déplacement du joueur : ralenti par un outil lourd en marche (monobrosse), optionnel.")]
        [SerializeField] FirstPersonController movement;
        [Tooltip("Inventaire à 4 emplacements (grande mise à jour) : seuls les objets portés se sélectionnent ; optionnel.")]
        [SerializeField] PlayerInventory inventory;
        bool wantedUse;

        readonly Dictionary<Collider, RugCleaningController> rugCache = new Dictionary<Collider, RugCleaningController>();
        int activeIndex = -1;

        public IReadOnlyList<CleaningToolBase> Tools => tools;
        public CleaningToolBase ActiveTool => activeIndex >= 0 && activeIndex < tools.Count ? tools[activeIndex] : null;
        public ToolAim CurrentAim { get; private set; }
        public PlayerEquipment Equipment => equipment;
        public PlayerInventory Inventory => inventory;

        /// <summary>Utilisation forcée (tests automatisés uniquement) ; jamais activée en jeu.</summary>
        public bool AutomationUseOverride { get; set; }
        /// <summary>Joueur à terre (chute) : aucun outil utilisable.</summary>
        public bool Locked { get; set; }

        bool suspended;
        /// <summary>
        /// Gadget de farce en main (v0.1.2) : l'outil est rangé (modèle caché) et inutilisable. Une touche d'outil ou la
        /// molette demande le retour aux outils (<see cref="ResumeRequested"/>).
        /// </summary>
        public bool Suspended
        {
            get => suspended;
            set
            {
                if (suspended == value) return;
                suspended = value;
                if (value)
                {
                    ActiveTool?.Unequip();
                    if (targetIndicator != null) targetIndicator.Hide();
                }
                else ActiveTool?.Equip();
                SuspendedChanged?.Invoke();
            }
        }

        /// <summary>Gadget sorti ou rangé (la barre d'outils retire ou remet la surbrillance de l'outil actif).</summary>
        public event Action SuspendedChanged;

        /// <summary>Le joueur a choisi un outil pendant qu'un gadget est en main.</summary>
        public event Action ResumeRequested;

        public event Action<CleaningToolBase> ActiveToolChanged;
        /// <summary>Le joueur a demandé un outil pas encore acheté.</summary>
        public event Action<CleaningToolBase> LockedToolRequested;
        /// <summary>Touche d'un emplacement d'inventaire qui porte un gadget de farce (le contrôleur de gadgets l'équipe).</summary>
        public event Action<Pranks.PrankGadget> GadgetRequested;

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
            // Premier outil porté (inventaire), dans l'ordre de l'inventaire ; sans inventaire, le premier débloqué.
            if (inventory != null)
                for (int slot = 0; slot < inventory.Carried.Count; slot++)
                {
                    var item = inventory.ItemAt(slot);
                    if (item.IsTool && item.Tool.IsUnlocked) return tools.IndexOf(item.Tool);
                }
            for (int i = 0; i < tools.Count; i++)
                if (tools[i].IsUnlocked) return i;
            return 0;
        }

        bool IsUsable(CleaningToolBase tool) => tool != null && tool.IsUnlocked && (inventory == null || inventory.IsCarried(tool));

        /// <summary>Si l'outil actif n'est pas (ou plus) utilisable (non acheté, rangé au casier), sélectionne le premier outil porté.</summary>
        public void EnsureUsableToolSelected()
        {
            if (activeIndex >= 0 && ActiveTool != null && !IsUsable(ActiveTool)) SelectFirstCarried();
        }

        /// <summary>Sélectionne le premier outil porté (après le rangement de l'outil en main au casier).</summary>
        public void SelectFirstCarried()
        {
            int index = FirstUsableIndex();
            if (index == activeIndex || index < 0 || index >= tools.Count) return;
            ActiveTool?.Unequip();
            activeIndex = index;
            if (!Suspended) ActiveTool.Equip();
            ActiveToolChanged?.Invoke(ActiveTool);
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

            if (Suspended && gameState.IsGameplayActive && !Locked)
            {
                int pressed = input.ToolSlotPressedThisFrame();
                var item = inventory != null && pressed > 0 ? inventory.ItemAt(pressed - 1) : default;
                // Touche d'un gadget porté : changement de gadget ; touche d'un outil ou molette : retour aux outils.
                if (item.IsGadget) GadgetRequested?.Invoke(item.Gadget);
                else if (pressed > 0 || input.ToolScrollStep != 0) ResumeRequested?.Invoke();
            }
            if (Suspended)
            {
                if (movement != null) movement.SpeedMultiplier = 1f;
                return;
            }

            if (!gameState.IsGameplayActive || Locked)
            {
                tool.StopUsing();
                targetIndicator.Hide();
                if (movement != null) movement.SpeedMultiplier = 1f;
                return;
            }

            HandleSelection();
            tool = ActiveTool;
            if (input.ProductPressed && equipment.Inventory != null) equipment.Inventory.Book.SelectNext();

            var aim = ComputeAim();
            CurrentAim = aim;
            bool wantsUse = input.UseHeld || AutomationUseOverride;
            tool.Tick(wantsUse, aim, Time.deltaTime);
            // Appui sur « utiliser » sans effet possible (réserve vide…) : son de refus ; la raison s'affiche dans le HUD.
            if (wantsUse && !wantedUse && !tool.IsUsing && tool.IsUnlocked && tool.GetBlockReason() != null && audioManager != null)
                audioManager.PlayInterface(audioManager.DenyClip, 0.8f);
            wantedUse = wantsUse;
            if (movement != null) movement.SpeedMultiplier = tool.MoveSpeedFactor;

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
                if (inventory == null)
                {
                    SelectSlot(slot);
                    return;
                }
                // Touches 1 à 4 : emplacements de l'inventaire (outil ou gadget porté) ; un emplacement vide ne fait rien.
                var item = inventory.ItemAt(slot - 1);
                if (item.IsTool) SelectIndex(tools.IndexOf(item.Tool));
                else if (item.IsGadget) GadgetRequested?.Invoke(item.Gadget);
                return;
            }
            int step = input.ToolScrollStep;
            if (step == 0 || tools.Count <= 1) return;
            // La molette parcourt les outils utilisables (achetés, et portés si l'inventaire est actif).
            int index = activeIndex;
            for (int i = 0; i < tools.Count; i++)
            {
                index = (index - step + tools.Count) % tools.Count;
                if (IsUsable(tools[index])) break;
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
            // Un outil au casier ne se sort pas d'une touche : il faut d'abord le prendre au casier (sauf automatisation).
            if (inventory != null && !inventory.IsCarried(tools[index]) && !inventory.CarryForAutomation(tools[index])) return;
            ActiveTool?.Unequip();
            activeIndex = index;
            if (!Suspended) ActiveTool.Equip();
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
                var surface = ResolveRug(hit.collider);
                // Le sol de la salle est hors de la zone de nettoyage : visé, il ne reçoit rien (seul l'effet visuel du jet).
                aim.Rug = surface != null && surface.AcceptsDirt ? surface : null;
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

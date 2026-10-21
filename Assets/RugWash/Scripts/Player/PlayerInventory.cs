using System;
using System.Collections.Generic;
using RugWash.Cleaning;
using RugWash.Pranks;
using UnityEngine;

namespace RugWash.Player
{
    /// <summary>Résultat d'un déplacement entre l'inventaire et le casier.</summary>
    public enum InventoryResult
    {
        Done,
        NotOwned,
        Full,
        AlreadyCarried,
        NotCarried,
        /// <summary>Le dernier outil de nettoyage porté ne peut pas être rangé (les mains ne restent jamais vides).</summary>
        LastTool,
        /// <summary>Le pipi n'est jamais un objet d'inventaire (toujours disponible).</summary>
        NotAnItem,
    }

    /// <summary>Objet transportable : un outil de nettoyage ou un gadget de farce (le pipi n'en fait jamais partie).</summary>
    public readonly struct InventoryItem
    {
        public readonly string Id;
        public readonly CleaningToolBase Tool;
        public readonly PrankGadget Gadget;
        public readonly string DisplayName;

        public InventoryItem(string id, CleaningToolBase tool, PrankGadget gadget, string displayName)
        {
            Id = id;
            Tool = tool;
            Gadget = gadget;
            DisplayName = displayName;
        }

        public bool IsTool => Tool != null;
        public bool IsGadget => Gadget != PrankGadget.None;
        public bool IsValid => !string.IsNullOrEmpty(Id);
    }

    /// <summary>
    /// Règles pures de l'inventaire (testées en EditMode). Ce qui est porté est une liste ordonnée d'identifiants (au plus
    /// <see cref="PlayerInventory.Capacity"/>) ; le casier contient tout ce qui est possédé et pas porté. Le casier n'est
    /// donc jamais une seconde copie : un objet est soit porté, soit rangé — jamais les deux, jamais dupliqué.
    /// </summary>
    public static class InventoryRules
    {
        /// <summary>Identifiant d'inventaire d'un gadget (le pipi n'en a pas : null).</summary>
        public static string GadgetId(PrankGadget gadget) =>
            gadget == PrankGadget.Bat ? "prank:bat" : gadget == PrankGadget.Blaster ? "prank:blaster" : gadget == PrankGadget.Dynamite ? "prank:dynamite" : null;

        public static PrankGadget GadgetOf(string id) =>
            id == "prank:bat" ? PrankGadget.Bat : id == "prank:blaster" ? PrankGadget.Blaster : id == "prank:dynamite" ? PrankGadget.Dynamite : PrankGadget.None;

        /// <summary>
        /// Identifiant d'inventaire de l'objet que débloque cet achat : l'outil (son identifiant) ou le gadget porté ; null
        /// pour tout ce qui ne se porte pas (améliorations, atelier, pipi qui n'occupe aucun emplacement).
        /// </summary>
        public static string ItemIdOf(Economy.UpgradeDefinition upgrade)
        {
            if (upgrade == null || string.IsNullOrEmpty(upgrade.targetToolId)) return null;
            if (upgrade.effect == Economy.UpgradeEffect.UnlockTool) return upgrade.targetToolId;
            if (upgrade.effect != Economy.UpgradeEffect.PrankGadget) return null;
            return upgrade.targetToolId == "bat" ? GadgetId(PrankGadget.Bat) : upgrade.targetToolId == "blaster" ? GadgetId(PrankGadget.Blaster)
                : upgrade.targetToolId == "dynamite" ? GadgetId(PrankGadget.Dynamite) : null;
        }

        /// <summary>Ordre de départ (sauvegarde sans inventaire) : détergent, brosse, seau, puis les outils dans l'ordre des touches.</summary>
        public static readonly string[] Preferred = { "soap", "brush", "bucket", "rinse", "vacuum", "extractor" };

        /// <summary>
        /// Inventaire relu ou recalculé : objets possédés seulement, sans doublon, au plus <paramref name="capacity"/>, au moins un
        /// outil. Sans inventaire enregistré (ancienne sauvegarde, nouvelle partie), les objets préférés puis l'ordre des touches.
        /// </summary>
        public static List<string> Normalize(IEnumerable<string> saved, IReadOnlyList<string> ownedInOrder, Func<string, bool> isTool, int capacity)
        {
            var owned = new HashSet<string>(ownedInOrder);
            var result = new List<string>();
            if (saved != null)
                foreach (string id in saved)
                    if (result.Count < capacity && owned.Contains(id) && !result.Contains(id)) result.Add(id);
            if (result.Count == 0)
            {
                foreach (string id in Preferred)
                    if (result.Count < capacity && owned.Contains(id) && !result.Contains(id)) result.Add(id);
                foreach (string id in ownedInOrder)
                    if (result.Count < capacity && isTool(id) && !result.Contains(id)) result.Add(id);
            }
            if (!result.Exists(id => isTool(id)))
            {
                foreach (string id in ownedInOrder)
                {
                    if (!isTool(id)) continue;
                    if (result.Count >= capacity) result.RemoveAt(result.Count - 1);
                    result.Insert(0, id);
                    break;
                }
            }
            return result;
        }

        public static InventoryResult CanCarry(IReadOnlyList<string> carried, ICollection<string> owned, string id, int capacity)
        {
            if (string.IsNullOrEmpty(id)) return InventoryResult.NotAnItem;
            if (!owned.Contains(id)) return InventoryResult.NotOwned;
            if (Contains(carried, id)) return InventoryResult.AlreadyCarried;
            if (carried.Count >= capacity) return InventoryResult.Full;
            return InventoryResult.Done;
        }

        public static InventoryResult CanStore(IReadOnlyList<string> carried, string id, Func<string, bool> isTool)
        {
            if (string.IsNullOrEmpty(id)) return InventoryResult.NotAnItem;
            if (!Contains(carried, id)) return InventoryResult.NotCarried;
            if (isTool(id))
            {
                int tools = 0;
                foreach (string other in carried) if (isTool(other)) tools++;
                if (tools <= 1) return InventoryResult.LastTool;
            }
            return InventoryResult.Done;
        }

        static bool Contains(IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == id) return true;
            return false;
        }

        public static string Describe(InventoryResult result, string name = null) => result switch
        {
            InventoryResult.Done => string.Empty,
            InventoryResult.NotOwned => $"{name ?? "Objet"} : à acheter d'abord",
            InventoryResult.Full => $"Inventaire plein : rangez d'abord un objet au casier",
            InventoryResult.AlreadyCarried => $"{name ?? "Objet"} : déjà sur vous",
            InventoryResult.NotCarried => $"{name ?? "Objet"} : déjà au casier",
            InventoryResult.LastTool => "Gardez au moins un outil de nettoyage sur vous",
            InventoryResult.NotAnItem => "Le pipi n'occupe pas d'emplacement : il est toujours disponible (G)",
            _ => "Impossible",
        };
    }

    /// <summary>
    /// Inventaire du joueur : <see cref="Capacity"/> emplacements pour les outils de nettoyage et les gadgets de farce
    /// transportés (touches 1 à 4, molette). Le pipi n'occupe jamais d'emplacement. Le reste de ce qui est possédé attend au
    /// casier (<see cref="Stored"/>). Un nouvel achat va dans l'inventaire s'il reste de la place, sinon au casier.
    /// Sauvegardé dans l'emplacement de sauvegarde (liste portée) ; propre à chaque joueur, aussi en ligne.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInventory : MonoBehaviour
    {
        /// <summary>Emplacements de base ; V0.4.2 : un 5e s'achète (<see cref="Economy.UpgradeEffect.InventorySlot"/>).</summary>
        public const int Capacity = 4;
        /// <summary>V0.4.2 : nombre maximal d'emplacements (barre d'objets et casier en affichent autant, verrouillés au besoin).</summary>
        public const int MaxCapacity = 5;

        /// <summary>Emplacements réels avec <paramref name="extraSlots"/> emplacements achetés (4 à 5).</summary>
        public static int CapacityFor(int extraSlots) => Mathf.Clamp(Capacity + extraSlots, Capacity, MaxCapacity);

        /// <summary>Emplacements utilisables par ce joueur (4, ou 5 après l'achat de l'emplacement supplémentaire).</summary>
        public int SlotCount => CapacityFor(upgrades != null && upgrades.Stats != null ? upgrades.Stats.ExtraInventorySlots : 0);

        /// <summary>Article de la boutique qui ajoute un emplacement (prix affiché sur l'emplacement verrouillé) ; null s'il n'existe pas.</summary>
        public Economy.UpgradeDefinition SlotUpgrade
        {
            get
            {
                if (upgrades == null) return null;
                foreach (var upgrade in upgrades.Catalog)
                    if (upgrade != null && upgrade.effect == Economy.UpgradeEffect.InventorySlot) return upgrade;
                return null;
            }
        }

        /// <summary>
        /// Tests automatisés et benchmark uniquement : sélectionner un outil non porté le met dans l'inventaire (à la place du
        /// dernier objet). Jamais activé en jeu : le joueur passe par le casier.
        /// </summary>
        public static bool AutomationCarriesOnSelect;

        [SerializeField] PlayerToolController tools;
        [SerializeField] PrankGadgetController gadgets;
        [SerializeField] Economy.UpgradeManager upgrades;

        readonly List<string> carried = new List<string>();
        readonly HashSet<string> known = new HashSet<string>();
        readonly List<string> ownedOrder = new List<string>();
        bool restored;

        public IReadOnlyList<string> Carried => carried;
        public event Action Changed;
        /// <summary>Message pour le joueur (achat rangé au casier, refus…).</summary>
        public event Action<string> Message;

        public bool IsConfigured(out string missing)
        {
            missing = tools == null ? nameof(tools) : upgrades == null ? nameof(upgrades) : null;
            return missing == null;
        }

        void OnEnable()
        {
            if (upgrades != null) upgrades.Changed += OnOwnedChanged;
        }

        void OnDisable()
        {
            if (upgrades != null) upgrades.Changed -= OnOwnedChanged;
        }

        bool IsTool(string id) => FindTool(id) != null;

        CleaningToolBase FindTool(string id)
        {
            if (tools == null || string.IsNullOrEmpty(id)) return null;
            foreach (var tool in tools.Tools)
                if (tool != null && tool.Definition != null && tool.Definition.id == id) return tool;
            return null;
        }

        /// <summary>Objets possédés, dans l'ordre des touches (outils), puis les gadgets.</summary>
        public IReadOnlyList<string> Owned()
        {
            ownedOrder.Clear();
            if (tools != null)
                foreach (var tool in tools.Tools)
                    if (tool != null && tool.IsUnlocked && tool.Definition != null) ownedOrder.Add(tool.Definition.id);
            if (gadgets != null)
                foreach (var gadget in PrankTuning.All)
                {
                    string id = InventoryRules.GadgetId(gadget);
                    if (id != null && gadgets.Owns(gadget)) ownedOrder.Add(id);
                }
            return ownedOrder;
        }

        /// <summary>Contenu du casier : possédé et pas porté.</summary>
        public List<string> Stored()
        {
            var stored = new List<string>();
            foreach (string id in Owned())
                if (!carried.Contains(id)) stored.Add(id);
            return stored;
        }

        public InventoryItem Describe(string id)
        {
            var tool = FindTool(id);
            if (tool != null) return new InventoryItem(id, tool, PrankGadget.None, tool.Definition.displayName);
            var gadget = InventoryRules.GadgetOf(id);
            if (gadget != PrankGadget.None) return new InventoryItem(id, null, gadget, gadgets != null ? gadgets.Name(gadget) : id);
            return default;
        }

        public InventoryItem ItemAt(int index) => index >= 0 && index < carried.Count ? Describe(carried[index]) : default;

        public bool IsCarried(CleaningToolBase tool) => tool != null && tool.Definition != null && carried.Contains(tool.Definition.id);
        public bool IsCarried(PrankGadget gadget) => gadget == PrankGadget.Pee || carried.Contains(InventoryRules.GadgetId(gadget) ?? string.Empty);

        /// <summary>Inventaire enregistré (sauvegarde) : relu et vérifié contre ce qui est possédé.</summary>
        public void Restore(IEnumerable<string> saved)
        {
            var owned = Owned();
            carried.Clear();
            carried.AddRange(InventoryRules.Normalize(saved, owned, IsTool, SlotCount));
            known.Clear();
            foreach (string id in owned) known.Add(id);
            restored = true;
            Changed?.Invoke();
        }

        public List<string> Snapshot() => new List<string>(carried);

        void OnOwnedChanged()
        {
            if (!restored)
            {
                Restore(null);
                return;
            }
            var owned = Owned();
            var ownedSet = new HashSet<string>(owned);
            bool changed = carried.RemoveAll(id => !ownedSet.Contains(id)) > 0;
            foreach (string id in owned)
            {
                if (!known.Add(id)) continue;
                // Nouvel objet (achat, carrière reçue de l'hôte) : dans l'inventaire s'il reste de la place.
                if (carried.Count < SlotCount)
                {
                    carried.Add(id);
                    changed = true;
                }
                else Message?.Invoke($"{Describe(id).DisplayName} rangé dans le casier (inventaire plein)");
            }
            known.RemoveWhere(id => !ownedSet.Contains(id));
            if (!carried.Exists(IsTool))
            {
                var normalized = InventoryRules.Normalize(carried, owned, IsTool, SlotCount);
                carried.Clear();
                carried.AddRange(normalized);
                changed = true;
            }
            // Emplacement acheté retiré (jamais en jeu, mais une sauvegarde modifiée) : le surplus retourne au casier.
            if (carried.Count > SlotCount)
            {
                carried.RemoveRange(SlotCount, carried.Count - SlotCount);
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>Prend un objet au casier.</summary>
        public InventoryResult Carry(string id)
        {
            var owned = new HashSet<string>(Owned());
            var result = InventoryRules.CanCarry(carried, owned, id, SlotCount);
            if (result != InventoryResult.Done) return result;
            carried.Add(id);
            Changed?.Invoke();
            return result;
        }

        /// <summary>Range un objet au casier (l'outil ou le gadget en main est d'abord rangé).</summary>
        public InventoryResult Store(string id)
        {
            var result = InventoryRules.CanStore(carried, id, IsTool);
            if (result != InventoryResult.Done) return result;
            var item = Describe(id);
            if (item.IsGadget && gadgets != null && gadgets.Equipped == item.Gadget) gadgets.Unequip();
            carried.Remove(id);
            if (item.IsTool && tools != null && tools.ActiveTool == item.Tool) tools.SelectFirstCarried();
            Changed?.Invoke();
            return result;
        }

        /// <summary>Automatisation (voir <see cref="AutomationCarriesOnSelect"/>) : porte le gadget (batte, fusil), à la place du dernier objet si besoin.</summary>
        public bool CarryGadgetForAutomation(PrankGadget gadget)
        {
            string id = InventoryRules.GadgetId(gadget);
            if (!AutomationCarriesOnSelect || id == null || gadgets == null || !gadgets.Owns(gadget)) return false;
            if (carried.Contains(id)) return true;
            if (carried.Count >= SlotCount)
            {
                // Jamais le dernier outil : le dernier objet qui n'est pas l'outil en main laisse sa place.
                for (int i = carried.Count - 1; i >= 0; i--)
                    if (tools.ActiveTool == null || carried[i] != tools.ActiveTool.Definition.id) { carried.RemoveAt(i); break; }
            }
            carried.Add(id);
            known.Add(id);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Automatisation (voir <see cref="AutomationCarriesOnSelect"/>) : porte l'outil, à la place du dernier objet si besoin.</summary>
        public bool CarryForAutomation(CleaningToolBase tool)
        {
            if (!AutomationCarriesOnSelect || tool == null || tool.Definition == null || !tool.IsUnlocked) return false;
            if (IsCarried(tool)) return true;
            if (carried.Count >= SlotCount)
            {
                int victim = carried.Count - 1;
                for (int i = carried.Count - 1; i >= 0; i--)
                    if (tools.ActiveTool == null || carried[i] != tools.ActiveTool.Definition.id) { victim = i; break; }
                carried.RemoveAt(victim);
            }
            carried.Add(tool.Definition.id);
            known.Add(tool.Definition.id);
            Changed?.Invoke();
            return true;
        }
    }
}

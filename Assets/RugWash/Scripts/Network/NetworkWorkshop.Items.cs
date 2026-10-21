using System;
using System.Collections.Generic;
using RugWash.Economy;
using RugWash.Player;
using RugWash.Pranks;
using RugWash.Save;
using RugWash.Workshop;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>Objet tombé au sol (V0.2.5), tel que l'hôte le diffuse à tous les postes.</summary>
    public struct DroppedItemNet : INetworkSerializable, IEquatable<DroppedItemNet>
    {
        public uint Id;
        public FixedString64Bytes Upgrade;
        public Vector3 Position;
        public float Yaw;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Upgrade);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Yaw);
        }

        public bool Equals(DroppedItemNet other) => Id == other.Id && Upgrade.Equals(other.Upgrade) && Position == other.Position && Yaw.Equals(other.Yaw);
    }

    /// <summary>
    /// Objets en ligne (V0.2.5) : revente et objets perdus sous un coup de batte. L'hôte fait autorité sur chaque objet,
    /// qui a toujours un seul état — porté ou rangé par un joueur, au sol (sans propriétaire), ou revendu :
    /// <list type="bullet">
    /// <item>coup de batte validé (portée, recharge, joueur pas déjà à terre) → un objet acheté que porte le joueur touché
    /// quitte ses achats et apparaît au sol, dans la liste diffusée (et dans la sauvegarde de la session) ;</item>
    /// <item>ramassage (E) → l'hôte retire l'objet de la liste à la première demande valable (à portée, objet pas déjà
    /// possédé) et le donne à ce joueur ; une seconde demande ne trouve plus rien ;</item>
    /// <item>revente → l'objet quitte les achats du joueur et la moitié de son prix de base est versée, une fois, dans
    /// l'argent de la session.</item>
    /// </list>
    /// Les objets du kit de départ, les améliorations et l'atelier ne tombent jamais et ne se revendent pas.
    /// </summary>
    public sealed partial class NetworkWorkshop : IItemLoss
    {
        /// <summary>Distance maximale entre les pieds du joueur et l'objet pour le ramasser (m).</summary>
        public const float PickupReach = 3.2f;

        readonly NetworkList<DroppedItemNet> drops = new NetworkList<DroppedItemNet>();
        readonly Dictionary<uint, GameObject> dropVisuals = new Dictionary<uint, GameObject>();
        readonly System.Random dropRandom = new System.Random();
        uint nextDropId;

        /// <summary>Objets au sol (copie de la liste diffusée).</summary>
        public List<DroppedItemNet> Drops
        {
            get
            {
                var copy = new List<DroppedItemNet>();
                foreach (var drop in drops) copy.Add(drop);
                return copy;
            }
        }

        /// <summary>Objets au sol affichés sans modèle (modèles pas encore déclarés) : recréés dès que les icônes sont prêtes.</summary>
        readonly HashSet<uint> dropsWithoutModel = new HashSet<uint>();

        void SetUpItems()
        {
            drops.OnListChanged += OnDropsChanged;
            if (game.ToolIcons != null) game.ToolIcons.Changed += RebuildDropsWithoutModel;
            if (IsServer)
            {
                // Objets restés au sol à la dernière partie sur cet emplacement : ils y sont toujours.
                foreach (var saved in game.SaveSystem.Drops())
                {
                    if (game.Upgrades.Find(saved.upgrade) == null) continue;
                    drops.Add(new DroppedItemNet
                    {
                        Id = ++nextDropId, Upgrade = new FixedString64Bytes(Truncate(saved.upgrade, 60)), Position = new Vector3(saved.x, saved.y, saved.z), Yaw = saved.yaw,
                    });
                }
            }
            RefreshDropVisuals();
        }

        void TearDownItems()
        {
            drops.OnListChanged -= OnDropsChanged;
            if (game.ToolIcons != null) game.ToolIcons.Changed -= RebuildDropsWithoutModel;
            dropsWithoutModel.Clear();
            foreach (var visual in dropVisuals.Values)
                if (visual != null) Destroy(visual);
            dropVisuals.Clear();
        }

        // ---------------------------------------------------------------- Perte d'objets (hôte)

        /// <summary>Coup de batte validé par l'autorité : le joueur touché perd <paramref name="count"/> objet(s) acheté(s) qu'il porte.</summary>
        public void LoseItems(ulong victim, Vector3 feet, Vector3 push, int count)
        {
            if (!IsServer || game == null) return;
            Vector3 away = new Vector3(push.x, 0f, push.z);
            if (away.sqrMagnitude < 1e-4f) away = Quaternion.Euler(0f, (float)dropRandom.NextDouble() * 360f, 0f) * Vector3.forward;
            away.Normalize();
            for (int n = 0; n < count; n++)
            {
                var upgrade = PickLostItem(victim);
                if (upgrade == null || !TakeFrom(victim, upgrade)) break;
                Vector3 side = Vector3.Cross(Vector3.up, away) * ((float)dropRandom.NextDouble() - 0.5f) * 0.6f;
                AddDrop(upgrade, feet + away * (0.8f + 0.3f * n) + side);
                PersistDrops("objet tombé au sol");
                ToastRpc($"{session?.NameOf(victim) ?? "Un joueur"} a laissé tomber : {upgrade.displayName}");
            }
        }

        /// <summary>
        /// Objet perdu : V0.4.2 — l'objet acheté que ce joueur TIENT en main (outil ou gadget de son emplacement actif) ;
        /// sinon le dernier objet acheté qu'il porte (emplacement le plus à droite de son inventaire). Null s'il ne porte rien
        /// d'acheté (kit de départ seulement).
        /// </summary>
        UpgradeDefinition PickLostItem(ulong victim)
        {
            var held = HeldUpgrade(victim);
            if (held != null) return held;
            IEnumerable<string> carried;
            ICollection<string> owned;
            if (victim == NetworkManager.LocalClientId)
            {
                if (game.ToolController.Inventory == null) return null;
                carried = game.ToolController.Inventory.Carried;
                owned = game.Upgrades.OwnedIds;
            }
            else if (members.TryGetValue(victim, out var member))
            {
                carried = member.Data.carried;
                owned = member.Personal;
            }
            else return null;
            var candidates = new List<UpgradeDefinition>();
            foreach (string item in carried)
            {
                var upgrade = game.Upgrades.ItemUpgrade(item);
                if (SessionEconomyRules.IsDroppable(upgrade) && owned.Contains(upgrade.id) && !candidates.Contains(upgrade)) candidates.Add(upgrade);
            }
            return candidates.Count == 0 ? null : candidates[candidates.Count - 1];
        }

        void AddDrop(UpgradeDefinition upgrade, Vector3 position) => drops.Add(new DroppedItemNet
        {
            Id = ++nextDropId, Upgrade = new FixedString64Bytes(upgrade.id), Position = position, Yaw = (float)dropRandom.NextDouble() * 360f,
        });

        /// <summary>Objet acheté tenu par ce joueur (hôte : son outil ou gadget actif ; invité : l'emplacement que montre sa silhouette).</summary>
        UpgradeDefinition HeldUpgrade(ulong victim)
        {
            string item;
            if (victim == NetworkManager.LocalClientId) item = LocalHeldItem();
            else
            {
                var avatar = AvatarOf(victim);
                item = avatar != null ? ItemOfSlot(avatar.HeldSlot) : null;
            }
            return HeldDroppable(victim, item);
        }

        /// <summary>Objet tenu en main sur ce poste : gadget sorti, sinon outil actif (identifiant d'inventaire).</summary>
        string LocalHeldItem()
        {
            var gadget = game.Gadgets != null ? game.Gadgets.Equipped : PrankGadget.None;
            if (gadget != PrankGadget.None) return InventoryRules.GadgetId(gadget);
            var tool = game.ToolController.ActiveTool;
            return tool != null && !game.ToolController.Suspended ? tool.Definition.id : null;
        }

        /// <summary>L'objet <paramref name="item"/>, s'il est acheté, porté et lâchable par ce joueur (sinon null).</summary>
        UpgradeDefinition HeldDroppable(ulong player, string item)
        {
            if (string.IsNullOrEmpty(item)) return null;
            ICollection<string> owned;
            IEnumerable<string> carried;
            if (player == NetworkManager.LocalClientId)
            {
                owned = game.Upgrades.OwnedIds;
                carried = game.ToolController.Inventory != null ? game.ToolController.Inventory.Carried : (IEnumerable<string>)Array.Empty<string>();
            }
            else
            {
                if (!members.TryGetValue(player, out var member)) return null;
                owned = member.Personal;
                carried = member.Data.carried;
            }
            bool isCarried = false;
            foreach (string c in carried) isCarried |= c == item;
            var upgrade = game.Upgrades.ItemUpgrade(item);
            return isCarried && SessionEconomyRules.IsDroppable(upgrade) && owned.Contains(upgrade.id) ? upgrade : null;
        }

        // ---------------------------------------------------------------- Lâcher l'objet en main (V0.4.2)

        /// <summary>
        /// Touche « Lâcher » (Q par défaut, remappable) : l'objet acheté tenu en main est posé devant le joueur, au sol pour
        /// tous ; il quitte ses achats (comme sous la batte) et se ramasse avec E. L'hôte décide (un invité envoie ce qu'il tient,
        /// vérifié sur SA fiche) ; le kit de départ ne se lâche pas.
        /// </summary>
        public void RequestDropHeld()
        {
            string item = LocalHeldItem();
            if (IsServer)
            {
                game.Hud.ShowToast(DropHeld(NetworkManager.LocalClientId, item));
                return;
            }
            FlushInventoryReport();
            DropHeldRpc(new FixedString64Bytes(item ?? string.Empty));
        }

        [Rpc(SendTo.Server)]
        void DropHeldRpc(FixedString64Bytes item, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (sender == NetworkManager.LocalClientId) return;
            ToastToRpc(DropHeld(sender, item.ToString()), RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        /// <summary>Hôte : pose l'objet tenu par ce joueur devant lui (une fois) ; message pour le joueur.</summary>
        string DropHeld(ulong player, string item)
        {
            if (string.IsNullOrEmpty(item)) return "Rien à lâcher : aucun objet en main";
            var upgrade = HeldDroppable(player, item);
            if (upgrade == null) return "Cet objet ne se lâche pas (kit de départ ou objet non porté)";
            if (!TryGetFeet(player, out var feet)) return "Partie en cours de synchronisation";
            Vector3 forward = player == NetworkManager.LocalClientId ? game.PlayerController.transform.forward
                : AvatarOf(player) != null ? AvatarOf(player).transform.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            if (!TakeFrom(player, upgrade)) return "Cet objet ne se lâche pas";
            AddDrop(upgrade, feet + forward.normalized * 0.7f);
            PersistDrops("objet posé au sol");
            return $"{upgrade.displayName} posé au sol (E pour le reprendre)";
        }

        /// <summary>Objet d'un emplacement de silhouette (outil, batte, fusil ; 0 et le pipi : main libre).</summary>
        string ItemOfSlot(int slot)
        {
            if (slot <= 0) return null;
            if (slot == PrankTuning.AvatarSlot(PrankGadget.Bat)) return InventoryRules.GadgetId(PrankGadget.Bat);
            if (slot == PrankTuning.AvatarSlot(PrankGadget.Blaster)) return InventoryRules.GadgetId(PrankGadget.Blaster);
            foreach (var tool in game.ToolController.Tools)
                if (tool != null && tool.Definition != null && tool.Definition.slot == slot) return tool.Definition.id;
            return null;
        }

        /// <summary>L'objet quitte les achats du joueur (et ce qu'il porte) ; sa vue lui est renvoyée.</summary>
        bool TakeFrom(ulong victim, UpgradeDefinition upgrade)
        {
            if (victim == NetworkManager.LocalClientId) return game.Upgrades.Revoke(upgrade.id);
            if (!members.TryGetValue(victim, out var member) || !member.Personal.Remove(upgrade.id)) return false;
            member.Data.carried.Remove(InventoryRules.ItemIdOf(upgrade));
            StoreMember(member);
            SendView(victim, inventory: true);
            return true;
        }

        void PersistDrops(string reason)
        {
            var saved = new List<DroppedItemData>();
            foreach (var drop in drops)
                saved.Add(new DroppedItemData { upgrade = drop.Upgrade.ToString(), x = drop.Position.x, y = drop.Position.y, z = drop.Position.z, yaw = drop.Yaw });
            game.SaveSystem.StoreDrops(saved);
            game.SaveSystem.SaveNow(reason);
        }

        // ---------------------------------------------------------------- Ramassage

        public void RequestPickup(uint dropId) => PickupRpc(dropId);

        /// <summary>Ramassage demandé par ce poste (comme un appui sur E devant l'objet) ; message pour le joueur.</summary>
        public string TryPickup(uint dropId) => LocalPickup(dropId);

        /// <summary>Appui sur E devant un objet au sol (tous les postes) : l'hôte décide.</summary>
        string LocalPickup(uint dropId)
        {
            if (IsServer) return Pickup(NetworkManager.LocalClientId, dropId);
            PickupRpc(dropId);
            return "Ramassage…";
        }

        [Rpc(SendTo.Server)]
        void PickupRpc(uint dropId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (sender == NetworkManager.LocalClientId) return;
            ToastToRpc(Pickup(sender, dropId), RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        /// <summary>Hôte : la première demande valable obtient l'objet ; il quitte aussitôt le sol (aucun double ramassage).</summary>
        string Pickup(ulong picker, uint dropId)
        {
            int index = -1;
            for (int i = 0; i < drops.Count; i++)
                if (drops[i].Id == dropId) index = i;
            if (index < 0) return "Cet objet a déjà été ramassé";
            var drop = drops[index];
            var upgrade = game.Upgrades.Find(drop.Upgrade.ToString());
            if (upgrade == null)
            {
                drops.RemoveAt(index);
                PersistDrops("objet inconnu retiré");
                return "Objet inconnu";
            }
            if (!TryGetFeet(picker, out var feet) || (feet - drop.Position).sqrMagnitude > PickupReach * PickupReach) return "Trop loin de l'objet";
            bool given;
            if (picker == NetworkManager.LocalClientId) given = game.Upgrades.Grant(upgrade.id);
            else if (members.TryGetValue(picker, out var member))
            {
                given = member.Personal.Add(upgrade.id);
                if (given) StoreMember(member);
            }
            else return "Partie en cours de synchronisation";
            if (!given) return $"Vous avez déjà : {upgrade.displayName}";
            drops.RemoveAt(index);
            PersistDrops("objet ramassé");
            if (picker != NetworkManager.LocalClientId) SendView(picker, inventory: false);
            return $"{upgrade.displayName} ramassé (revente : {WalletManager.Format(SessionEconomyRules.ResaleValue(upgrade))})";
        }

        // ---------------------------------------------------------------- Revente (invité → hôte)

        public void RequestSale(string upgradeId)
        {
            FlushInventoryReport();
            SaleRpc(new FixedString64Bytes(upgradeId ?? string.Empty));
        }

        /// <summary>Inventaire signalé d'abord (même canal fiable et ordonné) : l'hôte sait ce que l'invité porte à l'instant.</summary>
        void FlushInventoryReport()
        {
            if (!careerReceived || game.ToolController.Inventory == null) return;
            inventoryTimer = 0f;
            inventoryDirty = false;
            lastReportedSoap = game.ToolController.Equipment.Soap.Amount;
            InventoryRpc(new FixedString512Bytes(Truncate(string.Join(",", game.ToolController.Inventory.Carried), 500)), lastReportedSoap);
        }

        /// <summary>Revente demandée par un invité : vérifiée sur SA fiche (possédé, rangé), payée une fois dans l'argent de la session.</summary>
        [Rpc(SendTo.Server)]
        void SaleRpc(FixedString64Bytes upgradeId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var upgrade = game.Upgrades.Find(upgradeId.ToString());
            if (upgrade == null || !members.TryGetValue(sender, out var member))
            {
                ShopResultRpc(upgrade == null ? "Objet inconnu" : "Partie en cours de synchronisation", false, RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            bool carried = member.Data.carried.Contains(InventoryRules.ItemIdOf(upgrade) ?? string.Empty);
            int value = SessionEconomyRules.ResaleValue(upgrade);
            var result = SessionEconomyRules.TrySell(upgrade, member.Personal, carried, game.Wallet.Wallet);
            if (result == SaleResult.Sold)
            {
                StoreMember(member);
                game.SaveSystem.SaveNow("objet revendu par un joueur");
                SendView(sender, inventory: false);
            }
            ShopResultRpc(SessionEconomyRules.Describe(result, value), result == SaleResult.Sold, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        // ---------------------------------------------------------------- Objets au sol : affichage (tous les postes)

        void OnDropsChanged(NetworkListEvent<DroppedItemNet> change) => RefreshDropVisuals();

        void RebuildDropsWithoutModel()
        {
            if (dropsWithoutModel.Count == 0) return;
            foreach (uint id in new List<uint>(dropsWithoutModel))
            {
                if (dropVisuals.TryGetValue(id, out var visual) && visual != null) Destroy(visual);
                dropVisuals.Remove(id);
            }
            dropsWithoutModel.Clear();
            RefreshDropVisuals();
        }

        /// <summary>Objet au sol affiché avec son modèle sur ce poste (tests multijoueur).</summary>
        public bool DropVisible(uint dropId) =>
            dropVisuals.TryGetValue(dropId, out var visual) && visual != null && !dropsWithoutModel.Contains(dropId)
            && visual.GetComponentsInChildren<Renderer>().Length > 0;

        void RefreshDropVisuals()
        {
            if (game == null) return;
            var present = new HashSet<uint>();
            foreach (var drop in drops)
            {
                present.Add(drop.Id);
                if (!dropVisuals.ContainsKey(drop.Id)) dropVisuals[drop.Id] = CreateDropVisual(drop);
            }
            var gone = new List<uint>();
            foreach (var pair in dropVisuals)
                if (!present.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (uint id in gone)
            {
                if (dropVisuals[id] != null) Destroy(dropVisuals[id]);
                dropVisuals.Remove(id);
            }
        }

        GameObject CreateDropVisual(DroppedItemNet drop)
        {
            var upgrade = game.Upgrades.Find(drop.Upgrade.ToString());
            var holder = new GameObject($"DroppedItem_{drop.Id}");
            holder.transform.SetPositionAndRotation(drop.Position, Quaternion.Euler(0f, drop.Yaw, 0f));
            var copy = game.ToolIcons != null ? game.ToolIcons.CreateWorldCopy(InventoryRules.ItemIdOf(upgrade), holder.transform) : null;
            var bounds = new Bounds(drop.Position + Vector3.up * 0.1f, new Vector3(0.4f, 0.2f, 0.4f));
            if (copy != null)
            {
                // Couché sur le flanc, posé sur le sol (point le plus bas de l'objet à la hauteur du sol).
                copy.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                bool any = false;
                foreach (var renderer in copy.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled) continue;
                    if (!any) bounds = renderer.bounds;
                    else bounds.Encapsulate(renderer.bounds);
                    any = true;
                }
                if (any)
                {
                    float lift = drop.Position.y + 0.01f - bounds.min.y;
                    copy.transform.position += Vector3.up * lift;
                    bounds.center += Vector3.up * lift;
                }
            }
            else
            {
                dropsWithoutModel.Add(drop.Id);
                Debug.LogWarning($"[RugWash] Objet au sol « {drop.Upgrade} » sans modèle pour l'instant : recréé quand les modèles sont prêts.");
            }
            var collider = holder.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = holder.transform.InverseTransformPoint(bounds.center);
            collider.size = Vector3.Max(bounds.size, new Vector3(0.3f, 0.2f, 0.3f));
            var pickup = holder.AddComponent<DroppedItemPickup>();
            pickup.Init(drop.Id, upgrade != null ? upgrade.displayName : "Objet", SessionEconomyRules.ResaleValue(upgrade), LocalPickup);
            return holder;
        }
    }
}

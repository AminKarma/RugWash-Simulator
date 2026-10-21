using System.Collections.Generic;
using RugWash.Economy;
using RugWash.Pranks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>
    /// Farces en ligne (v0.1.2). Un invité demande, l'hôte décide : achat (fiche de l'invité dans la sauvegarde de la session),
    /// recharge, coût (débité sur l'argent de la session), portée mesurée depuis la position de sa silhouette chez l'hôte (jamais
    /// celle annoncée), cible, plafond de saleté et protection des joueurs déjà visés. Ensuite : taches par la réplication
    /// des cellules, gouttes ou chute envoyées au seul joueur visé, effets (jet, tir, sons) rejoués par chaque poste.
    /// </summary>
    public sealed partial class NetworkWorkshop : IPrankVictims
    {
        readonly List<PrankTargetInfo> prankTargets = new List<PrankTargetInfo>();

        void SetUpHostPranks()
        {
            if (game.Pranks == null || game.Gadgets == null) return;
            game.Pranks.Victims = this;
            game.Pranks.ItemLoss = this;
            game.Pranks.Executed += OnPrankExecuted;
            SetUpLocalGadgets();
        }

        void SetUpGuestPranks()
        {
            if (game.Gadgets == null) return;
            game.Gadgets.Remote = this;
            SetUpLocalGadgets();
        }

        void SetUpLocalGadgets()
        {
            game.Gadgets.LocalId = NetworkManager.LocalClientId;
            game.Gadgets.LocalName = session?.NameOf(NetworkManager.LocalClientId) ?? "Un joueur";
            game.Gadgets.RemotePlayers = RemotePrankTargets;
            game.Gadgets.Swung -= OnLocalSwing;
            game.Gadgets.Swung += OnLocalSwing;
            // V0.4.2 : ralenti de la dynamite local ; jamais chez l'hôte quand des invités jouent (l'autorité ne ralentit pas).
            game.Gadgets.AllowSlowMotion = () => !IsServer || NetworkManager.ConnectedClientsIds.Count <= 1;
        }

        // ---------------------------------------------------------------- Coups de batte (geste visible par tous)

        readonly Dictionary<ulong, float> lastSwingFx = new Dictionary<ulong, float>();

        void OnLocalSwing()
        {
            if (IsServer) BroadcastSwing(NetworkManager.LocalClientId);
            else SwingRpc();
        }

        /// <summary>Invité : son coup de batte (même dans le vide), rejoué chez les autres. Cadence bornée par l'hôte.</summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        void SwingRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var avatar = AvatarOf(sender);
            if (avatar == null || !syncedClients.Contains(sender)) return;
            float now = Time.time;
            if (lastSwingFx.TryGetValue(sender, out float last) && now - last < 0.3f) return;
            lastSwingFx[sender] = now;
            BroadcastSwing(sender);
        }

        void BroadcastSwing(ulong actor)
        {
            if (actor != NetworkManager.LocalClientId) PlayRemoteSwing(actor);
            SwingFxRpc(actor);
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        void SwingFxRpc(ulong actor)
        {
            if (actor == NetworkManager.LocalClientId) return;
            PlayRemoteSwing(actor);
        }

        void PlayRemoteSwing(ulong actor)
        {
            var avatar = AvatarOf(actor);
            if (avatar == null) return;
            avatar.PlaySwing();
            if (game.Gadgets != null && game.Gadgets.Effects != null)
                game.Gadgets.Effects.PlaySwing(avatar.transform.position + Vector3.up * 1.3f + avatar.transform.forward * 0.4f);
        }

        void TearDownPranks()
        {
            if (game == null) return;
            lastSwingFx.Clear();
            if (game.Gadgets != null) game.Gadgets.Swung -= OnLocalSwing;
            if (game.Pranks != null)
            {
                if (ReferenceEquals(game.Pranks.Victims, this)) game.Pranks.Victims = null;
                if (ReferenceEquals(game.Pranks.ItemLoss, this)) game.Pranks.ItemLoss = null;
                game.Pranks.Executed -= OnPrankExecuted;
            }
            if (game.Gadgets == null) return;
            game.Gadgets.Remote = null;
            game.Gadgets.RemotePlayers = null;
            game.Gadgets.AllowSlowMotion = null;
            game.Gadgets.LocalId = 0;
            // Fin de partie : plus aucune farce subie à l'écran.
            game.Gadgets.ClearReceived();
        }

        IReadOnlyList<PrankTargetInfo> RemotePrankTargets()
        {
            prankTargets.Clear();
            foreach (var avatar in FindObjectsByType<NetworkPlayerAvatar>())
                if (avatar.IsSpawned && !avatar.IsOwner)
                    prankTargets.Add(new PrankTargetInfo { ClientId = avatar.OwnerClientId, Feet = avatar.transform.position });
            return prankTargets;
        }

        NetworkPlayerAvatar AvatarOf(ulong clientId)
        {
            // V0.4.2 : registre des silhouettes, valable chez l'hôte comme chez les invités (voir NetworkPlayerAvatar.Of).
            return NetworkPlayerAvatar.Of(clientId);
        }

        // ---------------------------------------------------------------- Invité → hôte

        public void RequestPrank(PrankRequest request) =>
            PrankRpc((byte)request.Gadget, (byte)request.Target, request.Point, request.Victim, request.Direction);

        [Rpc(SendTo.Server)]
        void PrankRpc(byte gadget, byte target, Vector3 point, ulong victim, Vector3 direction, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var target1 = RpcTarget.Single(sender, RpcTargetUse.Temp);
            if (game.Pranks == null || gadget < 1 || gadget > 4 || target > 2 || !syncedClients.Contains(sender)
                || !members.TryGetValue(sender, out var member) || AvatarOf(sender) == null)
            {
                PrankResultRpc(gadget, (byte)PrankResult.Unavailable, false, target1);
                return;
            }
            var actor = new GuestPrankActor(this, sender, member);
            var request = new PrankRequest
            {
                Gadget = (PrankGadget)gadget, Target = (PrankTarget)target, Point = point, Victim = victim,
                Origin = actor.Eye, Direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward,
            };
            var result = game.Pranks.Execute(actor, request, out var outcome);
            PrankResultRpc(gadget, (byte)result, outcome.HitPlayer, target1);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void PrankResultRpc(byte gadget, byte result, bool hitPlayer, RpcParams rpcParams)
        {
            if (game.Gadgets != null) game.Gadgets.OnRemoteResult((PrankGadget)gadget, (PrankResult)result, hitPlayer);
        }

        // ---------------------------------------------------------------- Effets diffusés

        /// <summary>Hôte : farce validée (de l'hôte ou d'un invité) → effets chez les autres postes.</summary>
        void OnPrankExecuted(PrankOutcome outcome)
        {
            // L'auteur joue ses effets à la réponse ; l'hôte les joue ici pour un invité.
            if (outcome.Actor != NetworkManager.LocalClientId) PlayPrankEffects(outcome.Gadget, outcome.Actor, outcome.Point, outcome.HitPlayer);
            PrankFxRpc((byte)outcome.Gadget, outcome.Actor, outcome.Point, outcome.HitPlayer);
        }

        [Rpc(SendTo.NotServer)]
        void PrankFxRpc(byte gadget, ulong actor, Vector3 point, bool hitPlayer)
        {
            if (actor == NetworkManager.LocalClientId) return;
            PlayPrankEffects((PrankGadget)gadget, actor, point, hitPlayer);
        }

        void PlayPrankEffects(PrankGadget gadget, ulong actor, Vector3 point, bool hitPlayer)
        {
            if (game.Gadgets == null || game.Gadgets.Effects == null) return;
            var avatar = AvatarOf(actor);
            Vector3 origin = point;
            if (avatar != null)
            {
                var body = avatar.transform;
                origin = gadget == PrankGadget.Pee
                    ? body.position + Vector3.up * 0.9f + body.forward * 0.25f
                    : (avatar.View != null ? avatar.View.position : body.position + Vector3.up * 1.6f) + body.forward * 0.5f + body.right * 0.25f;
            }
            var settings = game.Gadgets.Tuning != null ? game.Gadgets.Tuning.Get(gadget) : null;
            game.Gadgets.Effects.Play(gadget, origin, point, hitPlayer, settings != null ? settings.useSeconds : 0.5f);
        }

        // ---------------------------------------------------------------- Joueurs visés (hôte)

        public bool TryGetFeet(ulong clientId, out Vector3 feet)
        {
            feet = default;
            if (clientId == NetworkManager.LocalClientId)
            {
                feet = game.PlayerController.transform.position;
                return true;
            }
            var avatar = AvatarOf(clientId);
            if (avatar == null || !syncedClients.Contains(clientId)) return false;
            feet = avatar.transform.position;
            return true;
        }

        /// <summary>V0.4.2 : joueurs de la partie (l'hôte et les invités synchronisés) : candidats d'une explosion.</summary>
        public void CollectPlayers(List<ulong> into)
        {
            into.Add(NetworkManager.LocalClientId);
            foreach (ulong id in syncedClients)
                if (id != NetworkManager.LocalClientId && AvatarOf(id) != null) into.Add(id);
        }

        public bool IsDown(ulong clientId)
        {
            if (clientId == NetworkManager.LocalClientId) return game.Knockdown != null && game.Knockdown.IsDown;
            var avatar = AvatarOf(clientId);
            return avatar != null && avatar.IsKnockedDown;
        }

        public void Splash(ulong clientId, float intensity, float seconds, float fadeSeconds, string from)
        {
            if (clientId == NetworkManager.LocalClientId) game.Gadgets.ReceiveSplash(intensity, seconds, fadeSeconds, from);
            else SplashRpc(intensity, seconds, fadeSeconds, new FixedString64Bytes(from ?? string.Empty), RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        public void Knock(ulong clientId, Vector3 impulse, float seconds, string from, PrankGadget cause)
        {
            if (clientId == NetworkManager.LocalClientId) game.Gadgets.ReceiveKnock(impulse, seconds, from, cause);
            else KnockRpc(impulse, seconds, new FixedString64Bytes(from ?? string.Empty), (byte)cause, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void SplashRpc(float intensity, float seconds, float fadeSeconds, FixedString64Bytes from, RpcParams rpcParams)
        {
            // Valeurs bornées même venant de l'hôte : l'écran n'est jamais masqué ni l'effet permanent.
            game.Gadgets.ReceiveSplash(Mathf.Clamp(intensity, 0f, 0.85f), Mathf.Clamp(seconds, 0.5f, 15f), Mathf.Clamp(fadeSeconds, 0.1f, 5f), from.ToString());
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void KnockRpc(Vector3 impulse, float seconds, FixedString64Bytes from, byte cause, RpcParams rpcParams)
        {
            // Valeurs bornées même venant de l'hôte : élan raisonnable, KO de 6 s au plus.
            var gadget = cause == (byte)PrankGadget.Blaster ? PrankGadget.Blaster : cause == (byte)PrankGadget.Dynamite ? PrankGadget.Dynamite : PrankGadget.Bat;
            game.Gadgets.ReceiveKnock(Vector3.ClampMagnitude(impulse, 12f), Mathf.Clamp(seconds, 0.5f, 6f), from.ToString(), gadget);
        }

        /// <summary>
        /// Invité vu par l'autorité : position de sa silhouette chez l'hôte ; gadgets et argent de l'économie de session
        /// (achats et argent de l'atelier de l'hôte, règle des sauvegardes).
        /// </summary>
        sealed class GuestPrankActor : IPrankActor
        {
            readonly NetworkWorkshop owner;
            readonly ulong id;
            readonly SessionMember member;

            public GuestPrankActor(NetworkWorkshop owner, ulong id, SessionMember member)
            {
                this.owner = owner;
                this.id = id;
                this.member = member;
            }

            public ulong Id => id;
            public string Name => owner.session?.NameOf(id) ?? "Un joueur";

            public Vector3 Eye
            {
                get
                {
                    var avatar = owner.AvatarOf(id);
                    if (avatar == null) return new Vector3(0f, -1000f, 0f);
                    return avatar.View != null ? avatar.View.position : avatar.transform.position + Vector3.up * 1.62f;
                }
            }

            public bool IsDown => owner.IsDown(id);

            public bool Owns(PrankGadget gadget)
            {
                var settings = owner.game.Pranks.Tuning.Get(gadget);
                return settings != null && member.Personal.Contains(settings.upgradeId);
            }

            /// <summary>Argent de la session (sauvegarde de l'hôte) : le coût d'un tir est payé par tous.</summary>
            public int Money => owner.game.Wallet.Balance;

            public bool TrySpend(int amount) => amount >= 0 && owner.game.Wallet.Wallet.TrySpend(amount);
        }
    }
}

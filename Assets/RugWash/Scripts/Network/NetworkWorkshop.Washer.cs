using System;
using RugWash.Workshop;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>Machine à laver de l'hôte vue par les invités (grande mise à jour).</summary>
    public struct WasherNetState : INetworkSerializable, IEquatable<WasherNetState>
    {
        public byte State;
        /// <summary>Temps restant et durée du cycle, en secondes entières (l'invité décompte entre deux mises à jour).</summary>
        public ushort Remaining;
        public ushort Cycle;
        public FixedString64Bytes OrderId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref Remaining);
            serializer.SerializeValue(ref Cycle);
            serializer.SerializeValue(ref OrderId);
        }

        public bool Equals(WasherNetState other) =>
            State == other.State && Remaining == other.Remaining && Cycle == other.Cycle && OrderId.Equals(other.OrderId);
    }

    public sealed partial class NetworkWorkshop
    {
        readonly NetworkVariable<WasherNetState> washerState = new NetworkVariable<WasherNetState>();

        WashingMachine Washer => game != null && game.Extension != null ? game.Extension.Washer : null;

        /// <summary>Hôte : état de la machine publié (chargement, fin, sortie, et le temps restant toutes les 0,5 s).</summary>
        void PushWasher()
        {
            if (!IsServer) return;
            var washer = Washer;
            var state = new WasherNetState();
            if (washer != null && washer.State != WasherState.Free)
            {
                state.State = (byte)washer.State;
                state.Remaining = (ushort)Mathf.Clamp(Mathf.CeilToInt(washer.Remaining), 0, ushort.MaxValue);
                state.Cycle = (ushort)Mathf.Clamp(Mathf.CeilToInt(washer.CycleSeconds), 0, ushort.MaxValue);
                state.OrderId = new FixedString64Bytes(Truncate(washer.OrderId, 60));
            }
            if (!state.Equals(washerState.Value)) washerState.Value = state;
        }

        void ApplyWasher(WasherNetState state)
        {
            var washer = Washer;
            if (washer == null || IsServer) return;
            washer.ApplyRemote((WasherState)Mathf.Clamp(state.State, 0, 2), state.Remaining, state.Cycle, state.OrderId.ToString());
        }

        void HookWasher()
        {
            var washer = Washer;
            if (washer != null) washer.StateChanged += PushWasher;
        }

        void UnhookWasher()
        {
            var washer = Washer;
            if (washer != null) washer.StateChanged -= PushWasher;
        }

        /// <summary>Invité : action sur la machine de l'hôte, validée ici (machine achetée, zone, état du cycle).</summary>
        [Rpc(SendTo.Server)]
        void WasherRpc(byte action, byte bay, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var washer = Washer;
            if (washer == null || action < (byte)WasherAction.Load || action > (byte)WasherAction.Abandon)
            {
                ToastToRpc("Machine à laver indisponible.", RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            string message = washer.Execute((WasherAction)action, bay);
            ToastToRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
            PushWasher();
        }
    }
}

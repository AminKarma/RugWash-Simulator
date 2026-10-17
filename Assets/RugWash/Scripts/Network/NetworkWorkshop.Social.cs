using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RugWash.Network
{
    /// <summary>
    /// Grande mise à jour, phase 5 : chat textuel et chat vocal de proximité. Le chat passe par l'hôte, qui nettoie le texte,
    /// limite la cadence et rediffuse avec le nom connu de la session (un invité ne peut pas parler au nom d'un autre). La voix
    /// (trames μ-law, <see cref="VoiceCodec"/>) passe aussi par l'hôte, qui la relaie aux autres joueurs ; chacun l'entend
    /// depuis la silhouette de celui qui parle (<see cref="VoicePlayback"/>, volume selon la distance).
    /// </summary>
    public sealed partial class NetworkWorkshop
    {
        public const string VoiceMessageName = "rugwash.voice";

        readonly ChatRateLimiter chatLimiter = new ChatRateLimiter();
        readonly ChatLog chatLog = new ChatLog();
        readonly Dictionary<ulong, float> voiceWindowStart = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, int> voiceWindowCount = new Dictionary<ulong, int>();
        readonly byte[] voiceRelay = new byte[VoiceCodec.MaxPacketBytes];
        byte[] voiceReceive = new byte[VoiceCodec.MaxPacketBytes];
        /// <summary>Trames vocales par seconde et par joueur au plus (25 attendues) : au-delà, l'hôte les ignore.</summary>
        const int MaxVoiceFramesPerSecond = 40;

        /// <summary>Historique du chat de la session (lignes de joueurs et messages de l'atelier).</summary>
        public ChatLog Chat => chatLog;
        public event Action<ChatLine> ChatReceived;
        public int VoiceFramesRelayed { get; private set; }

        // ---------------------------------------------------------------- Chat textuel

        /// <summary>Envoie un message (nettoyé ici, puis à nouveau par l'hôte). Faux si le texte est vide.</summary>
        public bool SendChat(string text)
        {
            text = ChatRules.Clean(text);
            if (text.Length == 0) return false;
            if (IsServer) BroadcastChat(NetworkManager.LocalClientId, text);
            else ChatRpc(new FixedString512Bytes(text));
            return true;
        }

        [Rpc(SendTo.Server)]
        void ChatRpc(FixedString512Bytes text, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!syncedClients.Contains(sender)) return;
            string clean = ChatRules.Clean(text.ToString());
            if (clean.Length == 0) return;
            BroadcastChat(sender, clean);
        }

        void BroadcastChat(ulong sender, string text)
        {
            if (!chatLimiter.Allow(sender, Time.unscaledTime))
            {
                if (sender == NetworkManager.LocalClientId) AddChatLine(null, "Trop de messages : patientez un instant.", true);
                else ChatNoticeRpc(new FixedString512Bytes("Trop de messages : patientez un instant."), RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            string name = session != null ? session.NameOf(sender) : $"Joueur {sender + 1}";
            ChatMessageRpc(new FixedString64Bytes(Truncate(name, 60)), new FixedString512Bytes(text));
        }

        [Rpc(SendTo.Everyone)]
        void ChatMessageRpc(FixedString64Bytes sender, FixedString512Bytes text) => AddChatLine(sender.ToString(), text.ToString(), false);

        [Rpc(SendTo.SpecifiedInParams)]
        void ChatNoticeRpc(FixedString512Bytes text, RpcParams rpcParams) => AddChatLine(null, text.ToString(), true);

        void AddChatLine(string sender, string text, bool system)
        {
            var line = new ChatLine(sender, ChatRules.Clean(text), system, Time.unscaledTime);
            chatLog.Add(line);
            ChatReceived?.Invoke(line);
        }

        // ---------------------------------------------------------------- Chat vocal

        void SetUpVoice()
        {
            var messaging = NetworkManager.CustomMessagingManager;
            messaging.RegisterNamedMessageHandler(VoiceMessageName, IsServer ? OnVoiceFromGuest : (CustomMessagingManager.HandleNamedMessageDelegate)OnVoiceFromHost);
            var voice = game != null ? game.Voice : null;
            if (voice == null) return;
            voice.Online = true;
            voice.Send = SendVoice;
        }

        void TearDownVoice()
        {
            var messaging = NetworkManager != null ? NetworkManager.CustomMessagingManager : null;
            messaging?.UnregisterNamedMessageHandler(VoiceMessageName);
            var voice = game != null ? game.Voice : null;
            if (voice == null) return;
            voice.Online = false;
            voice.Send = null;
        }

        /// <summary>Trame du joueur local : vers l'hôte (invité), ou vers tous les invités (hôte).</summary>
        void SendVoice(byte[] data, int length)
        {
            if (NetworkManager == null || length <= 0 || length > VoiceCodec.HeaderBytes + VoiceCodec.FrameSamples) return;
            if (IsServer)
            {
                if (syncedClients.Count == 0) return;
                RelayVoice(NetworkManager.LocalClientId, data, 0, length, NetworkManager.LocalClientId);
                return;
            }
            using var buffer = new FastBufferWriter(length, Allocator.Temp);
            buffer.WriteBytesSafe(data, length);
            NetworkManager.CustomMessagingManager.SendNamedMessage(VoiceMessageName, NetworkManager.ServerClientId, buffer, NetworkDelivery.Unreliable);
        }

        /// <summary>Hôte : trame d'un invité, jouée ici et relayée aux autres (jamais renvoyée à celui qui parle).</summary>
        void OnVoiceFromGuest(ulong sender, FastBufferReader reader)
        {
            int length = reader.Length - reader.Position;
            if (length <= VoiceCodec.HeaderBytes || length > VoiceCodec.HeaderBytes + VoiceCodec.FrameSamples) return;
            if (!syncedClients.Contains(sender) || !AllowVoice(sender)) return;
            reader.ReadBytesSafe(ref voiceReceive, length);
            PlayVoice(sender, voiceReceive, 0, length);
            RelayVoice(sender, voiceReceive, 0, length, sender);
        }

        bool AllowVoice(ulong sender)
        {
            float now = Time.unscaledTime;
            if (!voiceWindowStart.TryGetValue(sender, out float start) || now - start >= 1f)
            {
                voiceWindowStart[sender] = now;
                voiceWindowCount[sender] = 0;
            }
            int count = voiceWindowCount[sender] + 1;
            voiceWindowCount[sender] = count;
            return count <= MaxVoiceFramesPerSecond;
        }

        void RelayVoice(ulong speaker, byte[] data, int offset, int length, ulong except)
        {
            int total = 8 + length;
            for (int i = 0; i < 8; i++) voiceRelay[i] = (byte)(speaker >> (8 * i));
            Array.Copy(data, offset, voiceRelay, 8, length);
            relayTargets.Clear();
            foreach (ulong id in syncedClients)
                if (id != except && id != NetworkManager.LocalClientId) relayTargets.Add(id);
            if (relayTargets.Count == 0) return;
            using var buffer = new FastBufferWriter(total, Allocator.Temp);
            buffer.WriteBytesSafe(voiceRelay, total);
            NetworkManager.CustomMessagingManager.SendNamedMessage(VoiceMessageName, relayTargets, buffer, NetworkDelivery.Unreliable);
            VoiceFramesRelayed++;
        }

        readonly List<ulong> relayTargets = new List<ulong>();

        /// <summary>Invité : trame relayée par l'hôte (identifiant de celui qui parle, puis la trame).</summary>
        void OnVoiceFromHost(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId) return;
            int length = reader.Length - reader.Position;
            if (length <= 8 + VoiceCodec.HeaderBytes || length > VoiceCodec.MaxPacketBytes) return;
            reader.ReadBytesSafe(ref voiceReceive, length);
            ulong speaker = 0;
            for (int i = 0; i < 8; i++) speaker |= (ulong)voiceReceive[i] << (8 * i);
            if (speaker == NetworkManager.LocalClientId) return;
            PlayVoice(speaker, voiceReceive, 8, length - 8);
        }

        void PlayVoice(ulong speaker, byte[] data, int offset, int length)
        {
            var avatar = AvatarOf(speaker);
            var playback = avatar != null ? avatar.Voice : null;
            if (playback == null) return;
            ushort sequence = (ushort)(data[offset] | (data[offset + 1] << 8));
            playback.Push(data, offset + VoiceCodec.HeaderBytes, length - VoiceCodec.HeaderBytes, sequence);
        }

        void ForgetSocial(ulong clientId)
        {
            chatLimiter.Forget(clientId);
            voiceWindowStart.Remove(clientId);
            voiceWindowCount.Remove(clientId);
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RugWash.Network
{
    /// <summary>Micro du chat vocal (paramètres) : appuyer pour parler (touche V), toujours ouvert (détection de voix), ou coupé.</summary>
    public enum VoiceMode
    {
        PushToTalk = 0,
        OpenMic = 1,
        Off = 2,
    }

    /// <summary>
    /// Capture de la voix du joueur local (grande mise à jour) : micro de Windows via l'API Microphone de Unity, trames de
    /// 40 ms encodées en μ-law (<see cref="VoiceCodec"/>) et envoyées par l'atelier réseau (<see cref="NetworkWorkshop"/>).
    /// Actif seulement en partie en ligne. Aucune voix n'est enregistrée ni gardée : chaque trame part puis est oubliée.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceChat : MonoBehaviour
    {
        [Tooltip("Gain appliqué au micro avant l'envoi.")]
        [SerializeField, Range(0.5f, 4f)] float micGain = 1.5f;

        AudioClip microphone;
        string device;
        int readPosition;
        readonly float[] capture = new float[VoiceCodec.SampleRate];
        readonly float[] frame = new float[VoiceCodec.FrameSamples];
        int frameFill;
        readonly byte[] packet = new byte[VoiceCodec.HeaderBytes + VoiceCodec.FrameSamples];
        readonly VoiceActivity activity = new VoiceActivity();
        ushort sequence;
        float speakingUntil;

        public VoiceMode Mode { get; set; } = VoiceMode.PushToTalk;
        /// <summary>Vrai pendant une partie en ligne (le micro ne s'ouvre jamais en solo).</summary>
        public bool Online { get; set; }
        /// <summary>Contrôles du joueur actifs (pas de fenêtre ouverte) : appuyer pour parler n'agit qu'en jeu.</summary>
        public Func<bool> CanTalk { get; set; }
        /// <summary>Envoi d'une trame (en-tête + μ-law) : branché par l'atelier réseau.</summary>
        public Action<byte[], int> Send { get; set; }
        public bool IsCapturing => microphone != null;
        public bool IsSpeaking => Time.unscaledTime < speakingUntil;
        public string Status { get; private set; } = "Chat vocal inactif (hors ligne)";
        public int FramesSent { get; private set; }

        void OnDisable() => StopCapture();

        void Update()
        {
            bool wanted = Online && Mode != VoiceMode.Off && Send != null;
            if (wanted && microphone == null) StartCapture();
            else if (!wanted && microphone != null) StopCapture();
            if (microphone == null)
            {
                if (!wanted) Status = !Online ? "Chat vocal inactif (hors ligne)" : Mode == VoiceMode.Off ? "Micro coupé" : Status;
                return;
            }
            ReadMicrophone();
        }

        void StartCapture()
        {
            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                Status = "Aucun micro détecté : chat vocal en écoute seulement";
                return;
            }
            device = devices[0];
            microphone = Microphone.Start(device, true, 1, VoiceCodec.SampleRate);
            if (microphone == null)
            {
                Status = "Micro indisponible (déjà utilisé ?) : chat vocal en écoute seulement";
                return;
            }
            readPosition = 0;
            frameFill = 0;
            activity.Reset();
            Status = Mode == VoiceMode.PushToTalk ? "Micro prêt : maintenez V pour parler" : "Micro ouvert (détection de la voix)";
        }

        void StopCapture()
        {
            if (microphone == null) return;
            Microphone.End(device);
            Destroy(microphone);
            microphone = null;
        }

        bool TalkHeld()
        {
            if (Mode != VoiceMode.PushToTalk) return true;
            if (CanTalk != null && !CanTalk()) return false;
            return Keyboard.current != null && Keyboard.current.vKey.isPressed;
        }

        void ReadMicrophone()
        {
            int position = Microphone.GetPosition(device);
            if (position < 0 || position == readPosition) return;
            int length = microphone.samples;
            int available = position > readPosition ? position - readPosition : length - readPosition + position;
            if (available > capture.Length) available = capture.Length;
            // Lecture de l'anneau du micro en une ou deux parties.
            int first = Mathf.Min(available, length - readPosition);
            microphone.GetData(capture, readPosition);
            ConsumeSamples(capture, first);
            if (available > first)
            {
                microphone.GetData(capture, 0);
                ConsumeSamples(capture, available - first);
            }
            readPosition = position;
        }

        void ConsumeSamples(float[] samples, int count)
        {
            int index = 0;
            while (index < count)
            {
                int take = Mathf.Min(count - index, VoiceCodec.FrameSamples - frameFill);
                Array.Copy(samples, index, frame, frameFill, take);
                frameFill += take;
                index += take;
                if (frameFill < VoiceCodec.FrameSamples) continue;
                frameFill = 0;
                SendFrame();
            }
        }

        void SendFrame()
        {
            VoiceCodec.ApplyGain(frame, 0, frame.Length, micGain);
            float rms = VoiceCodec.Rms(frame, 0, frame.Length);
            bool open = Mode == VoiceMode.OpenMic ? activity.Update(rms) : TalkHeld();
            if (!open) return;
            sequence++;
            packet[0] = (byte)(sequence & 0xFF);
            packet[1] = (byte)(sequence >> 8);
            packet[2] = 0;
            VoiceCodec.EncodeFrame(frame, 0, frame.Length, packet, VoiceCodec.HeaderBytes);
            Send?.Invoke(packet, packet.Length);
            FramesSent++;
            speakingUntil = Time.unscaledTime + 0.3f;
        }
    }
}

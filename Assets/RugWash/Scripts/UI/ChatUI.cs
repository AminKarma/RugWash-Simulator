using System.Text;
using RugWash.Core;
using RugWash.Network;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RugWash.UI
{
    /// <summary>
    /// Chat textuel en ligne (grande mise à jour) : les dernières lignes en bas à gauche (elles s'effacent après quelques
    /// secondes), Entrée pour écrire (Entrée envoie, Échap annule). Visible seulement en partie en ligne ; à la manette, le chat
    /// se lit mais ne s'écrit pas (pas de clavier).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChatUI : MonoBehaviour, IModalPanel
    {
        [SerializeField] GameObject root;
        [SerializeField] Text logLabel;
        [SerializeField] CanvasGroup logGroup;
        [SerializeField] GameObject inputRow;
        [SerializeField] InputField input;
        [SerializeField] Text hintLabel;
        [Tooltip("Lignes affichées et durée d'affichage d'une nouvelle ligne quand le chat est fermé (s).")]
        [SerializeField, Range(3, 12)] int visibleLines = 7;
        [SerializeField] float lineSeconds = 10f;

        GameStateManager gameState;
        NetworkWorkshop workshop;
        int shownVersion = -1;
        float lastLineTime = -100f;
        readonly StringBuilder builder = new StringBuilder();

        public bool IsOpen { get; private set; }
        public bool IsOnline => workshop != null && workshop.IsSpawned;
        public string LogText => logLabel.text;
        public InputField Input => input;

        public bool IsConfigured(out string missing)
        {
            missing = root == null ? nameof(root) : logLabel == null ? nameof(logLabel) : logGroup == null ? nameof(logGroup)
                : inputRow == null ? nameof(inputRow) : input == null ? nameof(input) : hintLabel == null ? nameof(hintLabel) : null;
            return missing == null;
        }

        public void Bind(GameStateManager state)
        {
            gameState = state;
            input.characterLimit = ChatRules.MaxLength;
            input.lineType = InputField.LineType.SingleLine;
            input.onEndEdit.AddListener(OnEndEdit);
            inputRow.SetActive(false);
            root.SetActive(false);
        }

        void Update()
        {
            var current = NetworkWorkshop.Instance;
            if (current != workshop)
            {
                if (IsOpen) Close();
                workshop = current;
                shownVersion = -1;
            }
            bool online = IsOnline;
            if (root.activeSelf != online) root.SetActive(online);
            if (!online) return;

            if (!IsOpen && gameState != null && gameState.IsGameplayActive && Keyboard.current != null
                && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
                Open();

            var log = workshop.Chat;
            if (log.Version != shownVersion)
            {
                shownVersion = log.Version;
                Rebuild(log);
            }
            // Fermé : les lignes s'effacent quelques secondes après le dernier message.
            float target = IsOpen ? 1f : Mathf.Clamp01((lastLineTime + lineSeconds - Time.unscaledTime) / 1.5f);
            logGroup.alpha = Mathf.MoveTowards(logGroup.alpha, target, Time.unscaledDeltaTime * 4f);
        }

        void Rebuild(ChatLog log)
        {
            builder.Clear();
            int start = Mathf.Max(0, log.Lines.Count - visibleLines);
            for (int i = start; i < log.Lines.Count; i++)
            {
                var line = log.Lines[i];
                if (builder.Length > 0) builder.Append('\n');
                if (line.System) builder.Append("<color=#9FD8FF>").Append(Escape(line.Text)).Append("</color>");
                else builder.Append("<color=#FFE08A>").Append(Escape(line.Sender)).Append("</color> : ").Append(Escape(line.Text));
                lastLineTime = Mathf.Max(lastLineTime, line.Time);
            }
            logLabel.text = builder.ToString();
        }

        /// <summary>Le texte des joueurs ne peut pas injecter de balises de mise en forme.</summary>
        static string Escape(string text) => string.IsNullOrEmpty(text) ? string.Empty : text.Replace("<", "‹").Replace(">", "›");

        public bool Open()
        {
            if (IsOpen || !IsOnline || (gameState != null && !gameState.IsGameplayActive)) return false;
            IsOpen = true;
            inputRow.SetActive(true);
            input.text = string.Empty;
            input.Select();
            input.ActivateInputField();
            hintLabel.text = "Entrée : envoyer · Échap : annuler";
            gameState?.PushModal();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            input.DeactivateInputField();
            inputRow.SetActive(false);
            gameState?.PopModal();
        }

        void OnEndEdit(string text)
        {
            if (!IsOpen) return;
            bool submitted = Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
            if (submitted) Submit(text);
            Close();
        }

        /// <summary>Envoie le texte saisi (tests : appel direct).</summary>
        public bool Submit(string text) => IsOnline && workshop.SendChat(text);
    }
}

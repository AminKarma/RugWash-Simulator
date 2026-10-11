namespace RugWash.Core
{
    /// <summary>Fenêtre qui libère le curseur sans mettre en pause ; Échap la ferme.</summary>
    public interface IModalPanel
    {
        bool IsOpen { get; }
        void Close();
    }
}

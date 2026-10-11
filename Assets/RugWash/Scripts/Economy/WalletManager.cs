using System.Globalization;
using UnityEngine;

namespace RugWash.Economy
{
    /// <summary>Portefeuille du joueur dans la scène (données du joueur, indépendantes du tapis).</summary>
    [DisallowMultipleComponent]
    public sealed class WalletManager : MonoBehaviour
    {
        static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        [SerializeField, Min(0)] int startingBalance;

        public Wallet Wallet { get; private set; }
        public int Balance => Wallet.Balance;

        void Awake() => Wallet = new Wallet(startingBalance);

        public static string Format(int amount) => amount.ToString("N0", French) + " €";
    }
}

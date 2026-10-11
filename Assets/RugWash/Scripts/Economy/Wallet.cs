using System;

namespace RugWash.Economy
{
    /// <summary>Solde du joueur (règles pures) : jamais négatif, montants négatifs refusés.</summary>
    public sealed class Wallet
    {
        public Wallet(int initialBalance = 0) => Balance = Math.Max(0, initialBalance);

        public int Balance { get; private set; }

        /// <summary>(nouveau solde, variation)</summary>
        public event Action<int, int> Changed;

        public bool Add(int amount)
        {
            if (amount <= 0) return false;
            Balance += amount;
            Changed?.Invoke(Balance, amount);
            return true;
        }

        public bool CanAfford(int amount) => amount >= 0 && Balance >= amount;

        /// <summary>Débite si le solde suffit ; sinon ne modifie rien et retourne faux.</summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0 || Balance < amount) return false;
            Balance -= amount;
            Changed?.Invoke(Balance, -amount);
            return true;
        }

        /// <summary>Restauration depuis une sauvegarde (phase 4).</summary>
        public void Restore(int balance)
        {
            Balance = Math.Max(0, balance);
            Changed?.Invoke(Balance, 0);
        }
    }
}

namespace CardGame
{
    /// <summary>
    /// A single card during a match. Several instances can share the same CardData
    /// (e.g. two copies of "Kaedrun"), but each one tracks its own damage and turn state.
    /// </summary>
    public class CardInstance
    {
        public readonly CardData data;
        public int attack;
        public int health;

        /// <summary>Summoning sickness: summoned during its owner's current turn.</summary>
        public bool summonedThisTurn;
        /// <summary>Moved (or swapped) during its owner's current turn.</summary>
        public bool movedThisTurn;
        /// <summary>Already attacked during its owner's current turn.</summary>
        public bool attackedThisTurn;

        /// <summary>The on-screen object for this card (null while it sits in a deck, grave or the enemy's hidden hand).</summary>
        public CardView view;

        public bool IsDead => health <= 0;
        public bool IsServant => data.type == CardType.Servant;
        public bool IsTrap => data.type == CardType.Trap;

        /// <summary>True if this servant may attack now (ignores the first-turn rule, which is game-wide).</summary>
        public bool CanAttack => IsServant && !summonedThisTurn && !movedThisTurn && !attackedThisTurn;

        public CardInstance(CardData data)
        {
            this.data = data;
            ResetStats();
        }

        /// <summary>Back to printed stats, e.g. when the card goes to the graveyard.</summary>
        public void ResetStats()
        {
            attack = data.attack;
            health = data.health;
            summonedThisTurn = movedThisTurn = attackedThisTurn = false;
        }

        /// <summary>Called at the start of the owner's turn.</summary>
        public void NewTurn()
        {
            summonedThisTurn = movedThisTurn = attackedThisTurn = false;
        }
    }
}

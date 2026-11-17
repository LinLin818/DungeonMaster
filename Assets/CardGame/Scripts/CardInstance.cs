namespace CardGame
{
    /// <summary>
    /// A single card during a match. Several instances can share the same CardData
    /// (e.g. three copies of "Knight"), but each one tracks its own damage.
    /// </summary>
    public class CardInstance
    {
        public readonly CardData data;
        public int attack;
        public int health;

        /// <summary>The on-screen object for this card (null while it sits in a deck, grave or the enemy's hidden hand).</summary>
        public CardView view;

        public bool IsDead => health <= 0;
        public bool IsServant => data.type == CardType.Servant;
        public bool IsTrap => data.type == CardType.Trap;

        public CardInstance(CardData data)
        {
            this.data = data;
            attack = data.attack;
            health = data.health;
        }
    }
}

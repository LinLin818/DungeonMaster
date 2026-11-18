using UnityEngine;

namespace CardGame
{
    public enum CardType
    {
        /// <summary>A creature placed in a servant slot. Fights the enemy servant in the same lane.</summary>
        Servant,
        /// <summary>Placed face-down in a trap slot. Triggers when an enemy servant enters that lane.</summary>
        Trap
    }

    /// <summary>
    /// One card design, stored as an asset.
    /// Create new cards with: Right-click in Project > Create > Card Game > Card
    /// </summary>
    [CreateAssetMenu(fileName = "NewCard", menuName = "Card Game/Card")]
    public class CardData : ScriptableObject
    {
        public string cardName = "New Card";
        public CardType type = CardType.Servant;
        [Tooltip("Optional family/colour, e.g. Fire or Grass (taken from the sprite name by the importer).")]
        public string element;
        [TextArea] public string description;

        [Tooltip("Optional full card frame (your *_Base sprite). Replaces the default frame.")]
        public Sprite baseSprite;

        [Tooltip("Optional picture shown in the art window (your *_Art sprite). If empty, the art box is filled with Art Color.")]
        public Sprite artwork;
        public Color artColor = Color.white;

        [Min(0)] public int cost = 1;

        [Tooltip("Servant: damage dealt each attack. Trap: damage dealt to the servant that triggers it.")]
        [Min(0)] public int attack = 1;

        [Tooltip("Servant only. Traps ignore this.")]
        [Min(1)] public int health = 1;
    }
}

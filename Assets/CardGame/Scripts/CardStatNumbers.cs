using UnityEngine;

namespace CardGame
{
    /// <summary>
    /// The number sprites drawn on a card (from Card_StatNumbers_Sheet.png): index = value.
    /// The asset lives in a Resources folder so every CardView finds it without extra setup.
    /// </summary>
    [CreateAssetMenu(fileName = "CardStatNumbers", menuName = "Card Game/Card Stat Numbers")]
    public class CardStatNumbers : ScriptableObject
    {
        public const string ResourcePath = "CardStatNumbers";

        [Tooltip("Mana cost, 0-10 (white on the blue orb).")]
        public Sprite[] cost;
        [Tooltip("ATK at its printed value or lower, 0-10.")]
        public Sprite[] attack;
        [Tooltip("ATK above its printed value, 0-10 (green).")]
        public Sprite[] attackBuffed;
        [Tooltip("HP at its printed value, 0-20.")]
        public Sprite[] health;
        [Tooltip("HP below its printed value, 0-20 (red).")]
        public Sprite[] healthDamaged;
        [Tooltip("HP above its printed value, 0-20 (green).")]
        public Sprite[] healthBuffed;

        static CardStatNumbers cached;

        public static CardStatNumbers Default
        {
            get
            {
                if (cached == null) cached = Resources.Load<CardStatNumbers>(ResourcePath);
                return cached;
            }
        }

        /// <summary>Returns set[value], clamped to what the sheet has. Null if the set is empty.</summary>
        public static Sprite Pick(Sprite[] set, int value)
        {
            if (set == null || set.Length == 0) return null;
            return set[Mathf.Clamp(value, 0, set.Length - 1)];
        }
    }
}

using UnityEngine;

namespace CardGame
{
    public enum CardType
    {
        /// <summary>A creature placed in a servant slot. Fights the enemy servant in the same lane.</summary>
        Servant,
        /// <summary>Placed face-down in a trap slot. Fires on its own when its condition is met.</summary>
        Trap,
        /// <summary>Cast from your hand during your turn, then goes to the graveyard.</summary>
        Spell
    }

    /// <summary>What a spell or trap does. Effect Amount sets the number (damage, HP, ATK).</summary>
    public enum CardEffect
    {
        None,

        // ---- spells
        /// <summary>Deal Amount damage to a servant. (Fireball)</summary>
        DamageServant,
        /// <summary>A servant gains BLAZE and +Amount ATK this turn. (Kindle)</summary>
        BlazeAndAttackThisTurn,
        /// <summary>Deal Amount damage to the enemy player. (Scorch)</summary>
        DamageEnemyPlayer,
        /// <summary>TORRENT all enemy servants. (Tidal Wave)</summary>
        TorrentAllEnemies,
        /// <summary>Move an enemy servant to another lane (swaps if taken). (Riptide)</summary>
        MoveEnemyServant,
        /// <summary>Prevent the next damage a servant would take. (Bubble Shield)</summary>
        ShieldServant,
        /// <summary>An enemy servant can't move or attack on its next turn. (Entangle)</summary>
        EntangleEnemy,
        /// <summary>A servant gains +Amount ATK and +Amount HP. (Bloom)</summary>
        BuffServant,
        /// <summary>Heal all your servants Amount HP. (Rejuvenate)</summary>
        HealAllies,

        // ---- traps (fire in their own lane)
        /// <summary>An enemy servant summoned in this lane takes Amount damage. (Fire Pit)</summary>
        TrapDamageOnSummon,
        /// <summary>TORRENT an enemy servant summoned in this lane. (Whirlpool)</summary>
        TrapTorrentOnSummon,
        /// <summary>An enemy servant attacking in this lane takes Amount damage. (Flame Wall, Bramble Snare)</summary>
        TrapDamageOnAttack,
        /// <summary>Cancel an enemy servant's attack in this lane. (Undertow)</summary>
        TrapCancelAttack,
        /// <summary>When attacked in this lane: your servant there gets +1 HP, the attacker takes Amount damage. (Thornwall)</summary>
        TrapThornwall
    }

    /// <summary>
    /// A servant's triggered ability (used for "On summon" and "When slain"). Amount sets the number.
    /// "Lane enemy" is the enemy servant straight across, in the same lane.
    /// </summary>
    public enum ServantAbility
    {
        None,
        /// <summary>Deal Amount damage to the lane enemy. (Kaedrun, Katon, Burning Skeleton)</summary>
        DamageLaneEnemy,
        /// <summary>TORRENT the lane enemy. (Hydruge, Drowned Skeleton)</summary>
        TorrentLaneEnemy,
        /// <summary>TORRENT every enemy servant. (Lochness)</summary>
        TorrentAllEnemies,
        /// <summary>Heal your other servants Amount HP. (Overgrown Skeleton)</summary>
        HealAllies,
        /// <summary>Your other servants get +Amount ATK and +Amount HP. (Eco)</summary>
        BuffAllies,
        /// <summary>Give one of your other servants OVERGROW (you pick it). (Fern)</summary>
        GiveAllyOvergrow
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
        [Tooltip("Deck/family: Fire, Grass or Water. Each player's deck is every card of one element.")]
        public string element;
        [TextArea] public string description;

        [Tooltip("Optional full card frame (your *_Base sprite). Replaces the default frame.")]
        public Sprite baseSprite;

        [Tooltip("Optional picture shown in the art window (your *_Art sprite). If empty, the art box is filled with Art Color.")]
        public Sprite artwork;
        public Color artColor = Color.white;

        [Min(0)] public int cost = 1;

        [Tooltip("Servant only.")]
        [Min(0)] public int attack = 1;

        [Tooltip("Servant only.")]
        [Min(1)] public int health = 1;

        [Header("Servant keywords")]
        [Tooltip("BLAZE: can attack on the turn it is summoned.")]
        public bool blaze;
        [Tooltip("OVERGROW: gains +1 HP at the start of each of your turns, including the turn it is summoned.")]
        public bool overgrow;

        [Header("Servant abilities")]
        [Tooltip("Happens when this servant is summoned.")]
        public ServantAbility onSummon = ServantAbility.None;
        [Min(0)] public int onSummonAmount;
        [Tooltip("Happens when this servant is destroyed, however it dies.")]
        public ServantAbility whenSlain = ServantAbility.None;
        [Min(0)] public int whenSlainAmount;
        [Tooltip("Moves lanes for 0 mana.")]
        public bool freeMove;
        [Tooltip("Takes this much less damage in battle (attacking or blocking). Spells and traps are not reduced.")]
        [Min(0)] public int battleDamageReduction;

        [Header("Spell / trap")]
        public CardEffect effect = CardEffect.None;
        [Tooltip("Damage, HP or ATK for the effect.")]
        [Min(0)] public int effectAmount;

        public bool IsSummonTrap => effect == CardEffect.TrapDamageOnSummon || effect == CardEffect.TrapTorrentOnSummon;
        public bool IsAttackTrap => effect == CardEffect.TrapDamageOnAttack || effect == CardEffect.TrapCancelAttack || effect == CardEffect.TrapThornwall;

        /// <summary>True for spells that need a servant picked as the target.</summary>
        public bool SpellNeedsTarget =>
            effect == CardEffect.DamageServant || effect == CardEffect.BlazeAndAttackThisTurn ||
            effect == CardEffect.MoveEnemyServant || effect == CardEffect.ShieldServant ||
            effect == CardEffect.EntangleEnemy || effect == CardEffect.BuffServant;

        /// <summary>True for spells that may only target enemy servants (others may target any servant).</summary>
        public bool SpellTargetsEnemyOnly => effect == CardEffect.MoveEnemyServant || effect == CardEffect.EntangleEnemy;
    }
}

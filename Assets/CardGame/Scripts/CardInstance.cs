namespace CardGame
{
    /// <summary>
    /// A single card during a match. Several instances can share the same CardData
    /// (e.g. two copies of "Kaedrun"), but each one tracks its own damage, buffs and turn state.
    /// </summary>
    public class CardInstance
    {
        public readonly CardData data;
        public int attack;
        public int health;
        /// <summary>Highest HP it can heal to: printed HP plus permanent buffs (Bloom, OVERGROW, Thornwall).</summary>
        public int maxHealth;

        // ---- this turn
        /// <summary>Summoning sickness: summoned during its owner's current turn.</summary>
        public bool summonedThisTurn;
        /// <summary>Moved (or swapped) during its owner's current turn.</summary>
        public bool movedThisTurn;
        /// <summary>Already attacked during its owner's current turn.</summary>
        public bool attackedThisTurn;
        /// <summary>Locked by TORRENT or Entangle for its owner's current turn.</summary>
        public bool cannotAttackThisTurn, cannotMoveThisTurn;
        /// <summary>Kindle: BLAZE and extra ATK until the end of its owner's turn.</summary>
        public bool blazeThisTurn;
        public int attackBonusThisTurn;

        // ---- lasting
        public bool hasBlaze;
        public bool hasOvergrow;
        /// <summary>Bubble Shield: the next damage it would take is prevented.</summary>
        public bool shielded;
        /// <summary>TORRENT / Entangle land on its owner's next turn.</summary>
        public bool torrentPending, entanglePending;

        /// <summary>The on-screen object for this card (null while it sits in a deck, grave or the enemy's hidden hand).</summary>
        public CardView view;

        public bool IsDead => health <= 0;
        public bool IsServant => data.type == CardType.Servant;
        public bool IsTrap => data.type == CardType.Trap;
        public bool IsSpell => data.type == CardType.Spell;
        public bool HasBlazeNow => hasBlaze || blazeThisTurn;

        /// <summary>True if this servant may attack now (ignores the first-turn rule, which is game-wide).</summary>
        public bool CanAttack =>
            IsServant && !attackedThisTurn && !movedThisTurn && !cannotAttackThisTurn && (!summonedThisTurn || HasBlazeNow);

        public CardInstance(CardData data)
        {
            this.data = data;
            ResetStats();
        }

        /// <summary>Back to printed stats with no statuses, e.g. when the card goes to the graveyard.</summary>
        public void ResetStats()
        {
            attack = data.attack;
            health = maxHealth = data.health;
            summonedThisTurn = movedThisTurn = attackedThisTurn = false;
            cannotAttackThisTurn = cannotMoveThisTurn = false;
            blazeThisTurn = false;
            attackBonusThisTurn = 0;
            hasBlaze = data.blaze;
            hasOvergrow = data.overgrow;
            shielded = false;
            torrentPending = entanglePending = false;
        }

        /// <summary>Start of its owner's turn: clear per-turn flags and apply TORRENT / Entangle.</summary>
        public void StartOwnTurn()
        {
            summonedThisTurn = movedThisTurn = attackedThisTurn = false;
            cannotAttackThisTurn = torrentPending || entanglePending;
            cannotMoveThisTurn = entanglePending;
            torrentPending = entanglePending = false;
        }

        /// <summary>End of its owner's turn: "this turn" effects wear off.</summary>
        public void EndOwnTurn()
        {
            attack -= attackBonusThisTurn;
            attackBonusThisTurn = 0;
            blazeThisTurn = false;
            cannotAttackThisTurn = cannotMoveThisTurn = false;
        }

        /// <summary>Permanent HP gain (raises max HP too).</summary>
        public void GainHealth(int amount)
        {
            health += amount;
            maxHealth += amount;
        }

        /// <summary>Heal up to max HP. Returns how much was healed.</summary>
        public int Heal(int amount)
        {
            int before = health;
            health = System.Math.Min(maxHealth, health + amount);
            return health - before;
        }
    }
}

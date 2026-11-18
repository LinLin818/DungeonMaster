using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame
{
    /// <summary>
    /// Runs a match following the Servant Card Game rules.
    ///
    /// Setup
    ///   Both players start with startingHealth HP and a 30-card deck (max 2 copies of a card).
    ///   The first player draws 4, the second draws 5. The first player can't attack on their first turn.
    ///
    /// Each turn
    ///   1. Refresh: discard your hand, draw 5 (not on your very first turn), mana refills to 5.
    ///   2. Main phase, in any order:
    ///        summon a servant / set a trap ..... card cost   (click a hand card, then a glowing slot)
    ///        move or swap a servant ............ 1 mana      (click your servant, then another servant slot)
    ///        attack ............................ free        (click your servant, then the enemy slot in
    ///                                                          its lane or the enemy portrait)
    ///   3. End turn. Unspent mana stays for the opponent's turn, where blocking costs 1.
    ///
    /// Servants can't attack the turn they're summoned or moved, and attack once per turn.
    /// Combat stays in its lane: the defender may pay 1 mana to block with their servant there
    /// (both deal damage to each other, no damage to the player); otherwise the player is hit.
    /// Traps spring on an enemy servant entering their lane (summoned or moved), deal their
    /// attack to it and go to the graveyard. An empty deck is refilled by shuffling the graveyard.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public const int Lanes = 3;
        static readonly string[] LaneNames = { "LEFT", "MID", "RIGHT" };

        public enum FirstTurn { Random, Player, Enemy }

        [Header("Cards")]
        public List<CardData> cardPool = new List<CardData>();
        [Tooltip("Rules: no more than 2 copies of any card.")]
        [Range(1, 2)] public int copiesPerCard = 2;
        public CardView cardPrefab;

        [Header("Piles")]
        public DeckPile playerDeck;
        public DeckPile enemyDeck;
        public DeckPile playerGrave;
        public DeckPile enemyGrave;

        [Header("Board slots (index = lane: 0 left, 1 mid, 2 right)")]
        public BoardSlot[] playerServantSlots = new BoardSlot[Lanes];
        public BoardSlot[] playerTrapSlots = new BoardSlot[Lanes];
        public BoardSlot[] enemyServantSlots = new BoardSlot[Lanes];
        public BoardSlot[] enemyTrapSlots = new BoardSlot[Lanes];

        [Header("Hand (Horizontal Layout Group)")]
        public RectTransform playerHand;

        [Header("Heroes")]
        public RectTransform playerPortrait;
        public RectTransform enemyPortrait;
        public TMP_Text playerHealthText;
        public TMP_Text enemyHealthText;
        [Tooltip("Sprite-based HP display (Counter_HP sheet). Used instead of the text when set.")]
        public SpriteCounter playerHealthCounter;
        public SpriteCounter enemyHealthCounter;
        public ManaBar playerMana;
        public ManaBar enemyMana;
        public TMP_Text enemyHandText;

        [Header("UI")]
        public TMP_Text logText;
        public Button endTurnButton;
        public Button restartButton;
        [Tooltip("Shown when the enemy attacks and you can block. Made from a copy of End Turn if left empty.")]
        public Button blockButton;
        public Button skipBlockButton;

        [Header("Rules")]
        [Range(10, 20)] public int startingHealth = 20;
        public int handSize = 5;
        public int firstPlayerOpeningHand = 4;
        public int secondPlayerOpeningHand = 5;
        public int manaPerTurn = 5;
        public int moveCost = 1;
        public int blockCost = 1;
        public FirstTurn firstTurn = FirstTurn.Random;

        [Header("Timing")]
        public float drawStagger = 0.14f;
        public float enemyPlayDelay = 0.45f;
        public float attackPause = 0.35f;

        readonly List<CardInstance> hand = new List<CardInstance>();
        readonly List<CardInstance> enemyHand = new List<CardInstance>();

        int playerHealth, enemyHealth;
        int mana, enemyManaLeft;        // what each player has left right now (carries into the opponent's turn)
        int playerTurns, enemyTurns;    // turns started so far
        bool playerGoesFirst, playerTurnActive, busy, gameOver;
        int blockChoice = -1;           // -1 waiting, 0 no block, 1 block
        CardView selected;              // a card in your hand, or one of your servants on the board

        // ================================================================ lifecycle

        void Start()
        {
            playerDeck.Clicked += OnPlayerDeckClicked;
            foreach (BoardSlot s in AllSlots()) s.Clicked += OnSlotClicked;
            AddClickArea(enemyPortrait, OnEnemyPortraitClicked);

            // Copy End Turn for the block prompt before End Turn gets its own listener.
            if (!blockButton) blockButton = CloneButton(endTurnButton, "BlockButton", $"BLOCK ({blockCost})", -190f);
            if (!skipBlockButton) skipBlockButton = CloneButton(endTurnButton, "NoBlockButton", "NO BLOCK", -275f);
            if (blockButton) blockButton.onClick.AddListener(() => blockChoice = 1);
            if (skipBlockButton) skipBlockButton.onClick.AddListener(() => blockChoice = 0);

            endTurnButton.onClick.AddListener(OnEndTurnPressed);
            if (restartButton) restartButton.onClick.AddListener(Restart);
            StartGame();
        }

        IEnumerable<BoardSlot> AllSlots()
        {
            foreach (BoardSlot s in playerServantSlots) yield return s;
            foreach (BoardSlot s in playerTrapSlots) yield return s;
            foreach (BoardSlot s in enemyServantSlots) yield return s;
            foreach (BoardSlot s in enemyTrapSlots) yield return s;
        }

        void StartGame()
        {
            hand.Clear();
            enemyHand.Clear();
            selected = null;
            playerHealth = enemyHealth = startingHealth;
            mana = enemyManaLeft = 0;
            playerTurns = enemyTurns = 0;
            gameOver = false;
            playerTurnActive = false;
            playerGoesFirst = firstTurn == FirstTurn.Player || (firstTurn == FirstTurn.Random && UnityEngine.Random.value < 0.5f);

            int copies = Mathf.Clamp(copiesPerCard, 1, 2);
            playerDeck.Fill(cardPool, copies);
            enemyDeck.Fill(cardPool, copies);
            playerGrave.Clear();
            enemyGrave.Clear();
            ShowBlockButtons(false);

            StartCoroutine(OpeningRoutine());
        }

        void Restart()
        {
            StopAllCoroutines();
            for (int i = playerHand.childCount - 1; i >= 0; i--) Destroy(playerHand.GetChild(i).gameObject);
            foreach (BoardSlot s in AllSlots())
            {
                foreach (CardView v in s.GetComponentsInChildren<CardView>(true)) Destroy(v.gameObject);
                s.Occupant = null;
                s.SetHighlight(false);
            }
            busy = false;
            StartGame();
        }

        IEnumerator OpeningRoutine()
        {
            busy = true;
            endTurnButton.interactable = false;
            RefreshUI();
            yield return null; // let destroyed objects disappear and layouts settle

            int mine = playerGoesFirst ? firstPlayerOpeningHand : secondPlayerOpeningHand;
            int theirs = playerGoesFirst ? secondPlayerOpeningHand : firstPlayerOpeningHand;
            Log(playerGoesFirst
                ? $"You go first. You draw {mine}, the enemy draws {theirs}."
                : $"The enemy goes first. You draw {mine}, the enemy draws {theirs}.");

            yield return PlayerDraw(mine);
            EnemyDraw(theirs);
            yield return new WaitForSeconds(0.6f);

            if (!playerGoesFirst)
            {
                yield return EnemyTurnRoutine();
                if (gameOver) yield break;
            }
            yield return PlayerTurnStart();
        }

        // ================================================================ turns

        IEnumerator PlayerTurnStart()
        {
            busy = true;
            playerTurns++;

            // Refresh: discard the hand and draw a new one (your opening hand is your first turn's hand).
            if (playerTurns > 1)
            {
                yield return DiscardPlayerHand();
                yield return PlayerDraw(handSize);
            }

            mana = manaPerTurn;
            foreach (BoardSlot s in playerServantSlots) s.Occupant?.NewTurn();

            busy = false;
            playerTurnActive = true;
            endTurnButton.interactable = true;
            Log(FirstTurnNoAttack(true)
                ? "Your turn. Play cards and set up. (The first player can't attack on their first turn.)"
                : "Your turn. Play cards, then click a servant to attack or move it.");
            RefreshUI();
        }

        void OnEndTurnPressed()
        {
            if (busy || gameOver || !playerTurnActive) return;
            StartCoroutine(EndTurnRoutine());
        }

        IEnumerator EndTurnRoutine()
        {
            playerTurnActive = false;
            Deselect();
            endTurnButton.interactable = false;

            yield return EnemyTurnRoutine();
            if (gameOver) yield break;
            yield return PlayerTurnStart();
        }

        IEnumerator EnemyTurnRoutine()
        {
            busy = true;
            playerTurnActive = false;
            endTurnButton.interactable = false;
            enemyTurns++;

            Log("Enemy turn...");
            yield return new WaitForSeconds(0.4f);

            if (enemyTurns > 1)
            {
                foreach (CardInstance c in enemyHand) ToGrave(c, enemyGrave);
                enemyHand.Clear();
                EnemyDraw(handSize);
            }
            enemyManaLeft = manaPerTurn;
            foreach (BoardSlot s in enemyServantSlots) s.Occupant?.NewTurn();
            RefreshUI();

            yield return EnemyPlayRoutine();
            if (CheckGameOver()) yield break;

            if (FirstTurnNoAttack(false))
            {
                Log("The enemy can't attack on its first turn.");
                yield return new WaitForSeconds(0.6f);
            }
            else
            {
                yield return EnemyAttackRoutine();
                if (CheckGameOver()) yield break;
            }

            busy = false;
        }

        /// <summary>The first player can't attack during their first turn.</summary>
        bool FirstTurnNoAttack(bool forPlayer) =>
            forPlayer ? playerGoesFirst && playerTurns == 1 : !playerGoesFirst && enemyTurns == 1;

        // ================================================================ drawing

        void OnPlayerDeckClicked()
        {
            if (gameOver) return;
            Log("Cards are drawn automatically: each turn you discard your hand and draw 5.");
        }

        /// <summary>Refills an empty deck from the graveyard. Returns false if there is nothing to draw at all.</summary>
        bool EnsureCanDraw(DeckPile deck, DeckPile grave, bool isPlayer)
        {
            if (deck.Count > 0) return true;
            if (grave.Count == 0) return false;
            List<CardInstance> cards = grave.TakeAll();
            foreach (CardInstance c in cards) c.ResetStats();
            deck.AddAndShuffle(cards);
            Log(isPlayer ? "Your deck ran out: your graveyard was shuffled into a new deck."
                         : "The enemy's deck ran out: their graveyard was shuffled into a new deck.");
            return true;
        }

        IEnumerator PlayerDraw(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!EnsureCanDraw(playerDeck, playerGrave, true)) { Log("You have no cards left to draw."); yield break; }

                Vector3 from = playerDeck.TopWorldPosition;
                CardInstance card = playerDeck.Draw();
                hand.Add(card);

                CardView view = Instantiate(cardPrefab, playerHand);
                view.Setup(card, true, OnCardClicked);
                view.InHand = true;
                view.FlyFrom(from, 0f, 6f);

                RefreshUI();
                yield return new WaitForSeconds(drawStagger);
            }
        }

        void EnemyDraw(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!EnsureCanDraw(enemyDeck, enemyGrave, false)) break;
                enemyHand.Add(enemyDeck.Draw());
            }
            RefreshUI();
        }

        IEnumerator DiscardPlayerHand()
        {
            Deselect();
            if (hand.Count == 0) yield break;

            for (int i = 0; i < hand.Count; i++)
            {
                CardInstance card = hand[i];
                if (card.view) card.view.FlyToAndDestroy(playerGrave.TopWorldPosition, () => ToGrave(card, playerGrave), i * 0.05f);
                else ToGrave(card, playerGrave);
            }
            hand.Clear();
            yield return new WaitForSeconds(0.6f);
        }

        // ================================================================ selecting

        void OnCardClicked(CardView view)
        {
            if (busy || gameOver || !playerTurnActive) return;

            if (view.InHand) { SelectHandCard(view); return; }

            BoardSlot slot = SlotOf(view.Card);
            if (slot == null) return;

            // Your own servant: select it (or unselect it) unless another servant is waiting to move here.
            if (slot.isPlayerSide && view.Card.IsServant && (selected == null || selected.InHand || selected == view))
            {
                if (selected == view) { Deselect(); RefreshUI(); }
                else SelectBoardServant(view);
                return;
            }
            OnSlotClicked(slot);
        }

        void SelectHandCard(CardView view)
        {
            if (selected == view) { Deselect(); RefreshUI(); return; }

            CardInstance card = view.Card;
            if (card.data.cost > mana) { Log($"Not enough mana for {card.data.cardName} (costs {card.data.cost})."); return; }

            Deselect();
            selected = view;
            view.Selected = true;

            int open = 0;
            foreach (BoardSlot s in card.IsTrap ? playerTrapSlots : playerServantSlots)
                if (s.IsEmpty) { s.SetHighlight(true); open++; }

            Log(open == 0
                ? $"No empty {(card.IsTrap ? "trap" : "servant")} slot for {card.data.cardName}."
                : $"Place {card.data.cardName} in a glowing {(card.IsTrap ? "trap" : "servant")} slot.");
        }

        void SelectBoardServant(CardView view)
        {
            Deselect();
            selected = view;
            view.Selected = true;

            CardInstance c = view.Card;
            BoardSlot from = SlotOf(c);
            string why = WhyCantAttack(c, true);
            if (why == null) enemyServantSlots[from.lane].SetHighlight(true);
            if (mana >= moveCost)
                foreach (BoardSlot s in playerServantSlots)
                    if (s != from) s.SetHighlight(true);

            string attackPart = why == null
                ? "Click the glowing enemy slot or the enemy portrait to attack."
                : $"It can't attack: {why}.";
            string movePart = mana >= moveCost
                ? $"Click another servant slot to move it ({moveCost} mana)."
                : "No mana left to move it.";
            Log($"{c.data.cardName} ({c.attack}/{c.health}). {attackPart} {movePart}");
        }

        void Deselect()
        {
            if (selected != null) selected.Selected = false;
            selected = null;
            foreach (BoardSlot s in AllSlots()) s.SetHighlight(false);
        }

        BoardSlot SlotOf(CardInstance card)
        {
            if (card == null) return null;
            foreach (BoardSlot s in AllSlots())
                if (s.Occupant == card) return s;
            return null;
        }

        string WhyCantAttack(CardInstance c, bool forPlayer)
        {
            if (FirstTurnNoAttack(forPlayer)) return "the first player can't attack on their first turn";
            if (c.summonedThisTurn) return "it was summoned this turn";
            if (c.movedThisTurn) return "it moved this turn";
            if (c.attackedThisTurn) return "it already attacked this turn";
            return null;
        }

        // ================================================================ player actions

        void OnSlotClicked(BoardSlot slot)
        {
            if (busy || gameOver || !playerTurnActive) return;
            if (selected == null) { Log("Select a card in your hand or one of your servants first."); return; }

            CardInstance card = selected.Card;

            // ---- placing a card from your hand
            if (selected.InHand)
            {
                if (!slot.isPlayerSide || !slot.IsEmpty || slot.kind != card.data.type)
                {
                    Log(card.IsTrap ? "Traps go in your empty TRAP slots." : "Servants go in your empty SERVANT slots.");
                    return;
                }
                StartCoroutine(PlayerPlaceRoutine(selected, slot));
                return;
            }

            // ---- a servant on the board is selected
            BoardSlot from = SlotOf(card);
            if (from == null) { Deselect(); return; }
            if (slot == from) { Deselect(); RefreshUI(); return; }

            if (!slot.isPlayerSide && slot.kind == CardType.Servant && slot.lane == from.lane) { TryPlayerAttack(from); return; }
            if (slot.isPlayerSide && slot.kind == CardType.Servant) { TryPlayerMove(from, slot); return; }

            Log(slot.isPlayerSide
                ? "Servants can only move to your SERVANT slots."
                : "Attacks stay in their lane: click the enemy slot straight across, or the enemy portrait.");
        }

        void OnEnemyPortraitClicked()
        {
            if (busy || gameOver || !playerTurnActive) return;
            if (selected == null || selected.InHand) { Log("Select one of your servants first, then click here to attack."); return; }
            BoardSlot from = SlotOf(selected.Card);
            if (from != null) TryPlayerAttack(from);
        }

        IEnumerator PlayerPlaceRoutine(CardView view, BoardSlot slot)
        {
            busy = true;
            CardInstance card = view.Card;
            Deselect();

            mana -= card.data.cost;
            hand.Remove(card);
            card.summonedThisTurn = true;

            Vector3 from = view.visual.position;
            view.InHand = false;
            view.SetPlayable(true);
            slot.Hold(view);
            view.FlyFrom(from, 0f, -8f);

            Log(card.IsTrap ? $"You set a trap: {card.data.cardName}." : $"You summoned {card.data.cardName}.");
            RefreshUI();

            if (card.IsServant)
                yield return SpringTrapIfAny(enemyTrapSlots[slot.lane], enemyGrave, slot, playerGrave);

            busy = false;
            CheckGameOver();
            RefreshUI();
        }

        void TryPlayerAttack(BoardSlot from)
        {
            CardInstance attacker = from.Occupant;
            if (attacker == null) return;
            string why = WhyCantAttack(attacker, true);
            if (why != null) { Log($"{attacker.data.cardName} can't attack: {why}."); return; }
            StartCoroutine(PlayerAttackRoutine(from));
        }

        IEnumerator PlayerAttackRoutine(BoardSlot from)
        {
            busy = true;
            Deselect();
            Log($"{from.Occupant.data.cardName} attacks the {LaneNames[from.lane]} lane!");
            yield return new WaitForSeconds(0.25f);
            yield return ResolveAttack(from, true);
            busy = false;
            if (!CheckGameOver()) RefreshUI();
        }

        void TryPlayerMove(BoardSlot from, BoardSlot to)
        {
            if (mana < moveCost) { Log($"Moving a servant costs {moveCost} mana."); return; }
            StartCoroutine(MoveRoutine(from, to, true));
        }

        /// <summary>Moves the servant in 'from' to 'to'; if 'to' holds a servant they swap. Both count as moved.</summary>
        IEnumerator MoveRoutine(BoardSlot from, BoardSlot to, bool isPlayer)
        {
            busy = true;
            Deselect();

            CardInstance a = from.Occupant;
            CardInstance b = to.Occupant;
            if (a == null || a.view == null) { busy = false; yield break; }

            if (isPlayer) mana -= moveCost; else enemyManaLeft -= moveCost;

            Vector3 aStart = a.view.visual.position;
            Vector3 bStart = b != null && b.view != null ? b.view.visual.position : Vector3.zero;
            from.Occupant = null;
            to.Occupant = null;

            to.Hold(a.view);
            a.movedThisTurn = true;
            a.view.FlyFrom(aStart, 0f, -4f);
            if (b != null && b.view != null)
            {
                from.Hold(b.view);
                b.movedThisTurn = true;
                b.view.FlyFrom(bStart, 0f, 4f);
            }

            string owner = isPlayer ? "" : "Enemy ";
            Log(b != null
                ? $"{owner}{a.data.cardName} and {b.data.cardName} swapped lanes."
                : $"{owner}{a.data.cardName} moved to the {LaneNames[to.lane]} lane.");
            RefreshUI();

            // Entering a lane can spring the opponent's trap there.
            BoardSlot[] opposingTraps = isPlayer ? enemyTrapSlots : playerTrapSlots;
            DeckPile myGrave = isPlayer ? playerGrave : enemyGrave;
            DeckPile theirGrave = isPlayer ? enemyGrave : playerGrave;
            yield return SpringTrapIfAny(opposingTraps[to.lane], theirGrave, to, myGrave);
            if (b != null && from.Occupant == b)
                yield return SpringTrapIfAny(opposingTraps[from.lane], theirGrave, from, myGrave);

            busy = false;
            CheckGameOver();
            RefreshUI();
        }

        // ================================================================ combat

        /// <summary>One attack straight down a lane. The defender may pay to block with their servant in that lane.</summary>
        IEnumerator ResolveAttack(BoardSlot attackerSlot, bool attackerIsPlayer)
        {
            CardInstance a = attackerSlot.Occupant;
            if (a == null || a.view == null) yield break;
            while (a.view.IsAnimating) yield return null;
            a.attackedThisTurn = true;

            int lane = attackerSlot.lane;
            BoardSlot defSlot = (attackerIsPlayer ? enemyServantSlots : playerServantSlots)[lane];
            CardInstance b = defSlot.Occupant;
            int defenderMana = attackerIsPlayer ? enemyManaLeft : mana;

            bool blocked = false;
            if (b != null && b.view != null && defenderMana >= blockCost)
            {
                if (attackerIsPlayer)
                {
                    blocked = EnemyWantsToBlock(a, b);
                    Log(blocked ? $"The enemy pays {blockCost} mana to block with {b.data.cardName}!"
                                : "The enemy doesn't block.");
                    yield return new WaitForSeconds(0.45f);
                }
                else
                {
                    yield return AskPlayerToBlock(a, b, defSlot);
                    blocked = blockChoice == 1;
                }

                if (blocked)
                {
                    if (attackerIsPlayer) enemyManaLeft -= blockCost; else mana -= blockCost;
                    RefreshUI();
                }
            }

            if (blocked)
            {
                yield return a.view.AttackNudge(b.view.visual.position);
                // Both deal damage at the same time; the defending player takes none.
                b.health -= a.attack;
                a.health -= b.attack;
                a.view.Refresh();
                b.view.Refresh();
                Log($"{a.data.cardName} and {b.data.cardName} clash.");

                if (b.IsDead) SendToGrave(defSlot, attackerIsPlayer ? enemyGrave : playerGrave);
                if (a.IsDead) SendToGrave(attackerSlot, attackerIsPlayer ? playerGrave : enemyGrave);
            }
            else
            {
                yield return a.view.AttackNudge((attackerIsPlayer ? enemyPortrait : playerPortrait).position);
                if (attackerIsPlayer) enemyHealth -= a.attack;
                else playerHealth -= a.attack;
                Log(attackerIsPlayer ? $"{a.data.cardName} hits the enemy for {a.attack}." : $"{a.data.cardName} hits you for {a.attack}.");
            }

            RefreshUI();
            yield return new WaitForSeconds(attackPause);
        }

        IEnumerator AskPlayerToBlock(CardInstance attacker, CardInstance blocker, BoardSlot blockerSlot)
        {
            blockChoice = -1;
            blockerSlot.SetHighlight(true);
            if (blocker.view) blocker.view.Selected = true;
            Log($"Enemy {attacker.data.cardName} ({attacker.attack}/{attacker.health}) attacks your {LaneNames[blockerSlot.lane]} lane. " +
                $"Block with {blocker.data.cardName} ({blocker.attack}/{blocker.health}) for {blockCost} mana?");

            if (blockButton == null || skipBlockButton == null) blockChoice = 0; // no prompt available
            else ShowBlockButtons(true);

            while (blockChoice < 0) yield return null;

            ShowBlockButtons(false);
            blockerSlot.SetHighlight(false);
            if (blocker.view) blocker.view.Selected = false;
        }

        void ShowBlockButtons(bool on)
        {
            if (blockButton) blockButton.gameObject.SetActive(on);
            if (skipBlockButton) skipBlockButton.gameObject.SetActive(on);
        }

        /// <summary>If trapSlot holds a trap, it springs on the servant that just entered servantSlot.</summary>
        IEnumerator SpringTrapIfAny(BoardSlot trapSlot, DeckPile trapOwnerGrave, BoardSlot servantSlot, DeckPile servantOwnerGrave)
        {
            CardInstance trap = trapSlot.Occupant;
            CardInstance victim = servantSlot.Occupant;
            if (trap == null || victim == null || trap.view == null || victim.view == null) yield break;

            while (victim.view.IsAnimating) yield return null; // let it land first

            trap.view.SetFaceUp(true);
            Log($"Trap! {trap.data.cardName} hits {victim.data.cardName} for {trap.attack}.");
            yield return trap.view.Pulse();
            yield return trap.view.AttackNudge(victim.view.visual.position);

            victim.health -= trap.attack;
            victim.view.Refresh();

            SendToGrave(trapSlot, trapOwnerGrave);
            if (victim.IsDead) SendToGrave(servantSlot, servantOwnerGrave);
            yield return new WaitForSeconds(0.5f);
        }

        void SendToGrave(BoardSlot slot, DeckPile grave)
        {
            CardInstance card = slot.Occupant;
            slot.Occupant = null;
            if (card == null) return;
            if (card.view == null) { ToGrave(card, grave); return; }
            card.view.SetFaceUp(true);
            card.view.FlyToAndDestroy(grave.TopWorldPosition, () => ToGrave(card, grave));
        }

        void ToGrave(CardInstance card, DeckPile grave)
        {
            card.ResetStats();
            grave.AddToTop(card);
            RefreshUI();
        }

        // ================================================================ enemy AI

        /// <summary>
        /// Plays the most expensive card it can afford, keeping 1 mana back for blocking once it has a servant out.
        /// Servants go opposite your servants when possible (to block), otherwise into any empty lane.
        /// Traps go opposite lanes where you have no servant yet (where you're likely to play next).
        /// </summary>
        IEnumerator EnemyPlayRoutine()
        {
            int played = 0;
            bool placedOne = true;
            while (placedOne && !gameOver)
            {
                placedOne = false;
                enemyHand.Sort((x, y) => y.data.cost.CompareTo(x.data.cost));
                int reserve = HasServant(enemyServantSlots) ? blockCost : 0;

                foreach (CardInstance card in enemyHand)
                {
                    if (card.data.cost > enemyManaLeft - reserve) continue;

                    BoardSlot slot = card.IsServant ? ChooseEnemyServantSlot() : ChooseEnemyTrapSlot();
                    if (slot == null) continue;

                    enemyManaLeft -= card.data.cost;
                    enemyHand.Remove(card);
                    card.summonedThisTurn = true;

                    CardView view = Instantiate(cardPrefab, slot.transform);
                    view.Setup(card, card.IsServant, OnCardClicked); // enemy traps stay face-down
                    slot.Hold(view);
                    view.FlyFrom(enemyDeck.TopWorldPosition, 0f, 8f);

                    Log(card.IsTrap ? "Enemy set a trap." : $"Enemy summoned {card.data.cardName}.");
                    RefreshUI();
                    yield return new WaitForSeconds(enemyPlayDelay);

                    if (card.IsServant)
                        yield return SpringTrapIfAny(playerTrapSlots[slot.lane], playerGrave, slot, enemyGrave);

                    played++;
                    placedOne = true;
                    break; // hand changed, re-sort and look again
                }
            }

            if (played == 0) Log("Enemy played nothing.");
        }

        /// <summary>Attacks with every ready servant, unless it would just die to a blocker you can afford.</summary>
        IEnumerator EnemyAttackRoutine()
        {
            for (int lane = 0; lane < Lanes; lane++)
            {
                BoardSlot slot = enemyServantSlots[lane];
                CardInstance a = slot.Occupant;
                if (a == null || a.view == null || WhyCantAttack(a, false) != null) continue;

                CardInstance b = playerServantSlots[lane].Occupant;
                bool badTrade = b != null && mana >= blockCost && b.attack >= a.health && b.health > a.attack;
                if (badTrade) continue;

                Log($"Enemy {a.data.cardName} attacks your {LaneNames[lane]} lane!");
                yield return new WaitForSeconds(0.35f);
                yield return ResolveAttack(slot, false);
                if (CheckGameOver()) yield break;
            }
        }

        bool EnemyWantsToBlock(CardInstance attacker, CardInstance blocker)
        {
            bool lethal = attacker.attack >= enemyHealth;
            bool blockerSurvives = blocker.health > attacker.attack;
            bool killsAttacker = blocker.attack >= attacker.health;
            return lethal || blockerSurvives || killsAttacker;
        }

        static bool HasServant(BoardSlot[] slots)
        {
            foreach (BoardSlot s in slots) if (!s.IsEmpty) return true;
            return false;
        }

        BoardSlot ChooseEnemyServantSlot()
        {
            var empty = new List<BoardSlot>();
            var blocking = new List<BoardSlot>();
            for (int lane = 0; lane < Lanes; lane++)
            {
                if (!enemyServantSlots[lane].IsEmpty) continue;
                empty.Add(enemyServantSlots[lane]);
                if (!playerServantSlots[lane].IsEmpty) blocking.Add(enemyServantSlots[lane]);
            }
            List<BoardSlot> pickFrom = blocking.Count > 0 ? blocking : empty;
            return pickFrom.Count == 0 ? null : pickFrom[UnityEngine.Random.Range(0, pickFrom.Count)];
        }

        BoardSlot ChooseEnemyTrapSlot()
        {
            var empty = new List<BoardSlot>();
            var preferred = new List<BoardSlot>();
            for (int lane = 0; lane < Lanes; lane++)
            {
                if (!enemyTrapSlots[lane].IsEmpty) continue;
                empty.Add(enemyTrapSlots[lane]);
                if (playerServantSlots[lane].IsEmpty) preferred.Add(enemyTrapSlots[lane]);
            }
            List<BoardSlot> pickFrom = preferred.Count > 0 ? preferred : empty;
            return pickFrom.Count == 0 ? null : pickFrom[UnityEngine.Random.Range(0, pickFrom.Count)];
        }

        // ================================================================ end of game

        bool CheckGameOver()
        {
            if (gameOver) return true;
            if (enemyHealth > 0 && playerHealth > 0) return false;

            gameOver = true;
            busy = false;
            playerTurnActive = false;
            Deselect();
            ShowBlockButtons(false);
            endTurnButton.interactable = false;
            Log(enemyHealth <= 0 ? "You win! Press Restart to play again." : "You lose. Press Restart to try again.");
            RefreshUI();
            return true;
        }

        // ================================================================ UI

        void RefreshUI()
        {
            if (playerHealthCounter) playerHealthCounter.Show(Mathf.Max(0, playerHealth));
            else if (playerHealthText) playerHealthText.text = Mathf.Max(0, playerHealth).ToString();
            if (enemyHealthCounter) enemyHealthCounter.Show(Mathf.Max(0, enemyHealth));
            else if (enemyHealthText) enemyHealthText.text = Mathf.Max(0, enemyHealth).ToString();
            if (playerMana) playerMana.Show(mana, manaPerTurn);
            if (enemyMana) enemyMana.Show(enemyManaLeft, manaPerTurn);
            if (enemyHandText) enemyHandText.text = $"HAND {enemyHand.Count}";

            foreach (CardInstance c in hand)
                if (c.view) c.view.SetPlayable(!gameOver && c.data.cost <= mana);

            playerDeck.SetHighlight(false);
            playerDeck.SetLabel($"DECK\n({playerDeck.Count})");
            enemyDeck.SetHighlight(false);
            enemyDeck.SetLabel($"DECK\n({enemyDeck.Count})");
            playerGrave.SetHighlight(false);
            playerGrave.SetLabel($"GRAVE\n({playerGrave.Count})");
            enemyGrave.SetHighlight(false);
            enemyGrave.SetLabel($"GRAVE\n({enemyGrave.Count})");
        }

        void Log(string message)
        {
            if (logText) logText.text = message;
            Debug.Log("[CardGame] " + message);
        }

        // ================================================================ setup helpers

        static void AddClickArea(RectTransform target, Action onClick)
        {
            if (target == null) return;
            Image img = target.GetComponent<Image>();
            if (img == null) { img = target.gameObject.AddComponent<Image>(); img.color = new Color(1f, 1f, 1f, 0f); }
            img.raycastTarget = true;
            ClickArea area = target.GetComponent<ClickArea>();
            if (area == null) area = target.gameObject.AddComponent<ClickArea>();
            area.Clicked += onClick;
        }

        static Button CloneButton(Button source, string name, string label, float yOffset)
        {
            if (source == null) return null;
            Button b = Instantiate(source, source.transform.parent);
            b.name = name;
            b.onClick.RemoveAllListeners();
            b.interactable = true;
            var rt = (RectTransform)b.transform;
            rt.anchoredPosition = ((RectTransform)source.transform).anchoredPosition + new Vector2(0f, yOffset);
            TMP_Text text = b.GetComponentInChildren<TMP_Text>();
            if (text) text.text = label;
            b.gameObject.SetActive(false);
            return b;
        }
    }
}

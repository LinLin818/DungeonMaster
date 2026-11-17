using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame
{
    /// <summary>
    /// Runs a lane-based match (left / mid / right).
    ///
    /// Your turn
    ///   1. Click your deck once per turn: you draw until you hold 3 cards (hand limit is 3).
    ///   2. Click a card in your hand to select it, then click a glowing slot to place it:
    ///      servants go in SERVANT slots, traps go face-down in TRAP slots.
    ///   3. Press End turn.
    ///
    /// Combat (end of each turn)
    ///   Each servant attacks straight across its lane. If an enemy servant is there they
    ///   damage each other; if the lane is empty the servant hits the enemy hero.
    ///
    /// Traps
    ///   When a servant is placed in a lane, the opposing trap in that lane springs:
    ///   it deals its attack value to that servant, then goes to the grave.
    ///
    /// Dead servants and used traps fly to their owner's grave pile.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public const int Lanes = 3;

        [Header("Cards")]
        public List<CardData> cardPool = new List<CardData>();
        public int copiesPerCard = 2;
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
        public ManaBar playerMana;
        public ManaBar enemyMana;
        public TMP_Text enemyHandText;

        [Header("UI")]
        public TMP_Text logText;
        public Button endTurnButton;
        public Button restartButton;

        [Header("Rules")]
        public int startingHealth = 20;
        public int handLimit = 3;
        public int maxManaCap = 5;

        [Header("Timing")]
        public float drawStagger = 0.14f;
        public float enemyPlayDelay = 0.45f;
        public float attackPause = 0.2f;

        readonly List<CardInstance> hand = new List<CardInstance>();
        readonly List<CardInstance> enemyHand = new List<CardInstance>();

        int playerHealth, enemyHealth, mana, maxMana, enemyManaLeft;
        bool drewThisTurn, busy, gameOver;
        CardView selected;

        // ================================================================ lifecycle

        void Start()
        {
            playerDeck.Clicked += OnPlayerDeckClicked;
            foreach (BoardSlot s in AllSlots()) s.Clicked += OnSlotClicked;
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
            maxMana = mana = enemyManaLeft = 1;
            gameOver = false;
            drewThisTurn = true; // opening hand counts as this turn's draw

            playerDeck.Fill(cardPool, copiesPerCard);
            enemyDeck.Fill(cardPool, copiesPerCard);
            playerGrave.Clear();
            enemyGrave.Clear();

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
            endTurnButton.interactable = true;
            StartGame();
        }

        IEnumerator OpeningRoutine()
        {
            busy = true;
            RefreshUI();
            yield return null; // let destroyed objects disappear and layouts settle

            Log("Drawing your opening hand...");
            yield return PlayerDrawRoutine();
            EnemyDraw();

            busy = false;
            Log("Click a card, then click a glowing slot to place it.");
            RefreshUI();
        }

        // ================================================================ drawing

        void OnPlayerDeckClicked()
        {
            if (busy || gameOver) return;
            if (drewThisTurn) { Log("You already drew this turn."); return; }
            if (hand.Count >= handLimit) { Log($"Your hand is full ({handLimit} cards max)."); return; }
            if (playerDeck.Count == 0) { Log("Your deck is empty."); return; }
            StartCoroutine(DrawButtonRoutine());
        }

        IEnumerator DrawButtonRoutine()
        {
            busy = true;
            drewThisTurn = true;
            Deselect();
            int before = hand.Count;
            yield return PlayerDrawRoutine();
            int drawn = hand.Count - before;
            busy = false;
            Log($"You drew {drawn} card{(drawn == 1 ? "" : "s")}.");
            RefreshUI();
        }

        /// <summary>Draws until the hand holds handLimit cards, each flying from the deck.</summary>
        IEnumerator PlayerDrawRoutine()
        {
            while (hand.Count < handLimit && playerDeck.Count > 0)
            {
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

        void EnemyDraw()
        {
            while (enemyHand.Count < handLimit && enemyDeck.Count > 0)
                enemyHand.Add(enemyDeck.Draw());
            RefreshUI();
        }

        // ================================================================ selecting and placing

        void OnCardClicked(CardView view)
        {
            if (busy || gameOver || !view.InHand) return;

            if (selected == view) { Deselect(); RefreshUI(); return; }

            CardInstance card = view.Card;
            if (card.data.cost > mana) { Log($"Not enough mana for {card.data.cardName}."); return; }

            Deselect();
            selected = view;
            view.Selected = true;

            int open = 0;
            foreach (BoardSlot s in SlotsFor(card, true))
                if (s.IsEmpty) { s.SetHighlight(true); open++; }

            Log(open == 0
                ? $"No empty {(card.IsTrap ? "trap" : "servant")} slot for {card.data.cardName}."
                : $"Place {card.data.cardName} in a glowing {(card.IsTrap ? "trap" : "servant")} slot.");
        }

        void Deselect()
        {
            if (selected != null) selected.Selected = false;
            selected = null;
            foreach (BoardSlot s in AllSlots()) s.SetHighlight(false);
        }

        BoardSlot[] SlotsFor(CardInstance card, bool playerSide)
        {
            if (playerSide) return card.IsTrap ? playerTrapSlots : playerServantSlots;
            return card.IsTrap ? enemyTrapSlots : enemyServantSlots;
        }

        void OnSlotClicked(BoardSlot slot)
        {
            if (busy || gameOver) return;
            if (selected == null) { Log("Select a card in your hand first."); return; }

            CardInstance card = selected.Card;
            if (!slot.isPlayerSide || !slot.IsEmpty || slot.kind != card.data.type)
            {
                Log(card.IsTrap ? "Traps go in your empty TRAP slots." : "Servants go in your empty SERVANT slots.");
                return;
            }
            StartCoroutine(PlayerPlaceRoutine(selected, slot));
        }

        IEnumerator PlayerPlaceRoutine(CardView view, BoardSlot slot)
        {
            busy = true;
            CardInstance card = view.Card;
            Deselect();

            mana -= card.data.cost;
            hand.Remove(card);

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
            if (card == null || card.view == null) { if (card != null) grave.AddToTop(card); return; }
            card.view.SetFaceUp(true);
            card.view.FlyToAndDestroy(grave.TopWorldPosition, () => grave.AddToTop(card));
        }

        // ================================================================ turns

        void OnEndTurnPressed()
        {
            if (busy || gameOver) return;
            StartCoroutine(EndTurnRoutine());
        }

        IEnumerator EndTurnRoutine()
        {
            busy = true;
            Deselect();
            endTurnButton.interactable = false;
            RefreshUI();

            // --- your servants attack
            Log("Your servants attack!");
            yield return LaneCombat(playerServantSlots, enemyServantSlots, enemyPortrait, playerGrave, enemyGrave, false);
            if (CheckGameOver()) yield break;

            // --- enemy turn
            Log("Enemy turn...");
            yield return new WaitForSeconds(0.4f);
            EnemyDraw();
            enemyManaLeft = maxMana;
            RefreshUI();
            yield return EnemyPlayRoutine();
            if (CheckGameOver()) yield break;

            yield return new WaitForSeconds(0.3f);
            yield return LaneCombat(enemyServantSlots, playerServantSlots, playerPortrait, enemyGrave, playerGrave, true);
            if (CheckGameOver()) yield break;

            // --- back to you
            maxMana = Mathf.Min(maxManaCap, maxMana + 1);
            mana = maxMana;
            drewThisTurn = false;
            busy = false;
            endTurnButton.interactable = true;
            Log("Your turn. Click your deck to draw up to 3 cards.");
            RefreshUI();
        }

        /// <summary>Every servant attacks straight across its lane, left to right.</summary>
        IEnumerator LaneCombat(BoardSlot[] attackers, BoardSlot[] defenders, RectTransform defendingHero,
                               DeckPile attackerGrave, DeckPile defenderGrave, bool defenderIsPlayer)
        {
            for (int lane = 0; lane < Lanes; lane++)
            {
                CardInstance attacker = attackers[lane].Occupant;
                if (attacker == null || attacker.view == null) continue;
                while (attacker.view.IsAnimating) yield return null;

                CardInstance target = defenders[lane].Occupant;
                if (target != null && target.view != null)
                {
                    yield return attacker.view.AttackNudge(target.view.visual.position);

                    target.health -= attacker.attack;
                    attacker.health -= target.attack;
                    target.view.Refresh();
                    attacker.view.Refresh();

                    if (target.IsDead) SendToGrave(defenders[lane], defenderGrave);
                    if (attacker.IsDead) SendToGrave(attackers[lane], attackerGrave);
                }
                else
                {
                    yield return attacker.view.AttackNudge(defendingHero.position);
                    if (defenderIsPlayer) playerHealth -= attacker.attack;
                    else enemyHealth -= attacker.attack;
                }

                RefreshUI();
                yield return new WaitForSeconds(attackPause);
                if (playerHealth <= 0 || enemyHealth <= 0) yield break;
            }
        }

        // ================================================================ enemy AI

        /// <summary>
        /// Simple AI: keeps playing the most expensive card it can afford.
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
                enemyHand.Sort((a, b) => b.data.cost.CompareTo(a.data.cost));

                foreach (CardInstance card in enemyHand)
                {
                    if (card.data.cost > enemyManaLeft) continue;

                    BoardSlot slot = card.IsServant ? ChooseEnemyServantSlot() : ChooseEnemyTrapSlot();
                    if (slot == null) continue;

                    enemyManaLeft -= card.data.cost;
                    enemyHand.Remove(card);

                    CardView view = Instantiate(cardPrefab, slot.transform);
                    view.Setup(card, card.IsServant, null); // enemy traps stay face-down
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
            return pickFrom.Count == 0 ? null : pickFrom[Random.Range(0, pickFrom.Count)];
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
            return pickFrom.Count == 0 ? null : pickFrom[Random.Range(0, pickFrom.Count)];
        }

        // ================================================================ end of game

        bool CheckGameOver()
        {
            if (gameOver) return true;
            if (enemyHealth > 0 && playerHealth > 0) return false;

            gameOver = true;
            busy = false;
            Deselect();
            endTurnButton.interactable = false;
            Log(enemyHealth <= 0 ? "You win! Press Restart to play again." : "You lose. Press Restart to try again.");
            RefreshUI();
            return true;
        }

        // ================================================================ UI

        void RefreshUI()
        {
            if (playerHealthText) playerHealthText.text = Mathf.Max(0, playerHealth).ToString();
            if (enemyHealthText) enemyHealthText.text = Mathf.Max(0, enemyHealth).ToString();
            if (playerMana) playerMana.Show(mana, maxMana);
            if (enemyMana) enemyMana.Show(enemyManaLeft, maxMana);
            if (enemyHandText) enemyHandText.text = $"HAND {enemyHand.Count}";

            foreach (CardInstance c in hand)
                if (c.view) c.view.SetPlayable(!gameOver && c.data.cost <= mana);

            bool canDraw = !drewThisTurn && !busy && !gameOver && playerDeck.Count > 0 && hand.Count < handLimit;
            playerDeck.SetHighlight(canDraw);
            playerDeck.SetLabel(canDraw ? $"DRAW\n({playerDeck.Count})" : $"DECK\n({playerDeck.Count})");
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
    }
}

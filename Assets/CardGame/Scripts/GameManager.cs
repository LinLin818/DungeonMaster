using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame
{
    /// <summary>
    /// Runs a match following the Servant Card Game rules and the starter-deck card list.
    ///
    /// Setup
    ///   Each player gets one element deck (Fire, Grass or Water; random by default), 2 copies of each card.
    ///   Both start with startingHealth HP. The first player draws 4, the second draws 5.
    ///   The first player can't attack on their first turn.
    ///
    /// Each turn
    ///   1. Refresh: discard your hand, draw 5 (not on your very first turn), mana refills to 5.
    ///   2. Main phase, in any order:
    ///        summon a servant / set a trap ..... card cost   (click a hand card, then a glowing slot)
    ///        cast a spell ...................... card cost   (click it, then a glowing target; or click it
    ///                                                          again if it has no target)
    ///        move or swap a servant ............ 1 mana      (click your servant, then another servant slot)
    ///        attack ............................ free        (click your servant, then the enemy slot in
    ///                                                          its lane or the enemy portrait)
    ///   3. End turn.
    ///
    /// Servants can't attack the turn they're summoned (unless they have BLAZE) or moved, and attack once
    /// per turn. Combat stays in its lane. Blocking is automatic and free: a servant in that lane always
    /// blocks (both deal damage to each other, no damage to the player); an empty lane means the player is hit.
    ///
    /// Keywords: BLAZE (attack the turn it's summoned), TORRENT (can't attack on its next turn),
    /// OVERGROW (+1 HP at the start of each of your turns and when summoned).
    /// Traps fire on their own in their lane: "summon" traps when an enemy servant is summoned there,
    /// "attack" traps when an enemy servant attacks there (before damage). Used traps and cast spells go to
    /// the graveyard. An empty deck is refilled by shuffling the graveyard.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public const int Lanes = 3;
        static readonly string[] LaneNames = { "LEFT", "MID", "RIGHT" };

        public enum FirstTurn { Random, Player, Enemy }
        public enum DeckElement { Random, Fire, Grass, Water }

        [Header("Cards")]
        [Tooltip("Every card in the game. Decks are built from it by element (CardData.element).")]
        public List<CardData> cardPool = new List<CardData>();
        [Tooltip("Rules: no more than 2 copies of any card.")]
        [Range(1, 2)] public int copiesPerCard = 2;
        [Tooltip("Which element deck you play. Random picks one each game.")]
        public DeckElement playerDeckElement = DeckElement.Random;
        [Tooltip("Which element deck the enemy plays. Random picks one different from yours when possible.")]
        public DeckElement enemyDeckElement = DeckElement.Random;
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

        [Header("Rules")]
        [Range(10, 20)] public int startingHealth = 20;
        public int handSize = 5;
        public int firstPlayerOpeningHand = 4;
        public int secondPlayerOpeningHand = 5;
        public int manaPerTurn = 5;
        public int moveCost = 1;
        public FirstTurn firstTurn = FirstTurn.Random;

        [Header("Timing")]
        public float drawStagger = 0.14f;
        public float enemyPlayDelay = 0.45f;
        public float attackPause = 0.35f;

        readonly List<CardInstance> hand = new List<CardInstance>();
        readonly List<CardInstance> enemyHand = new List<CardInstance>();

        int playerHealth, enemyHealth;
        int mana, enemyManaLeft;        // what each player has left right now
        int playerTurns, enemyTurns;    // turns started so far
        bool playerGoesFirst, playerTurnActive, busy, gameOver;
        string playerElement, enemyElement;   // deck element for this game (null = whole pool)
        CardView selected;              // a card in your hand, or one of your servants on the board
        BoardSlot spellSourceSlot;      // Riptide: the enemy servant picked, waiting for a destination lane
        readonly List<CardView> castViews = new List<CardView>(); // spells shown mid-board / flying to the grave
        bool attackCancelled;           // set by attack traps
        BoardSlot choosingAllyFor;      // Fern: the servant whose "give an ally OVERGROW" is waiting for your click
        BoardSlot chosenAllySlot;

        // ================================================================ lifecycle

        void Start()
        {
            playerDeck.Clicked += OnPlayerDeckClicked;
            foreach (BoardSlot s in AllSlots()) s.Clicked += OnSlotClicked;
            AddClickArea(enemyPortrait, OnEnemyPortraitClicked);
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

        IEnumerable<BoardSlot> AllServantSlots()
        {
            foreach (BoardSlot s in playerServantSlots) yield return s;
            foreach (BoardSlot s in enemyServantSlots) yield return s;
        }

        void StartGame()
        {
            hand.Clear();
            enemyHand.Clear();
            selected = null;
            spellSourceSlot = null;
            choosingAllyFor = chosenAllySlot = null;
            playerHealth = enemyHealth = startingHealth;
            mana = enemyManaLeft = 0;
            playerTurns = enemyTurns = 0;
            gameOver = false;
            playerTurnActive = false;
            playerGoesFirst = firstTurn == FirstTurn.Player || (firstTurn == FirstTurn.Random && UnityEngine.Random.value < 0.5f);

            // Each side plays one element deck (Fill with 1 copy per entry; the list already holds the copies).
            playerElement = PickElement(playerDeckElement, null);
            enemyElement = PickElement(enemyDeckElement, playerElement);
            playerDeck.Fill(BuildElementDeck(playerElement), 1);
            enemyDeck.Fill(BuildElementDeck(enemyElement), 1);
            playerGrave.Clear();
            enemyGrave.Clear();

            StartCoroutine(OpeningRoutine());
        }

        /// <summary>Resolves a deck choice to an element name. Random avoids 'avoid' when another element exists.</summary>
        string PickElement(DeckElement choice, string avoid)
        {
            if (choice != DeckElement.Random) return choice.ToString();

            var elements = new List<string>();
            foreach (CardData c in cardPool)
                if (c != null && !string.IsNullOrEmpty(c.element) && !elements.Exists(e => SameElement(e, c.element)))
                    elements.Add(c.element);
            if (elements.Count == 0) return null; // no elements set: use the whole pool

            List<string> options = elements.FindAll(e => !SameElement(e, avoid));
            if (options.Count == 0) options = elements;
            return options[UnityEngine.Random.Range(0, options.Count)];
        }

        static bool SameElement(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>copiesPerCard (max 2) of every pool card of this element, shuffled. Null element = whole pool.</summary>
        List<CardData> BuildElementDeck(string element)
        {
            int copies = Mathf.Clamp(copiesPerCard, 1, 2);
            var deck = new List<CardData>();
            var seen = new HashSet<CardData>();
            foreach (CardData c in cardPool)
            {
                if (c == null || !seen.Add(c)) continue; // skip empty and repeated pool entries
                if (element != null && !SameElement(c.element, element)) continue;
                for (int i = 0; i < copies; i++) deck.Add(c);
            }

            if (deck.Count == 0)
            {
                Debug.LogWarning($"[CardGame] No cards with element '{element}' in the Card Pool; using the whole pool.");
                return BuildElementDeck(null);
            }

            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
            return deck;
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
            // Destroying a cast card also stops its flight, so it never lands in the new game's graveyard.
            foreach (CardView v in castViews) if (v) Destroy(v.gameObject);
            castViews.Clear();
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
            string decks = $"Your {playerElement ?? "mixed"} deck vs the enemy's {enemyElement ?? "mixed"} deck.";
            Log(playerGoesFirst
                ? $"{decks} You go first: you draw {mine}, the enemy draws {theirs}."
                : $"{decks} The enemy goes first: you draw {mine}, the enemy draws {theirs}.");

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
            StartOwnTurn(playerServantSlots);

            busy = false;
            playerTurnActive = true;
            endTurnButton.interactable = true;
            Log(FirstTurnNoAttack(true)
                ? "Your turn. Play cards and set up. (The first player can't attack on their first turn.)"
                : "Your turn. Play cards, then click a servant to attack or move it.");
            RefreshUI();
        }

        /// <summary>Start of a side's turn: per-turn flags reset, TORRENT / Entangle land, OVERGROW grows.</summary>
        void StartOwnTurn(BoardSlot[] servantSlots)
        {
            var grown = new List<string>();
            foreach (BoardSlot s in servantSlots)
            {
                CardInstance c = s.Occupant;
                if (c == null) continue;
                c.StartOwnTurn();
                if (c.hasOvergrow) { c.GainHealth(1); grown.Add(c.data.cardName); }
                if (c.view) c.view.Refresh();
            }
            if (grown.Count > 0) Debug.Log("[CardGame] OVERGROW +1 HP: " + string.Join(", ", grown));
        }

        void EndOwnTurn(BoardSlot[] servantSlots)
        {
            foreach (BoardSlot s in servantSlots)
            {
                CardInstance c = s.Occupant;
                if (c == null) continue;
                c.EndOwnTurn();
                if (c.view) c.view.Refresh();
            }
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
            EndOwnTurn(playerServantSlots);

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
            StartOwnTurn(enemyServantSlots);
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

            EndOwnTurn(enemyServantSlots);
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
            if (choosingAllyFor != null) { if (!view.InHand) OnAllyChoiceClicked(SlotOf(view.Card)); return; }
            if (busy || gameOver || !playerTurnActive) return;

            if (view.InHand) { OnHandCardClicked(view); return; }

            BoardSlot slot = SlotOf(view.Card);
            if (slot == null) return;

            // Your own servant: select it (or unselect it), unless a spell is waiting for a target
            // or another servant is waiting to move here.
            bool spellSelected = selected != null && selected.InHand && selected.Card.IsSpell;
            if (!spellSelected && slot.isPlayerSide && view.Card.IsServant && (selected == null || selected.InHand || selected == view))
            {
                if (selected == view) { Deselect(); RefreshUI(); }
                else SelectBoardServant(view);
                return;
            }
            OnSlotClicked(slot);
        }

        void OnHandCardClicked(CardView view)
        {
            CardInstance card = view.Card;

            if (selected == view)
            {
                // Clicking a selected no-target spell again casts it.
                if (card.IsSpell && !card.data.SpellNeedsTarget) { StartCoroutine(CastSpellRoutine(card, view, true, null, null)); return; }
                Deselect();
                RefreshUI();
                return;
            }

            if (card.data.cost > mana) { Log($"Not enough mana for {card.data.cardName} (costs {card.data.cost})."); return; }

            Deselect();
            selected = view;
            view.Selected = true;

            if (card.IsSpell)
            {
                if (!card.data.SpellNeedsTarget) { Log($"{card.data.cardName}: {card.data.description} Click it again to cast."); return; }
                int targets = HighlightSpellTargets(card.data, true);
                Log(targets == 0
                    ? $"{card.data.cardName} has no target right now."
                    : $"{card.data.cardName}: {card.data.description} Click a glowing servant.");
                return;
            }

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
            int cost = MoveCostOf(c);
            bool canMove = mana >= cost && !c.cannotMoveThisTurn;
            if (canMove)
                foreach (BoardSlot s in playerServantSlots)
                    if (s != from) s.SetHighlight(true);

            string attackPart = why == null
                ? "Click the glowing enemy slot or the enemy portrait to attack."
                : $"It can't attack: {why}.";
            string movePart = c.cannotMoveThisTurn ? "It's entangled and can't move."
                : mana >= cost ? $"Click another servant slot to move it ({(cost == 0 ? "free" : cost + " mana")})."
                : "No mana left to move it.";
            Log($"{c.data.cardName} ({c.attack}/{c.health}){Statuses(c)}. {attackPart} {movePart}");
        }

        static string Statuses(CardInstance c)
        {
            var s = new List<string>();
            if (c.HasBlazeNow) s.Add("BLAZE");
            if (c.hasOvergrow) s.Add("OVERGROW");
            if (c.shielded) s.Add("shielded");
            if (c.torrentPending) s.Add("TORRENT next turn");
            if (c.entanglePending) s.Add("entangled next turn");
            return s.Count == 0 ? "" : " [" + string.Join(", ", s) + "]";
        }

        void Deselect()
        {
            if (selected != null) selected.Selected = false;
            selected = null;
            spellSourceSlot = null;
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
            if (c.cannotAttackThisTurn) return "it's held by TORRENT or Entangle this turn";
            if (c.summonedThisTurn && !c.HasBlazeNow) return "it was summoned this turn";
            if (c.movedThisTurn) return "it moved this turn";
            if (c.attackedThisTurn) return "it already attacked this turn";
            return null;
        }

        // ================================================================ player actions

        void OnSlotClicked(BoardSlot slot)
        {
            if (choosingAllyFor != null) { OnAllyChoiceClicked(slot); return; }
            if (busy || gameOver || !playerTurnActive) return;
            if (selected == null) { Log("Select a card in your hand or one of your servants first."); return; }

            CardInstance card = selected.Card;

            // ---- a spell in your hand is waiting for a target
            if (selected.InHand && card.IsSpell) { OnSpellTargetClicked(slot); return; }

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

            if (card.IsServant) yield return OnServantSummoned(slot, true);

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
            CardInstance a = from.Occupant, b = to.Occupant;
            if (mana < MoveCostOf(a)) { Log($"Moving {a.data.cardName} costs {MoveCostOf(a)} mana."); return; }
            if (a.cannotMoveThisTurn) { Log($"{a.data.cardName} is entangled and can't move this turn."); return; }
            if (b != null && b.cannotMoveThisTurn) { Log($"{b.data.cardName} is entangled and can't be swapped this turn."); return; }
            StartCoroutine(PlayerMoveRoutine(from, to));
        }

        /// <summary>Mana to move this servant: moveCost, or 0 for "Moves lanes for 0 mana" (School of Fish).</summary>
        int MoveCostOf(CardInstance c) => c != null && c.data.freeMove ? 0 : moveCost;

        /// <summary>Your move: pay the mover's cost; if 'to' holds a servant they swap. Both count as moved.</summary>
        IEnumerator PlayerMoveRoutine(BoardSlot from, BoardSlot to)
        {
            busy = true;
            Deselect();

            CardInstance a = from.Occupant;
            CardInstance b = to.Occupant;
            mana -= MoveCostOf(a);
            MoveServant(from, to);
            a.movedThisTurn = true;
            if (b != null) b.movedThisTurn = true;

            Log(b != null
                ? $"{a.data.cardName} and {b.data.cardName} swapped lanes."
                : $"{a.data.cardName} moved to the {LaneNames[to.lane]} lane.");
            RefreshUI();
            yield return new WaitForSeconds(0.35f);

            busy = false;
            RefreshUI();
        }

        /// <summary>Moves the servant in 'from' to 'to'; a servant already in 'to' swaps into 'from'. No cost, no flags.</summary>
        void MoveServant(BoardSlot from, BoardSlot to)
        {
            CardInstance a = from.Occupant;
            CardInstance b = to.Occupant;
            if (a == null || a.view == null) return;

            Vector3 aStart = a.view.visual.position;
            Vector3 bStart = b != null && b.view != null ? b.view.visual.position : Vector3.zero;
            from.Occupant = null;
            to.Occupant = null;

            to.Hold(a.view);
            a.view.FlyFrom(aStart, 0f, -4f);
            if (b != null && b.view != null)
            {
                from.Hold(b.view);
                b.view.FlyFrom(bStart, 0f, 4f);
            }
        }

        // ================================================================ spells

        /// <summary>Lights up every servant this spell may target. Returns how many.</summary>
        int HighlightSpellTargets(CardData spell, bool casterIsPlayer)
        {
            int n = 0;
            foreach (BoardSlot s in AllServantSlots())
                if (IsValidSpellTarget(spell, s, casterIsPlayer)) { s.SetHighlight(true); n++; }
            return n;
        }

        bool IsValidSpellTarget(CardData spell, BoardSlot slot, bool casterIsPlayer)
        {
            if (slot == null || slot.kind != CardType.Servant) return false;
            CardInstance t = slot.Occupant;
            if (t == null || t.view == null) return false;
            bool targetIsEnemy = slot.isPlayerSide != casterIsPlayer;
            if (spell.SpellTargetsEnemyOnly && !targetIsEnemy) return false;
            if (spell.effect == CardEffect.BlazeAndAttackThisTurn && targetIsEnemy) return false; // "this turn" only matters for your own
            return true;
        }

        void OnSpellTargetClicked(BoardSlot slot)
        {
            CardInstance spell = selected.Card;
            CardData d = spell.data;

            if (!d.SpellNeedsTarget) { Log($"Click {d.cardName} again to cast it."); return; }

            // Riptide, second click: the lane to move the enemy servant to.
            if (d.effect == CardEffect.MoveEnemyServant && spellSourceSlot != null)
            {
                if (slot.isPlayerSide || slot.kind != CardType.Servant || slot == spellSourceSlot)
                {
                    Log("Pick another enemy SERVANT slot to move it to.");
                    return;
                }
                StartCoroutine(CastSpellRoutine(spell, selected, true, spellSourceSlot, slot));
                return;
            }

            if (!IsValidSpellTarget(d, slot, true))
            {
                Log(d.SpellTargetsEnemyOnly ? "Pick a glowing enemy servant."
                    : d.effect == CardEffect.BlazeAndAttackThisTurn ? "Pick one of your servants." : "Pick a glowing servant.");
                return;
            }

            if (d.effect == CardEffect.MoveEnemyServant)
            {
                spellSourceSlot = slot;
                foreach (BoardSlot s in AllSlots()) s.SetHighlight(false);
                foreach (BoardSlot s in enemyServantSlots) if (s != slot) s.SetHighlight(true);
                Log($"Move {slot.Occupant.data.cardName} to which lane? Click a glowing enemy slot (taken slots swap).");
                return;
            }

            StartCoroutine(CastSpellRoutine(spell, selected, true, slot, null));
        }

        /// <summary>Pays for a spell, shows it in the middle of the board, applies it and sends it to the graveyard.</summary>
        IEnumerator CastSpellRoutine(CardInstance spell, CardView view, bool byPlayer, BoardSlot target, BoardSlot dest)
        {
            busy = true;
            if (byPlayer) Deselect();

            if (byPlayer) { mana -= spell.data.cost; hand.Remove(spell); }
            else { enemyManaLeft -= spell.data.cost; enemyHand.Remove(spell); }
            RefreshUI();

            CardView shown = ShowCastCard(spell, view, byPlayer);
            Log($"{(byPlayer ? "You cast" : "The enemy casts")} {spell.data.cardName}: {spell.data.description}");
            yield return new WaitForSeconds(0.9f);

            yield return ApplySpell(spell, byPlayer, target, dest);

            DeckPile grave = byPlayer ? playerGrave : enemyGrave;
            if (shown) shown.FlyToAndDestroy(grave.TopWorldPosition, () => ToGrave(spell, grave));
            else ToGrave(spell, grave);

            ResolveDeaths();
            RefreshUI();
            yield return new WaitForSeconds(0.45f);

            if (byPlayer) busy = false; // the enemy's turn stays busy until it ends
            CheckGameOver();
            RefreshUI();
        }

        /// <summary>Puts the spell card face up in the middle of the board (from the hand, or from the enemy deck).</summary>
        CardView ShowCastCard(CardInstance spell, CardView view, bool byPlayer)
        {
            Transform canvasT = playerHand.parent;
            Vector3 from;
            if (view == null)
            {
                view = Instantiate(cardPrefab, canvasT);
                view.Setup(spell, true, null);
                from = enemyDeck.TopWorldPosition;
            }
            else
            {
                from = view.visual.position;
                view.InHand = false;
                view.Selected = false;
                view.SetPlayable(true);
                view.transform.SetParent(canvasT, false);
            }

            var rt = (RectTransform)view.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one * 1.15f;
            view.transform.SetAsLastSibling();
            if (view.canvasGroup) view.canvasGroup.blocksRaycasts = false;
            view.FlyFrom(from, 0f, byPlayer ? -6f : 6f);
            castViews.RemoveAll(v => v == null); // already landed and destroyed
            castViews.Add(view);
            return view;
        }

        IEnumerator ApplySpell(CardInstance spell, bool byPlayer, BoardSlot target, BoardSlot dest)
        {
            CardData d = spell.data;
            int amount = d.effectAmount;
            CardInstance t = target != null ? target.Occupant : null;
            BoardSlot[] mine = byPlayer ? playerServantSlots : enemyServantSlots;
            BoardSlot[] theirs = byPlayer ? enemyServantSlots : playerServantSlots;

            if (d.SpellNeedsTarget && (t == null || t.view == null))
            {
                Log($"{d.cardName} fizzles: its target is gone.");
                yield break;
            }

            switch (d.effect)
            {
                case CardEffect.DamageServant:
                {
                    yield return t.view.Pulse();
                    int dealt = DealDamage(t, amount);
                    Log(dealt > 0 ? $"{d.cardName} deals {dealt} damage to {t.data.cardName}." : $"{t.data.cardName}'s shield blocks {d.cardName}.");
                    break;
                }
                case CardEffect.BlazeAndAttackThisTurn:
                    t.attack += amount;
                    t.attackBonusThisTurn += amount;
                    t.blazeThisTurn = true;
                    t.view.Refresh();
                    yield return t.view.Pulse();
                    Log($"{t.data.cardName} gains BLAZE and +{amount} ATK this turn.");
                    break;

                case CardEffect.DamageEnemyPlayer:
                    yield return new WaitForSeconds(0.2f);
                    if (byPlayer) enemyHealth -= amount; else playerHealth -= amount;
                    Log(byPlayer ? $"{d.cardName} hits the enemy for {amount}." : $"{d.cardName} hits you for {amount}.");
                    break;

                case CardEffect.TorrentAllEnemies:
                {
                    int n = 0;
                    foreach (BoardSlot s in theirs)
                        if (s.Occupant != null) { s.Occupant.torrentPending = true; n++; }
                    Log(n == 0 ? $"{d.cardName} washes over an empty board." : $"TORRENT! {n} servant{(n == 1 ? "" : "s")} can't attack on their next turn.");
                    break;
                }
                case CardEffect.MoveEnemyServant:
                    if (dest == null) break;
                    MoveServant(target, dest);
                    Log($"{t.data.cardName} is swept to the {LaneNames[dest.lane]} lane.");
                    yield return new WaitForSeconds(0.45f);
                    break;

                case CardEffect.ShieldServant:
                    t.shielded = true;
                    yield return t.view.Pulse();
                    Log($"{t.data.cardName} is shielded: the next damage it takes is prevented.");
                    break;

                case CardEffect.EntangleEnemy:
                    t.entanglePending = true;
                    yield return t.view.Pulse();
                    Log($"{t.data.cardName} is entangled: it can't move or attack on its next turn.");
                    break;

                case CardEffect.BuffServant:
                    t.attack += amount;
                    t.GainHealth(amount);
                    t.view.Refresh();
                    yield return t.view.Pulse();
                    Log($"{t.data.cardName} gains +{amount} ATK and +{amount} HP.");
                    break;

                case CardEffect.HealAllies:
                {
                    int healed = 0;
                    foreach (BoardSlot s in mine)
                    {
                        CardInstance c = s.Occupant;
                        if (c == null) continue;
                        healed += c.Heal(amount);
                        if (c.view) c.view.Refresh();
                    }
                    Log(healed == 0 ? $"{d.cardName}: nothing to heal." : $"{d.cardName} heals {healed} HP across your servants.");
                    break;
                }
                default:
                    Log($"{d.cardName} has no effect set.");
                    break;
            }
            RefreshUI();
        }

        // ================================================================ summoning and traps

        /// <summary>
        /// A servant just entered the board: OVERGROW grows, its "On summon" ability goes off,
        /// then the opposing summon trap in that lane may fire.
        /// </summary>
        IEnumerator OnServantSummoned(BoardSlot slot, bool byPlayer)
        {
            CardInstance s = slot.Occupant;
            if (s == null) yield break;
            if (s.view) while (s.view.IsAnimating) yield return null; // let it land first

            if (s.hasOvergrow)
            {
                s.GainHealth(1);
                if (s.view) s.view.Refresh();
            }
            if (s.HasBlazeNow && !FirstTurnNoAttack(byPlayer))
                Log($"{s.data.cardName} has BLAZE: it can attack right away.");

            CardData d = s.data;
            if (d.onSummon != ServantAbility.None)
            {
                if (d.onSummon == ServantAbility.GiveAllyOvergrow && byPlayer)
                    yield return PlayerChooseAllyForOvergrow(slot);
                else
                    UseAbility(d.onSummon, d.onSummonAmount, s, slot.lane, byPlayer, "On summon");
                ResolveDeaths();
                RefreshUI();
                yield return new WaitForSeconds(0.6f);
            }

            if (slot.Occupant == s) yield return SummonTraps(slot, byPlayer);
            ResolveDeaths();
        }

        // ================================================================ servant abilities

        /// <summary>
        /// Applies a servant ability right away. 'lane' and 'ownerIsPlayer' describe where the servant is (or was,
        /// for "When slain"). GiveAllyOvergrow picks the sturdiest ally here (the enemy's pick; you choose yours).
        /// </summary>
        void UseAbility(ServantAbility ability, int amount, CardInstance source, int lane, bool ownerIsPlayer, string when)
        {
            BoardSlot[] mine = ownerIsPlayer ? playerServantSlots : enemyServantSlots;
            BoardSlot[] theirs = ownerIsPlayer ? enemyServantSlots : playerServantSlots;
            CardInstance laneEnemy = theirs[lane].Occupant;
            string name = source.data.cardName;

            switch (ability)
            {
                case ServantAbility.DamageLaneEnemy:
                    if (laneEnemy == null) { Log($"{when}: {name} has no lane enemy to hit."); break; }
                    int dealt = DealDamage(laneEnemy, amount);
                    PulseView(laneEnemy);
                    Log(dealt > 0 ? $"{when}: {name} deals {dealt} damage to {laneEnemy.data.cardName}."
                                  : $"{when}: {name} fires, but {laneEnemy.data.cardName}'s shield blocks it.");
                    break;

                case ServantAbility.TorrentLaneEnemy:
                    if (laneEnemy == null) { Log($"{when}: {name} has no lane enemy to TORRENT."); break; }
                    laneEnemy.torrentPending = true;
                    PulseView(laneEnemy);
                    Log($"{when}: {name} hits {laneEnemy.data.cardName} with TORRENT. It can't attack on its next turn.");
                    break;

                case ServantAbility.TorrentAllEnemies:
                {
                    int n = 0;
                    foreach (BoardSlot s in theirs)
                        if (s.Occupant != null) { s.Occupant.torrentPending = true; PulseView(s.Occupant); n++; }
                    Log(n == 0 ? $"{when}: {name}'s TORRENT finds no enemies."
                               : $"{when}: {name} hits {n} enem{(n == 1 ? "y" : "ies")} with TORRENT. They can't attack on their next turn.");
                    break;
                }
                case ServantAbility.HealAllies:
                {
                    int healed = 0;
                    foreach (BoardSlot s in mine)
                    {
                        CardInstance c = s.Occupant;
                        if (c == null || c == source) continue;
                        int h = c.Heal(amount);
                        if (h > 0) { healed += h; PulseView(c); }
                        if (c.view) c.view.Refresh();
                    }
                    Log(healed == 0 ? $"{when}: {name} has no hurt allies to heal." : $"{when}: {name} heals its allies for {healed} HP.");
                    break;
                }
                case ServantAbility.BuffAllies:
                {
                    int n = 0;
                    foreach (BoardSlot s in mine)
                    {
                        CardInstance c = s.Occupant;
                        if (c == null || c == source) continue;
                        c.attack += amount;
                        c.GainHealth(amount);
                        if (c.view) c.view.Refresh();
                        PulseView(c);
                        n++;
                    }
                    Log(n == 0 ? $"{when}: {name} has no allies to empower."
                               : $"{when}: {name} gives {n} all{(n == 1 ? "y" : "ies")} +{amount} ATK and +{amount} HP.");
                    break;
                }
                case ServantAbility.GiveAllyOvergrow:
                {
                    BoardSlot pick = Best(mine, c => c == source || c.hasOvergrow ? -1 : c.attack + c.health);
                    if (pick == null) { Log($"{when}: {name} has no ally to give OVERGROW."); break; }
                    GiveOvergrow(pick.Occupant, name, when);
                    break;
                }
            }
            RefreshUI();
        }

        void GiveOvergrow(CardInstance ally, string sourceName, string when)
        {
            ally.hasOvergrow = true;
            PulseView(ally);
            Log($"{when}: {sourceName} gives {ally.data.cardName} OVERGROW (+1 HP at the start of each of its owner's turns).");
        }

        /// <summary>Fern (yours): highlight your other servants and wait for you to click one.</summary>
        IEnumerator PlayerChooseAllyForOvergrow(BoardSlot sourceSlot)
        {
            CardInstance source = sourceSlot.Occupant;
            var options = new List<BoardSlot>();
            foreach (BoardSlot s in playerServantSlots)
                if (s != sourceSlot && s.Occupant != null && s.Occupant.view != null) options.Add(s);

            if (options.Count == 0) { Log($"On summon: {source.data.cardName} has no ally to give OVERGROW."); yield break; }

            BoardSlot chosen;
            if (options.Count == 1) chosen = options[0];
            else
            {
                chosenAllySlot = null;
                choosingAllyFor = sourceSlot;
                foreach (BoardSlot s in options) s.SetHighlight(true);
                Log($"{source.data.cardName}: click one of your glowing servants to give it OVERGROW.");
                while (chosenAllySlot == null) yield return null;
                chosen = chosenAllySlot;
                choosingAllyFor = null;
                chosenAllySlot = null;
                foreach (BoardSlot s in AllSlots()) s.SetHighlight(false);
            }
            GiveOvergrow(chosen.Occupant, source.data.cardName, "On summon");
            RefreshUI();
        }

        void OnAllyChoiceClicked(BoardSlot slot)
        {
            if (slot == null || !slot.isPlayerSide || slot.kind != CardType.Servant || slot == choosingAllyFor || slot.Occupant == null)
            {
                Log("Click one of your glowing servants to give it OVERGROW.");
                return;
            }
            chosenAllySlot = slot;
        }

        void PulseView(CardInstance c)
        {
            if (c != null && c.view != null && c.view.isActiveAndEnabled) c.view.StartCoroutine(c.view.Pulse());
        }

        IEnumerator SummonTraps(BoardSlot servantSlot, bool servantIsPlayer)
        {
            BoardSlot trapSlot = (servantIsPlayer ? enemyTrapSlots : playerTrapSlots)[servantSlot.lane];
            CardInstance trap = trapSlot.Occupant;
            CardInstance victim = servantSlot.Occupant;
            if (trap == null || victim == null || trap.view == null || victim.view == null || !trap.data.IsSummonTrap) yield break;

            while (victim.view.IsAnimating) yield return null; // let it land first
            yield return RevealTrap(trap);

            if (trap.data.effect == CardEffect.TrapDamageOnSummon)
            {
                yield return trap.view.AttackNudge(victim.view.visual.position);
                int dealt = DealDamage(victim, trap.data.effectAmount);
                Log(dealt > 0 ? $"Trap! {trap.data.cardName} hits {victim.data.cardName} for {dealt}."
                              : $"Trap! {trap.data.cardName} fires, but {victim.data.cardName}'s shield blocks it.");
            }
            else
            {
                victim.torrentPending = true;
                Log($"Trap! {trap.data.cardName}: {victim.data.cardName} is hit by TORRENT and can't attack on its next turn.");
            }

            SendToGrave(trapSlot, servantIsPlayer ? enemyGrave : playerGrave);
            ResolveDeaths();
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>Fires the defender's attack trap in this lane (before damage). Sets attackCancelled.</summary>
        IEnumerator AttackTraps(BoardSlot attackerSlot, bool attackerIsPlayer)
        {
            attackCancelled = false;
            int lane = attackerSlot.lane;
            BoardSlot trapSlot = (attackerIsPlayer ? enemyTrapSlots : playerTrapSlots)[lane];
            CardInstance trap = trapSlot.Occupant;
            CardInstance a = attackerSlot.Occupant;
            if (trap == null || trap.view == null || a == null || a.view == null || !trap.data.IsAttackTrap) yield break;

            yield return RevealTrap(trap);
            int amount = trap.data.effectAmount;

            switch (trap.data.effect)
            {
                case CardEffect.TrapDamageOnAttack:
                {
                    yield return trap.view.AttackNudge(a.view.visual.position);
                    int dealt = DealDamage(a, amount);
                    Log(dealt > 0 ? $"Trap! {trap.data.cardName} hits {a.data.cardName} for {dealt}."
                                  : $"Trap! {trap.data.cardName} fires, but {a.data.cardName}'s shield blocks it.");
                    break;
                }
                case CardEffect.TrapCancelAttack:
                    attackCancelled = true;
                    Log($"Trap! {trap.data.cardName} cancels {a.data.cardName}'s attack.");
                    break;

                case CardEffect.TrapThornwall:
                {
                    CardInstance defender = (attackerIsPlayer ? enemyServantSlots : playerServantSlots)[lane].Occupant;
                    if (defender != null)
                    {
                        defender.GainHealth(1);
                        if (defender.view) defender.view.Refresh();
                    }
                    yield return trap.view.AttackNudge(a.view.visual.position);
                    int dealt = DealDamage(a, amount);
                    Log($"Trap! {trap.data.cardName}: " +
                        (defender != null ? $"{defender.data.cardName} gets +1 HP, " : "") +
                        (dealt > 0 ? $"{a.data.cardName} takes {dealt}." : $"{a.data.cardName}'s shield blocks the thorns."));
                    break;
                }
            }

            SendToGrave(trapSlot, attackerIsPlayer ? enemyGrave : playerGrave);
            ResolveDeaths();
            if (a.IsDead) attackCancelled = true;
            yield return new WaitForSeconds(0.5f);
        }

        IEnumerator RevealTrap(CardInstance trap)
        {
            trap.view.SetFaceUp(true);
            yield return trap.view.Pulse();
        }

        // ================================================================ combat and damage

        /// <summary>
        /// One attack straight down a lane. The defender's attack trap fires first. Then a servant in that
        /// lane always blocks (free, automatic); an empty lane means the attack hits the defending player.
        /// </summary>
        IEnumerator ResolveAttack(BoardSlot attackerSlot, bool attackerIsPlayer)
        {
            CardInstance a = attackerSlot.Occupant;
            if (a == null || a.view == null) yield break;
            while (a.view.IsAnimating) yield return null;
            a.attackedThisTurn = true;

            yield return AttackTraps(attackerSlot, attackerIsPlayer);
            if (attackCancelled || attackerSlot.Occupant != a || a.view == null)
            {
                RefreshUI();
                yield return new WaitForSeconds(attackPause);
                yield break;
            }

            int lane = attackerSlot.lane;
            BoardSlot defSlot = (attackerIsPlayer ? enemyServantSlots : playerServantSlots)[lane];
            CardInstance b = defSlot.Occupant;

            if (b != null && b.view != null)
            {
                yield return a.view.AttackNudge(b.view.visual.position);
                // Both deal damage at the same time; the defending player takes none.
                int toB = DealDamage(b, a.attack, inBattle: true);
                int toA = DealDamage(a, b.attack, inBattle: true);
                Log($"{b.data.cardName} blocks {a.data.cardName}: {a.data.cardName} takes {toA}, {b.data.cardName} takes {toB}.");
                ResolveDeaths();
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

        /// <summary>
        /// Damages a servant. In battle, its Battle Damage Reduction (Coral Golem) applies first;
        /// then a Bubble Shield soaks whatever is left. Returns the damage actually taken.
        /// </summary>
        int DealDamage(CardInstance c, int amount, bool inBattle = false)
        {
            if (c == null) return 0;
            if (inBattle) amount -= c.data.battleDamageReduction;
            if (amount <= 0) return 0;
            if (c.shielded)
            {
                c.shielded = false;
                return 0;
            }
            c.health -= amount;
            if (c.view) c.view.Refresh();
            return amount;
        }

        /// <summary>
        /// Sends every servant at 0 HP to its owner's graveyard and fires their "When slain" abilities.
        /// Servants that die together leave together, so a "lane enemy" that died at the same time isn't hit.
        /// Repeats while abilities keep killing things.
        /// </summary>
        void ResolveDeaths()
        {
            for (int round = 0; round < 10; round++)
            {
                var dead = new List<(BoardSlot slot, CardInstance card)>();
                foreach (BoardSlot s in AllServantSlots())
                    if (s.Occupant != null && s.Occupant.IsDead) dead.Add((s, s.Occupant));
                if (dead.Count == 0) return;

                foreach (var (slot, card) in dead)
                    SendToGrave(slot, slot.isPlayerSide ? playerGrave : enemyGrave);

                foreach (var (slot, card) in dead)
                    if (card.data.whenSlain != ServantAbility.None)
                        UseAbility(card.data.whenSlain, card.data.whenSlainAmount, card, slot.lane, slot.isPlayerSide, "When slain");
            }
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
        /// Keeps playing the most expensive card that's worth playing until nothing else fits.
        /// Servants go opposite your servants when possible (to block), otherwise into any empty lane.
        /// Summon traps go where you have no servant yet; attack traps go where you do.
        /// Spells are cast only when they have a sensible target (see PlanEnemySpell).
        /// </summary>
        IEnumerator EnemyPlayRoutine()
        {
            int played = 0;
            bool placedOne = true;
            while (placedOne && !gameOver)
            {
                placedOne = false;
                enemyHand.Sort((x, y) => y.data.cost.CompareTo(x.data.cost));

                foreach (CardInstance card in enemyHand)
                {
                    if (card.data.cost > enemyManaLeft) continue;

                    if (card.IsSpell)
                    {
                        if (!PlanEnemySpell(card.data, out BoardSlot target, out BoardSlot dest)) continue;
                        yield return CastSpellRoutine(card, null, false, target, dest);
                    }
                    else
                    {
                        BoardSlot slot = card.IsServant ? ChooseEnemyServantSlot() : ChooseEnemyTrapSlot(card.data);
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

                        if (card.IsServant) yield return OnServantSummoned(slot, false);
                    }

                    played++;
                    placedOne = true;
                    break; // hand changed, re-sort and look again
                }
            }

            if (played == 0) Log("Enemy played nothing.");
        }

        /// <summary>Decides whether a spell is worth casting now, and on what.</summary>
        bool PlanEnemySpell(CardData d, out BoardSlot target, out BoardSlot dest)
        {
            target = null;
            dest = null;
            switch (d.effect)
            {
                case CardEffect.DamageServant:
                    // Prefer a servant it can finish off, the strongest first.
                    target = Best(playerServantSlots, c => !c.shielded && c.health <= d.effectAmount ? 100 + c.attack : c.attack);
                    return target != null;

                case CardEffect.BlazeAndAttackThisTurn:
                    if (FirstTurnNoAttack(false)) return false;
                    // A servant that could attack this turn with BLAZE, ideally into an empty lane.
                    target = Best(enemyServantSlots, c =>
                        c.attackedThisTurn || c.movedThisTurn || c.cannotAttackThisTurn ? -1 :
                        (playerServantSlots[SlotOf(c).lane].IsEmpty ? 100 : 0) + c.attack);
                    return target != null;

                case CardEffect.DamageEnemyPlayer:
                    return true;

                case CardEffect.TorrentAllEnemies:
                    return CountServants(playerServantSlots) >= 2;

                case CardEffect.MoveEnemyServant:
                    // Drag your strongest servant in front of one of its servants that would kill it.
                    for (int i = 0; i < Lanes; i++)
                    {
                        CardInstance p = playerServantSlots[i].Occupant;
                        if (p == null) continue;
                        for (int j = 0; j < Lanes; j++)
                        {
                            CardInstance e = enemyServantSlots[j].Occupant;
                            if (j == i || e == null || e.attack < p.health) continue;
                            if (target == null || p.attack > target.Occupant.attack) { target = playerServantSlots[i]; dest = playerServantSlots[j]; }
                        }
                    }
                    return target != null; // both slots are on your (the caster's enemy's) side

                case CardEffect.ShieldServant:
                    target = Best(enemyServantSlots, c => c.shielded ? -1 : c.attack + c.health);
                    return target != null;

                case CardEffect.EntangleEnemy:
                    target = Best(playerServantSlots, c => c.entanglePending ? -1 : c.attack);
                    return target != null;

                case CardEffect.BuffServant:
                    target = Best(enemyServantSlots, c => c.attack + c.health);
                    return target != null;

                case CardEffect.HealAllies:
                {
                    int missing = 0;
                    foreach (BoardSlot s in enemyServantSlots)
                        if (s.Occupant != null) missing += s.Occupant.maxHealth - s.Occupant.health;
                    return missing >= 2;
                }
            }
            return false;
        }

        /// <summary>The slot whose servant scores highest (scores below 0 are skipped). Null if none.</summary>
        static BoardSlot Best(BoardSlot[] slots, Func<CardInstance, int> score)
        {
            BoardSlot best = null;
            int bestScore = -1;
            foreach (BoardSlot s in slots)
            {
                CardInstance c = s.Occupant;
                if (c == null || c.view == null) continue;
                int v = score(c);
                if (v > bestScore) { bestScore = v; best = s; }
            }
            return best;
        }

        static int CountServants(BoardSlot[] slots)
        {
            int n = 0;
            foreach (BoardSlot s in slots) if (!s.IsEmpty) n++;
            return n;
        }

        /// <summary>Attacks with every ready servant, unless your blocker in that lane would kill it and survive.</summary>
        IEnumerator EnemyAttackRoutine()
        {
            for (int lane = 0; lane < Lanes; lane++)
            {
                BoardSlot slot = enemyServantSlots[lane];
                CardInstance a = slot.Occupant;
                if (a == null || a.view == null || WhyCantAttack(a, false) != null) continue;

                CardInstance b = playerServantSlots[lane].Occupant;
                int toA = b != null ? b.attack - a.data.battleDamageReduction : 0;
                int toB = b != null ? a.attack - b.data.battleDamageReduction : 0;
                bool badTrade = b != null && !a.shielded && toA >= a.health && (b.shielded || b.health > toB);
                if (badTrade) continue;

                Log($"Enemy {a.data.cardName} attacks your {LaneNames[lane]} lane!");
                yield return new WaitForSeconds(0.35f);
                yield return ResolveAttack(slot, false);
                if (CheckGameOver()) yield break;
            }
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

        BoardSlot ChooseEnemyTrapSlot(CardData trap)
        {
            var empty = new List<BoardSlot>();
            var preferred = new List<BoardSlot>();
            for (int lane = 0; lane < Lanes; lane++)
            {
                if (!enemyTrapSlots[lane].IsEmpty) continue;
                empty.Add(enemyTrapSlots[lane]);
                bool youHaveServant = !playerServantSlots[lane].IsEmpty;
                // Summon traps wait where you'll summon next; attack traps wait where you'll attack from.
                if (trap.IsAttackTrap ? youHaveServant : !youHaveServant) preferred.Add(enemyTrapSlots[lane]);
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
    }
}

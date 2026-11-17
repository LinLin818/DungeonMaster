using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardGame
{
    /// <summary>
    /// A face-down deck shown on the side of the table.
    /// The stack of card backs shrinks as cards are drawn.
    /// Click it (player deck) to draw. Also used for the grave (discard) piles.
    /// </summary>
    public class DeckPile : MonoBehaviour, IPointerClickHandler
    {
        [Header("Visuals")]
        [Tooltip("Empty RectTransform the card backs are stacked inside.")]
        public RectTransform stackRoot;
        [Tooltip("A simple card-back image prefab.")]
        public GameObject cardBackPrefab;
        public TMP_Text label;
        [Tooltip("Glow shown when the player is allowed to draw.")]
        public GameObject highlight;

        [Header("Stack look")]
        public int maxBacksShown = 6;
        public int cardsPerBack = 3;
        public float stackOffset = 3f;

        /// <summary>Fired when the deck is clicked.</summary>
        public event Action Clicked;

        readonly List<CardInstance> cards = new List<CardInstance>();
        readonly List<GameObject> backs = new List<GameObject>();

        public int Count => cards.Count;

        /// <summary>World position of the top card, used as the start point when drawing.</summary>
        public Vector3 TopWorldPosition =>
            backs.Count > 0 ? backs[backs.Count - 1].transform.position : stackRoot.position;

        public void Fill(IEnumerable<CardData> pool, int copiesEach)
        {
            cards.Clear();
            foreach (CardData data in pool)
            {
                if (data == null) continue;
                for (int i = 0; i < copiesEach; i++) cards.Add(new CardInstance(data));
            }
            Shuffle();
            RefreshStack();
        }

        public void Shuffle()
        {
            // Fisher-Yates shuffle
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                CardInstance tmp = cards[i];
                cards[i] = cards[j];
                cards[j] = tmp;
            }
        }

        public CardInstance Draw()
        {
            if (cards.Count == 0) return null;
            CardInstance top = cards[0];
            cards.RemoveAt(0);
            RefreshStack();
            return top;
        }

        /// <summary>Puts a card on top of the pile (used for the grave).</summary>
        public void AddToTop(CardInstance card)
        {
            cards.Insert(0, card);
            RefreshStack();
        }

        public void Clear()
        {
            cards.Clear();
            RefreshStack();
        }

        public void SetHighlight(bool on)
        {
            if (highlight) highlight.SetActive(on);
        }

        public void SetLabel(string text)
        {
            if (label) label.text = text;
        }

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();

        void RefreshStack()
        {
            int wanted = cards.Count == 0 ? 0 : Mathf.Clamp(Mathf.CeilToInt(cards.Count / (float)cardsPerBack), 1, maxBacksShown);

            while (backs.Count < wanted)
                backs.Add(Instantiate(cardBackPrefab, stackRoot));

            while (backs.Count > wanted)
            {
                Destroy(backs[backs.Count - 1]);
                backs.RemoveAt(backs.Count - 1);
            }

            for (int i = 0; i < backs.Count; i++)
            {
                var rt = (RectTransform)backs[i].transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(i * stackOffset, i * stackOffset);
            }
        }
    }
}

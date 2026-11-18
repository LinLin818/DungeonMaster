using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardGame
{
    /// <summary>
    /// One place on the board that can hold a single card:
    /// a SERVANT slot or a TRAP slot, in lane 0 (left), 1 (mid) or 2 (right),
    /// on your side or the enemy's side.
    /// </summary>
    public class BoardSlot : MonoBehaviour, IPointerClickHandler
    {
        public CardType kind = CardType.Servant;
        [Range(0, 2)] public int lane;
        public bool isPlayerSide = true;

        [Tooltip("Glow shown when the selected card can be placed here.")]
        public GameObject highlight;

        [Tooltip("Size of a card sitting in this slot (1 = same size as in your hand).")]
        public float cardScale = 1f;

        /// <summary>The card sitting in this slot, or null.</summary>
        public CardInstance Occupant { get; set; }
        public bool IsEmpty => Occupant == null;

        public event Action<BoardSlot> Clicked;

        public void SetHighlight(bool on)
        {
            if (highlight) highlight.SetActive(on);
        }

        /// <summary>Parents a card's view to this slot and centres it.</summary>
        public void Hold(CardView view)
        {
            var rt = (RectTransform)view.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one * cardScale;
            Occupant = view.Card;
        }

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(this);
    }
}

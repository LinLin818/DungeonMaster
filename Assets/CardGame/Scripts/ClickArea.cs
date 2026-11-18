using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardGame
{
    /// <summary>Raises Clicked when this UI object is clicked (needs a Graphic with Raycast Target on).</summary>
    public class ClickArea : MonoBehaviour, IPointerClickHandler
    {
        public event Action Clicked;
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
    }
}

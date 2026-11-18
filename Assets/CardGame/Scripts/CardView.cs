using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGame
{
    /// <summary>
    /// The visual card (UI prefab).
    ///
    /// Prefab layout:
    ///   Card (this script, an invisible Image for clicks, CanvasGroup)
    ///     Visual            &lt;- the part that moves / flies / lifts on hover
    ///       SelectedGlow    (shown while the card is selected in your hand)
    ///       Front           (name, cost, art, type, attack, health)
    ///       Back            (card back, shown when face down)
    ///
    /// The root object snaps to its new place (hand layout or board slot) instantly.
    /// The "Visual" child then animates from where the card used to be, which gives
    /// the "card flying onto the table" effect.
    /// </summary>
    public class CardView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Animated child")]
        public RectTransform visual;

        [Header("Front")]
        public GameObject front;
        public TMP_Text nameText;
        public TMP_Text costText;
        public TMP_Text typeText;
        public TMP_Text attackText;
        public TMP_Text healthText;
        public TMP_Text descriptionText;
        public GameObject healthBox;
        public Image artImage;

        [Tooltip("Image on the Front object (the frame). Found automatically if left empty.")]
        public Image frameImage;
        [Tooltip("Dark panel inside the frame (Front/Face). Hidden when the card has its own Base sprite. Found automatically if left empty.")]
        public Image faceBackground;

        [Header("Back")]
        public GameObject back;

        [Header("Misc")]
        public GameObject selectedGlow;
        public CanvasGroup canvasGroup;
        public float hoverLift = 24f;
        public float hoverScale = 1.12f;
        public float flyDuration = 0.5f;

        public CardInstance Card { get; private set; }
        public bool InHand { get; set; }
        public bool IsAnimating => animCount > 0;

        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                if (selectedGlow) selectedGlow.SetActive(value);
                if (!IsAnimating) RestPose();
            }
        }

        Action<CardView> onClick;
        int animCount;
        bool selected;
        bool hovered;
        Canvas onTopCanvas;
        Color healthDefaultColor = Color.white;

        // default frame look, restored for cards that have no Base sprite
        bool defaultsSaved;
        Sprite defaultFrameSprite;
        Image.Type defaultFrameType;
        Color defaultFrameColor;

        // ---------------------------------------------------------------- setup

        public void Setup(CardInstance card, bool faceUp, Action<CardView> clickHandler)
        {
            Card = card;
            card.view = this;
            onClick = clickHandler;
            if (healthText) healthDefaultColor = healthText.color;
            SaveFrameDefaults();
            if (selectedGlow) selectedGlow.SetActive(false);
            SetFaceUp(faceUp);
            Refresh();
        }

        public void SetFaceUp(bool faceUp)
        {
            if (front) front.SetActive(faceUp);
            if (back) back.SetActive(!faceUp);
        }

        public bool IsFaceUp => front == null || front.activeSelf;

        public void Refresh()
        {
            if (Card == null) return;
            CardData d = Card.data;
            if (nameText) nameText.text = d.cardName;
            if (costText) costText.text = d.cost.ToString();
            if (typeText) typeText.text = d.type == CardType.Trap ? "TRAP" : "SERVANT";
            if (attackText) attackText.text = Card.attack.ToString();
            if (healthBox) healthBox.SetActive(d.type == CardType.Servant);
            if (healthText)
            {
                healthText.text = Card.health.ToString();
                healthText.color = Card.health < d.health ? new Color(1f, 0.35f, 0.35f) : healthDefaultColor;
            }
            if (descriptionText) descriptionText.text = d.description;
            if (artImage)
            {
                artImage.sprite = d.artwork;
                artImage.color = d.artwork ? Color.white : d.artColor;
                artImage.type = d.artwork ? Image.Type.Simple : defaultFrameType;
                artImage.preserveAspect = d.artwork != null;
                // A card with only a Base sprite (picture already drawn on it) needs no art box.
                artImage.enabled = d.artwork != null || d.baseSprite == null;
            }

            // Your own card frame (the *_Base sprite) replaces the default frame and dark panel.
            bool customFrame = d.baseSprite != null;
            if (frameImage)
            {
                frameImage.sprite = customFrame ? d.baseSprite : defaultFrameSprite;
                frameImage.type = customFrame ? Image.Type.Simple : defaultFrameType;
                frameImage.color = customFrame ? Color.white : defaultFrameColor;
                frameImage.preserveAspect = customFrame;
            }
            if (faceBackground) faceBackground.enabled = !customFrame;
        }

        void SaveFrameDefaults()
        {
            if (defaultsSaved) return;
            defaultsSaved = true;

            // Works with prefabs made before these fields existed.
            if (frameImage == null && front != null) frameImage = front.GetComponent<Image>();
            if (faceBackground == null && front != null)
            {
                Transform face = front.transform.Find("Face");
                if (face != null) faceBackground = face.GetComponent<Image>();
            }

            if (frameImage)
            {
                defaultFrameSprite = frameImage.sprite;
                defaultFrameType = frameImage.type;
                defaultFrameColor = frameImage.color;
            }
            else defaultFrameType = Image.Type.Sliced;
        }

        /// <summary>Greys out cards you can't afford.</summary>
        public void SetPlayable(bool playable)
        {
            if (canvasGroup) canvasGroup.alpha = playable ? 1f : 0.5f;
        }

        // ---------------------------------------------------------------- input

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!IsAnimating) onClick?.Invoke(this);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            if (InHand && !IsAnimating) RestPose();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            if (InHand && !IsAnimating) RestPose();
        }

        /// <summary>Normal resting position: lifted if hovered/selected in hand, flat otherwise.</summary>
        void RestPose()
        {
            bool lifted = InHand && (hovered || selected);
            visual.localPosition = lifted ? new Vector3(0f, hoverLift, 0f) : Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one * (lifted ? hoverScale : 1f);
            SetOnTop(lifted);
        }

        // ---------------------------------------------------------------- animations

        /// <summary>
        /// Call right after the card was moved to its new place (hand or slot).
        /// The card's picture starts at worldStart and flies to its new spot.
        /// </summary>
        public void FlyFrom(Vector3 worldStart, float delay = 0f, float startRotationZ = 0f)
        {
            StartCoroutine(FlyRoutine(worldStart, delay, startRotationZ));
        }

        IEnumerator FlyRoutine(Vector3 worldStart, float delay, float startRotZ)
        {
            animCount++;
            SetOnTop(true);

            // Make a layout group (if any) place the root at its final slot right now.
            if (transform.parent is RectTransform parentRect)
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);

            visual.position = worldStart;
            visual.localRotation = Quaternion.Euler(0f, 0f, startRotZ);
            visual.localScale = Vector3.one * 0.95f;

            if (delay > 0f) yield return new WaitForSeconds(delay);

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / flyDuration;
                float e = EaseOutBack(Mathf.Clamp01(t));
                // Target is read every frame, so it stays correct if the layout shifts mid-flight.
                visual.position = Vector3.LerpUnclamped(worldStart, transform.position, e);
                visual.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(startRotZ, 0f, e));
                visual.localScale = Vector3.one * Mathf.LerpUnclamped(0.95f, 1f, e);
                yield return null;
            }

            animCount--;
            RestPose();
        }

        /// <summary>Quick lunge toward a target and back. Yield on it from a coroutine.</summary>
        public IEnumerator AttackNudge(Vector3 targetWorld, float duration = 0.3f)
        {
            animCount++;
            SetOnTop(true);
            Vector3 start = transform.position;
            Vector3 hit = Vector3.Lerp(start, targetWorld, 0.6f);
            float half = duration * 0.5f;

            for (float t = 0f; t < 1f; t += Time.deltaTime / half)
            {
                visual.position = Vector3.Lerp(start, hit, t * t);
                yield return null;
            }
            for (float t = 0f; t < 1f; t += Time.deltaTime / half)
            {
                visual.position = Vector3.Lerp(hit, transform.position, 1f - (1f - t) * (1f - t));
                yield return null;
            }

            animCount--;
            RestPose();
        }

        /// <summary>Grows and shrinks once (used when a trap springs).</summary>
        public IEnumerator Pulse(float duration = 0.35f)
        {
            animCount++;
            SetOnTop(true);
            for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
            {
                visual.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(t * Mathf.PI));
                yield return null;
            }
            animCount--;
            RestPose();
        }

        /// <summary>Flies the card to a point (e.g. the grave), then destroys it and calls onArrive.</summary>
        public void FlyToAndDestroy(Vector3 targetWorld, Action onArrive, float delay = 0.15f)
        {
            InHand = false;
            if (canvasGroup) canvasGroup.blocksRaycasts = false;
            if (Card != null && Card.view == this) Card.view = null;
            StartCoroutine(FlyToRoutine(targetWorld, onArrive, delay));
        }

        IEnumerator FlyToRoutine(Vector3 targetWorld, Action onArrive, float delay)
        {
            animCount++;
            SetOnTop(true);
            if (delay > 0f) yield return new WaitForSeconds(delay);

            Vector3 start = visual.position;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
            {
                float e = 1f - (1f - t) * (1f - t);
                visual.position = Vector3.Lerp(start, targetWorld, e);
                visual.localRotation = Quaternion.Euler(0f, 0f, e * 12f);
                visual.localScale = Vector3.one * Mathf.Lerp(1f, 0.9f, e);
                yield return null;
            }

            onArrive?.Invoke();
            Destroy(gameObject);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Draws this card above everything else (nested canvas with a high sorting order).</summary>
        void SetOnTop(bool on)
        {
            if (!on && onTopCanvas == null) return;
            if (onTopCanvas == null)
            {
                onTopCanvas = visual.GetComponent<Canvas>();
                if (onTopCanvas == null) onTopCanvas = visual.gameObject.AddComponent<Canvas>();
            }
            onTopCanvas.overrideSorting = on;
            onTopCanvas.sortingOrder = on ? 100 : 0;
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float p = t - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame
{
    /// <summary>
    /// Shows a number using pre-drawn sprites (e.g. the HP counter sheet: frames[0] = "0" ... frames[20] = "20").
    /// Values past the last frame fall back to the text (if set), otherwise show the last frame.
    /// </summary>
    public class SpriteCounter : MonoBehaviour
    {
        public Image image;
        [Tooltip("frames[i] is the sprite for the value i.")]
        public Sprite[] frames;
        [Tooltip("Optional. Used for values that have no sprite.")]
        public TMP_Text fallbackText;

        public void Show(int value)
        {
            if (value < 0) value = 0;
            bool hasFrame = frames != null && value < frames.Length && frames[value] != null;

            if (fallbackText)
            {
                fallbackText.gameObject.SetActive(!hasFrame);
                if (!hasFrame) fallbackText.text = value.ToString();
            }

            if (!image) return;
            if (hasFrame) image.sprite = frames[value];
            else if (!fallbackText && frames != null && frames.Length > 0) image.sprite = frames[frames.Length - 1];
            image.enabled = hasFrame || !fallbackText;
        }
    }
}

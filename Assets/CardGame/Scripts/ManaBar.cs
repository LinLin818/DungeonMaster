using UnityEngine;
using UnityEngine.UI;

namespace CardGame
{
    /// <summary>
    /// Shows a player's mana.
    /// Panel mode (used with the Counter_Mana sheet): panel shows panelSprites[current], i.e. "MANA 3/5".
    /// Gem mode (no panel set): a row of gems, bright = available, dim = spent this turn, dark = not unlocked yet.
    /// </summary>
    public class ManaBar : MonoBehaviour
    {
        [Header("Panel mode")]
        public Image panel;
        [Tooltip("panelSprites[i] is the panel showing i mana available (Counter_Mana_0 ... Counter_Mana_5).")]
        public Sprite[] panelSprites;

        [Header("Gem mode")]
        public Image[] gems;
        public Color available = new Color(0.35f, 0.65f, 1f);
        public Color spent = new Color(0.16f, 0.27f, 0.42f);
        public Color locked = new Color(0.12f, 0.11f, 0.15f);

        public void Show(int current, int max)
        {
            if (panel && panelSprites != null && panelSprites.Length > 0)
            {
                Sprite s = panelSprites[Mathf.Clamp(current, 0, panelSprites.Length - 1)];
                if (s) panel.sprite = s;
                return;
            }

            if (gems == null) return;
            for (int i = 0; i < gems.Length; i++)
            {
                if (gems[i] == null) continue;
                gems[i].color = i < current ? available : (i < max ? spent : locked);
            }
        }
    }
}

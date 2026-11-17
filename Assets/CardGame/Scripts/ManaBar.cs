using UnityEngine;
using UnityEngine.UI;

namespace CardGame
{
    /// <summary>A row of mana gems: bright = available, dim = spent this turn, dark = not unlocked yet.</summary>
    public class ManaBar : MonoBehaviour
    {
        public Image[] gems;
        public Color available = new Color(0.35f, 0.65f, 1f);
        public Color spent = new Color(0.16f, 0.27f, 0.42f);
        public Color locked = new Color(0.12f, 0.11f, 0.15f);

        public void Show(int current, int max)
        {
            for (int i = 0; i < gems.Length; i++)
            {
                if (gems[i] == null) continue;
                gems[i].color = i < current ? available : (i < max ? spent : locked);
            }
        }
    }
}

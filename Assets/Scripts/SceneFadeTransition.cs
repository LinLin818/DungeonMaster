using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneFadeTransition : MonoBehaviour
{
    [SerializeField] CanvasGroup blackScreen;
    [SerializeField] float fadeDuration = 0.45f;
    bool transitioning;

    void Awake()
    {
        blackScreen.alpha = 1f;
        blackScreen.blocksRaycasts = true;
    }

    IEnumerator Start()
    {
        yield return null;
        yield return Fade(1f, 0f);
        blackScreen.blocksRaycasts = false;
    }

    public void LoadScene(string sceneName)
    {
        if (!transitioning)
            StartCoroutine(FadeOutAndLoad(sceneName));
    }

    IEnumerator FadeOutAndLoad(string sceneName)
    {
        transitioning = true;
        blackScreen.blocksRaycasts = true;
        yield return Fade(blackScreen.alpha, 1f);
        SceneManager.LoadScene(sceneName);
    }

    IEnumerator Fade(float from, float to)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            blackScreen.alpha = Mathf.Lerp(from, to, elapsed / fadeDuration);
            yield return null;
        }
        blackScreen.alpha = to;
    }
}

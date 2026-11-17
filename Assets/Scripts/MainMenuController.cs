using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    [SerializeField] string gameSceneName = "Main";

    public void StartGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }
    public void PlayCardGame()
{
    UnityEngine.SceneManagement.SceneManager.LoadScene("CardGame");
}
}
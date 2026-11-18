using UnityEngine;

public class PrologueExitTrigger : MonoBehaviour
{
    [SerializeField] PrologueStoryController storyController;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.TryGetComponent(out SideScrollerPlayerController _))
            storyController.TryLeaveChapter();
    }
}

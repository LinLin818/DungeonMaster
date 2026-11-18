using UnityEngine;

public class PrologueStoryTrigger : MonoBehaviour
{
    [SerializeField] PrologueStoryController storyController;
    bool used;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (used || !other.TryGetComponent(out SideScrollerPlayerController _))
            return;

        if (storyController.BeginChapterConversation())
            used = true;
    }
}

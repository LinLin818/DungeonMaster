using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(RectTransform))]
public class MenuParallaxLayer : MonoBehaviour
{
    [SerializeField] private float strength = 6f;
    [SerializeField, Range(1f, 20f)] private float smoothing = 8f;

    private RectTransform rectTransform;
    private Vector2 velocity;

    private void Reset()
    {
        strength = name.Contains("Foreground") ? 18f : 6f;
    }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    private void Update()
    {
        Vector2 screenSize = new Vector2(Screen.width, Screen.height);
        Vector2 pointer = Pointer.current != null
            ? Pointer.current.position.ReadValue()
            : screenSize * 0.5f;

        Vector2 normalized = new Vector2(
            Mathf.Clamp((pointer.x / Mathf.Max(screenSize.x, 1f) - 0.5f) * 2f, -1f, 1f),
            Mathf.Clamp((pointer.y / Mathf.Max(screenSize.y, 1f) - 0.5f) * 2f, -1f, 1f));

        rectTransform.anchoredPosition = Vector2.SmoothDamp(
            rectTransform.anchoredPosition,
            -normalized * strength,
            ref velocity,
            1f / smoothing,
            Mathf.Infinity,
            Time.unscaledDeltaTime);
    }
}

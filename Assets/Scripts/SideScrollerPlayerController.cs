using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(SpriteRenderer))]
public class SideScrollerPlayerController : MonoBehaviour
{
    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float acceleration = 35f;
    [SerializeField] float deceleration = 45f;
    [SerializeField] float jumpForce = 10f;
    [SerializeField] Sprite idleSprite;
    [SerializeField] Sprite[] runFrames;
    [SerializeField] float runFramesPerSecond = 8f;

    Rigidbody2D body;
    SpriteRenderer spriteRenderer;
    float horizontalInput;
    float runAnimationTime;
    readonly HashSet<Collider2D> groundColliders = new();
    bool movementEnabled = true;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (idleSprite == null)
            idleSprite = spriteRenderer.sprite;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (!movementEnabled || keyboard == null)
        {
            horizontalInput = 0f;
            UpdateAnimation();
            return;
        }

        horizontalInput = 0f;
        if (keyboard.aKey.isPressed)
            horizontalInput -= 1f;
        if (keyboard.dKey.isPressed)
            horizontalInput += 1f;

        if (horizontalInput != 0f)
            spriteRenderer.flipX = horizontalInput < 0f;

        if (keyboard.wKey.wasPressedThisFrame && groundColliders.Count > 0)
            body.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

        UpdateAnimation();
    }

    void FixedUpdate()
    {
        float targetSpeed = horizontalInput * moveSpeed;
        float changeRate = Mathf.Abs(targetSpeed) > 0.01f ? acceleration : deceleration;
        float horizontalSpeed = Mathf.MoveTowards(
            body.linearVelocity.x,
            targetSpeed,
            changeRate * Time.fixedDeltaTime);

        body.linearVelocity = new Vector2(horizontalSpeed, body.linearVelocity.y);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.5f)
            {
                groundColliders.Add(collision.collider);
                return;
            }
        }
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        groundColliders.Remove(collision.collider);
    }

    public void SetMovementEnabled(bool enabled)
    {
        movementEnabled = enabled;
        if (!enabled)
        {
            horizontalInput = 0f;
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
        }
    }

    void UpdateAnimation()
    {
        float speed = Mathf.Abs(body.linearVelocity.x);
        if (speed < 0.1f || runFrames == null || runFrames.Length == 0)
        {
            runAnimationTime = 0f;
            spriteRenderer.sprite = idleSprite;
            return;
        }

        float speedRatio = Mathf.Clamp(speed / moveSpeed, 0.35f, 1f);
        runAnimationTime += Time.deltaTime * runFramesPerSecond * speedRatio;
        int frame = Mathf.FloorToInt(runAnimationTime) % runFrames.Length;
        spriteRenderer.sprite = runFrames[frame];
    }
}

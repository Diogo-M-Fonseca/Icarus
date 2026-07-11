using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerMovement : MonoBehaviour
{
    public event Action OnJumped;
    public event Action<float> OnLanded;


    [Header("Movement")]
    [SerializeField] private float moveSpeed = 7f;
    [SerializeField] private float acceleration = 60f; //Quão rápido atinge o alvo
    [SerializeField] private float deceleration = 60f; //Quão rápido trava quando acaba input
    [SerializeField] private float airControlMultiplier = 0.6f; // Reduz controlo no ar

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 12f; //Quão rápido o personagem gira para a direção do input

    [Header("Jumping")]
    [SerializeField] private float jumpForce = 7f;
    [SerializeField] private float gravityMultiplier = 2.5f; // Aumenta a gravidade para uma queda mais rápida
    [SerializeField] private float lowJumpGravityMultiplier = 2f; // Aumenta a gravidade quando o jogador solta o botão de pulo
    [SerializeField] private int maxAirJumps = 1; // Número máximo de pulos no ar

    [Header("Coyote Time and Jump Buffer")]
    [SerializeField] private float coyoteTime = 0.12f; // Tempo que o jogador pode pular após sair do chão
    [SerializeField] private float jumpBufferTime = 0.12f; // Tempo que o jogador pode apertar o botão de pulo antes de tocar o chão

    [Header("Ground Check")]
    [SerializeField] private float groundCheckDistance = 0.15f; // Distância de raycast
    [SerializeField] private float groundCheckOffset = 0.05f; // Offset para o raio começar fora do collider

    private Rigidbody rb;
    private CapsuleCollider capsule;

    private Vector2 moveInput;
    private Vector3 currentVelocity; // velocidade horizontal atual

    private bool isGrounded;
    private bool wasGroundedLastFrame;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private int airJumpsRemaining;
    private bool jumpHeld;
    private float previousVerticalVelocity;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        // Congela rotação fisica para controlar a rotação manualmente
        rb.constraints = 
            RigidbodyConstraints.FreezeRotationX 
            | RigidbodyConstraints.FreezeRotationY 
            | RigidbodyConstraints.FreezeRotationZ;

        rb.interpolation = RigidbodyInterpolation.Interpolate; // Suaviza o movimento do Rigidbody
    }

    private void Update()
    {
        ReadInput();
        UpdateTimers();
        HandleJumpBuffer();
    }

    private void FixedUpdate()
    {
        UpdateGroundedState();
        ApplyHorizontalMovement();
        ApplyExtraGravity();
        RotateTowardsMovement();

        previousVerticalVelocity = rb.linearVelocity.y;
    }


    /// <summary>
    /// Reads the movement and jump state
    /// manages jump buffering
    /// </summary>
    private void ReadInput()
    {
        moveInput = Vector2.zero;
        bool jumpPressedThisFrame = false;

        // Teclado
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            float x = 0f;
            float y = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;

            moveInput = new Vector2(x, y);
            jumpHeld = keyboard.spaceKey.isPressed;
            jumpPressedThisFrame = keyboard.spaceKey.wasPressedThisFrame;
        }

        // Comando
        var gamepad = Gamepad.current;
        if (gamepad != null)
        {
            Vector2 stick = gamepad.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0.01f) moveInput = stick;

            if (gamepad.buttonSouth.isPressed) jumpHeld = true;
            if (gamepad.buttonSouth.wasPressedThisFrame) jumpPressedThisFrame = true;
        }

        if (jumpPressedThisFrame)
        {
            jumpBufferTimer = jumpBufferTime;
        }
    }

    /// <summary>
    /// Updates the grounded state by performing a raycast to detect ground contact beneath the player.
    /// </summary>
    /// <remarks>Resets the air jump count when transitioning from airborne to grounded.</remarks>
    private void UpdateGroundedState()
    {
        wasGroundedLastFrame = isGrounded;

        // Origem do raio ligeiramente abaixo da base do collider
        // para o raio nascer fora da propria capsula e nunca colidir com o player
        float capsuleBottomY = transform.position.y - (capsule.height / 2f) + capsule.radius;
        Vector3 origin = new Vector3 (transform.position.x, capsuleBottomY - groundCheckOffset, transform.position.z);

        isGrounded = Physics.Raycast(origin, Vector3.down, groundCheckDistance);

        // Reseta saltos no ar quando o jogador toca o chão
        if (isGrounded && !wasGroundedLastFrame)
        {
            airJumpsRemaining = maxAirJumps;
            OnLanded?.Invoke(previousVerticalVelocity);
        }
    }

    /// <summary>
    /// Updates the coyote time timer (reset while grounded,
    /// counts down in the air) and the jump buffer timer (counts down
    /// continuously, starting from the moment the jump button is pressed).
    /// </summary>
    private void UpdateTimers()
    {
        if (isGrounded)
        {
            coyoteTimer = coyoteTime;
        }
        else
        {
            coyoteTimer -= Time.deltaTime;
        }

        if (jumpBufferTimer > 0f)
        {
            jumpBufferTimer -= Time.deltaTime;
        }
    }

    /// <summary>
    /// Checks for a "buffered" jump (input pressed recently)
    /// and, if so, attempts to execute it: first via coyote time
    /// (a normal jump immediately after leaving a ledge), and, if that has expired,
    /// via an air jump (double jump), provided one is still available.
    /// </summary>
    private void HandleJumpBuffer()
    {
        bool hasJumpBuffered = jumpBufferTimer > 0f;
        if (!hasJumpBuffered) return;

        bool canCoyoteJump = coyoteTimer > 0f;

        if (canCoyoteJump)
        {
            Jump();
            coyoteTimer = 0f;
        }
        else if (airJumpsRemaining > 0)
        {
            Jump();
            airJumpsRemaining--;
        }
    }

    /// <summary>
    /// Jumps consuming jump buffer
    /// and defines the vertical velocity to the jump force.
    /// </summary>
    private void Jump()
    {
        jumpBufferTimer = 0f;
        Vector3 velocity = rb.linearVelocity;
        velocity.y = jumpForce;
        rb.linearVelocity = velocity;
        OnJumped?.Invoke();
    }

    /// <summary>
    /// Applies additional gravity to the rigidbody to enhance jump and fall responsiveness.
    /// </summary>
    /// <remarks>Increases gravity during downward movement for faster falls and applies extra gravity when
    /// the jump button is released to create variable jump heights.</remarks>
    private void ApplyExtraGravity()
    {
        float verticalVelocity = rb.linearVelocity.y;

        if (verticalVelocity < 0f)
        {
            // Aumenta a gravidade para uma queda mais rápida
            rb.linearVelocity += Vector3.up * Physics.gravity.y * (gravityMultiplier - 1f) * Time.fixedDeltaTime;
        }
        else if (verticalVelocity > 0f && !jumpHeld)
        {
            // Aumenta a gravidade quando o jogador solta o botão de pulo
            rb.linearVelocity += Vector3.up * Physics.gravity.y * (lowJumpGravityMultiplier - 1f) * Time.fixedDeltaTime;
        }
    }


    /// <summary>
    /// Applies horizontal movement to the player based on camera orientation and input, adjusting for ground and air
    /// control.
    /// </summary>
    /// <remarks>Normalizes movement direction, interpolates velocity, and preserves vertical velocity during
    /// movement updates.</remarks>
    private void ApplyHorizontalMovement()
    {
        Vector3 camForward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
        Vector3 camRight = Camera.main != null ? Camera.main.transform.right : Vector3.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 wishDirection = (camForward * moveInput.y + camRight * moveInput.x);
        if (wishDirection.sqrMagnitude > 1f) wishDirection.Normalize();

        Vector3 targetVelocity = wishDirection * moveSpeed;

        float accel = wishDirection.sqrMagnitude > 0.01f ? acceleration : deceleration;
        if (!isGrounded) accel *= airControlMultiplier;

        currentVelocity = Vector3.MoveTowards(currentVelocity, targetVelocity, accel * Time.fixedDeltaTime);

        Vector3 finalVelocity = new Vector3(currentVelocity.x, rb.linearVelocity.y, currentVelocity.z);
        rb.linearVelocity = finalVelocity;
    }

    /// <summary>
    /// Rotates the player to face the direction of its horizontal movement.
    /// </summary>
    private void RotateTowardsMovement()
    {
        Vector3 horizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (horizontalVel.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(horizontalVel.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
    }

    private void OnDrawGizmosSelected()
    {
        if (capsule == null) capsule = GetComponent<CapsuleCollider>();
        float capsuleBottomY = transform.position.y - (capsule.height / 2f) + capsule.radius;
        Vector3 origin = new Vector3(transform.position.x, capsuleBottomY - groundCheckOffset, transform.position.z);

        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawLine(origin, origin + Vector3.down * groundCheckDistance);
    }
}
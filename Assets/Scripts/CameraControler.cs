using UnityEngine;
using UnityEngine.InputSystem;

public class CameraControler : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;                 // o Player (root com Rigidbody)
    [SerializeField] private Vector3 pivotOffset = new Vector3(0f, 1.6f, 0f); // ponto à volta do qual a câmara orbita (ex: altura dos "ombros")

    [Header("Distance")]
    [SerializeField] private float distance = 5f;
    [SerializeField] private float minDistance = 1.2f;
    [SerializeField] private float maxDistance = 8f;

    [Header("Orbit / Sensitivity")]
    [SerializeField] private float mouseSensitivityX = 0.2f;
    [SerializeField] private float mouseSensitivityY = 0.15f;
    [SerializeField] private float gamepadSensitivity = 180f;
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 70f;

    [Header("Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.08f;
    [SerializeField] private float rotationSmoothSpeed = 12f;

    [Header("Collision")]
    [SerializeField] private float collisionRadius = 0.25f;   // raio do spherecast
    [SerializeField] private float collisionBuffer = 0.15f;   // margem extra para não colar à parede

    [Header("Jump Reaction")]
    [SerializeField] private float jumpKickAmount = 0.25f;       // quanto a câmara "cede" para baixo ao saltar
    [SerializeField] private float jumpFovKick = 4f;              // aumento de FOV ao saltar

    [Header("Land Reaction")]
    [SerializeField] private float landKickAmount = 0.6f;        // "cede" máxima ao aterrar com impacto forte
    [SerializeField] private float maxLandImpactSpeed = 20f;     // velocidade de queda considerada "impacto máximo"
    [SerializeField] private float landFovKick = 6f;              // aumento de FOV máximo ao aterrar

    [Header("Kick Recovery")]
    [SerializeField] private float kickRecoverySpeed = 6f;       // quão rápido o offset de posição volta a 0
    [SerializeField] private float fovRecoverySpeed = 5f;        // quão rápido o FOV volta ao normal

    private Camera cam;
    private PlayerMovement playerMovement;

    private float yaw;
    private float pitch = 15f;

    private Vector3 currentPositionVelocity; // usado pelo SmoothDamp
    private Vector3 currentCameraPosition;

    private float verticalKickOffset;  // offset vertical temporário aplicado por saltos/aterragens
    private float baseFov;
    private float fovKickCurrent;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        baseFov = cam.fieldOfView;

        if (target != null)
        {
            playerMovement = target.GetComponent<PlayerMovement>();
            yaw = target.eulerAngles.y;
        }

        currentCameraPosition = transform.position;
    }

    private void OnEnable()
    {
        if (playerMovement != null)
        {
            playerMovement.OnJumped += HandleJumped;
            playerMovement.OnLanded += HandleLanded;
        }
    }

    private void OnDisable()
    {
        if (playerMovement != null)
        {
            playerMovement.OnJumped -= HandleJumped;
            playerMovement.OnLanded -= HandleLanded;
        }
    }

    private void Update()
    {
        ReadOrbitInput();
        ReadZoomInput();
    }

    private void LateUpdate()
    {
        if (target == null) return;

        UpdateOrbitPosition();
        UpdateKickRecovery();
    }

    private void ReadOrbitInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 mouseDelta = mouse.delta.ReadValue();
            yaw += mouseDelta.x * mouseSensitivityX;
            pitch -= mouseDelta.y * mouseSensitivityY;
        }

        var gamepad = Gamepad.current;
        if (gamepad != null)
        {
            Vector2 rightStick = gamepad.rightStick.ReadValue();
            if (rightStick.sqrMagnitude > 0.01f)
            {
                yaw += rightStick.x * gamepadSensitivity * Time.deltaTime;
                pitch -= rightStick.y * gamepadSensitivity * Time.deltaTime;
            }
        }

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void ReadZoomInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance = Mathf.Clamp(distance - scroll * 0.01f, minDistance, maxDistance);
        }
    }

    private void UpdateOrbitPosition()
    {
        Vector3 pivot = target.position + pivotOffset;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredDirection = rotation * Vector3.back; // "back" = atrás do alvo
        Vector3 desiredPosition = pivot + desiredDirection * distance;

        float finalDistance = distance;

        // Colisão: lança uma esfera do pivot até à posição desejada.
        // Ignora o próprio jogador comparando o root do hit com o root do alvo.
        if (Physics.SphereCast(pivot, collisionRadius, desiredDirection, out RaycastHit hit, distance))
        {
            if (hit.transform.root != target.root)
            {
                finalDistance = Mathf.Clamp(hit.distance - collisionBuffer, minDistance, distance);
            }
        }

        Vector3 targetPosition = pivot + desiredDirection * finalDistance;
        targetPosition.y += verticalKickOffset;

        currentCameraPosition = Vector3.SmoothDamp(currentCameraPosition, targetPosition, ref currentPositionVelocity, positionSmoothTime);
        transform.position = currentCameraPosition;

        Quaternion lookRotation = Quaternion.LookRotation(pivot - currentCameraPosition, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, rotationSmoothSpeed * Time.deltaTime);
    }

    private void UpdateKickRecovery()
    {
        verticalKickOffset = Mathf.Lerp(verticalKickOffset, 0f, kickRecoverySpeed * Time.deltaTime);
        fovKickCurrent = Mathf.Lerp(fovKickCurrent, 0f, fovRecoverySpeed * Time.deltaTime);
        cam.fieldOfView = baseFov + fovKickCurrent;
    }

    private void HandleJumped()
    {
        verticalKickOffset -= jumpKickAmount;
        fovKickCurrent += jumpFovKick;
    }

    private void HandleLanded(float impactVerticalVelocity)
    {
        float impactStrength = Mathf.Clamp01(Mathf.Abs(impactVerticalVelocity) / maxLandImpactSpeed);
        verticalKickOffset -= landKickAmount * impactStrength;
        fovKickCurrent += landFovKick * impactStrength;
    }
}

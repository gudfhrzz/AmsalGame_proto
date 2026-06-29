using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Agent")]
    [SerializeField] private AgentData agentData;

    [Header("Fallback (AgentData 없을 때)")]
    [SerializeField] private float defaultMoveSpeed = 3.5f;
    [SerializeField] private float defaultWalkMultiplier = 0.6f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 720f;

    private CharacterController _controller;
    private InputSystem_Actions _input;
    private Vector2 _moveInput;
    private bool _isWalking;

    // 외부 시스템(사운드 등)에서 읽는 프로퍼티
    public bool IsMoving { get; private set; }
    public bool IsWalking => _isWalking;
    public float CurrentSpeed => _isWalking ? MoveSpeed * WalkMultiplier : MoveSpeed;
    public float CurrentSoundRadius => agentData != null
        ? (_isWalking ? agentData.walkSoundRadius : agentData.moveSoundRadius)
        : 0f;

    private float MoveSpeed => agentData != null ? agentData.moveSpeed : defaultMoveSpeed;
    private float WalkMultiplier => agentData != null ? agentData.walkSpeedMultiplier : defaultWalkMultiplier;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _input = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        _input.Player.Enable();
        _input.Player.Move.performed += OnMove;
        _input.Player.Move.canceled += OnMove;
        // Sprint 액션(Left Shift)을 걷기 모드로 사용
        _input.Player.Sprint.performed += OnWalkToggle;
        _input.Player.Sprint.canceled += OnWalkToggle;
    }

    private void OnDisable()
    {
        _input.Player.Move.performed -= OnMove;
        _input.Player.Move.canceled -= OnMove;
        _input.Player.Sprint.performed -= OnWalkToggle;
        _input.Player.Sprint.canceled -= OnWalkToggle;
        _input.Player.Disable();
    }

    private void OnMove(InputAction.CallbackContext ctx) => _moveInput = ctx.ReadValue<Vector2>();
    private void OnWalkToggle(InputAction.CallbackContext ctx) => _isWalking = ctx.performed;

    private void Update()
    {
        HandleMovement();
    }

    private void HandleMovement()
    {
        Vector3 direction = new Vector3(_moveInput.x, 0f, _moveInput.y);
        IsMoving = direction.sqrMagnitude > 0.01f;

        Vector3 move = IsMoving ? direction.normalized * CurrentSpeed : Vector3.zero;
        _controller.Move(move * Time.deltaTime);

        if (IsMoving)
        {
            Quaternion targetRot = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (agentData == null) return;
        // 이동 발소리 반경 (주황)
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, agentData.moveSoundRadius);
        // 걷기 발소리 반경 (초록)
        Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, agentData.walkSoundRadius);
    }
#endif
}

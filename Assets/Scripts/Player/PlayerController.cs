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
    private Camera _mainCam;
    private InputSystem_Actions _input;
    private Vector2 _moveInput;
    private bool _isWalking;

    // 외부 시스템(사운드, 존버 방지 등)에서 읽는 프로퍼티
    public bool IsMoving { get; private set; }
    public AgentData Data => agentData;
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

        // 비활성화(사망 처리 등) 후에도 SoundEmitter/ExposureSystem이 이 값들을 읽으므로 잔존 상태를 정리
        _moveInput = Vector2.zero;
        IsMoving = false;
        _isWalking = false;
    }

    private void OnMove(InputAction.CallbackContext ctx) => _moveInput = ctx.ReadValue<Vector2>();
    private void OnWalkToggle(InputAction.CallbackContext ctx) => _isWalking = ctx.performed;

    private void Update()
    {
        HandleMovement();
        HandleAimRotation();
    }

    // 이동은 WASD(월드 8방향) — 회전과 완전히 분리. 몸이 어디를 보든 이동 방향은 그대로다
    private void HandleMovement()
    {
        Vector3 direction = new Vector3(_moveInput.x, 0f, _moveInput.y);
        IsMoving = direction.sqrMagnitude > 0.01f;

        Vector3 move = IsMoving ? direction.normalized * CurrentSpeed : Vector3.zero;
        _controller.Move(move * Time.deltaTime);
    }

    // 시야(몸 방향)는 마우스 커서를 따라간다 — 이동이 8방향으로 제한돼도 시야는 자유롭게.
    // 손전등(자식 Light)과 부채꼴 시야 마스크가 transform.forward를 쓰므로 이것 하나로 연동된다
    private void HandleAimRotation()
    {
        if (Mouse.current == null) return;
        if (_mainCam == null)
        {
            _mainCam = Camera.main;
            if (_mainCam == null) return;
        }

        Ray ray = _mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());
        var aimPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
        if (!aimPlane.Raycast(ray, out float enter)) return;

        Vector3 look = ray.GetPoint(enter) - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude < 0.04f) return; // 커서가 플레이어 바로 위 — 방향 노이즈 방지

        Quaternion targetRot = Quaternion.LookRotation(look);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
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

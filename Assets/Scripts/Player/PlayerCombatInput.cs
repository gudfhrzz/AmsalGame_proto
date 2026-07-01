using UnityEngine;
using UnityEngine.InputSystem;

// LMB: 암살 가능하면 암살(AssassinationSystem), 아니면 CQC 공격(CombatController)
// RMB(홀드): 막기 / 누른 직후 짧은 구간: 패링
// F: 그랩 (막기 중인 상대에게만 유효)
[RequireComponent(typeof(CombatController), typeof(AssassinationSystem))]
public class PlayerCombatInput : MonoBehaviour
{
    private CombatController _combat;
    private AssassinationSystem _assassination;
    private InputSystem_Actions _input;

    private void Awake()
    {
        _combat = GetComponent<CombatController>();
        _assassination = GetComponent<AssassinationSystem>();
        _input = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        _input.Player.Enable();
        _input.Player.Attack.performed += OnAttack;
    }

    private void OnDisable()
    {
        _input.Player.Attack.performed -= OnAttack;
        _input.Player.Disable();
    }

    private void Update()
    {
        _combat.SetBlocking(Mouse.current != null && Mouse.current.rightButton.isPressed);

        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            _combat.TryGrab();
    }

    private void OnAttack(InputAction.CallbackContext ctx)
    {
        if (_assassination.CanAssassinate)
            _assassination.TryAssassinate();
        else
            _combat.TryAttack();
    }
}

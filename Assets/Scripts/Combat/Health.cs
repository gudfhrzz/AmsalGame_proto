using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField] private int maxHp = 3;

    private int _currentHp;

    public event Action OnDeath;
    public bool IsDead { get; private set; }
    public int Current => _currentHp;

    private void Awake() => _currentHp = maxHp;

    public void TakeDamage(int amount)
    {
        if (IsDead) return;
        _currentHp = Mathf.Max(0, _currentHp - amount);
        if (_currentHp == 0) Die();
    }

    private void Die()
    {
        IsDead = true;
        OnDeath?.Invoke();
    }
}

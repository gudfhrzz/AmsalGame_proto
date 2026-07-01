using System;
using UnityEngine;

// 승리: 씬의 모든 AIController 처치 / 패배: Player 사망.
public class GameStateManager : MonoBehaviour
{
    public enum Result { None, Victory, Defeat }

    [SerializeField] private Health playerHealth;

    private AIController[] _enemies;

    public Result CurrentResult { get; private set; } = Result.None;
    public event Action<Result> OnGameEnded;

    public void Bind(Health player) => playerHealth = player;

    private void Start()
    {
        _enemies = FindObjectsByType<AIController>(FindObjectsSortMode.None);

        if (playerHealth != null) playerHealth.OnDeath += HandleDefeat;

        foreach (var ai in _enemies)
        {
            var health = ai.GetComponent<Health>();
            if (health != null) health.OnDeath += CheckVictory;
        }
    }

    private void CheckVictory()
    {
        if (CurrentResult != Result.None) return;

        foreach (var ai in _enemies)
        {
            // 이미 파괴된(사망 후 2초 뒤 Destroy된) 참조는 죽은 것으로 취급하고 건너뛴다
            if (ai == null) continue;

            var health = ai.GetComponent<Health>();
            if (health != null && !health.IsDead) return;
        }

        CurrentResult = Result.Victory;
        OnGameEnded?.Invoke(Result.Victory);
    }

    private void HandleDefeat()
    {
        if (CurrentResult != Result.None) return;

        CurrentResult = Result.Defeat;
        OnGameEnded?.Invoke(Result.Defeat);
    }
}

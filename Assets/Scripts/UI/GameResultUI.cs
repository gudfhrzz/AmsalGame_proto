using UnityEngine;
using UnityEngine.UI;

// 승리/패배 배너. GameStateManager.OnGameEnded를 구독해 결과 텍스트만 띄운다.
public class GameResultUI : MonoBehaviour
{
    [SerializeField] private GameStateManager gameState;
    [SerializeField] private Text resultText;

    public void Bind(GameStateManager state, Text text)
    {
        gameState = state;
        resultText = text;
    }

    private void OnEnable()
    {
        if (gameState != null) gameState.OnGameEnded += HandleGameEnded;
    }

    private void OnDisable()
    {
        if (gameState != null) gameState.OnGameEnded -= HandleGameEnded;
    }

    private void HandleGameEnded(GameStateManager.Result result)
    {
        if (resultText == null) return;

        resultText.gameObject.SetActive(true);
        resultText.text = result == GameStateManager.Result.Victory ? "승리!" : "패배...";
        resultText.color = result == GameStateManager.Result.Victory ? new Color(0.3f, 1f, 1f) : new Color(1f, 0.3f, 0.3f);
    }
}

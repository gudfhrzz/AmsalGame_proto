using UnityEngine;

[CreateAssetMenu(fileName = "AgentData", menuName = "AmsalGame/Agent Data")]
public class AgentData : ScriptableObject
{
    public enum AgentType { Offensive, Defensive, Neutral }

    [Header("Identity")]
    public AgentType agentType = AgentType.Offensive;

    [Header("Movement")]
    [Tooltip("기본 이동 속도 (m/s)")]
    public float moveSpeed = 3.5f;

    [Tooltip("Shift 은신 이동 시 속도 배율")]
    [Range(0.1f, 1f)]
    public float walkSpeedMultiplier = 0.5f;

    [Header("Sound Radius")]
    [Tooltip("기본 이동 발소리 반경 (m)")]
    public float moveSoundRadius = 8f;

    [Tooltip("걷기 발소리 반경 (m)")]
    public float walkSoundRadius = 3f;
}

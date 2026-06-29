using System;
using UnityEngine;

public struct SoundEvent
{
    public Vector3 Position;
    public float Radius;
    public GameObject Source;

    public SoundEvent(Vector3 position, float radius, GameObject source)
    {
        Position = position;
        Radius = radius;
        Source = source;
    }
}

public class SoundEventSystem : MonoBehaviour
{
    public static SoundEventSystem Instance { get; private set; }

    public event Action<SoundEvent> OnSoundEmitted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void Emit(SoundEvent e) => OnSoundEmitted?.Invoke(e);
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Slow-motion helper. Gameplay slow-mo is per-character data
/// (Character.TimeBump) so the fixed-rate simulation stays deterministic;
/// global Time.timeScale is only touched by the editor debug key.
/// </summary>
public class TimeController : MonoBehaviour
{
#if UNITY_EDITOR
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            if (Time.timeScale > 0.5f)
            {
                Time.timeScale = 0.1f;
            }
            else
            {
                Time.timeScale = 1f;
            }
        }
    }
#endif

    /// <summary>Applies a radial slow-mo bump to nearby characters.</summary>
    public static void TimeBumpCharacters(Vector2 center, float durationM, float radius, bool dropOff)
    {
        foreach (var player in GameController.activePlayers)
        {
            if (player.character != null)
            {
                float dist = Vector2.Distance(center, player.character.transform.position);
                if (dist < radius)
                {
                    float m = dist / radius;
                    player.character.TimeBump(durationM, dropOff ? m * m : 0f);
                }
            }
        }
    }
}

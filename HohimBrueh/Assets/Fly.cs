using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FrogSmashers.Net.Sim;

public class Fly : MonoBehaviour, ISimTickable
{
    Vector2 velocity, targetVelocity;
    int terrainLayer;
    public SpriteRenderer sprite;

    public float maxSpeed;
    internal bool BeingIngested;
    internal Character ingestedBy;

    // Safety timeout: even with a valid owner, force release if a fly stays
    // claimed for longer than this. Prevents pathological "stuck fly" states.
    const float ingestTimeoutDuration = 3f;
    float ingestTimeout;

    float updateDirectionDelay;

    void Awake()
    {
        terrainLayer = 1 << LayerMask.NameToLayer("Ground");
    }

    /// <summary>Initializes mutable state at (re)spawn time.</summary>
    public void SpawnInit()
    {
        velocity = DeterministicRng.Match.UnitCircle() * 10f;
        targetVelocity = Vector2.zero;
        updateDirectionDelay = 0f;
        BeingIngested = false;
        ingestedBy = null;
        ingestTimeout = 0f;
    }

    /// <summary>Captures every mutable sim field into a snapshot.</summary>
    public void SaveTo(ref FlySnapshot s)
    {
        s.Active = gameObject.activeSelf;
        s.Position = transform.position;
        s.Velocity = velocity;
        s.TargetVelocity = targetVelocity;
        s.BeingIngested = BeingIngested;
        s.IngestedByPlayerIndex = ingestedBy != null
            && ingestedBy.player != null
            ? GameController.activePlayers.IndexOf(ingestedBy.player) : -1;
        s.IngestTimeout = ingestTimeout;
        s.UpdateDirectionDelay = updateDirectionDelay;
    }

    /// <summary>Restores every mutable sim field from a snapshot.</summary>
    public void RestoreFrom(in FlySnapshot s)
    {
        gameObject.SetActive(s.Active);
        transform.position = s.Position;
        velocity = s.Velocity;
        targetVelocity = s.TargetVelocity;
        BeingIngested = s.BeingIngested;
        ingestedBy = s.IngestedByPlayerIndex >= 0
            && s.IngestedByPlayerIndex < GameController.activePlayers.Count
            ? GameController.activePlayers[s.IngestedByPlayerIndex].character
            : null;
        ingestTimeout = s.IngestTimeout;
        updateDirectionDelay = s.UpdateDirectionDelay;
    }

    /// <summary>Mixes this fly's mutable sim state into a hash.</summary>
    public uint HashSimState(uint h)
    {
        h = StateHash.Mix(h, (Vector2)transform.position);
        h = StateHash.Mix(h, velocity);
        h = StateHash.Mix(h, targetVelocity);
        h = StateHash.Mix(h, BeingIngested);
        h = StateHash.Mix(h, ingestTimeout);
        h = StateHash.Mix(h, updateDirectionDelay);
        return h;
    }

    /// <summary>Flies tick after every character.</summary>
    public int SimOrder
    {
        get { return 200; }
    }

    void OnEnable()
    {
        SimulationDriver.Register(this);
    }

    void OnDisable()
    {
        SimulationDriver.Unregister(this);
    }

    internal bool TryClaim(Character claimant)
    {
        if (ingestedBy != null && ingestedBy != claimant)
            return false;
        ingestedBy = claimant;
        BeingIngested = true;
        ingestTimeout = ingestTimeoutDuration;
        return true;
    }

    internal void Release()
    {
        ingestedBy = null;
        BeingIngested = false;
        ingestTimeout = 0f;
    }

    /// <summary>Advances this fly by one fixed simulation step.</summary>
    public void SimTick(float dt)
    {
        updateDirectionDelay -= dt;
        if (updateDirectionDelay < 0f)
        {
            updateDirectionDelay = DeterministicRng.Match.Range(3f, 10f);
            UpdateDirection();
        }

        if (BeingIngested)
        {
            // Watchdog: the owner must still exist, be active, and still point at us.
            // Otherwise the fly is orphaned (e.g. owner died, got knocked, or another
            // tongue raced past the claim check on an older code path) — release it.
            bool ownerLost = ingestedBy == null
                             || !ingestedBy.gameObject.activeInHierarchy
                             || ingestedBy.ingestingFly != this;
            ingestTimeout -= dt;
            if (ownerLost || ingestTimeout <= 0f)
                Release();
        }

        if (!BeingIngested)
            RunMotion(dt);

        if (transform.position.x < Terrain.LeftKillPoint || transform.position.x > Terrain.RightKillPoint || transform.position.y > Terrain.TopKillPoint || transform.position.y < Terrain.BotKillPoint)
            GameController.PoolFly(this);
    }

    void RunMotion(float dt)
    {
        velocity = Vector2.MoveTowards(velocity, targetVelocity, 5f * dt);

        Vector2 velocityT = velocity * dt + Vector2.up * Mathf.Sin(SimClock.SimTime * 8f) * 3f * dt;
        if (velocityT.x < 0)
        {
            if (Physics2D.Raycast(transform.position, Vector2.left, Mathf.Abs(velocityT.x) + 1f, terrainLayer))
            {
                velocityT.x = 0;
                velocity.x *= -0.5f;
                UpdateDirection();
            }
        }
        if (velocityT.x > 0)
        {
            if (Physics2D.Raycast(transform.position, Vector2.right, Mathf.Abs(velocityT.x) + 1f, terrainLayer))
            {
                velocityT.x = 0;
                velocity.x *= -0.5f;
                UpdateDirection();
            }
        }
        if (velocityT.y < 0)
        {
            if (Physics2D.Raycast(transform.position, Vector2.down, Mathf.Abs(velocityT.y) + 1f, terrainLayer))
            {
                velocityT.y = 0;
                velocity.y *= -0.5f;
                UpdateDirection();
            }
        }
        if (velocityT.y > 0)
        {
            if (Physics2D.Raycast(transform.position, Vector2.up, Mathf.Abs(velocityT.y) + 1f, terrainLayer))
            {
                velocityT.y = 0;
                velocity.y *= -0.5f;
                UpdateDirection();
            }
        }

        if (velocityT.x < 0)
            sprite.transform.localScale = new Vector3(1f, 1f, 1f);
        else
            sprite.transform.localScale = new Vector3(-1f, 1f, 1f);

        transform.position += (Vector3)velocityT;
    }

    private void UpdateDirection()
    {
        if (DeterministicRng.Match.Value < 0.1f)
        {
            targetVelocity = Vector2.zero;
            updateDirectionDelay = DeterministicRng.Match.Range(1f, 3f);
        }
        else
            targetVelocity = DeterministicRng.Match.UnitCircle() * maxSpeed;
    }
}

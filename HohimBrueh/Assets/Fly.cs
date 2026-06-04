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

    // Use this for initialization
    void Start()
    {
        terrainLayer = 1 << LayerMask.NameToLayer("Ground");
        velocity = Random.insideUnitCircle.normalized * 10f;
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
            updateDirectionDelay = Random.Range(3f, 10f);
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
            Destroy(gameObject);
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
        if (Random.value < 0.1f)
        {
            targetVelocity = Vector2.zero;
            updateDirectionDelay = Random.Range(1f, 3f);
        }
        else
            targetVelocity = Random.insideUnitCircle.normalized * maxSpeed;
    }
}

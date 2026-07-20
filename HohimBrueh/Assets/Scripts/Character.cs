using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FreeLives;
using FrogSmashers.Net.Sim;
using System;

public enum CharacterState
{
    Normal,
    Attacking,
    Bouncing,
    Tounge
}
public enum AttackState
{
    Idle,
    Charging,
    Attacking,
    Recovering
}
public enum TongueState
{
    Extending,
    Retracting,
    AttachedToTerrain,
    RetractingHitEnemy,
    RetractingHitEnemyTongue,
    RetractingHitFly,
    HitEnemyTongueStunned,
    HitFlyBurping
}

public class Character : MonoBehaviour, ISimTickable
{
    [HideInInspector]
    public InputState input = new InputState();
    [HideInInspector]
    public Player player;
    [HideInInspector]
    public Player lastHitByPlayer;

    /// <summary>Owner slot for SimFx de-dup keys (null-safe).</summary>
    int FxSlot
    {
        get { return player != null ? player.sortPriority : 0; }
    }

    public CharacterState state;
    public AttackState attackState;

    public static bool isTeamMode;

    int facingDir = 1;

    [Header("Standard Motion")]
    public float maxRunSpeed;
    public float runAccel, airAccel, skidAccel;
    public float maxFallSpeed;
    public float maxFallSpeedWallSlide;
    public float jumpVel;
    public float gravity;
    public float jumpGraceTime;
    public float gravityGraceTime;
    public float graceGravityM;
    public float gravityGraceThreshold;

    [Header("Bounce Motion")]
    public float bounceAccel;
    public float bounceGravityMin;
    public float bounceGravityMax;
    public float bounceGravityRestoreDelay;
    public float bounceGravityRestoreTime;
    public float bounceDodgePower;

    [HideInInspector]
    public bool canBounceDodge;
    [HideInInspector]
    public bool hasBounceDodged;

    bool canBounceTongue;
    bool hasBounceTongued;

    bool hasReachedApex;
    [HideInInspector]
    public bool wasHitDownwards;

    [HideInInspector]
    public float bounceGravityRestoreCounter;


    [Header("Attack")]
    public float attackChargeTime;
    float attackChargeCounter;

    [HideInInspector]
    public float gravityGraceTimeLeft;
    public float attackTime;
    public float attackRecoverTime;

    internal void TimeBump(float durationM, float scale)
    {
        if (TimeBumpActive)
        {
            timeBumpTimeScale = Mathf.Min(timeBumpTimeScale, scale);
        }
        else
            timeBumpTimeScale = scale;

        timeBumpTimeLeft = Mathf.Max(timeBumpTimeLeft, durationM * 0.175f);

    }

    public float attackRange;

    float jumpGraceTimeLeft;

    const float width = 2f, height = 2f;

    [Header("Tongue")]
    public float tongueRange;
    public float tongueSpeed;
    public float tongueRetractSpeedLatched;
    public float tongueRetractSpeedMissed;
    public Vector2 tongueOrigin;
    public float tongueDelay;
    public float minimumTongueDistance;

    float tongueDelayLeft;
    public Vector2 tongueDir { protected set; get; }
    public float tongueDistance { protected set; get; }
    public TongueState tongueState { protected set; get; }
    public bool wasBouncingBeforeTongue { protected set; get; }

    float timeBumpTimeLeft;
    public float timeBumpTimeScale { get; protected set; }

    public bool TimeBumpActive { get { return timeBumpTimeLeft > 0f; } }

    public float t { get; protected set; }

    float skidRecoverTimeLeft;
    [HideInInspector]
    public Vector2 velocity, velocityT;

    public Vector3 Center
    {
        get
        {
            return transform.position + height * 0.5f * Vector3.up;
        }
    }

    [HideInInspector]
    public int hitsTaken;

    public Fly ingestingFly { get; protected set; }



    [HideInInspector]
    public float attackTimeLeft;
    [HideInInspector]
    public float attackRecoverTimeLeft;




    public bool attackFullyCharged
    {
        get
        {
            return (attackChargeCounter > attackChargeTime) && attackState == AttackState.Charging;
        }
    }

    public float attackChargeM
    {
        get
        {
            return Mathf.Clamp01(attackChargeCounter / attackChargeTime);
        }
    }


    public bool OnGround
    {
        get
        {
            return onGround;
        }
    }

    public Vector2 Velocity
    {
        get
        {
            return velocity;
        }

    }

    public float HitTime
    {
        get
        {
            return timeSinceHit;
        }
    }

    public int FacingDirection
    {
        get
        {
            return facingDir;
        }
    }

    public bool WallSliding
    {
        get; protected set;
    }

    public bool IngestedFly
    {
        get; protected set;
    }


    public EffectsController.Side WallSlideSide { get; protected set; }

    bool onGround;

    float timeSinceHit;

    int terrainLayer, characterLayer, groundLayer, flyLayer;

    public bool RecoveringFromBounce
    {
        get
        {
            return hasReachedApex;
        }
    }

    // Use this for initialization
    void Awake()
    {
        terrainLayer = 1 << LayerMask.NameToLayer("Ground");
        groundLayer = terrainLayer | (1 << LayerMask.NameToLayer("OneWayPlatform"));
        characterLayer = 1 << LayerMask.NameToLayer("Character");
        flyLayer = 1 << LayerMask.NameToLayer("Fly");
    }

    /// <summary>
    /// Characters tick after GameController, before flies, ordered by
    /// player slot so the tick order survives pooling and rollback.
    /// </summary>
    public int SimOrder
    {
        get
        {
            int idx = player != null
                ? GameController.activePlayers.IndexOf(player)
                : -1;
            return 100 + (idx < 0 ? 9 : idx);
        }
    }

    void OnEnable()
    {
        SimulationDriver.Register(this);
    }

    void OnDisable()
    {
        SimulationDriver.Unregister(this);
    }

    void CheckInput()
    {
        if (player == null)
        {
            //InputReader.GetInput(input);
        }
        else
        {
            if (GameController.State == GameState.RoundFinished && !IsWinningPlayer())
                InputReader.ClearInputState(input);
            else
                InputReader.GetInput(player.inputDevice, input);


        }
    }

    public bool IsWinningPlayer()
    {
        if (GameController.isTeamMode)
        {
            if (GameController.GetWinningPlayer() != null)
                return player.team == GameController.GetWinningPlayer().team;
        }
        else
        {
            return player == GameController.GetWinningPlayer();
        }
        return false;
    }

    /// <summary>Captures every mutable sim field into a snapshot.</summary>
    public void SaveTo(ref CharacterSnapshot s)
    {
        s.Position = transform.position;
        s.Velocity = velocity;
        s.VelocityT = velocityT;
        s.AttackDir = attackDir;
        s.TongueDir = tongueDir;
        s.State = (int)state;
        s.AttackState = (int)attackState;
        s.TongueState = (int)tongueState;
        s.WallSlideSide = (int)WallSlideSide;
        s.HitsTaken = hitsTaken;
        s.FacingDir = facingDir;
        s.LastHitByPlayerIndex = lastHitByPlayer != null
            ? GameController.activePlayers.IndexOf(lastHitByPlayer) : -1;
        s.OnGround = onGround;
        s.WallSliding = WallSliding;
        s.TimeSinceHit = timeSinceHit;
        s.TimeBumpTimeLeft = timeBumpTimeLeft;
        s.TimeBumpTimeScale = timeBumpTimeScale;
        s.AttackChargeCounter = attackChargeCounter;
        s.AttackTimeLeft = attackTimeLeft;
        s.AttackRecoverTimeLeft = attackRecoverTimeLeft;
        s.TongueDistance = tongueDistance;
        s.TongueDelayLeft = tongueDelayLeft;
        s.SkidRecoverTimeLeft = skidRecoverTimeLeft;
        s.JumpCooldownLeft = jumpCooldownLeft;
        s.JumpGraceTimeLeft = jumpGraceTimeLeft;
        s.GravityGraceTimeLeft = gravityGraceTimeLeft;
        s.BounceGravityRestoreCounter = bounceGravityRestoreCounter;
        s.CanBounceDodge = canBounceDodge;
        s.HasBounceDodged = hasBounceDodged;
        s.CanBounceTongue = canBounceTongue;
        s.HasBounceTongued = hasBounceTongued;
        s.HasReachedApex = hasReachedApex;
        s.WasHitDownwards = wasHitDownwards;
        s.WasBouncingBeforeTongue = wasBouncingBeforeTongue;
        s.IngestedFly = IngestedFly;
        s.HasIngestingFly = ingestingFly != null;
        SaveInput(ref s.Input);
    }

    /// <summary>Restores every mutable sim field from a snapshot.</summary>
    public void RestoreFrom(in CharacterSnapshot s)
    {
        transform.position = s.Position;
        velocity = s.Velocity;
        velocityT = s.VelocityT;
        attackDir = s.AttackDir;
        tongueDir = s.TongueDir;
        state = (CharacterState)s.State;
        attackState = (AttackState)s.AttackState;
        tongueState = (TongueState)s.TongueState;
        WallSlideSide = (EffectsController.Side)s.WallSlideSide;
        hitsTaken = s.HitsTaken;
        facingDir = s.FacingDir;
        lastHitByPlayer = s.LastHitByPlayerIndex >= 0
            && s.LastHitByPlayerIndex < GameController.activePlayers.Count
            ? GameController.activePlayers[s.LastHitByPlayerIndex] : null;
        onGround = s.OnGround;
        WallSliding = s.WallSliding;
        timeSinceHit = s.TimeSinceHit;
        timeBumpTimeLeft = s.TimeBumpTimeLeft;
        timeBumpTimeScale = s.TimeBumpTimeScale;
        attackChargeCounter = s.AttackChargeCounter;
        attackTimeLeft = s.AttackTimeLeft;
        attackRecoverTimeLeft = s.AttackRecoverTimeLeft;
        tongueDistance = s.TongueDistance;
        tongueDelayLeft = s.TongueDelayLeft;
        skidRecoverTimeLeft = s.SkidRecoverTimeLeft;
        jumpCooldownLeft = s.JumpCooldownLeft;
        jumpGraceTimeLeft = s.JumpGraceTimeLeft;
        gravityGraceTimeLeft = s.GravityGraceTimeLeft;
        bounceGravityRestoreCounter = s.BounceGravityRestoreCounter;
        canBounceDodge = s.CanBounceDodge;
        hasBounceDodged = s.HasBounceDodged;
        canBounceTongue = s.CanBounceTongue;
        hasBounceTongued = s.HasBounceTongued;
        hasReachedApex = s.HasReachedApex;
        wasHitDownwards = s.WasHitDownwards;
        wasBouncingBeforeTongue = s.WasBouncingBeforeTongue;
        IngestedFly = s.IngestedFly;
        ingestingFly = s.HasIngestingFly
            ? GameController.ActiveFlyInstance : null;
        if (s.HasIngestingFly && ingestingFly == null)
        {
            IngestedFly = false;
            if (tongueState == TongueState.RetractingHitFly)
                tongueState = TongueState.Retracting;
        }
        RestoreInput(in s.Input);
    }

    /// <summary>Resets all mutable fields to fresh-spawn values.</summary>
    public void ResetForSpawn(Vector3 spawnPoint)
    {
        var fresh = default(CharacterSnapshot);
        fresh.Position = spawnPoint;
        fresh.FacingDir = 1;
        fresh.LastHitByPlayerIndex = -1;
        RestoreFrom(in fresh);
    }

    void SaveInput(ref InputSnapshot s)
    {
        s.XAxis = input.xAxis;
        s.YAxis = input.yAxis;
        s.LeftTrigger = input.leftTrigger;
        s.RightTrigger = input.rightTrigger;
        s.A = input.aButton;
        s.B = input.bButton;
        s.X = input.xButton;
        s.Y = input.yButton;
        s.Up = input.up;
        s.Down = input.down;
        s.Left = input.left;
        s.Right = input.right;
        s.Start = input.start;
        s.WasA = input.wasAButton;
        s.WasB = input.wasBButton;
        s.WasX = input.wasXButton;
        s.WasY = input.wasYButton;
        s.WasUp = input.wasUp;
        s.WasDown = input.wasDown;
        s.WasLeft = input.wasLeft;
        s.WasRight = input.wasRight;
        s.WasStart = input.wasStart;
    }

    void RestoreInput(in InputSnapshot s)
    {
        input.xAxis = s.XAxis;
        input.yAxis = s.YAxis;
        input.leftTrigger = s.LeftTrigger;
        input.rightTrigger = s.RightTrigger;
        input.aButton = s.A;
        input.bButton = s.B;
        input.xButton = s.X;
        input.yButton = s.Y;
        input.up = s.Up;
        input.down = s.Down;
        input.left = s.Left;
        input.right = s.Right;
        input.start = s.Start;
        input.wasAButton = s.WasA;
        input.wasBButton = s.WasB;
        input.wasXButton = s.WasX;
        input.wasYButton = s.WasY;
        input.wasUp = s.WasUp;
        input.wasDown = s.WasDown;
        input.wasLeft = s.WasLeft;
        input.wasRight = s.WasRight;
        input.wasStart = s.WasStart;
    }

    /// <summary>Mixes this character's mutable sim state into a hash.</summary>
    public uint HashSimState(uint h)
    {
        h = StateHash.Mix(h, (Vector2)transform.position);
        h = StateHash.Mix(h, velocity);
        h = StateHash.Mix(h, (int)state);
        h = StateHash.Mix(h, (int)attackState);
        h = StateHash.Mix(h, (int)tongueState);
        h = StateHash.Mix(h, hitsTaken);
        h = StateHash.Mix(h, facingDir);
        h = StateHash.Mix(h, onGround);
        h = StateHash.Mix(h, timeSinceHit);
        h = StateHash.Mix(h, timeBumpTimeLeft);
        h = StateHash.Mix(h, timeBumpTimeScale);
        h = StateHash.Mix(h, attackChargeCounter);
        h = StateHash.Mix(h, attackTimeLeft);
        h = StateHash.Mix(h, attackRecoverTimeLeft);
        h = StateHash.Mix(h, attackDir);
        h = StateHash.Mix(h, tongueDir);
        h = StateHash.Mix(h, tongueDistance);
        h = StateHash.Mix(h, tongueDelayLeft);
        h = StateHash.Mix(h, skidRecoverTimeLeft);
        h = StateHash.Mix(h, jumpCooldownLeft);
        h = StateHash.Mix(h, jumpGraceTimeLeft);
        h = StateHash.Mix(h, gravityGraceTimeLeft);
        h = StateHash.Mix(h, bounceGravityRestoreCounter);
        h = StateHash.Mix(h, canBounceDodge);
        h = StateHash.Mix(h, hasBounceDodged);
        h = StateHash.Mix(h, canBounceTongue);
        h = StateHash.Mix(h, hasBounceTongued);
        h = StateHash.Mix(h, hasReachedApex);
        h = StateHash.Mix(h, wasHitDownwards);
        h = StateHash.Mix(h, wasBouncingBeforeTongue);
        h = StateHash.Mix(h, WallSliding);
        h = StateHash.Mix(h, (int)WallSlideSide);
        h = StateHash.Mix(h, lastHitByPlayer != null
            ? GameController.activePlayers.IndexOf(lastHitByPlayer) : -1);
        h = StateHash.Mix(h, IngestedFly);
        h = StateHash.Mix(h, ingestingFly != null);
        return h;
    }

    /// <summary>Advances this character by one fixed simulation step.</summary>
    public void SimTick(float dt)
    {
        CheckInput();

        if (TimeBumpActive)
        {
            timeBumpTimeLeft -= dt;
            t = dt * timeBumpTimeScale;
        }
        else
        {
            t = dt;
        }

        if (state != CharacterState.Bouncing)
            AddInputMotionNormal();
        else
            AddInputMotionBouncing();

        RunPhysics();
        ClampMotion();
        ApplyMotionVector();
        if (state == CharacterState.Attacking)
            RunAttack();
        else if (state == CharacterState.Tounge)
            RunTongue();

#if UNITY_EDITOR
        if (input.yButton && !input.wasYButton)
        {
            Vector2 dir = Vector2.zero;
            if (input.right)
                dir = Vector2.right;
            if (input.left)
                dir = -Vector2.right;
            if (input.up)
                dir += Vector2.up;
            if (input.down)
                dir -= Vector2.up;

            GetHit(dir, DeterministicRng.Match.Value, this);
        }
#endif

        CheckDeath();
    }

    void CheckDeath()
    {
        if (transform.position.x < Terrain.LeftKillPoint || transform.position.x > Terrain.RightKillPoint || transform.position.y > Terrain.TopKillPoint || transform.position.y < Terrain.BotKillPoint)
        {
            EffectsController.Side killedOnSide;
            if (transform.position.x < Terrain.LeftKillPoint)
            {
                killedOnSide = EffectsController.Side.Left;
            }
            else if (transform.position.x > Terrain.RightKillPoint)
            {
                killedOnSide = EffectsController.Side.Right;
            }
            else if (transform.position.y > Terrain.TopKillPoint)
            {
                killedOnSide = EffectsController.Side.Top;
            }
            else
            {
                killedOnSide = EffectsController.Side.Bottom;
            }

            GameController.RegisterKill(lastHitByPlayer, player, hitsTaken);

            if (!SimulationDriver.IsResimulating)
            {
                if (lastHitByPlayer == null)
                    FreeLives.SoundController.PlaySoundEffect("KnockoutSuicide", 0.45f, transform.position);
                else if (hitsTaken <= 1)
                    FreeLives.SoundController.PlaySoundEffect("Knockout1", 0.55f, transform.position);
                else if (hitsTaken <= 3)
                    FreeLives.SoundController.PlaySoundEffect("Knockout2", 0.65f, transform.position);
                else
                    FreeLives.SoundController.PlaySoundEffect("Knockout3", 0.75f, transform.position);

                if (lastHitByPlayer != null)
                    EffectsController.CreateSideScorePlum(transform.position, killedOnSide, hitsTaken == 0 ? 1 : hitsTaken, lastHitByPlayer.color);
                else
                    EffectsController.CreateSideScorePlum(
                        transform.position, killedOnSide, -1,
                        player != null ? player.color : Color.white);
            }
            if (transform.position.y > Terrain.TopKillPoint)
            {
                if (!SimulationDriver.IsResimulating)
                    EffectsController.CreateKnockedUpEffect(GetComponent<CharacterAnimator>());
                if (player != null)
                    player.spawnDelay = 3f;

            }
            if (IngestedFly && ingestingFly != null)
            {
                ingestingFly.Release();
                GameController.PoolFly(ingestingFly);
            }
            if (player != null)
            {
                player.character = null;
                player.pooledCharacter = this;
                gameObject.SetActive(false);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }

    public Vector2 attackDir;
    private float jumpCooldownLeft;

    void RunAttack()
    {
        if (attackState == AttackState.Charging)
        {
            attackDir = facingDir * Vector2.right;
            attackChargeCounter += t;
            if (input.up)
            {
                if (!input.left && !input.right)
                {
                    attackDir = Vector2.up;
                    //attackOffset = Vector2.up * 2f;
                }
                else if (input.left)
                {
                    attackDir = Vector2.up + Vector2.left;
                    //attackOffset = new Vector2(-1f, 2f);
                }
                else if (input.right)
                {
                    attackDir = Vector2.up + Vector2.right;

                }
            }
            else if (input.down && !OnGround)
            {
                if (!input.left && !input.right)
                {
                    attackDir = Vector2.down;

                }
                else if (input.left)
                {
                    attackDir = Vector2.down + Vector2.left;

                }
                else if (input.right)
                {
                    attackDir = Vector2.down + Vector2.right;

                }
            }
        }

        if (!input.xButton && input.wasXButton && attackState == AttackState.Charging)
        {
            attackState = AttackState.Attacking;
            SimFx.Play(FxSlot, "BatSwing", 0.4f + attackChargeM * 0.4f, transform.position);
            if (attackChargeM > 0.25f || IngestedFly)
                SimFx.Play(FxSlot, "BatSwingVoice", 0.4f, transform.position);
            attackTimeLeft = attackTime;
            if (attackChargeM > 0.5f)
            {
                if (attackDir == Vector2.left || attackDir == Vector2.right)
                {
                    SimFx.Spawn(FxSlot, () => EffectsController.CreateShingEffect(Center + (Vector3)attackDir * 3f + Vector3.up * 0.2f, attackDir));
                }
                else if (attackDir == Vector2.up)
                {
                    SimFx.Spawn(FxSlot, () => EffectsController.CreateShingEffect(Center + (Vector3)attackDir * 3.75f, attackDir));
                }
                else if (attackDir == Vector2.down)
                {
                    SimFx.Spawn(FxSlot, () => EffectsController.CreateShingEffect(Center + (Vector3)attackDir * 2.75f, attackDir));
                }
                else if (attackDir.y > 0f)
                {
                    SimFx.Spawn(FxSlot, () => EffectsController.CreateShingEffect(Center + (Vector3)attackDir * 2.75f, attackDir));
                }
                else
                {
                    SimFx.Spawn(FxSlot, () => EffectsController.CreateShingEffect(Center + (Vector3)attackDir * 2.75f, attackDir));
                }
            }

        }
        else if (attackState == AttackState.Attacking)
        {
            attackTimeLeft -= t;
            if (attackTimeLeft <= 0f)
            {
                RaycastHit2D[] hits;
                Vector2 attackOffset = Vector2.up;
                float rangeBonus = 0f;
                if (attackChargeM > 0.5f)
                    rangeBonus = attackChargeM;
                float radius = 1.25f;
                if (attackDir.y < 0f)
                    radius = 1.75f;
                hits = Physics2D.CircleCastAll((Vector2)transform.position + attackOffset, radius, attackDir, attackRange + rangeBonus, characterLayer);
                Debug.DrawLine(transform.position + (Vector3)attackOffset, transform.position + (Vector3)attackOffset + (Vector3)attackDir.normalized * (attackRange + rangeBonus + radius), Color.red, 1f);

                if (hits.Length > 0)
                {
                    for (int i = 0; i < hits.Length; i++)
                    {
                        var hitChar = hits[i].collider.gameObject.GetComponent<Character>();
                        if (hitChar != null && hitChar != this)
                        {
                            //Debug.Break();
                            Hit(hitChar, attackDir);
                        }
                    }
                }


                attackState = AttackState.Recovering;
                attackRecoverTimeLeft = attackRecoverTime;
            }
        }
        else if (attackState == AttackState.Recovering)
        {
            attackRecoverTimeLeft -= t;
            if (attackRecoverTimeLeft < 0f)
            {
                attackState = AttackState.Idle;
                state = CharacterState.Normal;
            }
        }
    }

    void BounceFromWall(EffectsController.Side side)
    {
        if (timeSinceHit > 0.35f)
        {
            if (!hasBounceDodged)
                canBounceDodge = true;
            if (!hasBounceTongued)
                canBounceTongue = true;
        }
        if (((side == EffectsController.Side.Left || side == EffectsController.Side.Right) && Mathf.Abs(velocity.x) > 5f)
            || ((side == EffectsController.Side.Bottom || side == EffectsController.Side.Top) && Mathf.Abs(velocity.y) > 5f))
        {
            SimFx.Spawn(FxSlot, () => EffectsController.CreateBouncePuff(transform.position + (Vector3)velocityT, side));
            SimFx.Play(FxSlot, "FrogBounce", 0.5f, transform.position);
            SimFx.Play(FxSlot, "FrogBounceVoice", 0.4f, transform.position);
        }
        if (state == CharacterState.Tounge)
        {
            state = CharacterState.Bouncing;
        }
        wasHitDownwards = false;
    }

    void ClampMotion()
    {
        bool wasOnGround = OnGround;
        bool wasWallSlide = WallSliding;
        onGround = false;
        WallSliding = false;
        RaycastHit2D hit;
        bool bouncedThisFrame = false;

        Vector2 leftFootPos = (Vector2)transform.position - Vector2.right * width * 0.49f;
        if (velocityT.y < 0f)
        {
            Debug.DrawLine(leftFootPos, leftFootPos + Vector2.down * velocityT.y);

            int layer = groundLayer;

            if ((input.down && input.aButton) || (state == CharacterState.Bouncing && wasHitDownwards))
                layer = terrainLayer;

            hit = Physics2D.Raycast(leftFootPos, Vector2.up, velocityT.y, layer);
            if (hit.collider != null)
            {
                onGround = true;

                velocityT.y = hit.point.y - leftFootPos.y;
                if ((state == CharacterState.Bouncing && !RecoveringFromBounce) || (state == CharacterState.Tounge && wasBouncingBeforeTongue))
                {
                    BounceFromWall(EffectsController.Side.Bottom);
                    bouncedThisFrame = true;
                    velocity.y *= -0.5f;
                }
                else
                    velocity.y = 0f;
            }
        }

        Vector2 rightFootPos = (Vector2)transform.position + Vector2.right * width * 0.49f;
        if (velocityT.y < 0f)
        {
            int layer = groundLayer;

            if ((input.down && input.aButton) || (state == CharacterState.Bouncing && wasHitDownwards))
                layer = terrainLayer;

            hit = Physics2D.Raycast(rightFootPos, Vector2.up, velocityT.y, layer);
            if (hit.collider != null)
            {
                onGround = true;
                velocityT.y = hit.point.y - rightFootPos.y;
                if ((state == CharacterState.Bouncing && !RecoveringFromBounce) || (state == CharacterState.Tounge && wasBouncingBeforeTongue))
                {

                    if (!bouncedThisFrame)
                    {
                        BounceFromWall(EffectsController.Side.Bottom);
                        velocity.y *= -0.5f;
                    }
                }
                else
                    velocity.y = 0f;
            }
        }

        Vector2 leftHeadPos = (Vector2)transform.position - Vector2.right * width * 0.49f + Vector2.up * height;
        Vector2 rightHeadPos = (Vector2)transform.position + Vector2.right * width * 0.49f + Vector2.up * height;
        bouncedThisFrame = false;
        if (velocityT.y > 0f)
        {
            hit = Physics2D.Raycast(rightHeadPos, Vector2.up, velocityT.y, terrainLayer);
            if (hit.collider != null)
            {
                velocityT.y = hit.point.y - rightHeadPos.y;
                jumpGraceTimeLeft = 0f;
                if (state == CharacterState.Bouncing)
                {
                    BounceFromWall(EffectsController.Side.Top);
                    bouncedThisFrame = true;
                    velocity.y = -velocity.y;
                }
                else
                    velocity.y = 0f;
            }
            hit = Physics2D.Raycast(leftHeadPos, Vector2.up, velocityT.y, terrainLayer);
            if (hit.collider != null)
            {
                velocityT.y = hit.point.y - leftHeadPos.y;
                jumpGraceTimeLeft = 0f;
                if (state == CharacterState.Bouncing)
                {
                    if (!bouncedThisFrame)
                    {
                        velocity.y = -velocity.y;
                        BounceFromWall(EffectsController.Side.Top);
                    }
                }
                else
                    velocity.y = 0f;
            }
        }


        leftFootPos = (Vector2)transform.position - Vector2.right * width * 0.5f + Vector2.up * 0.05f;
        rightFootPos = (Vector2)transform.position + Vector2.right * width * 0.5f + Vector2.up * 0.05f;
        leftHeadPos = (Vector2)transform.position - Vector2.right * width * 0.5f + Vector2.up * height * 0.98f;
        rightHeadPos = (Vector2)transform.position + Vector2.right * width * 0.5f + Vector2.up * height * 0.98f;

        bouncedThisFrame = false;

        if (velocityT.x > 0)
        {
            hit = Physics2D.Raycast(rightFootPos, Vector2.right, velocityT.x, terrainLayer);
            if (hit.collider != null)
            {
                velocityT.x = hit.point.x - rightFootPos.x;
                if (state == CharacterState.Bouncing)
                {
                    bouncedThisFrame = true;
                    velocity.x = -velocity.x;
                    BounceFromWall(EffectsController.Side.Right);
                }
                else
                {
                    velocity.x = 0;
                    if (input.right && !OnGround && velocity.y < 0f)
                    {
                        WallSliding = true;
                        WallSlideSide = EffectsController.Side.Right;
                    }
                }
            }

            hit = Physics2D.Raycast(rightHeadPos, Vector2.right, velocityT.x, terrainLayer);
            if (hit.collider != null)
            {
                velocityT.x = hit.point.x - rightFootPos.x;
                if (state == CharacterState.Bouncing)
                {
                    if (!bouncedThisFrame)
                    {
                        velocity.x = -velocity.x;
                        BounceFromWall(EffectsController.Side.Right);
                    }
                }
                else
                    velocity.x = 0;
            }
        }

        if (velocityT.x < 0)
        {
            hit = Physics2D.Raycast(leftFootPos, Vector2.right, velocityT.x, terrainLayer);
            if (hit.collider != null)
            {
                velocityT.x = hit.point.x - leftFootPos.x;
                if (state == CharacterState.Bouncing)
                {
                    velocity.x = -velocity.x;
                    BounceFromWall(EffectsController.Side.Left);
                    bouncedThisFrame = true;
                }
                else
                {
                    velocity.x = 0;
                    if (input.left && !OnGround && velocity.y < 0f)
                    {
                        WallSliding = true;
                        WallSlideSide = EffectsController.Side.Left;
                    }
                }
            }
        }

        if (velocityT.x < 0)
        {
            hit = Physics2D.Raycast(leftHeadPos, Vector2.right, velocityT.x, terrainLayer);
            if (hit.collider != null)
            {
                velocityT.x = hit.point.x - leftFootPos.x;
                if (state == CharacterState.Bouncing)
                {
                    if (!bouncedThisFrame)
                    {
                        velocity.x = -velocity.x;
                        BounceFromWall(EffectsController.Side.Left);
                    }
                }
                else
                    velocity.x = 0;
            }
        }

        if (OnGround && !wasOnGround)
        {
            SimFx.Play(FxSlot, "Land", 0.4f, transform.position);
            jumpCooldownLeft = 0.1f;

        }
        if (WallSliding && !wasWallSlide)
        {
            SimFx.Play(FxSlot, "Land", 0.4f, transform.position);
            jumpCooldownLeft = 0.1f;
        }

        if (onGround)
        {
            jumpGraceTimeLeft = jumpGraceTime;
        }
        else if (WallSliding)
        {
            jumpGraceTimeLeft = jumpGraceTime * 0.66f;
        }
        else
        {
            jumpGraceTimeLeft -= t;
            if (velocity.y <= gravityGraceThreshold)
            {
                gravityGraceTimeLeft -= t;
                if (gravityGraceTimeLeft < 0f)
                    gravityGraceTimeLeft = 0f;
            }
        }
    }

    void Hit(Character hitChar, Vector2 hitDir)
    {
        float ingestPowerBoost = 0f;
        if (IngestedFly)
        {
            ingestPowerBoost = 1.5f;
        }

        if (GameController.isTeamMode)
        {
            if (player == null || hitChar.player == null
                || player.team != hitChar.player.team)
                hitChar.GetHit(hitDir, attackChargeM + ingestPowerBoost, this);
        }
        else
        {
            hitChar.GetHit(hitDir, attackChargeM + ingestPowerBoost, this);
        }
    }

    public void GetTongueHit(Vector2 hitDir, Character attacker)
    {

        if (GameController.isTeamMode)
        {
            if (player != null && attacker.player != null
                && player.team == attacker.player.team)
                return;
        }
        if (!IngestedFly && ingestingFly != null)
        {
            ingestingFly.Release();
            ingestingFly = null;
        }
        if (hitDir.y < -0.1f)
            wasHitDownwards = true;
        hasReachedApex = false;
        lastHitByPlayer = attacker.player;
        canBounceDodge = false;
        hasBounceDodged = false;
        canBounceTongue = false;
        hasBounceTongued = false;
        state = CharacterState.Bouncing;
        attackState = AttackState.Idle;
        if (hitDir.y == 0)
            hitDir.y = 0.1f;
        hitDir.Normalize();
        float totalPower = 25f;

        skidRecoverTimeLeft = 0.5f;
        velocity = hitDir.normalized * totalPower;
        timeSinceHit = 0f;
        SimFx.Play(FxSlot, "TongueCollide", 0.5f, transform.position);
        TimeBump(0.75f, 0f);
        attacker.TimeBump(0.5f, 0f);
        //TimeController.TimeBumpCharacters(transform.position, hitsTaken, 15f, true);
        //EffectsController.CreateHitEffect(transform.position + Vector3.up * height * 0.5f, timeBumpTimeLeft);
    }

    public void GetHitByBouncingCharacter(Vector2 hitVelocity, Character bouncer, Player attackingPlayer)
    {
        hitsTaken++;
        if (hitVelocity.y < -0.1f)
            wasHitDownwards = true;
        if (!IngestedFly && ingestingFly != null)
        {
            ingestingFly.Release();
            ingestingFly = null;
        }
        hasReachedApex = false;
        lastHitByPlayer = attackingPlayer;
        canBounceDodge = false;
        hasBounceDodged = false;
        canBounceTongue = false;
        hasBounceTongued = false;
        state = CharacterState.Bouncing;
        attackState = AttackState.Idle;
        if (hitVelocity.y == 0)
            hitVelocity.y = 0.33f;

        skidRecoverTimeLeft = 0.5f;
        velocity = hitVelocity;
        timeSinceHit = 0f;

        TimeBump(bouncer.hitsTaken, 0f);
        bouncer.TimeBump(bouncer.hitsTaken, 0f);
        //EffectsController.CreateHitParticles(transform.position + Vector3.up * height * 0.5f, hitDir, totalPower,(int) (totalPower / 5f));
        SimFx.Play(FxSlot, "CharacterCollision", 0.5f, transform.position);
        TimeController.TimeBumpCharacters(transform.position, bouncer.hitsTaken, 15f, true);
        SimFx.Spawn(FxSlot, () => EffectsController.CreateLocalizedShake(transform.position + Vector3.up * height * 0.5f, velocity, velocity.magnitude, timeBumpTimeLeft));
        SimFx.Spawn(FxSlot, () => EffectsController.CreateHitEffect((Center + bouncer.Center) * 0.5f, timeBumpTimeLeft, false));
    }

    public void GetHit(Vector2 hitDir, float power, Character attacker)
    {
        hitsTaken++;
        print("hit power: " + power);
        if (IngestedFly)
        {
            IngestedFly = false;
            if (ingestingFly != null)
            {
                ingestingFly.transform.position = Center;
                ingestingFly.Release();
                ingestingFly.gameObject.SetActive(true);
            }
        }
        if (ingestingFly != null)
        {
            ingestingFly.Release();
            ingestingFly = null;
        }
        if (hitDir.y < -0.1f)
            wasHitDownwards = true;
        hasReachedApex = false;
        lastHitByPlayer = attacker.player;
        canBounceDodge = false;
        hasBounceDodged = false;
        canBounceTongue = false;
        hasBounceTongued = false;
        state = CharacterState.Bouncing;
        attackState = AttackState.Idle;
        if (hitDir.y == 0)
            hitDir.y = 0.33f;
        hitDir.Normalize();
        float totalPower = 10f + hitsTaken * 10f + power * 30f;

        skidRecoverTimeLeft = 0.5f;
        velocity = hitDir.normalized * totalPower;
        timeSinceHit = 0f;

        TimeBump(hitsTaken + power, 0f);
        attacker.TimeBump(hitsTaken + power, 0f);
        //EffectsController.CreateHitParticles(transform.position + Vector3.up * height * 0.5f, hitDir, totalPower,(int) (totalPower / 5f));
        SimFx.Play(FxSlot, "BatHit" + Mathf.Clamp(hitsTaken, 1, 5).ToString(), 0.5f, transform.position);
        SimFx.Play(FxSlot, "BatHitVoice" + Mathf.Clamp(hitsTaken, 1, 5).ToString(), 0.5f, transform.position);
        TimeController.TimeBumpCharacters(transform.position, hitsTaken + power, 15f, true);
        SimFx.Spawn(FxSlot, () => EffectsController.CreateLocalizedShake(transform.position + Vector3.up * height * 0.5f, velocity, velocity.magnitude, timeBumpTimeLeft));
        SimFx.Spawn(FxSlot, () => EffectsController.CreateHitEffect(transform.position + Vector3.up * height * 0.5f, timeBumpTimeLeft, power >= 1f));
        if (power >= 1f)
        {
            SimFx.Spawn(FxSlot, () => EffectsController.ShakeCamera(hitDir, hitsTaken * 0.75f));
        }
    }

    void ApplyMotionVector()
    {
        var pos = transform.position;
        pos += (Vector3)velocityT;

        if (state == CharacterState.Attacking)
            pos.z = -0.1f;
        else
            pos.z = 0f;

        transform.position = pos;
    }

    void RunPhysicsBouncing()
    {
        timeSinceHit += t;
        float gravityThisFrame = bounceGravityMin;
        if (timeSinceHit > bounceGravityRestoreDelay)
        {
            bounceGravityRestoreCounter += t;
            gravityThisFrame = Mathf.Lerp(bounceGravityMin, bounceGravityMax, Mathf.Clamp01(bounceGravityRestoreCounter / bounceGravityRestoreTime));


        }

        if (velocity.y >= 0f && velocity.y - gravityThisFrame * t < 0f)
        {
            hasReachedApex = true;
            if (hasReachedApex && !hasBounceDodged)
                canBounceDodge = true;
            if (!hasBounceTongued)
                canBounceTongue = true;
        }

        if (timeSinceHit > 1f && !hasBounceDodged)
        {
            canBounceDodge = true;

        }

        if (timeSinceHit > 1f && !hasBounceTongued)
        {
            canBounceTongue = true;
        }

        if (velocity.y > maxFallSpeed)
            velocity.y -= gravityThisFrame * t;

        //if (velocity.y < maxFallSpeed)
        //velocity.y = maxFallSpeed;

        if (GameController.charactersBounceEachOther && !GameController.isTeamMode && !hasBounceDodged && hitsTaken >= 1 && !OnGround)
        {
            var cols = Physics2D.OverlapCircleAll((Vector2)transform.position + Vector2.up * height * 0.5f, 0.5f, characterLayer);
            {
                if (cols.Length > 0)
                {
                    foreach (var col in cols)
                    {
                        var chr = col.GetComponent<Character>();
                        if (chr != null && chr != this && chr.state != CharacterState.Bouncing && chr.player != lastHitByPlayer && !(chr.state == CharacterState.Attacking && chr.attackState == AttackState.Attacking))
                        {
                            if (!RecoveringFromBounce || !GameController.onlyBounceBeforeRecover)
                            {
                                if (!(TimeBumpActive && timeBumpTimeScale == 0))
                                {
                                    chr.GetHitByBouncingCharacter(velocity * 0.75f, this, lastHitByPlayer);

                                    if (GameController.weirdBounceTrajectories)
                                    {
                                        Vector3 relativePos = transform.position - chr.transform.position;
                                        velocity = velocity.magnitude * relativePos.normalized * 0.75f;
                                    }
                                }
                            }

                        }
                    }
                }
            }
        }


        if (onGround && RecoveringFromBounce)
        {
            if (state == CharacterState.Tounge)
                state = CharacterState.Bouncing;
            if (velocity.x > 0)
            {
                velocity.x -= t * skidAccel;
                if (velocity.x < 0)
                {
                    velocity.x = 0;
                }
            }
            else if (velocity.x < 0)
            {
                velocity.x += t * skidAccel;
                if (velocity.x > 0)
                {
                    velocity.x = 0;
                }
            }

            if (Mathf.Abs(velocity.x) < maxRunSpeed)
            {
                skidRecoverTimeLeft -= t;
                if (skidRecoverTimeLeft <= 0f)
                    StopBouncing();
            }
        }
    }

    void RunPhysicsNormal()
    {
        if (input.aButton && velocity.y <= gravityGraceThreshold && gravityGraceTimeLeft > 0f)
        {
            float gravM = 1f - (gravityGraceTimeLeft) / gravityGraceTime;
            velocity.y -= gravity * gravM * t;
        }
        else
            velocity.y -= gravity * t;
        if (WallSliding)
        {
            if (velocity.y < maxFallSpeedWallSlide)
                velocity.y = maxFallSpeedWallSlide;
        }
        else
        {
            if (velocity.y < maxFallSpeed)
                velocity.y = maxFallSpeed;
        }
    }

    void RunPhysics()
    {



        if (state == CharacterState.Bouncing)
        {
            RunPhysicsBouncing();
        }
        else if (state == CharacterState.Tounge && tongueState == TongueState.AttachedToTerrain)
        {
            velocity = tongueDir * tongueRetractSpeedLatched;
        }
        else if (state == CharacterState.Tounge)
        {
            if (wasBouncingBeforeTongue)
                RunPhysicsBouncing();
            else
                RunPhysicsNormal();
        }
        else
        {
            RunPhysicsNormal();
        }
        velocityT = velocity * t;
        if (state != CharacterState.Attacking && state != CharacterState.Tounge)
        {
            if (velocity.x > 0f)
                facingDir = 1;
            if (velocity.x < 0f)
                facingDir = -1;
        }

        jumpCooldownLeft -= t;

        if (state == CharacterState.Tounge)
        {
            if (tongueDir.x > 0)
                facingDir = 1;
            else if (tongueDir.x < 0)
                facingDir = -1;
        }
    }

    void AddInputMotionBouncing()
    {
        if (input.right)
        {
            if (velocity.x < maxRunSpeed * 0.5f)
                velocity.x += bounceAccel * t;
        }
        else if (input.left)
        {
            if (velocity.x > -maxRunSpeed * 0.5f)
                velocity.x -= bounceAccel * t;
        }

        if (canBounceDodge && input.aButton && !OnGround)
        {

            canBounceDodge = false;
            hasBounceDodged = true;

            Vector2 dir = Vector2.up;
            if (input.up)
                dir += Vector2.up;
            if (input.down)
                dir += Vector2.down;
            if (input.left)
                dir += Vector2.left;
            if (input.right)
                dir += Vector2.right;

            if (dir == Vector2.zero)
                dir = Vector2.up;
            dir.Normalize();

            velocity = dir * bounceDodgePower;
            bounceGravityRestoreCounter = 0f;

        }

        if (input.bButton && !input.wasBButton)
        {
            StartTongueAttack();
        }
    }

    void StopBouncing()
    {
        state = CharacterState.Normal;
        wasHitDownwards = false;
        hitsTaken = 0;
        lastHitByPlayer = null;
    }


    void AddInputMotionNormal()
    {

        if (input.right && state == CharacterState.Normal)
        {
            facingDir = 1;
            if (onGround)
            {
                if (velocity.x < 0f)
                {
                    if (velocity.x < maxRunSpeed * 0.9f)
                    {
                        SimFx.Spawn(FxSlot, () => EffectsController.CreateTurnAroundPuff(transform.position, 1f));
                    }
                    velocity.x = 0f;

                }
                velocity.x += t * runAccel;
            }
            else
            {
                velocity.x += t * airAccel;
            }
            if (velocity.x > maxRunSpeed)
                velocity.x = maxRunSpeed;
        }
        else if (input.left && state == CharacterState.Normal)
        {
            facingDir = -1;
            if (onGround)
            {
                if (velocity.x > 0f)
                {
                    if (velocity.x > -maxRunSpeed * 0.9f)
                    {
                        SimFx.Spawn(FxSlot, () => EffectsController.CreateTurnAroundPuff(transform.position, -1f));
                    }
                    velocity.x = 0f;
                }
                velocity.x -= t * runAccel;
            }
            else
            {
                velocity.x -= t * airAccel;
            }
            if (velocity.x < -maxRunSpeed)
                velocity.x = -maxRunSpeed;
        }
        else
        {
            if (onGround)
            {
                if (velocity.x > 0)
                {
                    velocity.x -= t * runAccel;
                    if (velocity.x < 0)
                        velocity.x = 0;
                }
                else if (velocity.x < 0)
                {
                    velocity.x += t * runAccel;
                    if (velocity.x > 0)
                        velocity.x = 0;
                }
            }
        }

        if (input.xButton)
        {
            if (state == CharacterState.Normal)
            {
                state = CharacterState.Attacking;
                if (attackState == AttackState.Idle)
                {
                    attackState = AttackState.Charging;
                    SimFx.Play(FxSlot, "BatChargeUp", 0.5f, transform.position);
                    attackChargeCounter = 0f;
                }
            }

            if (attackState == AttackState.Charging)
            {
                if (input.right)
                {
                    facingDir = 1;
                }
                else if (input.left)
                {
                    facingDir = -1;
                }
            }
        }

        if (input.bButton)
        {
            if (state == CharacterState.Normal && !input.wasBButton)
            {
                StartTongueAttack();
            }
        }

        if (input.aButton && !input.down && (jumpCooldownLeft <= 0f || (!input.wasAButton && state != CharacterState.Attacking)))
        {
            if (onGround || WallSliding)
            {

                velocity.y = jumpVel;
                gravityGraceTimeLeft = gravityGraceTime;

                if (WallSliding)
                {
                    if (WallSlideSide == EffectsController.Side.Left)
                        velocity.x = maxRunSpeed;
                    else if (WallSlideSide == EffectsController.Side.Right)
                        velocity.x = -maxRunSpeed;
                }



                //Debug.Break();
                SimFx.Play(FxSlot, "Jump", 0.4f, transform.position);
                if (WallSliding)
                    SimFx.Spawn(FxSlot, () => EffectsController.CreateJumpPuffStraight(transform.position, WallSlideSide));
                else
                    SimFx.Spawn(FxSlot, () => EffectsController.CreateJumpPuffStraight(transform.position, EffectsController.Side.Bottom));

            }
            else if (jumpGraceTimeLeft > 0f) // && (velocity.y > 0f || !input.wasAButton))
            {
                velocity.y = jumpVel;
            }
        }


    }

    /// <summary>
    /// Deterministic tongue-vs-tongue clash test computed from sim
    /// state only. The TongueTip collider is positioned by the render
    /// path (CharacterAnimator.LateUpdate), so a physics query against
    /// it reads frame-dependent positions and desyncs online peers;
    /// 1.5 = the old 1.0 query radius plus the tip collider radius.
    /// </summary>
    bool TongueClashesWith(Character chr, Vector2 myTip)
    {
        const float clashRangeSq = 1.5f * 1.5f;
        if (chr == null || chr == this)
            return false;
        if (chr.state != CharacterState.Tounge)
            return false;
        if (chr.tongueState == TongueState.HitEnemyTongueStunned)
            return false;
        Vector2 tip = (Vector2)chr.transform.position
            + chr.tongueOrigin + chr.tongueDir * chr.tongueDistance;
        return (tip - myTip).sqrMagnitude <= clashRangeSq;
    }

    void RunTongue()
    {
        if (tongueDelayLeft > 0f)
        {
            tongueDelayLeft -= t;
            return;
        }

        if (tongueState == TongueState.Extending)
        {
            tongueDistance += tongueSpeed * t;

            if (tongueDir.x != 0f)
            {
                if (Mathf.Sign(tongueDir.x) != Mathf.Sign(velocity.x))
                {
                    tongueDistance += Mathf.Abs(velocity.x) * t;
                }
            }
            if (tongueDir.y != 0f)
            {
                if (Mathf.Sign(tongueDir.y) != Mathf.Sign(velocity.y))
                {
                    tongueDistance += Mathf.Abs(velocity.y) * t;
                }
            }
            int layer = terrainLayer;
            if (tongueDir.y < 0f)
                layer = groundLayer;

            var fly = Physics2D.OverlapCircle((Vector2)transform.position + tongueOrigin + tongueDir * tongueDistance, 0.5f, flyLayer);
            if (fly != null)
            {
                var flyComp = fly.GetComponent<Fly>();
                if (flyComp.TryClaim(this))
                {
                    tongueState = TongueState.RetractingHitFly;
                    ingestingFly = flyComp;
                    SimFx.Play(FxSlot, "TongueCollideSurface", 0.5f, TongueTipPos);
                }
            }
            else
                if (Physics2D.OverlapCircle((Vector2)transform.position + tongueOrigin + tongueDir * tongueDistance, 0.5f, layer) != null)
            {
                if (tongueDistance > minimumTongueDistance)
                {
                    tongueState = TongueState.AttachedToTerrain;
                    SimFx.Play(FxSlot, "TongueCollideSurface", 0.5f, TongueTipPos);
                }

            }
            else
            {
                if (tongueDistance > minimumTongueDistance)
                {
                    bool hitTongue = false;
                    Vector2 myTip = (Vector2)transform.position
                        + tongueOrigin + tongueDir * tongueDistance;
                    for (int i = 0;
                        i < GameController.activePlayers.Count; i++)
                    {
                        var chr =
                            GameController.activePlayers[i].character;
                        if (TongueClashesWith(chr, myTip))
                        {
                            tongueState =
                                TongueState.RetractingHitEnemyTongue;
                            SimFx.Spawn(FxSlot, () =>
                                EffectsController.CreateTongueHitEffect(
                                    TongueTipPos, 0.2f));
                            hitTongue = true;
                        }
                    }
                    if (!hitTongue)
                    {
                        var cols = Physics2D.OverlapCircleAll((Vector2)transform.position + tongueOrigin + tongueDir * tongueDistance, 1f, characterLayer);
                        {
                            if (cols.Length > 0)
                            {
                                foreach (var col in cols)
                                {
                                    var chr = col.GetComponent<Character>();
                                    if (chr != null && chr != this)
                                    {
                                        if (!GameController.isTeamMode
                                            || chr.player == null
                                            || player == null
                                            || chr.player.team != player.team)
                                        {
                                            chr.GetTongueHit(-tongueDir, this);
                                            tongueState = TongueState.RetractingHitEnemy;
                                            SimFx.Spawn(FxSlot, () => EffectsController.CreateTongueHitEffect(TongueTipPos, 0.2f));
                                        }

                                    }
                                }
                            }
                        }
                    }
                }
            }


            if (tongueState == TongueState.Extending)
            {
                if (tongueDistance > tongueRange)
                    tongueState = TongueState.Retracting;

                //if (!input.bButton && tongueDistance > minimumTongueDistance)
                //    tongueState = TongueState.Retracting;
            }

        }
        else if (tongueState == TongueState.AttachedToTerrain)
        {
            tongueDistance -= tongueRetractSpeedLatched * t;
            if (tongueDistance <= 0f)
            {
                if (wasBouncingBeforeTongue)
                {
                    //if (tongueDir.y > 0f && tongueDir.x != 0f)
                    bounceGravityRestoreCounter = 0f;

                    state = CharacterState.Bouncing;
                }
                else
                {
                    state = CharacterState.Normal;
                    jumpGraceTimeLeft = jumpGraceTime;
                }
            }
        }
        else if (tongueState == TongueState.Retracting)
        {
            tongueDistance -= tongueRetractSpeedMissed * t;
            if (tongueDistance <= 0f)
            {
                if (wasBouncingBeforeTongue)
                    state = CharacterState.Bouncing;
                else
                    state = CharacterState.Normal;
            }
        }
        else if (tongueState == TongueState.RetractingHitEnemy)
        {
            tongueDistance -= tongueSpeed * t;
            if (tongueDistance <= 0f)
            {
                if (wasBouncingBeforeTongue)
                    state = CharacterState.Bouncing;
                else
                    state = CharacterState.Normal;
            }
        }
        else if (tongueState == TongueState.RetractingHitEnemyTongue)
        {
            tongueDistance -= tongueRetractSpeedMissed * t;
            if (tongueDistance <= 0f)
            {
                if (wasBouncingBeforeTongue)
                    state = CharacterState.Bouncing;
                else
                {
                    tongueState = TongueState.HitEnemyTongueStunned;
                    tongueDelayLeft = 0.65f;
                }
            }
        }
        else if (tongueState == TongueState.RetractingHitFly)
        {
            if (ingestingFly == null)
            {
                tongueState = TongueState.Retracting;
            }
            else
            {
                tongueDistance -= tongueRetractSpeedMissed * t;
                ingestingFly.transform.position = TongueTipPos;
                if (tongueDistance <= 0f)
                {
                    if (wasBouncingBeforeTongue)
                        StopBouncing();
                    {
                        SimFx.Play(FxSlot, "Burp", 0.5f, TongueTipPos);
                        tongueState = TongueState.HitFlyBurping;
                        tongueDelayLeft = 0.65f;
                        IngestedFly = true;
                        ingestingFly.Release();
                        ingestingFly.gameObject.SetActive(false);
                    }
                }
            }
        }
        else if (tongueState == TongueState.HitEnemyTongueStunned || tongueState == TongueState.HitFlyBurping)
        {
            state = CharacterState.Normal;
        }

    }


    Vector2 TongueTipPos
    {
        get
        {
            return (Vector2)transform.position + tongueOrigin + tongueDir * tongueDistance;
        }

    }

    void StartTongueAttack()
    {
        if (state == CharacterState.Bouncing)
        {
            if (!canBounceTongue)
                return;
            wasBouncingBeforeTongue = true;
            canBounceTongue = false;
            hasBounceDodged = true;
        }
        else
            wasBouncingBeforeTongue = false;

        state = CharacterState.Tounge;
        tongueDistance = 0f;
        tongueState = TongueState.Extending;
        tongueDelayLeft = tongueDelay;
        SimFx.Play(FxSlot, "TongueLaunch", 0.5f, transform.position);
        tongueDir = facingDir * Vector2.right;
        if (input.right)
            tongueDir = Vector2.right;
        else if (input.left)
            tongueDir = Vector2.left;
        if (input.up)
        {
            if (!input.left && !input.right)
            {
                tongueDir = Vector2.up;
            }
            else if (input.left)
            {
                tongueDir = Vector2.up + Vector2.left;
            }
            else if (input.right)
            {
                tongueDir = Vector2.up + Vector2.right;
            }
        }
        else if (input.down && !OnGround)
        {
            if (!input.left && !input.right)
            {
                tongueDir = Vector2.down;

            }
            else if (input.left)
            {
                tongueDir = Vector2.down + Vector2.left;

            }
            else if (input.right)
            {
                tongueDir = Vector2.down + Vector2.right;

            }
        }
        tongueDir = tongueDir.normalized;


    }


}

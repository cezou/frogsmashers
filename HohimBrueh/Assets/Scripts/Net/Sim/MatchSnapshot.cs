using UnityEngine;

namespace FrogSmashers.Net.Sim
{
    /// <summary>Captured InputState fields (current and previous).</summary>
    public struct InputSnapshot
    {
        public float XAxis, YAxis, LeftTrigger, RightTrigger;
        public bool A, B, X, Y, Up, Down, Left, Right, Start;
        public bool WasA, WasB, WasX, WasY;
        public bool WasUp, WasDown, WasLeft, WasRight, WasStart;
    }

    /// <summary>Full mutable state of one Character.</summary>
    public struct CharacterSnapshot
    {
        public Vector3 Position;
        public Vector2 Velocity, VelocityT, AttackDir, TongueDir;
        public int State, AttackState, TongueState, WallSlideSide;
        public int HitsTaken, FacingDir, LastHitByPlayerIndex;
        public bool OnGround, WallSliding;
        public float TimeSinceHit, TimeBumpTimeLeft, TimeBumpTimeScale;
        public float AttackChargeCounter, AttackTimeLeft;
        public float AttackRecoverTimeLeft;
        public float TongueDistance, TongueDelayLeft;
        public float SkidRecoverTimeLeft, JumpCooldownLeft;
        public float JumpGraceTimeLeft, GravityGraceTimeLeft;
        public float BounceGravityRestoreCounter;
        public bool CanBounceDodge, HasBounceDodged;
        public bool CanBounceTongue, HasBounceTongued;
        public bool HasReachedApex, WasHitDownwards;
        public bool WasBouncingBeforeTongue;
        public bool IngestedFly, HasIngestingFly;
        public InputSnapshot Input;
    }

    /// <summary>Full mutable state of the fly.</summary>
    public struct FlySnapshot
    {
        public bool Active;
        public Vector3 Position;
        public Vector2 Velocity, TargetVelocity;
        public bool BeingIngested;
        public int IngestedByPlayerIndex;
        public float IngestTimeout, UpdateDirectionDelay;
    }

    /// <summary>Per-player round state plus optional character.</summary>
    public struct PlayerSnapshot
    {
        public int Score, RoundWins;
        public float SpawnDelay;
        public bool HasCharacter;
        public CharacterSnapshot Character;
    }

    /// <summary>
    /// Complete simulation state for one tick. Instances are pooled in
    /// <see cref="SnapshotRingBuffer"/> and rewritten in place.
    /// </summary>
    public class MatchSnapshot
    {
        public const int MaxPlayers = 6;

        public bool Valid;
        public uint Tick;
        public ulong RngState;
        public int GameState;
        public float FlySpawnDelay, FinishDelay;
        public int WinningPlayerIndex;
        public int RedTeamScore, BlueTeamScore;
        public bool HasFly;
        public FlySnapshot Fly;
        public int PlayerCount;
        public PlayerSnapshot[] Players = new PlayerSnapshot[MaxPlayers];
    }
}

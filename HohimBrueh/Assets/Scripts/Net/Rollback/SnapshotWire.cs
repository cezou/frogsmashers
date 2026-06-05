using FrogSmashers.Net.Sim;
using Unity.Netcode;
using UnityEngine;

namespace FrogSmashers.Net.Rollback
{
    /// <summary>
    /// Binary serialization of MatchSnapshot for authoritative state
    /// correction messages (host to drifting client).
    /// </summary>
    public static class SnapshotWire
    {
        /// <summary>Worst-case serialized size in bytes.</summary>
        public const int MaxBytes = 2048;

        /// <summary>Writes a snapshot to the buffer.</summary>
        public static void Write(
            ref FastBufferWriter writer, MatchSnapshot snap)
        {
            writer.WriteValueSafe(snap.Tick);
            writer.WriteValueSafe(snap.RngState);
            writer.WriteValueSafe(snap.GameState);
            writer.WriteValueSafe(snap.FlySpawnDelay);
            writer.WriteValueSafe(snap.FinishDelay);
            writer.WriteValueSafe(snap.WinningPlayerIndex);
            writer.WriteValueSafe(snap.RedTeamScore);
            writer.WriteValueSafe(snap.BlueTeamScore);
            writer.WriteValueSafe(snap.HasFly);
            if (snap.HasFly)
                WriteFly(ref writer, in snap.Fly);
            writer.WriteValueSafe(snap.PlayerCount);
            for (int i = 0; i < snap.PlayerCount; i++)
            {
                writer.WriteValueSafe(snap.Players[i].Score);
                writer.WriteValueSafe(snap.Players[i].RoundWins);
                writer.WriteValueSafe(snap.Players[i].SpawnDelay);
                writer.WriteValueSafe(snap.Players[i].HasCharacter);
                if (snap.Players[i].HasCharacter)
                    WriteCharacter(ref writer,
                        in snap.Players[i].Character);
            }
        }

        /// <summary>Reads a snapshot from the buffer.</summary>
        public static void Read(
            ref FastBufferReader reader, MatchSnapshot snap)
        {
            reader.ReadValueSafe(out snap.Tick);
            reader.ReadValueSafe(out snap.RngState);
            reader.ReadValueSafe(out snap.GameState);
            reader.ReadValueSafe(out snap.FlySpawnDelay);
            reader.ReadValueSafe(out snap.FinishDelay);
            reader.ReadValueSafe(out snap.WinningPlayerIndex);
            reader.ReadValueSafe(out snap.RedTeamScore);
            reader.ReadValueSafe(out snap.BlueTeamScore);
            reader.ReadValueSafe(out snap.HasFly);
            if (snap.HasFly)
                ReadFly(ref reader, ref snap.Fly);
            reader.ReadValueSafe(out snap.PlayerCount);
            for (int i = 0; i < snap.PlayerCount; i++)
            {
                reader.ReadValueSafe(out snap.Players[i].Score);
                reader.ReadValueSafe(out snap.Players[i].RoundWins);
                reader.ReadValueSafe(out snap.Players[i].SpawnDelay);
                reader.ReadValueSafe(out snap.Players[i].HasCharacter);
                if (snap.Players[i].HasCharacter)
                    ReadCharacter(ref reader,
                        ref snap.Players[i].Character);
            }
            snap.Valid = true;
        }

        static void WriteFly(
            ref FastBufferWriter w, in FlySnapshot s)
        {
            w.WriteValueSafe(s.Active);
            w.WriteValueSafe(s.Position);
            w.WriteValueSafe(s.Velocity);
            w.WriteValueSafe(s.TargetVelocity);
            w.WriteValueSafe(s.BeingIngested);
            w.WriteValueSafe(s.IngestedByPlayerIndex);
            w.WriteValueSafe(s.IngestTimeout);
            w.WriteValueSafe(s.UpdateDirectionDelay);
        }

        static void ReadFly(ref FastBufferReader r, ref FlySnapshot s)
        {
            r.ReadValueSafe(out s.Active);
            r.ReadValueSafe(out s.Position);
            r.ReadValueSafe(out s.Velocity);
            r.ReadValueSafe(out s.TargetVelocity);
            r.ReadValueSafe(out s.BeingIngested);
            r.ReadValueSafe(out s.IngestedByPlayerIndex);
            r.ReadValueSafe(out s.IngestTimeout);
            r.ReadValueSafe(out s.UpdateDirectionDelay);
        }

        static void WriteCharacter(
            ref FastBufferWriter w, in CharacterSnapshot s)
        {
            w.WriteValueSafe(s.Position);
            w.WriteValueSafe(s.Velocity);
            w.WriteValueSafe(s.VelocityT);
            w.WriteValueSafe(s.AttackDir);
            w.WriteValueSafe(s.TongueDir);
            w.WriteValueSafe(s.State);
            w.WriteValueSafe(s.AttackState);
            w.WriteValueSafe(s.TongueState);
            w.WriteValueSafe(s.WallSlideSide);
            w.WriteValueSafe(s.HitsTaken);
            w.WriteValueSafe(s.FacingDir);
            w.WriteValueSafe(s.LastHitByPlayerIndex);
            w.WriteValueSafe(s.OnGround);
            w.WriteValueSafe(s.WallSliding);
            w.WriteValueSafe(s.TimeSinceHit);
            w.WriteValueSafe(s.TimeBumpTimeLeft);
            w.WriteValueSafe(s.TimeBumpTimeScale);
            w.WriteValueSafe(s.AttackChargeCounter);
            w.WriteValueSafe(s.AttackTimeLeft);
            w.WriteValueSafe(s.AttackRecoverTimeLeft);
            w.WriteValueSafe(s.TongueDistance);
            w.WriteValueSafe(s.TongueDelayLeft);
            w.WriteValueSafe(s.SkidRecoverTimeLeft);
            w.WriteValueSafe(s.JumpCooldownLeft);
            w.WriteValueSafe(s.JumpGraceTimeLeft);
            w.WriteValueSafe(s.GravityGraceTimeLeft);
            w.WriteValueSafe(s.BounceGravityRestoreCounter);
            w.WriteValueSafe(s.CanBounceDodge);
            w.WriteValueSafe(s.HasBounceDodged);
            w.WriteValueSafe(s.CanBounceTongue);
            w.WriteValueSafe(s.HasBounceTongued);
            w.WriteValueSafe(s.HasReachedApex);
            w.WriteValueSafe(s.WasHitDownwards);
            w.WriteValueSafe(s.WasBouncingBeforeTongue);
            w.WriteValueSafe(s.IngestedFly);
            w.WriteValueSafe(s.HasIngestingFly);
            WriteInput(ref w, in s.Input);
        }

        static void WriteInput(
            ref FastBufferWriter w, in InputSnapshot s)
        {
            w.WriteValueSafe(s.XAxis);
            w.WriteValueSafe(s.YAxis);
            w.WriteValueSafe(s.LeftTrigger);
            w.WriteValueSafe(s.RightTrigger);
            w.WriteValueSafe(s.A);
            w.WriteValueSafe(s.B);
            w.WriteValueSafe(s.X);
            w.WriteValueSafe(s.Y);
            w.WriteValueSafe(s.Up);
            w.WriteValueSafe(s.Down);
            w.WriteValueSafe(s.Left);
            w.WriteValueSafe(s.Right);
            w.WriteValueSafe(s.Start);
            w.WriteValueSafe(s.WasA);
            w.WriteValueSafe(s.WasB);
            w.WriteValueSafe(s.WasX);
            w.WriteValueSafe(s.WasY);
            w.WriteValueSafe(s.WasUp);
            w.WriteValueSafe(s.WasDown);
            w.WriteValueSafe(s.WasLeft);
            w.WriteValueSafe(s.WasRight);
            w.WriteValueSafe(s.WasStart);
        }

        static void ReadCharacter(
            ref FastBufferReader r, ref CharacterSnapshot s)
        {
            r.ReadValueSafe(out s.Position);
            r.ReadValueSafe(out s.Velocity);
            r.ReadValueSafe(out s.VelocityT);
            r.ReadValueSafe(out s.AttackDir);
            r.ReadValueSafe(out s.TongueDir);
            r.ReadValueSafe(out s.State);
            r.ReadValueSafe(out s.AttackState);
            r.ReadValueSafe(out s.TongueState);
            r.ReadValueSafe(out s.WallSlideSide);
            r.ReadValueSafe(out s.HitsTaken);
            r.ReadValueSafe(out s.FacingDir);
            r.ReadValueSafe(out s.LastHitByPlayerIndex);
            r.ReadValueSafe(out s.OnGround);
            r.ReadValueSafe(out s.WallSliding);
            r.ReadValueSafe(out s.TimeSinceHit);
            r.ReadValueSafe(out s.TimeBumpTimeLeft);
            r.ReadValueSafe(out s.TimeBumpTimeScale);
            r.ReadValueSafe(out s.AttackChargeCounter);
            r.ReadValueSafe(out s.AttackTimeLeft);
            r.ReadValueSafe(out s.AttackRecoverTimeLeft);
            r.ReadValueSafe(out s.TongueDistance);
            r.ReadValueSafe(out s.TongueDelayLeft);
            r.ReadValueSafe(out s.SkidRecoverTimeLeft);
            r.ReadValueSafe(out s.JumpCooldownLeft);
            r.ReadValueSafe(out s.JumpGraceTimeLeft);
            r.ReadValueSafe(out s.GravityGraceTimeLeft);
            r.ReadValueSafe(out s.BounceGravityRestoreCounter);
            r.ReadValueSafe(out s.CanBounceDodge);
            r.ReadValueSafe(out s.HasBounceDodged);
            r.ReadValueSafe(out s.CanBounceTongue);
            r.ReadValueSafe(out s.HasBounceTongued);
            r.ReadValueSafe(out s.HasReachedApex);
            r.ReadValueSafe(out s.WasHitDownwards);
            r.ReadValueSafe(out s.WasBouncingBeforeTongue);
            r.ReadValueSafe(out s.IngestedFly);
            r.ReadValueSafe(out s.HasIngestingFly);
            ReadInput(ref r, ref s.Input);
        }

        static void ReadInput(
            ref FastBufferReader r, ref InputSnapshot s)
        {
            r.ReadValueSafe(out s.XAxis);
            r.ReadValueSafe(out s.YAxis);
            r.ReadValueSafe(out s.LeftTrigger);
            r.ReadValueSafe(out s.RightTrigger);
            r.ReadValueSafe(out s.A);
            r.ReadValueSafe(out s.B);
            r.ReadValueSafe(out s.X);
            r.ReadValueSafe(out s.Y);
            r.ReadValueSafe(out s.Up);
            r.ReadValueSafe(out s.Down);
            r.ReadValueSafe(out s.Left);
            r.ReadValueSafe(out s.Right);
            r.ReadValueSafe(out s.Start);
            r.ReadValueSafe(out s.WasA);
            r.ReadValueSafe(out s.WasB);
            r.ReadValueSafe(out s.WasX);
            r.ReadValueSafe(out s.WasY);
            r.ReadValueSafe(out s.WasUp);
            r.ReadValueSafe(out s.WasDown);
            r.ReadValueSafe(out s.WasLeft);
            r.ReadValueSafe(out s.WasRight);
            r.ReadValueSafe(out s.WasStart);
        }
    }
}

using System.Reflection;
using FrogSmashers.Net;
using FrogSmashers.Net.Transport;
using NUnit.Framework;

namespace FrogSmashers.EditorTests
{
    /// <summary>
    /// Pins the online match-flow hardening invariants: standings
    /// ignore departed slots (their wins stay in the per-slot table),
    /// team-mode clinching compares team totals, the lobby-return
    /// level sentinel can never collide with a real level index, and
    /// the byte session epoch wraps deliberately.
    /// </summary>
    public class OnlineMatchTests
    {
        static readonly Team[] Ffa =
            { Team.Blue, Team.Red, Team.Blue, Team.Red };

        static bool[] Active(params bool[] flags)
        {
            return flags;
        }

        [Test]
        public void Clinched_WhenLeadUnreachable()
        {
            Assert.IsTrue(MatchStandings.Clinched(
                new[] { 3, 1, 0, 0 },
                Active(true, true, true, true), Ffa,
                teamMode: false, remaining: 1));
        }

        [Test]
        public void NotClinched_WhenLeadCatchable()
        {
            Assert.IsFalse(MatchStandings.Clinched(
                new[] { 3, 2, 0, 0 },
                Active(true, true, true, true), Ffa,
                teamMode: false, remaining: 1));
        }

        /// <summary>
        /// Slot 1 left with 3 wins; among those still playing the
        /// leader (2) is uncatchable with 1 round remaining.
        /// </summary>
        [Test]
        public void Clinched_IgnoresDepartedLeader()
        {
            Assert.IsTrue(MatchStandings.Clinched(
                new[] { 2, 3, 0, 0 },
                Active(true, false, true, true), Ffa,
                teamMode: false, remaining: 1));
        }

        /// <summary>
        /// Active slots are tied 1-1; the departed slot's 3 wins must
        /// not make the match look decided.
        /// </summary>
        [Test]
        public void NotClinched_DepartedWinsDoNotDecide()
        {
            Assert.IsFalse(MatchStandings.Clinched(
                new[] { 1, 3, 1, 0 },
                Active(true, false, true, false), Ffa,
                teamMode: false, remaining: 2));
        }

        /// <summary>
        /// Individuals: 3 vs 1 looks clinched with 1 remaining, but
        /// teams are blue 3 (slots 0+2) vs red 2 (slots 1+3) — red can
        /// still tie.
        /// </summary>
        [Test]
        public void TeamClinch_ComparesTeamTotals()
        {
            Assert.IsFalse(MatchStandings.Clinched(
                new[] { 3, 1, 0, 1 },
                Active(true, true, true, true), Ffa,
                teamMode: true, remaining: 1));
        }

        [Test]
        public void TeamClinch_WhenTeamLeadUnreachable()
        {
            Assert.IsTrue(MatchStandings.Clinched(
                new[] { 4, 1, 0, 0 },
                Active(true, true, true, true), Ffa,
                teamMode: true, remaining: 2));
        }

        [Test]
        public void LeadingSlot_SkipsDepartedSlots()
        {
            Assert.AreEqual(2, MatchStandings.LeadingSlot(
                new[] { 1, 5, 2, 0 },
                Active(true, false, true, false), Ffa,
                teamMode: false));
        }

        [Test]
        public void LeadingSlot_TiesGoToLowestSlot()
        {
            Assert.AreEqual(0, MatchStandings.LeadingSlot(
                new[] { 2, 2, 0, 0 },
                Active(true, true, true, true), Ffa,
                teamMode: false));
        }

        [Test]
        public void LeadingSlot_NoActiveSlotsIsNoWinner()
        {
            Assert.AreEqual(-1, MatchStandings.LeadingSlot(
                new[] { 2, 2, 0, 0 },
                Active(false, false, false, false), Ffa,
                teamMode: false));
        }

        /// <summary>
        /// Blue (slots 0+2) leads 4-2; the best blue slot is 2 even
        /// though red's slot 1 has more wins than blue's slot 0.
        /// </summary>
        [Test]
        public void LeadingSlot_TeamMode_BestOfWinningTeam()
        {
            Assert.AreEqual(2, MatchStandings.LeadingSlot(
                new[] { 1, 2, 3, 0 },
                Active(true, true, true, true), Ffa,
                teamMode: true));
        }

        [Test]
        public void LobbyReturnSentinel_CannotCollideWithLevels()
        {
            var online = typeof(OnlineMatch);
            const BindingFlags flags =
                BindingFlags.NonPublic | BindingFlags.Static;
            var levels = (string[])online
                .GetField("matchLevels", flags).GetValue(null);
            int sentinel = (int)online
                .GetField("lobbyReturnLevel", flags).GetValue(null);
            Assert.LessOrEqual(sentinel, byte.MaxValue);
            Assert.Less(levels.Length, sentinel);
        }

        [Test]
        public void Epoch_WrapsFrom255ToZero()
        {
            byte saved = NetMessages.CurrentEpoch;
            try
            {
                NetMessages.CurrentEpoch = byte.MaxValue;
                NetMessages.BumpEpoch();
                Assert.AreEqual(0, NetMessages.CurrentEpoch);
            }
            finally
            {
                NetMessages.CurrentEpoch = saved;
            }
        }
    }
}

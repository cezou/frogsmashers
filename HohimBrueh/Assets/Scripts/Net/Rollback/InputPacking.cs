using FreeLives;

namespace FrogSmashers.Net.Rollback
{
    /// <summary>
    /// Packs the 9 digital input fields the simulation reads into a
    /// 2-byte bitmask for network transport and input buffers. Analog
    /// axes and triggers are never read by the sim and are derived.
    /// </summary>
    public static class InputPacking
    {
        const ushort upBit = 1 << 0;
        const ushort downBit = 1 << 1;
        const ushort leftBit = 1 << 2;
        const ushort rightBit = 1 << 3;
        const ushort aBit = 1 << 4;
        const ushort bBit = 1 << 5;
        const ushort xBit = 1 << 6;
        const ushort yBit = 1 << 7;
        const ushort startBit = 1 << 8;

        /// <summary>
        /// Control-plane flag riding in the input word: the slot is in the
        /// online lobby's choose-color state (frog pinned to its spawn,
        /// frozen). Not an InputState field — set directly on the packed
        /// word by the rollback driver and read by the lobby pin step.
        /// </summary>
        public const ushort ChoosingBit = 1 << 9;

        /// <summary>Packs current input fields into a bitmask.</summary>
        public static ushort Pack(InputState s)
        {
            ushort packed = 0;
            if (s.up) packed |= upBit;
            if (s.down) packed |= downBit;
            if (s.left) packed |= leftBit;
            if (s.right) packed |= rightBit;
            if (s.aButton) packed |= aBit;
            if (s.bButton) packed |= bBit;
            if (s.xButton) packed |= xBit;
            if (s.yButton) packed |= yBit;
            if (s.start) packed |= startBit;
            return packed;
        }

        /// <summary>Unpacks a bitmask into current input fields.</summary>
        public static void Unpack(ushort packed, InputState target)
        {
            target.up = (packed & upBit) != 0;
            target.down = (packed & downBit) != 0;
            target.left = (packed & leftBit) != 0;
            target.right = (packed & rightBit) != 0;
            target.aButton = (packed & aBit) != 0;
            target.bButton = (packed & bBit) != 0;
            target.xButton = (packed & xBit) != 0;
            target.yButton = (packed & yBit) != 0;
            target.start = (packed & startBit) != 0;
            target.xAxis = target.right ? 1f : (target.left ? -1f : 0f);
            target.yAxis = target.up ? 1f : (target.down ? -1f : 0f);
            target.leftTrigger = 0f;
            target.rightTrigger = 0f;
        }
    }
}

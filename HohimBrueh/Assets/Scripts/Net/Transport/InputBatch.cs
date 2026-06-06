namespace FrogSmashers.Net.Transport
{
    /// <summary>
    /// Reusable payload for one input packet: every slot the sender
    /// relays (each as a contiguous tick window ending at its newest
    /// confirmed tick) plus the sender's per-slot contiguous confirmed
    /// ticks, which the receiver uses as resend acks. Windows start
    /// right after the receiver's last ack so lost packets are always
    /// retransmitted until acknowledged.
    /// </summary>
    public class InputBatch
    {
        public readonly uint[] Acks = new uint[NetMessages.AckSlots];
        public readonly byte[] Slots = new byte[NetMessages.AckSlots];
        public readonly uint[] LastTicks =
            new uint[NetMessages.AckSlots];
        public readonly byte[] Counts = new byte[NetMessages.AckSlots];
        public readonly ushort[][] Inputs;

        public int SlotCount;

        public InputBatch()
        {
            Inputs = new ushort[NetMessages.AckSlots][];
            for (int s = 0; s < NetMessages.AckSlots; s++)
                Inputs[s] = new ushort[NetMessages.MaxInputWindow];
        }
    }
}

namespace RotinaRemote.Protocol
{
    public enum ProtocolInputType : byte
    {
        Mouse = 0,
        Keyboard = 1
    }

    public class InputPacketPayload
    {
        public ProtocolInputType Type { get; set; }

        // Mouse fields
        public byte MouseType { get; set; } // RotinaRemote.Input.MouseEventType
        public double NormX { get; set; }
        public double NormY { get; set; }
        public int WheelDelta { get; set; }

        // Keyboard fields
        public byte KeyType { get; set; } // RotinaRemote.Input.KeyEventType
        public ushort VirtualKeyCode { get; set; }
    }
}

using System.Net;
using System.Net.Sockets;

namespace EthernetCommunication
{
    public abstract class CEthernetDevice
    {
        public enum State { NotConnected, StartingConnection, Connecting, Connected }

        public ConnectionStats Connstats { get; protected set; } = new ConnectionStats();

        public string ConnectionName { get; protected set; } = "";

        public IPAddress IPaddress { get; protected set; } = IPAddress.Parse("192.168.1.20");

        public SocketType SockType { get; protected set; } = SocketType.Stream;

        public ProtocolType ProtType { get; protected set; } = ProtocolType.Tcp;

        public int Port { get; protected set; } = 506;
    }
}
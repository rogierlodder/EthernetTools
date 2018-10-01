using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace EthernetCommunication
{
    public abstract class CEthernetDevice
    {
        public enum State { NotConnected, StartingConnection, Connecting, Connected, WaitingForData }

        public ConnectionStats Connstats { get; protected set; } = new ConnectionStats();

        public string ConnectionName { get; protected set; } = "";

        public IPAddress IPaddress { get; protected set; } = IPAddress.Parse("192.168.1.20");

        public SocketType SockType { get; protected set; } = SocketType.Stream;

        public ProtocolType ProtType { get; protected set; } = ProtocolType.Tcp;

        public int Port { get; protected set; } = 506;

        //return values for the asynchronous methods
        public const int sendFailed = -2;
        public const int receiveFailed = -3;
        public const int sendNotConnected = -4;
        public const int recvNotConnected = -5;
        public const int unexpectedNrBytesReceived = -6;

        /// <summary>
        /// Dictionary for translating the error codes to error message strings
        /// </summary>
        public Dictionary<int, string> IPerrorDict { get; private set; } = new Dictionary<int, string>
        {
            {sendFailed, "The send command failed" },
            {receiveFailed,"The receive command timed out" },
            {sendNotConnected, "A send was attempted on a non-connected socket"},
            {recvNotConnected, "A receive was attempted on a non-connected socket"},
            {unexpectedNrBytesReceived, "The number of received bytes was unequal to the expected number"}
        };
    }
}
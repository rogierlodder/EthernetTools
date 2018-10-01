using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace EthernetCommunication
{
    public class CEthernetServer<TConnection> : CEthernetDevice where TConnection : ConnectionBase, new()
    {
        public Action<string> NewConnection { get; set; }
        public Action<string, byte[], int> NewDataReceived { get; set; }

        public Dictionary<string, TConnection> AllConnections { get; protected set; } = new Dictionary<string, TConnection>();
        public List<TConnection> AllConnectionsList { get; protected set; } = new List<TConnection>();
        public int NrConnections { get { return AllConnections.Count; } }

        private Socket Listener;
        private int ConnectionBufSize = 65536;

        public CEthernetServer(string name, string ip, int port, string socktype, int bufsize)
        {
            IPAddress IP;
            if (IPAddress.TryParse(ip, out IP)) IPaddress = IP; else return;

            Port = port;
            ConnectionName = name;
            if (socktype == "UDP")
            {
                SockType = SocketType.Dgram;
                ProtType = ProtocolType.Udp;
            }
            if (socktype == "TCP")
            {
                SockType = SocketType.Stream;
                ProtType = ProtocolType.Tcp;
            }

            IPEndPoint localEndPoint = new IPEndPoint(IPaddress, Port);

            // Create a TCP/IP socket.
            Listener = new Socket(AddressFamily.InterNetwork, SockType, ProtType);
            // Bind the socket to the local endpoint and listen for incoming connections.
            try
            {
                Listener.Bind(localEndPoint);
                Listener.Listen(100);
                // Start an asynchronous socket to listen for connections.
                Listener.BeginAccept(new AsyncCallback(AcceptConnectCallback), Listener);
            }
            catch
            { }
            ConnectionBufSize = bufsize;
        }

        public void SendDataAsync(string name, byte[] sendData, int nrbytes)
        {
            TConnection C = AllConnections[name];
            try
            {
                C.ConnectionSocket.BeginSend(C.IncomingData, 0, C.IncomingData.Length, 0, new AsyncCallback(SendCallback), C.Name);
            }
            catch { }
        }

        public void RemoveConnection(string name)
        {
            if (!AllConnections.ContainsKey(name)) return;

            TConnection Conn = AllConnections[name];
            AllConnectionsList.Remove(Conn);

            Conn.IncomingData = null;
            Conn.OutgoingData = null;

            AllConnections.Remove(name);
        }

        public void DisconnectAll()
        {
            foreach (var C in AllConnections.Values)
            {
                try
                {
                    C.ConnectionSocket.Disconnect(false);
                }
                catch { }

            }
            ClearAllConnections();
        }

        private void AcceptConnectCallback(IAsyncResult ar)
        {
            Listener = (Socket)ar.AsyncState;
            //acknowledge the connection
            Socket incomingSocket = Listener.EndAccept(ar); 
            IPEndPoint ep = (IPEndPoint)incomingSocket.RemoteEndPoint;

            TConnection C = new TConnection();
            C.Setup(incomingSocket, ep.Address, ep.Port, ConnectionBufSize);

            if (AllConnections.ContainsKey(C.Name) == false) AddConnection(C);

            //Signal that a new connection has been created
            NewConnection?.Invoke(C.Name);

            try
            {
                //configure the socket to receive incoming data and arm the data reception event
                C.ConnectionSocket.BeginReceive(C.IncomingData, 0, C.IncomingData.Length, 0, new AsyncCallback(ReadCallback), C.Name);

                //put the listener back to listening
                Listener.BeginAccept(new AsyncCallback(AcceptConnectCallback), Listener);
            }
            catch { }
        }

        private void AddConnection(TConnection C)
        {
            AllConnections.Add(C.Name, C);
            AllConnectionsList.Add(C);
        }
        private void ClearAllConnections()
        {
            AllConnections.Clear();
            AllConnectionsList.Clear();
        }

        private void ReadCallback(IAsyncResult ar)
        {
            //get the client from the asynchronous state object
            TConnection C = AllConnections[(string)ar.AsyncState];

            int bytesread = 0;
            try
            {
                bytesread = C.ConnectionSocket.EndReceive(ar); //acknowledge the data receipt
            }
            catch { }
            if (bytesread > 0)
            {
                //call external function to process the data
                NewDataReceived?.Invoke(C.Name, C.IncomingData, bytesread);
                C.NrReceivedBytes = bytesread;

                //call the data processing method of the connection itself
                C.ProcessData();

                //signal the arrival of data
                C.DataReceived = true;

                //set the socket back to listening mode
                try
                {
                    C.ConnectionSocket.BeginReceive(C.IncomingData, 0, C.IncomingData.Length, 0, new AsyncCallback(ReadCallback), C.Name);
                }
                catch { }
            }
            else //if a received data event arrives with no data, a disconnect notification was received
            {
                //Handle disconnect
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                TConnection C = AllConnections[(string)ar.AsyncState];

                // Complete sending the data to the remote device.
                int bytesSent = C.ConnectionSocket.EndSend(ar);
            }
            catch
            { }
        }

        #region Tools
        /// <summary>
        /// A simple test for checking if a combination of IP address and Netmask can connect to this host.
        /// </summary>
        /// <param name="ipaddress"></param>
        /// <param name="netmask"></param>
        /// <returns></returns>
        public static bool CanConnectIPv4(string ip)
        {
            UInt32 clientIP;
            try
            {
                clientIP = GetUint(IPAddress.Parse(ip));
            }
            catch { return false; }

            var hosts = GetHostAddresses(true);
            bool matchFound = false;
            foreach (var K in hosts)
            {
                UInt32 hostIP = GetUint(K.Key);
                UInt32 hostNM = GetUint(K.Value);

                if ((hostIP & hostNM) == (clientIP & hostNM)) matchFound = true;
            }
            return matchFound;
        }

        /// <summary>
        /// Get the unsigned int value of the address
        /// </summary>
        /// <param name="ip">IP address input</param>
        /// <returns></returns>
        private static UInt32 GetUint(IPAddress ip)
        {
            byte[] buf = ip.GetAddressBytes();
            //reverse to little endian first
            buf = buf.Reverse().ToArray();
            return BitConverter.ToUInt32(buf, 0);
        }

        /// <summary>
        /// returns a string dictionary with the hosts' ipaddresses and netmasks
        /// </summary>
        /// <param name="IPv4Only">If IPv4Only is true, only IPv4 addresses are returned</param>
        /// <returns></returns>
        public static Dictionary<IPAddress, IPAddress> GetHostAddresses(bool IPv4Only)
        {
            var D = new Dictionary<IPAddress, IPAddress>();

            IPHostEntry ThisHost = Dns.GetHostEntry(Dns.GetHostName()); //get local IP address
            foreach (var S in ThisHost.AddressList)
            {
                //only add IPv4 addresses
                if (IPv4Only)
                {
                    if (S.AddressFamily == AddressFamily.InterNetwork) D.Add(S, GetSubnetMask(S));
                }
                else D.Add(S, GetSubnetMask(S));
            }
            return D;
        }

        /// <summary>
        /// Routine for getting the net mask of the network adapter
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public static IPAddress GetSubnetMask(IPAddress address)
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (UnicastIPAddressInformation unicastIPAddressInformation in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicastIPAddressInformation.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        if (address.Equals(unicastIPAddressInformation.Address))
                        {
                            return unicastIPAddressInformation.IPv4Mask;
                        }
                    }
                }
            }
            return IPAddress.Parse("0.0.0.0"); //netmask not found
        }
        #endregion
    }
}

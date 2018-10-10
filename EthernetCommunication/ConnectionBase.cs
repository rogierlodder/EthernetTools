using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace EthernetCommunication
{
    public class ConnectionBase
    {
        public byte[] IncomingData;
        public byte[] OutgoingData;

        public string Address { get; private set; }
        public Socket ConnectionSocket { get; protected set; }
        public bool Connected { get; protected set; } = false;

        public IPAddress IpAddress { get; protected set; }
        public int PortNr { get; protected set; }

        public int NrReceivedBytes { get; set; } = 0;
        public bool IsOnLoopback { get; set; } = true;
        public ConnectionStats ConnStats { get; set; } = new ConnectionStats();

        public Action ProcessDataAction { get; set; }
        public Action<string, string> ReportError { get; set; }
        protected int bytesSent = 0;

        public ConnectionBase() { }

        /// <summary>
        /// Setup the connection with the serverIP and the locally created port number
        /// </summary>
        /// <param name="connectionSocket">Incoming socket</param>
        /// <param name="adress">Server IP address</param>
        /// <param name="port">Locally created port number</param>
        public virtual void Setup(Socket connectionSocket, IPAddress adress, int port, int bufferSize)
        {
            IncomingData = new byte[bufferSize];
            OutgoingData = new byte[bufferSize];

            ConnectionSocket = connectionSocket;
            IpAddress = adress;

            if (IpAddress.Equals(IPAddress.Loopback)) IsOnLoopback = true;
            else IsOnLoopback = false;

            PortNr = port;
            Address = $"{adress.ToString()}:{port}";

            Connected = true;

            ConnStats.ConnectionTime.Restart();
            ConnStats.LastComm.Restart();
        }

        /// <summary>
        /// Send data asynchronously to an IP connection
        /// </summary>
        /// <param name="name">Name of the connection</param>
        /// <param name="sendData">Byte array with the data</param>
        /// <param name="nrbytes">Number of bytes to be sent</param>
        public bool SendDataAsync(byte[] sendData, int nrbytes)
        {
            try
            {
                ConnectionSocket.BeginSend(sendData, 0, nrbytes, 0, new AsyncCallback(SendCallback), Address);
                return true;
            }
            catch
            {
                ReportError?.Invoke("BeginSend failed in ConnectionBase.SendDataAsync", Address);
                return false;
            }
            
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                String S = (string)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = ConnectionSocket.EndSend(ar);
                ConnStats.LastComm.Restart();
                ConnStats.Sentpackets++;
            }
            catch
            {
                ReportError?.Invoke("EndSend failed in ConnectionBase.SendCallback", Address);
            }
        }

        /// <summary>
        /// Process the incoming data and send a reply
        /// </summary>
        public virtual void ProcessData()
        {

        }

    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EthernetCommunication
{
    public class ConnectionStats
    {
        /// <summary>
        /// The time the connection has been running since the last connect
        /// </summary>
        public Stopwatch ConnectionTime { get; set; } = new Stopwatch();

        /// <summary>
        /// Stopwatch for monitoring the time since the last communication
        /// </summary>
        public Stopwatch LastComm { get; set; } = new Stopwatch();

        /// <summary>
        /// The connection timeout. If no traffic has been received during this time, the connection will be terminated
        /// </summary>
        public long ConnectionTimeout { get; set; } = 30;

        public bool HasTimedOut
        {
            get
            {
                if (LastComm.Elapsed.Seconds > ConnectionTimeout) return true;
                else return false;
            }
        }

        /// <summary>
        /// Total number of received packets since the connections was created
        /// </summary>
        public long Receivedpackets { get; set; } = 0;

        /// <summary>
        /// Total number of sent packets since the connections was created
        /// </summary>
        public long Sentpackets { get; set; } = 0;

        /// <summary>
        /// Total amount of connects
        /// </summary>
        public int NrConnects { get; set; } = 0;

        /// <summary>
        /// Total amount of disconnects
        /// </summary>
        public int NrDisconnects { get; set; } = 0;
    }
}

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

using System;
using System.Threading;
using static CommunicationChannel.Channel;

namespace CommunicationChannel.DataIO
{
    internal partial class DataIO : IDisposable
    {
        // =================== This timer checks if the connection has been lost and reestablishes it ====================================
        internal readonly Timer TryReconnection;
        private const int TimerIntervalCheckConnection = 20 * 1000;
        internal readonly object LockIsConnected = new object();
        private void OnTryReconnection(object o)
        {
            Connect();
        }
        // ===============================================================================================================================

    }
}

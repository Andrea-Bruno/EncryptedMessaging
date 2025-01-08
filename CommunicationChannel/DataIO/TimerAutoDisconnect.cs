using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace CommunicationChannel
{
    internal partial class DataIO : IDisposable
    {
        // =================== This timer automatically closes the connection after a certain period of network inactivity ===============
        //public int ConnectionTimeout = Timeout.Infinite;
        private readonly Timer TimerAutoDisconnect;
        private void SuspendAutoDisconnectTimer()
        {
            TimerAutoDisconnect.Change(Timeout.Infinite, Timeout.Infinite);
        }
        private void ResumeAutoDisconnectTimer()
        {
            try
            {
                TimerAutoDisconnect.Change(Channel.ConnectionTimeout, Timeout.Infinite);
            }
            catch (Exception)
            {
                // Disposed object (TimerAutoDisconnect is not available)
            }
        }
        // ===============================================================================================================================

    }
}

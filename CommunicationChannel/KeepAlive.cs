using System;
using System.Diagnostics;
using System.Threading;

namespace CommunicationChannel
{
    internal partial class Tcp : IDisposable
    {
        internal readonly Timer TimerKeepAlive;
        internal readonly static TimeSpan KeepAliveInterval = TimeSpan.FromMinutes(5); // IMPORTANT: This value must be identical in the CommunicationChannel and RouterServer projects
        private void OnTimerKeepAlive(object o)
        {
            Channel.LastKeepAliveCheck = DateTime.UtcNow;
            try
            {
                var stream = Client?.GetStream();
                stream?.Write(new byte[] { 0, 0, 0, 0 }, 0, 4); // The server responds to this command with a ping command which will update the time of the last incoming data packet LastIN
                stream?.Flush();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Channel.KeepAliveFailures++;
#if DEBUG
                switch (ex.HResult)
                {
                    case -2146233079: // The server is pinging and may have dropped the connection because it is not responding during debugging. If the error is not the same, then it breaks on the next line
                        break;
                    case -2146232800: // Unable to write data to the transport connection: An established connection was aborted by the software in your host machine: Connection interrupted by the current machine! Is there a problem with the connection timeout?
                        Debugger.Break();
                        break;
                    default:
                        Debugger.Break();
                        break;
                }
#endif
            }
            if (IsConnected() && !ConnectionIsDead())
                TimerKeepAlive.Change(KeepAliveInterval, Timeout.InfiniteTimeSpan); // restart again
            else
            {
                Debugger.Break();
                Disconnect();
            }
        }

        /// <summary>
        /// Indicates whether the connection has timed out based on the last data transmission.
        /// Since the ping messages occur in persiodic mode, a lack of communication means beyond a certain period, they mean that the transmission is interrupted.
        /// </summary>
        /// <returns>True if the communication has timed out</returns>
        private bool ConnectionIsDead()
        {
            // NOTE: This routine must be equal in CommunicationChannel and RouterServer project with LastIN and LastOUT reversed
            var timeOut = KeepAliveInterval.Add(TimeSpan.FromSeconds(60)); // add a security margin
            var timeFromLastIN = DateTime.UtcNow - Channel.LastIN;
            return timeFromLastIN > timeOut;
        }

        internal void KeepAliveStart() => TimerKeepAlive.Change(KeepAliveInterval, Timeout.InfiniteTimeSpan);
        internal void KeepAliveStop() => TimerKeepAlive.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }
}

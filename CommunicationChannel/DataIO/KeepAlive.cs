using System;
using System.Diagnostics;
using System.Threading;

namespace CommunicationChannel.DataIO
{
    internal partial class DataIO : IDisposable
    {
        private readonly Timer TimerKeepAlive;
        private static TimeSpan KeepAliveInterval => TimeSpan.FromMinutes(5); // IMPORTANT: This value must be identical in the CommunicationChannel and RouterServer projects

        private void OnTimerKeepAlive(object o)
        {
            if (!IsConnected() || ConnectionIsDead())
            {
                Disconnect();
                return;
            }
            Channel.LastKeepAliveCheck = DateTime.UtcNow;
            try
            {
                var stream = Client?.GetStream();
                stream?.Write(new byte[] { 0, 0, 0, 0 }, 0, 4); // The server responds to this command with a ping command which will update the time of the last incoming data packet LastIN
                stream?.Flush();
                LastPingRequired = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Channel.KeepAliveFailures++;
#if DEBUG && !TEST
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
        }
        private DateTime LastPingRequired;

        /// <summary>
        /// Indicates whether the connection has timed out based on the last data transmission.
        /// Since the ping messages occur in periodic mode, a lack of communication means beyond a certain period, they mean that the transmission is interrupted.
        /// </summary>
        /// <returns>True if the communication has timed out</returns>
        private bool ConnectionIsDead()
        {
            // NOTE: This routine must be compatible between CommunicationChannel and RouterServer project with LastIN and LastOUT reversed
            if (LastPingRequired == default)
                return false;
            var isTimeout = Channel.LastPingReceived == default;
            Channel.LastPingReceived = default;
            return isTimeout;

            //var timeOut = KeepAliveInterval.Add(TimeSpan.FromSeconds(60)); // add a security margin
            //var timespanLastPingReceived = DateTime.UtcNow - Channel.LastPingReceived;
            //var result = timespanLastPingReceived > timeOut;

            ////var timeFromLastIN = DateTime.UtcNow - Channel.LastIN;
            ////var result = timeFromLastIN > timeOut;

            //if (result)
            //    Debugger.Break();
            //return result;
        }

        internal void KeepAliveRefresh() => TimerKeepAlive.Change(KeepAliveInterval, KeepAliveInterval);

        private void KeepAliveStart()
        {
            Channel.LastPingReceived = default;;
            LastPingRequired = default;
            KeepAliveRefresh();
        }

        private void KeepAliveStop() => TimerKeepAlive.Change(Timeout.Infinite, Timeout.Infinite);

        private bool _disposed;
        public void Dispose()
        {
            _disposed = true;
            Disconnect();
            TryReconnection?.Change(Timeout.Infinite, Timeout.Infinite);
            TryReconnection?.Dispose();
            TimerAutoDisconnect?.Change(Timeout.Infinite, Timeout.Infinite);
            TimerAutoDisconnect?.Dispose();
            TimerKeepAlive?.Change(Timeout.Infinite, Timeout.Infinite);
            TimerKeepAlive?.Dispose();
        }
    }
}

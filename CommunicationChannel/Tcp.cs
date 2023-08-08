using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static CommunicationChannel.Channel;

namespace CommunicationChannel
{
    /// <summary>
    /// This class is used for establishing link connection and communicating with the server.
    /// </summary>
    internal class Tcp : IDisposable
    {
        internal Tcp(Channel channel)
        {
            Channel = channel;
            TryReconnection = new Timer(OnTryReconnection, null, Timeout.Infinite, Timeout.Infinite);
            TimerAutoDisconnect = new Timer(OnTimerAutoDisconnect, null, Timeout.Infinite, Timeout.Infinite);
            TimerKeepAlive = new Timer(OnTimerKeepAlive, null, Timeout.Infinite, Timeout.Infinite);
        }
        internal readonly Channel Channel;
        /// <summary>
        /// When was the last data reception (Utc)
        /// </summary>
        public DateTime LastIN { get { return _LastIN; } internal set { KeepAliveStart(); _LastIN = value; } } // KeepAliveStart() = The arrival of the received data is the confirmation that the connection is still present. The data transmitted under WSL does not generate an error even if there is no more internet line
        private DateTime _LastIN;

        /// <summary>
        /// When was the last data transmission (Utc)
        /// </summary>
        public DateTime LastOUT { get; internal set; }

        /// <summary>
        /// the last moment in which there was data transmission (in reception or transmission). Utc value.
        /// </summary>
        public DateTime LastCommunication => LastIN > LastOUT ? LastIN : LastOUT;


        // =================== This timer checks if the connection has been lost and reestablishes it ====================================
        internal readonly Timer TryReconnection;
        private const int TimerIntervalCheckConnection = 10 * 1000;
        internal readonly object LockIsConnected = new object();
        private void OnTryReconnection(object o)
        {
            lock (LockIsConnected)
            {
                if (InternetAccess)
                    Connect();
            }
        }
        // ===============================================================================================================================

        // =================== This timer automatically closes the connection after a certain period of network inactivity ===============
        //public int ConnectionTimeout = Timeout.Infinite;
        private readonly Timer TimerAutoDisconnect;
        private void OnTimerAutoDisconnect(object o) => Disconnect(false);
        private void SuspendAutoDisconnectTimer()
        {

            TimerAutoDisconnect.Change(Timeout.Infinite, Timeout.Infinite);
        }
        private DateTime _timerStartedTime = DateTime.MinValue;
        private void ResumeAutoDisconnectTimer(int? connectionTimeout = null)
        {
            if (connectionTimeout == null)
                _timerStartedTime = DateTime.UtcNow;
            if (Channel.ConnectionTimeout != Timeout.Infinite)
                TimerAutoDisconnect.Change(connectionTimeout != null ? (int)connectionTimeout : Channel.ConnectionTimeout, Timeout.Infinite);
        }
        // ===============================================================================================================================

        // =============== keep alive timer ==============================================================================================
        internal readonly Timer TimerKeepAlive;
        internal readonly static TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(120); // Milliseconds (NOTE: This value must be identical in the CommunicationChannel and RouterServer projects)
        private void OnTimerKeepAlive(object o)
        {
            //bool pingable = Ping();

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

            var lastTimeLimitIN = KeepAliveInterval.Add(TimeSpan.FromSeconds(60)); // add a security margin
            var timeFromLastIN = DateTime.UtcNow - LastIN;
            var isInTimeOut = LastIN != default && timeFromLastIN > lastTimeLimitIN;

            if (IsConnected() && !isInTimeOut)
                TimerKeepAlive.Change(KeepAliveInterval, Timeout.InfiniteTimeSpan); // restart again
            else
                Disconnect();
        }

        private void KeepAliveStart()
        {
            if (Channel.ConnectionTimeout == Timeout.Infinite) //I'm a server
                TimerKeepAlive.Change(KeepAliveInterval, Timeout.InfiniteTimeSpan);
        }
        private void KeepAliveStop()
        {
            if (Channel.ConnectionTimeout == Timeout.Infinite) //I'm a server
                TimerKeepAlive.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        // ===============================================================================================================================


        private const int _maxDataLength = 64000000; //64 MB - max data length enable to received the server
        internal TcpClient Client;
        /// <summary>
        /// Send the data, which will be parked in the spooler, cannot be forwarded immediately: If there is a queue or if there is no internet line the data will be parked.
        /// </summary>
        /// <param name="data">Data to be sent</param>
        public void SendData(byte[] data)
        {
            Channel.Spooler.AddToQuee(data);
        }
        // private const int TimeOutMs = 10000; // Default value
        private const int TimeOutMs = TimerIntervalCheckConnection - 1000; // Experimental value: Some time cannot connet, I have encrease this value
        private const double LimitMbps = 0.01;
        internal SemaphoreSlim WaitConfirmationSemaphore;
        /// <summary>
        /// Send data to server (router) without going through the spooler
        /// </summary>
        /// <param name="data">Data to be sent</param>
        /// <param name="executeOnConfirmReceipt">Action to be taken when the router has successfully received the data sent</param>
        /// <param name="directlyWithoutSpooler">If true, it indicates to the router (server) that it should not park the data if the receiver is not connected</param>
        internal void ExecuteSendData(byte[] data, Action executeOnConfirmReceipt = null, bool directlyWithoutSpooler = false)
        {
            var dataLength = (uint)data.Length;
            lock (this)
            {
                if (IsConnected() && !Logged && dataLength > 0 && data[0] != (byte)Protocol.Command.ConnectionEstablished)
                {
                    SpinWait.SpinUntil(() => Logged, 5000);
#if DEBUG

                    Debug.WriteLine(Channel.ServerUri); // Current entry point
                    if (directlyWithoutSpooler)
                        Debugger.Break(); // Don't send message directly without spooler before authentication on the server!
                    else
                        Debugger.Break(); // Verify if the server running and if you have internet connection!  (Perhaps there is no server at the current entry point)
#endif
                }
                if (dataLength > _maxDataLength) { Channel.Spooler.OnSendCompleted(data, new Exception("excess data length"), false); return; }
                SuspendAutoDisconnectTimer();

                if (!IsConnected())
                {
                    Channel.Spooler.OnSendCompleted(data, new Exception("not connected"), true);
                }
                else
                {
                    var command = (Protocol.Command)data[0];
                    // var waitConfirmation = !directlyWithoutSpooler && command != Protocol.Command.DataReceivedConfirmation && command != Protocol.Command.ConnectionEstablished;
                    var waitConfirmation = !directlyWithoutSpooler && command != Protocol.Command.DataReceivedConfirmation;
                    var writed = 0;
                    var mbps = 0d;
#if RELEASE

                    try
                    {
#endif
                    lock (this)
                    {
                        var stream = Client.GetStream();
                        var mask = 0b10000000_00000000_00000000_00000000;
                        var lastBit = directlyWithoutSpooler ? mask : 0;
                        WaitConfirmationSemaphore = waitConfirmation ? new SemaphoreSlim(0, 1) : null;
                        var mb = (double)dataLength / 1000000;
                        stream.WriteTimeout = TimeOutMs + Convert.ToInt32(mb / LimitMbps);
                        Debug.WriteLine("start upload");
                        UpdateDownloadSpeed?.Invoke(0, 0, data.Length);
                        stream.Write((dataLength | lastBit).GetBytes(), 0, 4);
                        var watch = Stopwatch.StartNew();
                        LastOUT = DateTime.UtcNow;
                        while (writed < data.Length)
                        {
                            var toWrite = data.Length - writed;
                            if (toWrite > 65536) // limit a block to 64k to show progress barr by event UpdateDownloadSpeed
                                toWrite = 65536;
                            stream.Write(data, writed, toWrite);
                            writed += toWrite;
                            mbps = Math.Round((writed / (watch.ElapsedMilliseconds + 1d)) / 1000, 2);
                            UpdateDownloadSpeed?.Invoke(mbps, writed, data.Length);
                            LastOUT = DateTime.UtcNow;
                            Debug.WriteLine("upload " + writed + "\\" + data.Length + " " + mbps + "mbps" + (writed == data.Length ? " completed" : ""));
                        }
                        //stream.Flush();
                        //watch.Stop();
                        //var elapsedMs = watch.ElapsedMilliseconds;
                        //if (elapsedMs > 300)
                        //{
                        //    Debugger.Break();
                        //}
                        if (WaitConfirmationSemaphore == null || WaitConfirmationSemaphore.CurrentCount == 1 || WaitConfirmationSemaphore.Wait(stream.WriteTimeout))
                        {
                            // confirmation received                              
                            if (waitConfirmation)
                            {
                                executeOnConfirmReceipt?.Invoke();
                                Channel.Spooler.OnSendCompleted(data, null, false);
                            }
                            ResumeAutoDisconnectTimer();
                            if (Logged)
                                Channel.Spooler.SendNext(); //Upon receipt confirmation, sends the next message
                        }
                        else
                        {
                            if (command == Protocol.Command.ConnectionEstablished)
                            {
                                Console.WriteLine(DateTime.UtcNow.ToString("G") + " Client id " + Channel.MyId + ": Unable to connect to router");
                                Console.WriteLine("Has the license expired? Or the router has no more licenses available and refuses the connection of new devices.");
#if DEBUG
                                //                                    Debugger.Break();
#endif
                            }
                            // wait timed out
                            Channel.Spooler.OnSendCompleted(data, new Exception("time-out"), true);
                        }
                    }
#if RELEASE
                    }
                    catch (Exception ex)
                    {
                        Channel.Spooler.OnSendCompleted(data, ex, true);
                    }
#endif
                }
            }
        }

        /// <summary>
        /// Establish the connection and start the spooler
        /// </summary>
        /// <returns>Returns true if the connection is successful, false if there is an error</returns>
        internal bool Connect()
        {
            if (_disposed)
                return false;
            lock (this)
            {
                if (!IsConnected() && InternetAccess)
                {
                    StartLinger(5222, out var exception);
                    if (exception == null)
                    {
                        OnCennected();
                        KeepAliveStart();
                        return true;
                    }
                    Channel.OnTcpError(ErrorType.ConnectionFailure, exception.Message);
                    Disconnect();
                }
            }
            return false;
        }
        private SemaphoreSlim OnConnectedSemaphore;
        internal bool Logged;
        private void OnCennected()
        {
            TryReconnection.Change(Timeout.Infinite, Timeout.Infinite); // Stop check if connection is lost
            ResumeAutoDisconnectTimer();
            BeginRead(Client);
            void startSpooler()
            {
                Debug.WriteLine("Logged");
                Logged = true;
                OnConnectedSemaphore.Release();
                OnConnectedSemaphore = null;
                Channel.ConnectionChange(true);
            };
            var data = Channel.CommandsForServer.CreateCommand(Protocol.Command.ConnectionEstablished, null, null, Channel.MyId); // log in
            OnConnectedSemaphore = new SemaphoreSlim(0, 1);
            ExecuteSendData(data, startSpooler);
            OnConnectedSemaphore?.Wait(TimeOutMs);
        }

        private void StartLinger(int port, out Exception exception)
        {
            exception = null;
            try
            {
                var addresses = Dns.GetHostAddresses(Channel.ServerUri.Host).Reverse().ToArray();
                Client = new TcpClient
                {
                    LingerState = new LingerOption(true, 0)
                };
                var watch = Stopwatch.StartNew();
                if (!Client.ConnectAsync(addresses, port).Wait(TimeOutMs)) // ms timeout
                {
                    // the code that you want to measure comes here
                    watch.Stop();
                    if (watch.Elapsed.TotalMilliseconds >= TimeOutMs)
                        exception = new Exception("Unable to connect to router: Probably firewall on router on port " + port + " or the router does not run");
                    else
                        exception = new Exception("Failed to connect");
                    Debugger.Break();
                }
            }
            catch (Exception ex)
            {
                if (ex.HResult == -2147467259)
                {
                    exception = new Exception("Wrong entry point! There is no DNS/IP association with the specified entry point.");
                    Debugger.Break();
                }
                else if (ex.HResult == -2146233088)
                {
                    exception = new Exception("The router is off or unreachable");
                    Debugger.Break();
                }
                else
                {
                    exception = ex;
                    Debugger.Break();
                }
            }
        }

        /// <summary>
        /// Start reading the stream by reading the first 4 bytes which contain the length of the data packet to read
        /// </summary>
        /// <param name="client"></param>
        private void BeginRead(TcpClient client)
        {
            NetworkStream stream;
            try
            {
                if (client == null)
                {
                    Disconnect();
                    return;
                }
                stream = client.GetStream();
            }
            catch (Exception ex)
            {
                Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                Disconnect();
                return;
            }
            var first4bytes = new byte[4];
            void onReadLength(IAsyncResult result)
            {
                var bytesRead = 0;
                try { bytesRead = stream.EndRead(result); }
                catch (Exception ex)
                {
                    if (ex.Message.StartsWith("Cannot access a disposed object"))
                        Channel.OnTcpError(ErrorType.ConnectionClosed, "The timer has closed the connection");
                    else
                        Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                    Debug.WriteLine(client.Connected.ToString());
                    Disconnect();
                    return;
                }
                if (bytesRead != 4)
                {
                    Channel.OnTcpError(ErrorType.WrongDataLength, "BeginRead: bytesRead != 4");
                    Disconnect();
                    return;
                }
                else
                {
                    LastIN = DateTime.UtcNow;
                    var firstUint = BitConverter.ToUInt32(first4bytes, 0);
                    var dataLength = (int)(0B01111111_11111111_11111111_11111111 & firstUint);
                    var directlyWithoutSpooler = (firstUint & 0b10000000_00000000_00000000_00000000) != 0;
                    ReadBytes(dataLength, stream, directlyWithoutSpooler);
                }
            }
            try
            {
                stream.BeginRead(first4bytes, 0, 4, onReadLength, client);
            }
            catch (Exception ex)
            {
                Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                Disconnect();
            }
        }

        public delegate void Speed(double mbps, int partial, int total);

        // Declare the event.
        public event Speed UpdateDownloadSpeed;
        public event Speed UpdateUploadSpeed;

        /// <summary>
        /// Read incoming data packet
        /// </summary>
        /// <param name="lengthIncomingData">Length of incoming packet</param>
        /// <param name="stream">Data stream for reading incoming data</param>
        /// <param name="directlyWithoutSpooler">True if the data was sent without being parked in the spooler (in this case the data arrives at its destination only if the recipient device is connected to the router)</param>
        private void ReadBytes(int lengthIncomingData, NetworkStream stream, bool directlyWithoutSpooler)
        {
            Debug.WriteLine("RB " + lengthIncomingData);
            var timerStarted = _timerStartedTime;
            SuspendAutoDisconnectTimer();
            byte[] data;
            try
            {
                // The server can send several messages in a single data packet
                data = new byte[lengthIncomingData];
                var readed = 0;
                Debug.WriteLine("start download");
                UpdateDownloadSpeed?.Invoke(0, 0, lengthIncomingData);
                var mb = (double)lengthIncomingData / 1000000;
                stream.ReadTimeout = TimeOutMs + Convert.ToInt32(mb / LimitMbps);
                var watch = Stopwatch.StartNew();



                while (readed < lengthIncomingData)
                {
                    readed += stream.Read(data, readed, lengthIncomingData - readed);
                    var mbps = Math.Round((readed / (watch.ElapsedMilliseconds + 1d)) / 1000, 2);
                    UpdateDownloadSpeed?.Invoke(mbps, readed, lengthIncomingData);
                    LastIN = DateTime.UtcNow;
                    Debug.WriteLine("download " + readed + "\\" + lengthIncomingData + " " + mbps + "mbps" + (readed == lengthIncomingData ? " completed" : ""));
                }
                Debug.WriteLine("RB end " + lengthIncomingData);

            }
            catch (Exception ex)
            {
                Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                Disconnect();
                return;
            }

            if (data.Length == 5 && data[0] == (byte)Protocol.Command.DataReceivedConfirmation)
            {
                var dataId = BitConverter.ToUInt32(data, 1);
                Channel.Spooler.OnConfirmReceipt(dataId);
            }
            Channel.OnDataReceives(data, out var error, directlyWithoutSpooler);
            if (error != null)
            {
#if DEBUG
                Debugger.Break(); //something went wrong!
#endif
                Channel.OnTcpError(error.Item1, error.Item2);

                // ====== START new =======
                Disconnect();
                return;
                // ====== END new =======
            }
            if (Channel.ConnectionTimeout != Timeout.Infinite)
            {
                if (data.Length >= 1 && data[0] == (byte)Protocol.Command.Ping) // Pinging from the server does not reset the connection timeout, otherwise, if the pings occur frequently, the connection will never be closed
                {
                    var timePassedMs = (int)(DateTime.UtcNow - timerStarted).TotalMilliseconds;
                    var remainingTimeMs = Channel.ConnectionTimeout - timePassedMs;
                    if (remainingTimeMs < 0)
                        remainingTimeMs = 0; // It will immediately trigger the timer closing the connection
                    ResumeAutoDisconnectTimer(remainingTimeMs);
                }
                else
                    ResumeAutoDisconnectTimer();
            }
            new Task(() => BeginRead(Client)).Start(); // loop - restart reading a new incoming packet. Note: A new task is used to not add the call on this stack, and close the current stack.
        }


        internal void InvokeError(ErrorType errorId, string description) => Channel.OnTcpError(errorId, description);


        /// <summary>
        /// Used to make a connection if the communication link breaks.
        /// </summary>
        /// <param name="tryConnectAgain"></param>
        public void Disconnect(bool tryConnectAgain = true)
        {
            lock (this)
            {
                Debug.WriteLine("Disconnect");
                if (Client != null) // Do not disconnect again
                {
                    Logged = false;
                    KeepAliveStop();
                    SuspendAutoDisconnectTimer();
                    if (Client != null)
                    {
                        Client.Close();
                        Client.Dispose();
                    }
                    Client = null;
                    Channel.ConnectionChange(false);
                }
                if (tryConnectAgain && !_disposed)
                    TryReconnection.Change(TimerIntervalCheckConnection, Timeout.Infinite); // restart check if connection is lost
            }
        }
        /// <summary>
        /// Find if the socket is connected to the remote host.
        /// </summary>
        /// <returns></returns>
        public bool IsConnected()
        {
            //According to the specifications, the property _client.Connected returns the connection status based on the last data transmission. The server may not be connected even if this property returns true
            // https://docs.microsoft.com/it-it/dotnet/api/system.net.sockets.tcpclient.connected?f1url=https%3A%2F%2Fmsdn.microsoft.com%2Fquery%2Fdev16.query%3FappId%3DDev16IDEF1%26l%3DIT-IT%26k%3Dk(System.Net.Sockets.TcpClient.Connected);k(DevLang-csharp)%26rd%3Dtrue&view=netcore-3.1
            return Client != null && Client.Connected && InternetAccess;
        }

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
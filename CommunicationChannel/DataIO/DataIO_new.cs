using CommunicationChannel.DataConnection;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static CommunicationChannel.Channel;
using static CommunicationChannel.CommandsForServer;

namespace CommunicationChannel.DataIO
{
    /// <summary>
    /// Handles the connection and data transmission between client and server
    /// </summary>
    internal partial class DataIO_new : IDisposable
    {
        internal DataIO_new(Channel channel)
        {
            Channel = channel;
            TryReconnection = new Timer(OnTryReconnection);
            TimerAutoDisconnect = new Timer((o) => Disconnect(false));
            TimerKeepAlive = new Timer(OnTimerKeepAlive);
        }

        /// <summary>
        /// Creates a new client connection based on the configured connection type
        /// </summary>
        private void SetNewClient()
        {
            if (Channel.TypeOfConnection == ConnectivityType.Pipe)
                Client = new NamedPipeClientConnection();
            else
                Client = new TcpClientConnection();
        }

        internal readonly Channel Channel;
        internal const int MaxDataLength = 16000000; // 16 MB - maximum allowed data size
        internal IDataConnection Client;

        /// <summary>
        /// Queues data for transmission through the spooler when immediate sending isn't possible
        /// </summary>
        /// <param name="data">Data to be transmitted</param>
        public void SendData(byte[] data)
        {
            Channel.Spooler.AddToQueue(data);
        }

        private const int TimeOutMs = TimerIntervalCheckConnection - 1000;
        private const double LimitMbps = 0.01;
        internal AutoResetEvent WaitConfirmationSemaphore = new AutoResetEvent(false);

        private ConcurrentQueue<(byte[], Action, DataFlags)> _sendQueue = new ConcurrentQueue<(byte[], Action, DataFlags)>();
        private int _sendingData = 0;

        /// <summary>
        /// Sends data directly to server bypassing the spooler queue
        /// </summary>
        /// <param name="data">Data to transmit</param>
        /// <param name="executeOnConfirmReceipt">Callback when delivery is confirmed</param>
        /// <param name="flag">Transmission flags for special handling</param>
        internal void ExecuteSendData(byte[] data, Action executeOnConfirmReceipt = null, DataFlags flag = DataFlags.None)
        {
            if (true) // Original logic preserved
            {
                lock (_sendQueue)
                {
                    _ExecuteSendData(data, executeOnConfirmReceipt, flag);
                }
                return;
            }
            else
            {
#if DEBUG && !TEST
                if (_sendQueue.Count > 100)
                    Debugger.Break();
#endif
                _sendQueue.Enqueue((data, executeOnConfirmReceipt, flag));
                ProcessSendQueue();
            }
        }

        /// <summary>
        /// Processes the send queue asynchronously
        /// </summary>
        private async void ProcessSendQueue()
        {
            if (Interlocked.CompareExchange(ref _sendingData, 1, 0) != 0)
                return;

            try
            {
                while (_sendQueue.TryDequeue(out var item))
                {
                    try
                    {
                        await Task.Delay(100); // Async delay instead of Thread.Sleep
                        var data = item.Item1;
                        var executeOnConfirmReceipt = item.Item2;
                        var flag = item.Item3;
                        _ExecuteSendData(data, executeOnConfirmReceipt, flag);
                    }
                    catch (Exception ex)
                    {
                        Debugger.Break();
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _sendingData, 0);
            }
        }

        /// <summary>
        /// Internal implementation of data transmission with async operations
        /// </summary>
        private async void _ExecuteSendData(byte[] data, Action executeOnConfirmReceipt = null, DataFlags flag = DataFlags.None)
        {
            var dataLength = data.Length;
            if (!Logged)
            {
                var sendEvenWithoutLogin = dataLength > 0 && (data[0] == (byte)Protocol.Command.ConnectionEstablished || data[0] == (byte)Protocol.Command.DataReceivedConfirmation);
                if (!sendEvenWithoutLogin)
                {
                    Connect();

                    if (!flag.HasFlag(DataFlags.DirectlyWithoutSpooler))
                        Debugger.Break();

                    OnLogged = new AutoResetEvent(false);
                    if (!OnLogged.WaitOne(TimeOutMs + LoginSleepTime))
                    {
#if DEBUG && !TEST
                        Debug.WriteLine(Channel.ServerUri);
                        if (flag == DataFlags.DirectlyWithoutSpooler)
                            Debugger.Break();
                        else
                            Debugger.Break();
#endif
                        OnSendCompleted(data, flag, new Exception("Sending data without logging in!"), false);
                        return;
                    }
                }
            }
            if (dataLength > MaxDataLength)
            {
                OnSendCompleted(data, flag, new Exception("Data length over the allowed limit"), false);
                return;
            }

            if (!IsConnected())
            {
                OnSendCompleted(data, flag, new Exception("Not connected"), true);
            }
            else
            {
                var command = (Protocol.Command)data[0];
                Channel.LastCommandOUT = command;
                if (command != Protocol.Command.Ping)
                    SuspendAutoDisconnectTimer();
                var waitConfirmation = flag == DataFlags.None && command != Protocol.Command.DataReceivedConfirmation;
                var written = 0;
                var mbps = 0d;
                try
                {
                    var stream = Client.GetStream();
                    uint bit32 = flag == DataFlags.DirectlyWithoutSpooler ? 0b10000000_00000000_00000000_00000000U : 0;
                    uint bit31 = flag == DataFlags.RouterData ? 0b01000000_00000000_00000000_00000000U : 0;
                    if (waitConfirmation)
                        WaitConfirmationSemaphore.Reset();
                    var timeoutMs = DataTimeout(dataLength);
#if DEBUG && !TEST
                    timeoutMs = Timeout.Infinite;
#endif
                    if (stream.CanTimeout)
                    {
                        //stream.WriteTimeout = timeoutMs;
                        //UpdateUploadSpeed?.Invoke(0, 0, data.Length);
                    }
                    CancellationTokenSource cts = new CancellationTokenSource(timeoutMs);
                    cts.CancelAfter(TimeSpan.FromMilliseconds(1000));
                    // Async write for the header
                    await stream.WriteAsync(((uint)dataLength | bit32 | bit31).GetBytes(), 0, 4, cts.Token);

                    var watch = Stopwatch.StartNew();
                    Channel.LastOUT = DateTime.UtcNow;

                    // Async write for the data chunks
                    while (written < data.Length)
                    {
                        var toWrite = data.Length - written;
                        if (toWrite > 65536)
                            toWrite = 65536;

                        await stream.WriteAsync(data, written, toWrite);
                        written += toWrite;
                        mbps = Math.Round((written / (watch.ElapsedMilliseconds + 1d)) / 1000, 2);
                        if (stream.CanTimeout)
                        {
                            //UpdateUploadSpeed?.Invoke(mbps, written, data.Length);
                        }
                        Channel.LastOUT = DateTime.UtcNow;
                        // Debug.WriteLine("upload " + written + "\\" + data.Length + " " + mbps + "mbps" + (written == data.Length ? " completed" : ""));
                    }

                    await stream.FlushAsync();

                    if (waitConfirmation && !WaitConfirmationSemaphore.WaitOne(timeoutMs))
                    {
                        if (command == Protocol.Command.ConnectionEstablished)
                        {
                            Channel.LicenseExpired = true;
                            Console.WriteLine(DateTime.UtcNow.ToString("G") + " Client id " + Channel.MyId + " Unable to connect to router:");
                            Console.WriteLine("Has the license expired?");
                            Console.WriteLine("The router is off-line?");
                        }
#if DEBUG && !TEST
                        Debugger.Break();
#endif
                        OnSendCompleted(data, flag, new Exception("Confirmation time-out"), true);
                        Disconnect();
                        return;
                    }

                    if (waitConfirmation)
                    {
                        if (executeOnConfirmReceipt != null)
                        {
                            await Task.Run(() => executeOnConfirmReceipt.Invoke());
                        }
                        OnSendCompleted(data, flag, null, false);
                    }

                    if (command != Protocol.Command.Ping)
                        ResumeAutoDisconnectTimer();
                }
                catch (Exception ex)
                {
                    OnSendCompleted(data, flag, ex, true);
                    Disconnect();
                }
            }
        }

        /// <summary>
        /// Handles completion of data transmission
        /// </summary>
        private void OnSendCompleted(byte[] data, DataFlags flag, Exception ex, bool connectionIsLost)
        {
            if (ex != null)
            {
                Channel.DataIO.InvokeError(connectionIsLost ? ErrorType.LostConnection : ErrorType.SendDataError, ex.Message);
            }
            if (flag == DataFlags.None)
                Channel.Spooler.OnSendCompleted(data, connectionIsLost);
        }

        private bool _Logged;

        internal bool Logged
        {
            get { return _Logged; }
            set
            {
                _Logged = value;
                if (value)
                {
                    OnLogged?.Set();
                }
            }
        }

        private AutoResetEvent OnLogged;

        /// <summary>
        /// Handles the established connection and initiates login
        /// </summary>
        private void OnConnected()
        {
            Channel.LicenseExpired = false;
            TryReconnection.Change(Timeout.Infinite, Timeout.Infinite);

            ResumeAutoDisconnectTimer();

            BeginRead(Client);

#if DEBUG && !TEST
            var watchLogin = Stopwatch.StartNew();
            watchLogin.Start();
#endif

            void OnLogged()
            {
#if DEBUG && !TEST
                watchLogin.Stop();
                if (watchLogin.Elapsed.TotalMilliseconds > 500)
                    Debugger.Break();
#endif
                var timer = new System.Timers.Timer(LoginSleepTime);
                timer.Elapsed += (sender, e) =>
                {
                    Logged = true;
                    Channel.ConnectionChange(true);
                    Channel.Spooler.SendNext();
                    timer.Dispose();
                };
                timer.AutoReset = false;
                timer.Start();
            }

            var login = Channel.CommandsForRouter.CreateCommand(Protocol.Command.ConnectionEstablished, null, null, Channel.MyId);
            ExecuteSendData(login, OnLogged);
        }
        const int LoginSleepTime = 1500;

        /// <summary>
        /// Attempts to establish connection to the server
        /// </summary>
        private void StartLinger(out Exception exception)
        {
            exception = null;
            try
            {
                int? port = null;
                string address = null;
                if (Channel.ServerUri.Scheme == Uri.UriSchemeHttp || Channel.ServerUri.Scheme == Uri.UriSchemeHttps)
                {
                    var addresses = Dns.GetHostAddresses(Channel.ServerUri.Host).Reverse().ToArray();
                    port = Channel.ServerUri.Port;
                    address = addresses[0].ToString();
                }
                else
                {
                    address = Channel.ServerUri.Host;
                    if (Channel.ServerUri.Host.Contains(":"))
                        port = Channel.ServerUri.Port;
                }

                SetNewClient();

                var watch = Stopwatch.StartNew();
                if (!Client.Connect(address, port, TimeOutMs))
                {
                    watch.Stop();
                    if (watch.Elapsed.TotalMilliseconds >= TimeOutMs)
                        exception = new Exception("Unable to connect to router: Probably firewall on router on port " + port + " or the router does not run");
                    else
                        exception = new Exception("Failed to connect");
#if DEBUG && !TEST
                    Debugger.Break();
#endif
                }
            }
            catch (Exception ex)
            {
                if (ex is SocketException se)
                {
                    exception = new Exception("Socket exception: " + se.SocketErrorCode.ToString());
#if DEBUG && !TEST
                    Debugger.Break();
#endif
                }
                else if (ex.HResult == -2147467259)
                {
                    exception = new Exception("Wrong entry point! There is no DNS/IP association with the specified entry point.");
#if DEBUG && !TEST
                    Debugger.Break();
#endif
                }
                else if (ex.HResult == -2146233088)
                {
                    exception = new Exception("The router is off or unreachable");
#if DEBUG && !TEST
                    Debugger.Break();
#endif
                }
                else
                {
                    exception = ex;
#if DEBUG && !TEST
                    Debugger.Break();
#endif
                }
            }
        }

        public delegate void Speed(double mbps, int partial, int total);

        public event Speed UpdateDownloadSpeed;
        public event Speed UpdateUploadSpeed;

        /// <summary>
        /// Initiates asynchronous reading from the data stream
        /// </summary>
        private void BeginRead(IDataConnection client)
        {
            Stream stream;
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

            try
            {
                var stateObject = Tuple.Create(stream, client);
                if (stream.CanTimeout)
                {
                    //stream.ReadTimeout = Timeout.Infinite;
                }

                stream.BeginRead(First4bytes, 0, 4, OnReadLength, client);
            }
            catch (Exception ex)
            {
                Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                Disconnect();
            }
        }

        private byte[] First4bytes = new byte[4];

        /// <summary>
        /// Callback handler for reading data length prefix
        /// </summary>
        void OnReadLength(IAsyncResult result)
        {
            var client = (IDataConnection)result.AsyncState;
            Stream stream;

            var bytesRead = 0;
            try
            {
                stream = client.GetStream();
                bytesRead = stream.EndRead(result);
            }
            catch (Exception ex)
            {
                if (ex.HResult == -2146232798)
                    Channel.OnTcpError(ErrorType.ConnectionClosed, "The timer has closed the connection");
                else
                    Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                Debug.WriteLine(client.IsConnected.ToString());
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
                var firstUint = BitConverter.ToUInt32(First4bytes, 0);
                var dataLength = (int)(0B00111111_11111111_11111111_11111111U & firstUint);
                if (dataLength > MaxDataLength)
                {
                    Channel.OnTcpError(ErrorType.WrongDataLength, "Data length over the allowed limit");
                    Disconnect();
                    return;
                }

                var directlyWithoutSpooler = (firstUint & 0b10000000_00000000_00000000_00000000U) != 0;
                var routerData = (firstUint & 0b01000000_00000000_00000000_00000000U) != 0;
                DataFlags flag = DataFlags.None;
                if (directlyWithoutSpooler)
                    flag = DataFlags.DirectlyWithoutSpooler;
                else if (routerData)
                    flag = DataFlags.RouterData;
                ReadBytes(dataLength, stream, flag);
            }
        }

        /// <summary>
        /// Reads the actual data payload asynchronously
        /// </summary>
        private async void ReadBytes(int lengthIncomingData, Stream stream, DataFlags flag)
        {
            Protocol.Command command = default;
            byte[] data;
            try
            {
                data = new byte[lengthIncomingData];
                var readDataLen = 0;
                var watch = Stopwatch.StartNew();
                var first = true;
                int remaining;
                var timeOutAt = DateTime.UtcNow.AddMilliseconds(DataTimeout(lengthIncomingData));
                while (readDataLen < lengthIncomingData)
                {
                    int partialLength;
                    if (first == true)
                        partialLength = 1;
                    else
                    {
                        partialLength = lengthIncomingData - readDataLen;
                        if (partialLength > 65536)
                            partialLength = 65536;
                    }

                    remaining = lengthIncomingData - readDataLen;
                    if (partialLength > remaining)
                        partialLength = remaining;
                    if (stream.CanTimeout)
                    {
                        //stream.ReadTimeout = DataTimeout(partialLength);
                        //UpdateDownloadSpeed?.Invoke(0, 0, lengthIncomingData);
                    }

                    var cancellationTokenSource = new CancellationTokenSource();
                    cancellationTokenSource.CancelAfter(DataTimeout(partialLength));

                    // Async read operation
                    var bytesRead = await stream.ReadAsync(data, readDataLen, partialLength);
                    readDataLen += bytesRead;

                    if (stream.CanTimeout)
                    {
                        //var mbps = Math.Round((readDataLen / (watch.ElapsedMilliseconds + 1d)) / 1000, 2);
                        //UpdateDownloadSpeed?.Invoke(mbps, readDataLen, lengthIncomingData);
                    }
                    if (first == true && bytesRead > 0)
                    {
                        first = false;
                        if (!Enum.IsDefined(typeof(Protocol.Command), data[0]))
                            throw new Exception("Command not supported");
                        command = (Protocol.Command)data[0];
                        Channel.LastCommandIN = command;
                        if (command != Protocol.Command.Ping)
                            SuspendAutoDisconnectTimer();
                    }

                    Channel.LastIN = DateTime.UtcNow;
#if !DEBUG || TEST
                    if (DateTime.UtcNow >= timeOutAt)
                        throw new Exception("Data read timeout");
#endif
                }

                Debug.WriteLine("End download" + lengthIncomingData);
                if (stream.CanTimeout)
                {
                    //UpdateDownloadSpeed?.Invoke(0, 0, lengthIncomingData);
                }
            }
            catch (Exception ex)
            {
                Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                Disconnect();
                return;
            }

            Channel.OnDataReceives(data, flag, out var error, out var _);
            if (error != null)
            {
#if DEBUG && !TEST
                Debugger.Break();
#endif
                Channel.OnTcpError(error.Item1, error.Item2);
                Disconnect();
                return;
            }

            if (command != Protocol.Command.Ping)
                ResumeAutoDisconnectTimer();
            BeginRead(Client);
        }

        /// <summary>
        /// Calculates appropriate timeout based on data size
        /// </summary>
        private static int DataTimeout(int dataLength)
        {
            var mb = (double)dataLength / 1000000;
            return 15000 + Convert.ToInt32(mb / LimitMbps);
        }

        internal void InvokeError(ErrorType errorId, string description) => Channel.OnTcpError(errorId, description);

        /// <summary>
        /// Establishes connection to the server
        /// </summary>
        internal bool Connect()
        {
            Exception exception = null;
            if (antiConcurrentConnection == 0)
            {
                Interlocked.Increment(ref antiConcurrentConnection);
                if (!_disposed && !IsConnected() && Channel.Connectivity)
                {
                    StartLinger(out exception);
                    if (exception != null)
                    {
                        Channel.OnTcpError(ErrorType.ConnectionFailure, exception.Message);
                    }
                    else
                    {
                        OnConnected();
                        KeepAliveStart();
                    }
                }
                Interlocked.Decrement(ref antiConcurrentConnection);
            }
            if (exception != null)
                Disconnect();
            return exception == null;
        }

        /// <summary>
        /// Terminates the current connection
        /// </summary>
        public void Disconnect(bool tryConnectAgain = true)
        {
            if (antiConcurrentConnection == 0)
            {
                Interlocked.Increment(ref antiConcurrentConnection);
                if (Client != null)
                {
                    Logged = false;
                    KeepAliveStop();
                    SuspendAutoDisconnectTimer();
                    Client?.Disconnect();
                    Client?.Dispose();
                    Client = null;
                    Channel.ConnectionChange(false);
                }
                _sendQueue.Clear();
                if (tryConnectAgain && !_disposed)
                    TryReconnection.Change(TimerIntervalCheckConnection, Timeout.Infinite);
                Interlocked.Decrement(ref antiConcurrentConnection);
            }
        }

        int antiConcurrentConnection = 0;

        /// <summary>
        /// Checks if the connection is currently active
        /// </summary>
        public bool IsConnected()
        {
            return Client != null && Client.IsConnected && Channel.Connectivity;
        }
    }
}
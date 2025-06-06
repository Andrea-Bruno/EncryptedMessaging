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
    /// This class is used for establishing link connection and communicating with the server.
    /// </summary>
    internal partial class DataIO : IDisposable
    {
        internal DataIO(Channel channel)
        {
            Channel = channel;
            TryReconnection = new Timer(OnTryReconnection);
            TimerAutoDisconnect = new Timer((o) => Disconnect(false));
            TimerKeepAlive = new Timer(OnTimerKeepAlive);
        }

        private void SetNewClient()
        {
            if (Channel.TypeOfConnection == ConnectivityType.Pipe)
                Client = new NamedPipeClientConnection();
            else
                Client = new TcpClientConnection();


        }

        internal readonly Channel Channel;
        internal const int MaxDataLength = 16000000; //16 MB - max data length enable to received the server
        internal IDataConnection Client;

        /// <summary>
        /// Send the data, which will be parked in the spooler, cannot be forwarded immediately: If there is a queue or if there is no Internet line the data will be parked.
        /// </summary>
        /// <param name="data">Data to be sent</param>
        public void SendData(byte[] data)
        {
            Channel.Spooler.AddToQueue(data);
        }

        // private const int TimeOutMs = 10000; // Default value
        private const int TimeOutMs = TimerIntervalCheckConnection - 1000; // Experimental value: Some time cannot connect, I have increase this value
        private const double LimitMbps = 0.01;
        internal AutoResetEvent WaitConfirmationSemaphore = new AutoResetEvent(false);

        private ConcurrentQueue<(byte[], Action, DataFlags)> _sendQueue = new ConcurrentQueue<(byte[], Action, DataFlags)>();
        private int _sendingData = 0;
        /// <summary>
        /// Send data to server (router) without going through the spooler
        /// </summary>
        /// <param name="data">Data to be sent</param>
        /// <param name="executeOnConfirmReceipt">Action to be taken when the router has successfully received the data sent</param>
        /// <param name="flag">Indicates some settings that the server/router will have to interpret</param>
        internal void ExecuteSendData(byte[] data, Action executeOnConfirmReceipt = null, DataFlags flag = DataFlags.None)
        {
            if (true)
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
                    Debugger.Break(); // Queue is too big, investigate why!
#endif
                _sendQueue.Enqueue((data, executeOnConfirmReceipt, flag));
                ProcessSendQueue();
            }
        }

        private void ProcessSendQueue()
        {
            if (Interlocked.CompareExchange(ref _sendingData, 1, 0) != 0)
                return;
            Task.Run(() =>
            {
                try
                {
                    while (_sendQueue.TryDequeue(out var item))
                    {
                        try
                        {


                            // eliminate - prova per federe se risolve il problema di chiusura inaspettata della applicazione.

                            Thread.Sleep(100); // Give time to the spooler to send data, if it is not empty

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
            });
        }

        private void _ExecuteSendData(byte[] data, Action executeOnConfirmReceipt = null, DataFlags flag = DataFlags.None)
        {
            var dataLength = data.Length;
            if (!Logged)
            {
                var sendEvenWithoutLogin = dataLength > 0 && (data[0] == (byte)Protocol.Command.ConnectionEstablished || data[0] == (byte)Protocol.Command.DataReceivedConfirmation);
                if (!sendEvenWithoutLogin)
                {
                    Connect(); // force reconnection!

                    if (!flag.HasFlag(DataFlags.DirectlyWithoutSpooler))
                        Debugger.Break(); // Don't send any data before login is done!

                    // Wait for the login to complete
                    OnLogged = new AutoResetEvent(false);
                    if (!OnLogged.WaitOne(TimeOutMs + LoginSleepTime))
                    {
#if DEBUG && !TEST
                        // Login Timeout!
                        Debug.WriteLine(Channel.ServerUri); // Current entry point
                        if (flag == DataFlags.DirectlyWithoutSpooler)
                            Debugger.Break(); // Don't send message directly without spooler before authentication on the server!
                        else
                            Debugger.Break(); // Verify if the server running and if you have Internet connection!  (Perhaps there is no server at the current entry point)
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
                        stream.WriteTimeout = timeoutMs;
                        UpdateUploadSpeed?.Invoke(0, 0, data.Length);
                    }

                    stream.Write(((uint)dataLength | bit32 | bit31).GetBytes(), 0, 4);
                    var watch = Stopwatch.StartNew();
                    Channel.LastOUT = DateTime.UtcNow;
                    while (written < data.Length)
                    {
                        var toWrite = data.Length - written;
                        if (toWrite > 65536) // limit a block to 64k to show progress bar by event UpdateDownloadSpeed
                            toWrite = 65536;
                        stream.Write(data, written, toWrite);
                        written += toWrite;
                        mbps = Math.Round((written / (watch.ElapsedMilliseconds + 1d)) / 1000, 2);
                        if (stream.CanTimeout) // exclude pipe or similar
                            UpdateUploadSpeed?.Invoke(mbps, written, data.Length);
                        Channel.LastOUT = DateTime.UtcNow;
                        Debug.WriteLine("upload " + written + "\\" + data.Length + " " + mbps + "mbps" + (written == data.Length ? " completed" : ""));
                    }

                    stream.Flush();
                    if (waitConfirmation && !WaitConfirmationSemaphore.WaitOne(timeoutMs))
                    {
                        // Timeout!
                        if (command == Protocol.Command.ConnectionEstablished)
                        {
                            Channel.LicenseExpired = true;
                            Console.WriteLine(DateTime.UtcNow.ToString("G") + " Client id " + Channel.MyId + " Unable to connect to router:");
                            Console.WriteLine("Has the license expired?");
                            Console.WriteLine("The router is off-line?");
                        }
#if DEBUG && !TEST
                        Debugger.Break(); // Timeout! license expired or router off-line
#endif
                        OnSendCompleted(data, flag, new Exception("Confirmation time-out"), true);
                        Disconnect();
                        return;
                    }

                    // confirmation received                              
                    if (waitConfirmation)
                    {
                        if (executeOnConfirmReceipt != null)
                        {
                            Task.Run(() => executeOnConfirmReceipt.Invoke());
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
        /// On send completed it remove the sent packet and insert in the spooler queue before closing the communication channel.
        /// </summary>
        /// <param name="data">data</param>
        /// <param name="flag">Indicates some settings that the server/router will have to interpret</param>
        /// <param name="ex">exception</param>
        /// <param name="connectionIsLost">connection status</param>
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

        private void OnConnected()
        {
            Channel.LicenseExpired = false;
            TryReconnection.Change(Timeout.Infinite, Timeout.Infinite); // Stop check if connection is lost

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

                // Without a pause the sent data will not be received and the first time login will fail.
                // Thread.Sleep(LoginSleepTime);

                //Logged = true;
                //Channel.ConnectionChange(true);
                //Channel.Spooler.SendNext();

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

            ;
            var login = Channel.CommandsForRouter.CreateCommand(Protocol.Command.ConnectionEstablished, null, null, Channel.MyId); // log in

            ExecuteSendData(login, OnLogged);
        }
        const int LoginSleepTime = 1500;

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
                if (!Client.Connect(address, port, TimeOutMs)) // ms timeout
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

        // Declare the event.
        public event Speed UpdateDownloadSpeed;
        public event Speed UpdateUploadSpeed;

        /// <summary>
        /// Start reading the stream by reading the first 4 bytes which contain the length of the data packet to read
        /// </summary>
        /// <param name="client"></param>
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
                Tuple<Stream, byte[], IDataConnection> state = Tuple.Create(stream, First4bytes, client);
                if (stream.CanTimeout)
                    stream.ReadTimeout = Timeout.Infinite;
                stream.BeginRead(First4bytes, 0, 4, OnReadLength, state);
            }
            catch (Exception ex)
            {
                Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                Disconnect();
            }
        }

        private byte[] First4bytes = new byte[4];


        void OnReadLength(IAsyncResult result)
        {
            var state = (Tuple<Stream, byte[], IDataConnection>)result.AsyncState;
            var stream = state.Item1;
            var client = state.Item3;

            var bytesRead = 0;
            try
            {
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
        /// Read incoming data packet
        /// </summary>
        /// <param name="lengthIncomingData">Length of incoming packet</param>
        /// <param name="stream">Data stream for reading incoming data</param>
        /// <param name="flag">Information on the type of data interpretation (for more information see the description of the items in the enumerator)</param>
        private void ReadBytes(int lengthIncomingData, Stream stream, DataFlags flag)
        {
            Protocol.Command command = default;
            byte[] data;
            try
            {
                data = new byte[lengthIncomingData];
                var readDataLen = 0;
                Debug.WriteLine("Start download" + lengthIncomingData);
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
                        stream.ReadTimeout = DataTimeout(partialLength);
                        UpdateDownloadSpeed?.Invoke(0, 0, lengthIncomingData);
                    }

                    readDataLen += stream.Read(data, readDataLen, partialLength); //Avoid asynchronous method to overload the operation with asynchronous mode management
                    if (stream.CanTimeout)
                    {
                        // exclude pipe or similar
                        var mbps = Math.Round((readDataLen / (watch.ElapsedMilliseconds + 1d)) / 1000, 2);
                        UpdateDownloadSpeed?.Invoke(mbps, readDataLen, lengthIncomingData);
                        // Debug.WriteLine("download " + readDataLen + "\\" + lengthIncomingData + " " + mbps + "mbps" + (readDataLen == lengthIncomingData ? " completed" : ""));
                    }
                    if (first == true && readDataLen > 0)
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
                if (stream.CanTimeout) // exclude pipe or similar
                    UpdateDownloadSpeed?.Invoke(0, 0, lengthIncomingData);
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
            //new Task(() => BeginRead(Client)).Start();
        }

        /// <summary>
        /// Calculate a reasonable timeout (in milliseconds) for transmitting a data packet, based on the length of the transmitted data.
        /// </summary>
        /// <param name="dataLength">The length of the data on which to calculate the timeout</param>
        /// <returns>Milliseconds</returns>
        private static int DataTimeout(int dataLength)
        {
            var mb = (double)dataLength / 1000000;
            return 15000 + Convert.ToInt32(mb / LimitMbps);
        }

        internal void InvokeError(ErrorType errorId, string description) => Channel.OnTcpError(errorId, description);

        /// <summary>
        /// Establish the connection and start the spooler
        /// </summary>
        /// <returns>Returns true if the connection is successful, false if there is an error</returns>
        /// <summary>
        /// Establish the connection and start the spooler
        /// </summary>
        /// <returns>Returns true if the connection is successful, false if there is an error</returns>
        internal bool Connect()
        {
            Exception exception = null;
            if (antiConcurrentConnection == 0)
            {
                Interlocked.Increment(ref antiConcurrentConnection); // antiConcurrentConnection++;                
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
                Interlocked.Decrement(ref antiConcurrentConnection); // antiConcurrentConnection--;       
            }
            if (exception != null)
                Disconnect();
            return exception == null;
        }

        /// <summary>
        /// Used to make a connection if the communication link breaks.
        /// </summary>
        /// <param name="tryConnectAgain"></param>
        public void Disconnect(bool tryConnectAgain = true)
        {
            if (antiConcurrentConnection == 0)
            {
                Interlocked.Increment(ref antiConcurrentConnection); // antiConcurrentConnection++;       
                if (Client != null) // Do not disconnect again
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
                    TryReconnection.Change(TimerIntervalCheckConnection, Timeout.Infinite); // restart check if connection is lost
                Interlocked.Decrement(ref antiConcurrentConnection); // antiConcurrentConnection--;       
            }
        }

        int antiConcurrentConnection = 0;

        /// <summary>
        /// Find if the socket is connected to the remote host.
        /// </summary>
        /// <returns></returns>
        public bool IsConnected()
        {
            return Client != null && Client.IsConnected && Channel.Connectivity; //According to the specifications, the property _client.Connected returns the connection status based on the last data transmission. The server may not be connected even if this property returns true
        }


    }
}
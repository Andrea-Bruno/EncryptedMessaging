using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static CommunicationChannel.Channel;
using static CommunicationChannel.CommandsForServer;

namespace CommunicationChannel
{
    /// <summary>
    /// This class is used for establishing link connection and communicating with the server.
    /// </summary>
    internal partial class Tcp : IDisposable
    {
        internal Tcp(Channel channel)
        {
            Channel = channel;
            TryReconnection = new Timer(OnTryReconnection);
            TimerAutoDisconnect = new Timer((o) => Disconnect(false));
            TimerKeepAlive = new Timer(OnTimerKeepAlive);
        }
        internal readonly Channel Channel;


        // =================== This timer checks if the connection has been lost and reestablishes it ====================================
        internal readonly Timer TryReconnection;
        private const int TimerIntervalCheckConnection = 20 * 1000;
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


        private const int MaxDataLength = 16000000; //16 MB - max data length enable to received the server
        internal TcpClient Client;
        /// <summary>
        /// Send the data, which will be parked in the spooler, cannot be forwarded immediately: If there is a queue or if there is no internet line the data will be parked.
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

        /// <summary>
        /// Send data to server (router) without going through the spooler
        /// </summary>
        /// <param name="data">Data to be sent</param>
        /// <param name="executeOnConfirmReceipt">Action to be taken when the router has successfully received the data sent</param>
        /// <param name="flag">Indicates some settings that the server/router will have to interpret</param>
        internal void ExecuteSendData(byte[] data, Action executeOnConfirmReceipt = null, DataFlags flag = DataFlags.None)
        {
            var dataLength = data.Length;
            // if (IsConnected() && !Logged && dataLength > 0 && data[0] != (byte)Protocol.Command.ConnectionEstablished && data[0] != (byte)Protocol.Command.DataReceivedConfirmation)
            if (!Logged)
            {
                var sendEvenWithoutLogin = dataLength > 0 && (data[0] == (byte)Protocol.Command.ConnectionEstablished || data[0] == (byte)Protocol.Command.DataReceivedConfirmation);
                if (!sendEvenWithoutLogin)
                {
                    Debugger.Break(); // Don't send any data before login is done!

                    // Wait for the login to complete
                    OnLogged = new AutoResetEvent(false);
                    if (!OnLogged.WaitOne(TimeOutMs))
                    {                      
#if DEBUG && !TEST
                        // Login Timeout!
                        Debug.WriteLine(Channel.ServerUri); // Current entry point
                        if (flag == DataFlags.DirectlyWithoutSpooler)
                            Debugger.Break(); // Don't send message directly without spooler before authentication on the server!
                        else
                            Debugger.Break(); // Verify if the server running and if you have internet connection!  (Perhaps there is no server at the current entry point)
#endif
                        OnSendCompleted(data, flag, new Exception("Sending data without logging in!"), false);
                        return;
                    }
                }
            }
            Task.Run(() =>
            {
                lock (this)
                {
                    if (dataLength > MaxDataLength) { OnSendCompleted(data, flag, new Exception("Data length over the allowed limit"), false); return; }

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
                        // var waitConfirmation = !directlyWithoutSpooler && command != Protocol.Command.DataReceivedConfirmation && command != Protocol.Command.ConnectionEstablished;
                        var waitConfirmation = flag == DataFlags.None && command != Protocol.Command.DataReceivedConfirmation;
                        var written = 0;
                        var mbps = 0d;
                        try
                        {
                            var stream = Client.GetStream();
                            var bit32 = flag == DataFlags.DirectlyWithoutSpooler ? 0b10000000_00000000_00000000_00000000 : 0;
                            var bit31 = flag == DataFlags.RouterData ? 0b01000000_00000000_00000000_00000000 : 0;
                            if (waitConfirmation)
                                WaitConfirmationSemaphore.Reset();
                            //WaitConfirmationSemaphore = waitConfirmation ? new AutoResetEvent(false) : null;
                            //var mb = (double)dataLength / 1000000;
                            stream.WriteTimeout = DataTimeout(dataLength); // TimeOutMs + Convert.ToInt32(mb / LimitMbps);
                            Debug.WriteLine("start upload");
                            UpdateDownloadSpeed?.Invoke(0, 0, data.Length);
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
                                UpdateDownloadSpeed?.Invoke(mbps, written, data.Length);
                                Channel.LastOUT = DateTime.UtcNow;
                                Debug.WriteLine("upload " + written + "\\" + data.Length + " " + mbps + "mbps" + (written == data.Length ? " completed" : ""));
                            }
                            stream.Flush();
                            if (waitConfirmation && !WaitConfirmationSemaphore.WaitOne(stream.WriteTimeout))
                            {
                                // Timeout!
                                if (command == Protocol.Command.ConnectionEstablished)
                                {
                                    Channel.LicenseExpired = true;
                                    Console.WriteLine(DateTime.UtcNow.ToString("G") + " Client id " + Channel.MyId + " Unable to connect to router:");
                                    Console.WriteLine("Has the license expired?");
                                    Console.WriteLine("The router is offline?");
                                }
#if DEBUG && !TEST
                                Debugger.Break(); // Timeout! license expired or router offline
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
                                    new Thread(() =>
                                    {
                                        executeOnConfirmReceipt.Invoke();
                                    }).Start();
                                }
                                OnSendCompleted(data, flag, null, false);
                            }
                            if (command != Protocol.Command.Ping)
                                ResumeAutoDisconnectTimer();
                            if (Logged)
                                Channel.Spooler.SendNext(); //Upon receipt confirmation, sends the next message
                        }
                        catch (Exception ex)
                        {
                            //Debugger.Break();
                            OnSendCompleted(data, flag, ex, true);
                            Disconnect();
                        }
                    }
                }
            });
        }


        /// <summary>
        /// On send completed it remove the sent packet and insert in the spooler queue before closing the communication channnel.
        /// </summary>
        /// <param name="data">data</param>
        /// <param name="flag">Indicates some settings that the server/router will have to interpret</param>        /// <param name="ex">exception</param>
        /// <param name="connectionIsLost">connection status</param>
        private void OnSendCompleted(byte[] data, DataFlags flag, Exception ex, bool connectionIsLost)
        {
            if (ex != null)
                Channel.Tcp.InvokeError(connectionIsLost ? ErrorType.LostConnection : ErrorType.SendDataError, ex.Message);
            if (flag == DataFlags.None)
                Channel.Spooler.OnSendCompleted(data, connectionIsLost);
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
                    if (exception != null)
                    {
                        Channel.OnTcpError(ErrorType.ConnectionFailure, exception.Message);
                        Disconnect();
                        return false;
                    }
                    OnConnected();
                    KeepAliveRestart();
                }
            }
            return true;
        }
        // private AutoResetEvent OnConnectedSemaphore;
        private bool _Logged;
        internal bool Logged
        {
            get { return _Logged; }
            set
            {
                _Logged = value;
                if (value)
                    OnLogged?.Set();
            }
        }
        private AutoResetEvent OnLogged;
        private void OnConnected()
        {
            Channel.LicenseExpired = false;
            TryReconnection.Change(Timeout.Infinite, Timeout.Infinite); // Stop check if connection is lost
            ResumeAutoDisconnectTimer();
            BeginRead(Client);
            void OnLogged()
            {
                Thread.Sleep(500);
                Logged = true;
                Channel.ConnectionChange(true);
            };
            var login = Channel.CommandsForRouter.CreateCommand(Protocol.Command.ConnectionEstablished, null, null, Channel.MyId); // log in
            ExecuteSendData(login, OnLogged);
        }

        private void StartLinger(int port, out Exception exception)
        {
            exception = null;
            try
            {
                var addresses = Dns.GetHostAddresses(Channel.ServerUri.Host).Reverse().ToArray();

                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(addresses[0], 5000);
                    if (reply.Status != IPStatus.Success)
                    {
                        exception = new Exception("Ping result = " + reply.Status + " (The router is off or unreachable)");
                        return;
                    }
                }
                Client = new TcpClient
                {
                    LingerState = new LingerOption(true, 0), // Close the connection immediately after the Close() method
                };

                // Client.ReceiveTimeout = 10000; // 10 secondi Client.SendTimeout = 10000; // 10 secondi
                // Client.SendTimeout = 10000; // 10 secondi
                // Client.NoDelay = false;


                var watch = Stopwatch.StartNew();
                if (!Client.ConnectAsync(addresses, port).Wait(TimeOutMs)) // ms timeout
                {
                    // the code that you want to measure comes here
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
                    if (ex.HResult == -2146232798)
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
                    var firstUint = BitConverter.ToUInt32(first4bytes, 0);
                    var dataLength = (int)(0B00111111_11111111_11111111_11111111 & firstUint);
                    if (dataLength > MaxDataLength)
                    {
                        Channel.OnTcpError(ErrorType.WrongDataLength, "Data length over the allowed limit");
                        Disconnect();
                        return;
                    }
                    var directlyWithoutSpooler = (firstUint & 0b10000000_00000000_00000000_00000000) != 0;
                    var routerData = (firstUint & 0b01000000_00000000_00000000_00000000) != 0;
                    DataFlags flag = DataFlags.None;
                    if (directlyWithoutSpooler)
                        flag = DataFlags.DirectlyWithoutSpooler;
                    else if (routerData)
                        flag = DataFlags.RouterData;
                    ReadBytes(dataLength, stream, flag);
                }
            }
            try
            {
                stream.ReadTimeout = Timeout.Infinite;
                stream.BeginRead(first4bytes, 0, 4, onReadLength, client);
            }
            catch (Exception ex)
            {
                Channel.OnTcpError(ErrorType.LostConnection, ex.Message);
                Disconnect();
            }
        }

        /// <summary>
        /// Read incoming data packet
        /// </summary>
        /// <param name="lengthIncomingData">Length of incoming packet</param>
        /// <param name="stream">Data stream for reading incoming data</param>
        /// <param name="flag">Information on the type of data interpretation (for more information see the description of the items in the enumerator)</param>
        private void ReadBytes(int lengthIncomingData, NetworkStream stream, DataFlags flag)
        {
            Protocol.Command command = default;
            byte[] data;
            try
            {
                // The server can send several messages in a single data packet
                data = new byte[lengthIncomingData];
                var readDataLen = 0;
                Debug.WriteLine("Start download" + lengthIncomingData);
                var watch = Stopwatch.StartNew();
                var first = true;
                int remaining;
                var timeOutAt = DateTime.UtcNow.AddMilliseconds(DataTimeout(lengthIncomingData));
                while (readDataLen < lengthIncomingData)
                {
                    int partialLength; // the reading is broken into smaller pieces in order to have an efficient control over the timeout that indicates the interruption of the data flow
                    if (first == true)
                        partialLength = 1; // In the first reading it reads only the first bit in order to immediately check if the packet is valid (the first bit must contain a valid command)
                    else
                    {
                        partialLength = lengthIncomingData - readDataLen;
                        if (partialLength > 65536)
                            partialLength = 65536;
                    }
                    remaining = lengthIncomingData - readDataLen;
                    if (partialLength > remaining)
                        partialLength = remaining;
                    stream.ReadTimeout = DataTimeout(partialLength);
                    readDataLen += stream.Read(data, readDataLen, partialLength);
                    var mbps = Math.Round((readDataLen / (watch.ElapsedMilliseconds + 1d)) / 1000, 2);
                    UpdateDownloadSpeed?.Invoke(mbps, readDataLen, lengthIncomingData);
                    Debug.WriteLine("download " + readDataLen + "\\" + lengthIncomingData + " " + mbps + "mbps" + (readDataLen == lengthIncomingData ? " completed" : ""));
                    if (first == true && readDataLen > 0) // validates the data packet by checking the first byte that must contain a valid command
                    {
                        first = false;
                        if (!Enum.IsDefined(typeof(Protocol.Command), data[0]))
                        {
                            throw new Exception("Command not supported");
                        }
                        command = (Protocol.Command)data[0];
                        Channel.LastCommandIN = command;
                        if (command != Protocol.Command.Ping)
                            SuspendAutoDisconnectTimer();
                    }
                    Channel.LastIN = DateTime.UtcNow;
                    if (DateTime.UtcNow >= timeOutAt)
                        throw new Exception("Data read timeout");
                }
                Debug.WriteLine("End download" + lengthIncomingData);
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
            new Task(() => BeginRead(Client)).Start(); // loop - restart reading a new incoming packet. Note: A new task is used to not add the call on this stack, and close the current stack.
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
                    KeepAliveSuspend();
                    SuspendAutoDisconnectTimer();
                    Client?.Close();
                    Client?.Dispose();
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
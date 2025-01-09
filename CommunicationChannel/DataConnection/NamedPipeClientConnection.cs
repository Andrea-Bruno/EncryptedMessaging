using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CommunicationChannel.DataConnection
{
    /// <summary>
    /// Implementation of IDataConnection for Named Pipe client connections.
    /// </summary>
    public class NamedPipeClientConnection : IDataConnection
    {
        private FullDuplexStreamSupport.PipeStream _pipeClient;

        /// <summary>
        /// Establishes a connection to the specified named pipe.
        /// </summary>
        /// <param name="basePipeName">The base name of the pipe to connect to.</param>
        /// <param name="port">The port to connect to (used as part of the pipe name).</param>
        /// <param name="timeOutMs">The timeout for the connection in milliseconds. Default is 0.</param>
        /// <returns>True if the connection is successful, otherwise false.</returns>
        public bool Connect(string basePipeName, int? port = null, int timeOutMs = default)
        {
            lock (this)
            {
                if (!FullDuplexStreamSupport.PipeStream.IsInitialized)
                {
                    var pipeIn = new NamedPipeClientStream(".", basePipeName + nameof(PipeDirection.In), PipeDirection.In);
                    var pipeOut = new NamedPipeClientStream(".", basePipeName + nameof(PipeDirection.Out), PipeDirection.Out);
                    FullDuplexStreamSupport.PipeStream.Initialize(pipeIn, pipeOut);
                }
                _pipeClient = new FullDuplexStreamSupport.PipeStream(_nextPipeID);


                _pipeClient.Connect(timeOutMs);
                if (_pipeClient.IsConnected)
                    _nextPipeID++;


                if (!_pipeClient.IsConnected)
                    Debugger.Break();


                return _pipeClient.IsConnected;
            }
        }

        static private uint _nextPipeID;

        /// <summary>
        /// Disconnects the current named pipe connection.
        /// </summary>
        public void Disconnect()
        {
            _pipeClient?.Close();
            _pipeClient?.Dispose();
        }

        /// <summary>
        /// Gets a value indicating whether the named pipe connection is currently established.
        /// </summary>
        public bool IsConnected => _pipeClient?.IsConnected ?? false;

        /// <summary>
        /// Gets the network stream associated with the current named pipe connection.
        /// </summary>
        /// <returns>The network stream for the current named pipe connection.</returns>
        public Stream GetStream()
        {
            return _pipeClient;
        }

        /// <summary>
        /// Disposes the named pipe connection, releasing all resources.
        /// </summary>
        public void Dispose()
        {
            Disconnect();
        }
    }

}

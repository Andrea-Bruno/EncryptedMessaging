using FullDuplexStreamSupport;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace CommunicationChannel.DataConnection
{
    /// <summary>
    /// Implementation of IDataConnection for Named Pipe client connections.
    /// </summary>
    public class NamedPipeClientConnection : IDataConnection
    {
        private PipeStreamClient PipeStreamClient;

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
                if (PipeStream == null)
                {
                    PipeStream = new FullDuplexStreamSupport.PipeStream();
                }
                if (!PipeStream.IsInitialized)
                {
                    var pipeIn = new NamedPipeClientStream(".", basePipeName + nameof(PipeDirection.Out), PipeDirection.In); // Stream Input must be connected with the server's output
                    var pipeOut = new NamedPipeClientStream(".", basePipeName + nameof(PipeDirection.In), PipeDirection.Out); // Stream Output must be connected with the server's input
                    PipeStream.InitializeClient(pipeIn, pipeOut, timeOutMs);
                }
                PipeStreamClient = PipeStream.AddNewClient();
                // PipeStreamClient = new PipeStreamClient(PipeStream);
                PipeStreamClient.Connect(timeOutMs);
                if (!PipeStreamClient.IsConnected)
                    Debugger.Break();
                return PipeStreamClient.IsConnected;
            }
        }
        static private FullDuplexStreamSupport.PipeStream PipeStream;

        /// <summary>
        /// Disconnects the current named pipe connection.
        /// </summary>
        public void Disconnect()
        {
            PipeStreamClient?.Close();
            PipeStreamClient?.Dispose();
        }

        /// <summary>
        /// Gets a value indicating whether the named pipe connection is currently established.
        /// </summary>
        public bool IsConnected => PipeStreamClient?.IsConnected ?? false;

        /// <summary>
        /// Disposes the named pipe connection, releasing all resources.
        /// </summary>
        public void Dispose()
        {
            Disconnect();
        }

        /// <summary>
        /// Gets the network stream associated with the current named pipe connection.
        /// </summary>
        /// <returns>The network stream for the current named pipe connection.</returns>
        Stream IDataConnection.GetStream()
        {
            return PipeStreamClient;
        }
    }

}

using System;

namespace CommunicationChannel
{
    /// <summary>
    /// This class handle the commands to be executed at the server level.
    /// </summary>
    public class CommandsForServer
    {
        internal CommandsForServer(Channel Channel) => _Channel = Channel;
        private readonly Channel _Channel;
        internal void SendCommandToServer(Protocol.Command command, byte[] dataToSend = null, ulong? chatId = null, ulong? myId = null, DataFlags dataFlags = DataFlags.None)
        {
            if (!_Channel.Tcp.IsConnected())
                _Channel.Tcp.Connect();
            var data = CreateCommand(command, dataToSend, chatId, myId);
            if (dataFlags == DataFlags.DirectlyWithoutSpooler)
                _Channel.Tcp.ExecuteSendData(data, flag: dataFlags);  // Send directly without using the spooler
            else
                _Channel.Tcp.SendData(data);                                                         // Send data using the spooler
        }

        /// <summary>
        /// DataFlags indicate additional instructions on how the router should handle this data and its routing.
        /// </summary>
        internal enum DataFlags
        {
            None = 0,
            DirectlyWithoutSpooler = 1, // Flag indicating that data will be sent directly to the recipient if connected, otherwise it will be lost
            RouterData = 2, // Flag indicating that the data packet is destined directly for the router (these are not data that need to be routed to other devices)
        }

        internal byte[] CreateCommand(Protocol.Command command, byte[] dataToSend = null, ulong? chatId = null, ulong? myId = null)
        {
            var data = new[] { (byte)command }; // 1 byte
            if (myId != null)
            {
                // ConnectionEstablished: command [0], domainId [1][2][3][4], senderId [5][6][7][8][9][10][11][12]
                data = data.Combine(Converter.GetBytes(_Channel.Domain)); // 4 byte
                var idArray = Converter.GetBytes((ulong)myId);
                data = data.Combine(idArray); // 8 byte
                if (_Channel.LicenseActivator != null)
                {
                    // login mode [13], OEM id [14][15][16][17][18][19][20][21], signature [22..]
                    data = data.Combine(new byte[] { 0 }); // Login mode (The version of login used (useful for future expansions))
                    data = data.Combine(Converter.GetBytes(_Channel.LicenseActivator.Item1)); // OEM license Id;
                    if (_Channel.LicenseActivator.Item2 != null)
                    {
                        Array.Resize(ref idArray, 32);
                        var signature = _Channel.LicenseActivator.Item2(idArray);
                        data = data.Combine(signature);
                    }
                }
            }
            // command [0] chatId [1][2][3][4][5][6][7][8], data [0..]
            if (chatId != null)
                data = data.Combine(Converter.GetBytes((ulong)chatId)); // 8 byte
            if (dataToSend != null)
                data = data.Combine(dataToSend);
            return data;
        }

        /// <summary>
        /// Send data to the server/router.
        /// Sends a data packet that the server/router will resend to its destination.
        /// </summary>
        /// <param name="chatId">chat to which data belong to</param>
        /// <param name="dataToSend">data</param>
        /// <param name="directlyWithoutSpooler"> if you want to send directly without spooler make it true else false </param>
        public void SendPostToServer(ulong chatId, byte[] dataToSend, bool directlyWithoutSpooler = false) => SendCommandToServer(Protocol.Command.SetNewPost, dataToSend, chatId, dataFlags: directlyWithoutSpooler ? DataFlags.DirectlyWithoutSpooler : DataFlags.None);

        /// <summary>
        /// Sends a data packet addressed to the router/server. This data packet will be interpreted by the router based on the function that is passed to the router when it is initialized. If no function is passed during initialization, sending data to the router will have no effect.
        /// </summary>
        /// <param name="dataToSend"></param>
        public void SendRouterData(byte[] dataToSend)
        {
            SendCommandToServer(Protocol.Command.SetNewPost, dataToSend, null, dataFlags: DataFlags.RouterData);
        }

        /// <summary>
        /// Confirmation that data is received at the server side.
        /// </summary>
        /// <param name="dataReceived">Data to received confirmation</param>
        public void DataReceivedConfirmation(byte[] dataReceived) => SendCommandToServer(Protocol.Command.DataReceivedConfirmation, Utility.DataIdBinary(dataReceived), dataFlags: DataFlags.DirectlyWithoutSpooler);

    }
}
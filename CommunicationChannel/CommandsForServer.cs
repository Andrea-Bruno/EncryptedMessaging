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
        internal void SendCommandToServer(Protocol.Command command, byte[] dataToSend = null, ulong? chatId = null, ulong? myId = null, bool directlyWithoutSpooler = false)
        {
            if (!_Channel.Tcp.IsConnected())
                _Channel.Tcp.Connect();
            var data = CreateCommand(command, dataToSend, chatId, myId);
            if (directlyWithoutSpooler)
                _Channel.Tcp.ExecuteSendData(data, directlyWithoutSpooler: directlyWithoutSpooler);  // Send directly without using the spooler
            else
                _Channel.Tcp.SendData(data);                                                         // Send data using the spooler
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
        /// Send data to the server.
        /// </summary>
        /// <param name="chatId">chat to which data belong to</param>
        /// <param name="dataToSend">data</param>
        /// <param name="directlyWithoutSpooler"> if you want to send directly without spooler make it true else false </param>
        public void SendPostToServer(ulong chatId, byte[] dataToSend, bool directlyWithoutSpooler = false) => SendCommandToServer(Protocol.Command.SetNewpost, dataToSend, chatId, directlyWithoutSpooler: directlyWithoutSpooler);


        /// <summary>
        /// Confirmation that data is recieved at the server side.
        /// </summary>
        /// <param name="dataReceived"> data to recieve confirmation </param>
        public void DataReceivedConfirmation(byte[] dataReceived) => SendCommandToServer(Protocol.Command.DataReceivedConfirmation, Utility.DataIdBinary(dataReceived), directlyWithoutSpooler: true);

    }
}
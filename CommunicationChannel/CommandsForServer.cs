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
            if (!_Channel.DataIO.IsConnected())
                _Channel.DataIO.Connect();
            var data = CreateCommand(command, dataToSend, chatId, myId);
            _Channel.DataIO.ExecuteSendData(data, flag: dataFlags);  // Send directly without using the spooler
        }

        /// <summary>
        /// DataFlags indicate additional instructions on how the router should handle this data and its routing.
        /// </summary>
        public enum DataFlags : byte
        {
            None = 0,
            DirectlyWithoutSpooler = 1, // Flag indicating that data will be sent directly to the recipient if connected, otherwise it will be lost
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
        /// Confirmation that data is received at the server side.
        /// </summary>
        /// <param name="dataReceived">Data to received confirmation</param>
        internal void DataReceivedConfirmation(byte[] dataReceived) => SendCommandToServer(Protocol.Command.DataReceivedConfirmation, Utility.DataIdBinary(dataReceived), dataFlags: DataFlags.DirectlyWithoutSpooler);

    }
}
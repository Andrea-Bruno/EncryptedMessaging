using System;

namespace EncryptedMessaging
{
    /// <summary>
    /// Class for interactions with the cloud (if any). The cloud can save the avatar, the contact list and if applicable more.
    /// </summary>
    public interface ICloudManager
    {
        /// <summary>
        /// Save a data to the cloud in a set type, with a name key
        /// </summary>
        /// <param name="type">The group to which the data belongs</param>
        /// <param name="name">The unique key assigned to the object</param>
        /// <param name="data">An array of data to save </param>
        /// <param name="commonArea">If true, save the data in a common area among all contacts, otherwise they will be saved in a private area accessible only to the current user</param>
        void SaveDataOnCloud(string type, string name, byte[] data, bool commonArea = false);
        /// <summary>
        /// Sends a previously saved data request command, and if it exists an event will be generated OnDataLoad
        /// </summary>
        /// <param name="type">The group to which the data belongs</param>
        /// <param name="name">The unique key assigned to the object</param>
        /// <param name="ifSizeIsDifferent">Upload the data only if the size has changed (It is an empirical method to avoid creating communication traffic for data we already have, it would be more correct to use a hash, but this creates a computational load on the cloud)</param>
        /// <param name="commonArea">If true, load the data from a common area among all contacts, otherwise they will be load from a private area accessible only to the current user</param>
        void LoadDataFromCloud(string type, string name, int? ifSizeIsDifferent = null, bool commonArea = false);

        /// <summary>
        /// Upload from the cloud all the data saved in a specific type group. An event OnDataLoad will be generated for each data
        /// </summary>
        /// <param name="type">The group to which the data belongs</param>
        /// <param name="commonArea">If true, load the data from a common area among all contacts, otherwise they will be load from a private area accessible only to the current user</param>
        void LoadAllDataFromCloud(string type, bool commonArea = false);

        /// <summary>
        /// Delete a data that has been saved on the cloud
        /// </summary>
        /// <param name="type">The group to which the data belongs</param>
        /// <param name="name">The unique key assigned to the object</param>
        /// <param name="commonArea">If true, an object in the common area will be deleted, otherwise an object will be deleted from the private area accessible only to the current user</param>
        void DeleteDataOnCloud(string type, string name, bool commonArea = false);

        /// <summary>
        /// Send push notifications on the ios network via our open source cloud system
        /// </summary>
        /// <param name="deviceToken">IOS device Token</param>
        /// <param name="chatId">ChatId</param>
        /// <param name="isVideo">Is video</param>
        /// <param name="contactNameOrigin">Name of contact to generate a notification</param>
        void SendPushNotification(string deviceToken, ulong chatId, bool isVideo, string contactNameOrigin);


        Contact Cloud { get; set; }
        /// <summary>
        /// It works like an event. It is generated when something is received from the cloud
        /// </summary>
        /// <param name="command">Command</param>
        /// <param name="parameters">Parameters</param>
        void OnCommand(ushort command, byte[][] parameters);
       
        /// <summary>
        /// Placeholder for the function of sending commands
        /// </summary>
        Action<ushort, byte[][]> SendCommand { set; get; }

        //void SendCommand(Action<  float , byte[][] >);

    }
}

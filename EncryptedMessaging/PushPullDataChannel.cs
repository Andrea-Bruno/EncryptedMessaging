using System.IO;
using System.Net;

namespace EncryptedMessaging
{
    internal class PushPullDataChannel
    {
        private static byte[] DownloadFileToByteArray(string fileUrl)
        {
            using (WebClient client = new WebClient())
            {
                return client.DownloadData(fileUrl);
            }
        }
        public static byte[] UploadByteArrayToUrl(byte[] data, string targetUrl)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(targetUrl);
            request.Method = "POST";
            request.ContentType = "application/octet-stream";
            request.ContentLength = data.Length;

            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(data, 0, data.Length);
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (MemoryStream memoryStream = new MemoryStream())
            {
                responseStream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }

    }
}

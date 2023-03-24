using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;

namespace EncryptedMessaging
{
    /// <summary>
    /// This class deals with time configuration related to the timestamps and sending and delivery of messages.
    /// </summary>
    public static class Time
    {
        private static bool Detected;
        private static bool SystemIsUpToDate;
        private static TimeSpan Delta = new TimeSpan(long.MinValue);

        /// <summary>
        /// Get current time & time based on system timezone.
        /// </summary>
        /// <returns>Current time & time, or null If there is no internet connection</returns>
        public static DateTime GetCurrentTimeGMT(out bool internetConnectionError)
        {
            lock (Environment.OSVersion)
            {
                if (!Detected)
                {
                    Detected = (GetAverageDateTimeFromWeb(out DateTime realDateAndTime, out Delta));
                    if (Detected)
                    {
                        SystemIsUpToDate = UpdateSystemDate(realDateAndTime);
                    }
                }
            }
            internetConnectionError = !Detected;
            return SystemIsUpToDate ? DateTime.UtcNow : Detected ? DateTime.UtcNow + Delta : DateTime.UtcNow;
        }
        private static bool GetAverageDateTimeFromWeb(out DateTime dateTime, out TimeSpan delta)
        {
            var webs = new[] {
                new Uri("http://108.59.14.4"), // timeanddate.com
                new Uri("http://17.253.144.10"), // apple.com
                new Uri("http://208.80.154.224"), // wikipedia.org
                new Uri("http://3.13.31.214"), // linuxfoundation.org
                new Uri("http://142.251.35.174"), // google.com
                new Uri("http://20.112.52.29"), // microsoft.com
            };

            var deltas = new List<TimeSpan>();
            for (var i = 1; i <= 1; i++)
                foreach (var web in webs)
                {
                    var time = GetDateTimeFromWeb(web);
                    if (time != null)
                        deltas.Add(DateTime.UtcNow - (DateTime)time);
                }
            if (deltas.Count == 0)
            {
                dateTime = DateTime.UtcNow;
                return false;
            }
            deltas.Sort();
            var middle = deltas.Count / 2;
            delta = deltas.Count % 2 == 0 ? new TimeSpan(deltas[middle].Ticks / 2 + deltas[middle + 1].Ticks / 2) : deltas[middle];
            dateTime = DateTime.UtcNow.Add(-delta);
            return true;
        }

        private static DateTime? GetDateTimeFromWeb(Uri fromWebsite)
        {
            using (var client = new HttpClient())
            {
                try
                {
                    var cancellationTokenSource = new CancellationTokenSource();
                    cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(1));
                    var result = client.GetAsync(fromWebsite, HttpCompletionOption.ResponseHeadersRead, cancellationTokenSource.Token).Result;
                    if (result.Headers?.Date != null)
                        return result.Headers?.Date.Value.UtcDateTime.AddMilliseconds(366); // for stats the time of website have a error of 366 ms; 					
                }
                catch
                {
                    // ignored
                }
                return null;
            }
        }
        private const int TolleranceSec = 2;
        private static bool UpdateSystemDate(DateTime newDateTimeUtc)
        {
            var currentDelta = Math.Abs((newDateTimeUtc - DateTime.UtcNow).TotalSeconds);
            if (currentDelta < TolleranceSec) { return true; }
            var newDateTime = newDateTimeUtc.ToLocalTime();
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var parameters = newDateTime.ToString("MMddHHmmyy.ss", CultureInfo.InvariantCulture);
                    Functions.ExecuteCommand("date", parameters);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // https://stackoverflow.com/questions/15878810/how-to-execute-command-on-cmd-from-c-sharp
                    Functions.ExecuteCommand("cmd.exe", "/C date " + newDateTime.ToString("d"));
                    Functions.ExecuteCommand("cmd.exe", "/C time " + newDateTime.ToString("T"));
                }
            }
            catch (Exception)
            {
                Console.WriteLine("The system does not support updating the date and time, you probably need to run the administrator application!");
            }
            var sec = Math.Abs((newDateTimeUtc - DateTime.UtcNow).TotalSeconds);
            return sec < TolleranceSec;
        }
    }
}

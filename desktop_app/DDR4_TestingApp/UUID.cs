using System;
using System.Collections.Generic;
using System.Text;

namespace DDR4_TestingApp
{
    internal class UUID
    {
            
        //Current UUID
        public static byte[]? uuid = null;
        public static bool used = true;
        public static bool in_use = false;
        public static bool _fetchingUUID = false;

        /// <summary>
        /// Generate a random 3-byte identifier whose bytes are the UTF-8 (ASCII)
        /// codes of three randomly chosen capital letters A–Z. Valid as a
        /// VerifyCmd/DynamicCmd/UUIDCmd uuid, which the server requires to be
        /// exactly three uppercase ASCII letters.
        /// </summary>
        public static void RandomUuid()
        {
            uuid = new byte[3];
            for (int i = 0; i < uuid.Length; i++)
                uuid[i] = (byte)Random.Shared.Next('A', 'Z' + 1); // min inclusive, max exclusive → 65..90

            //Mark as invalid
            used = true;
        }

        /// <summary>
        /// Render a byte[] as its UTF-8 text with a space between each byte's
        /// character, e.g. { 0x51, 0x58, 0x4D } -> "Q X M". Inverse of RandomUuid
        /// for display/logging a uuid.
        /// </summary>
        public static string GetReadable()
        {
            if (uuid is null || uuid.Length == 0)
                return string.Empty;

            var sb = new StringBuilder(uuid.Length * 2);
            for (int i = 0; i < uuid.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append((char)uuid[i]);  // 0x00–0x7F: ASCII == single-byte UTF-8
            }
            return sb.ToString();
        }

        public static async void update()
        {
            //Check if UUID was used
            if(!used) return;


            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                //Need to be connected to validate UUID
                //uuid = null;
                return;
            }

            // --- Fire an info fetch if one isn't already in flight ---
            // The 100 ms server-side sample in info_command means each request takes
            // ~100 ms + RTT, so if the timer ticks faster than that we'd otherwise
            // pile up overlapping requests. The flag drops ticks that arrive while
            // a fetch is still going.
            if (!_fetchingUUID && !Info._infoFetchInProgress)
            {

                //Attempt to generate and validate a new UUID
                _fetchingUUID = true;

                //Generate a new UUID
                RandomUuid();

                //Ensure it has a value
                if (uuid is null)
                {
                    _fetchingUUID = false;
                    return;
                }

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    
                    //Create a UUID Cmd structure
                    var UUIDCmd = new UUIDCmd();

                    UUIDCmd.Uuid = uuid;

                    UUIDRsp rsp = await TcpManager.SendUuidAsync(UUIDCmd, cts.Token);

                    //Obtain result
                    used = !rsp.Success;

                    Console.WriteLine($"success = {rsp.Success}");

                    Console.WriteLine("Verify UUID.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Info fetch failed: {ex.GetType().Name}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                    uuid = null;
                    used = true;
                    _fetchingUUID =false;
                }
                finally
                {
                    _fetchingUUID = false;
                    in_use = false;
                }
            }
        }

    }
}
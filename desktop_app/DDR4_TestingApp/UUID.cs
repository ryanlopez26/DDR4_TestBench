using System;
using System.Collections.Generic;
using System.Text;

namespace DDR4_TestingApp
{
    internal class UUID
    {
            
        //Current UUID
        private static UInt16? uuid = null;
        public static bool used = true;
        public static bool _fetchingUUID = false;

        //Marker of last checked
        private static bool updated = false;

        /// <summary>
        /// Generate a random 3-byte identifier whose bytes are the UTF-8 (ASCII)
        /// codes of three randomly chosen capital letters A–Z. Valid as a
        /// VerifyCmd/DynamicCmd/UUIDCmd uuid, which the server requires to be
        /// exactly three uppercase ASCII letters.
        /// </summary>
        public static void RandomUuid()
        {
            // Random u16 id (0..65535) — matches the server's u16 uuid / {uuid}.csv naming.
            uuid = (ushort)Random.Shared.Next(0, 65536); // min inclusive, max exclusive → 0..65535

            // Mark as invalid until a UUID check confirms it's free.
            used = true;
        }

        public static UInt16? GetUuid()
        {
            return uuid;
        }

        public static void SetUUID(UInt16 u)
        {
            uuid = u;
            updated = false;
        }

        public static void invalidate()
        {
            updated = false;
        }

        public static bool hasChecked()
        {
            return updated;
        }


        public static async void verifyUUID()
        {
            //Only check if needed
            if(updated) return;

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

                    UUIDCmd.Uuid = uuid.Value;

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
                    used = true;
                    _fetchingUUID =false;
                }
                finally
                {
                    updated = true;
                    _fetchingUUID = false;
                }
            }
        }

    }
}
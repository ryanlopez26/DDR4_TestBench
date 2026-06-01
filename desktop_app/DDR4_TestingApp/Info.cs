using System;
using System.Collections.Generic;
using System.Text;

namespace DDR4_TestingApp
{
    static public class Info
    {

        // On the form class:
        public static InfoRsp? sys;
        private static bool _infoFetchInProgress;


        public static async void update()
        {

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                sys = null;
                return;
            }

            // --- Fire an info fetch if one isn't already in flight ---
            // The 100 ms server-side sample in info_command means each request takes
            // ~100 ms + RTT, so if the timer ticks faster than that we'd otherwise
            // pile up overlapping requests. The flag drops ticks that arrive while
            // a fetch is still going.
            if (!_infoFetchInProgress)
            {
                _infoFetchInProgress = true;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    sys = await TcpManager.SendInfoAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Info fetch failed: {ex.GetType().Name}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                    sys = null;
                }
                finally
                {
                   // _infoFetchInProgress = false;
                }
            }
        }

    }
}

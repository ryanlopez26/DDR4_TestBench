using System;
using System.Collections.Generic;
using System.Text;

namespace DDR4_TestingApp
{
    static public class Config
    {

        static public ConfigCmd sys = new ConfigCmd
        {
            ChipIndex = 0,      // 0..7
            BusBytesPerChip = 2,  // x8 -> 1, x16 -> 2
            BusSizeInBytes = 8,                                     // x64 bus -> 8 bytes
            ChipSizeBytes = 1 * 1024 * 1024,    // MiB -> bytes
            enableChipSelect = 0,
        };

        static public async void apply()
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            if (Info.sys is InfoRsp info)
            {

                sys.BusBytesPerChip  = (byte)(info.RamOrganization / 8);

                Program.taskName = "CONFIG";

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await TcpManager.SendConfigAsync(sys, cts.Token);
                    Program.taskInfo = $"Config applied: chip {sys.ChipIndex}, " +
                                       $"x{sys.BusBytesPerChip * 8}, " +
                                       $"{sys.ChipSizeBytes / 1024 / 1024} MiB";
                }
                catch (OperationCanceledException)
                {
                    Program.taskInfo= "Config timed out.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Config failed: {ex.Message}");

                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace DDR4_TestingApp
{
    static public class Config
    {
        public struct RAM_Card {
            public String name;


        }

        static public ConfigCmd sys = new ConfigCmd
        {
            ChipIndex               = 0,
            BusBytesPerChip         = 2,
            BusSizeInBytes          = 8,
            ChipSizeBytes           = 1 * 1024 * 1024 * 1024,
            EnableChipSelect        = false,
            AddressMultiplier       = 0x0,
        };

        static public async void apply()
        {
            Program.taskName = "CONFIG";
            Program.taskProgress = 0.0f;

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            if (Info.sys is InfoRsp info)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await TcpManager.SendConfigAsync(sys, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Program.taskInfo= "Config timed out.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Config failed: {ex.Message}");

                } 
                finally
                {
                    Program.taskProgress = 100.0f;
                }
            }
        }
    }
}

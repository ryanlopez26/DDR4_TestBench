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
            EnableLogging           = false,
            BlockSize = 0x0001_0000,          
            BlockFactor = 0x0000_0004,
            NumBlocks = 0x0000_0100,
        };

        static public void updateCalculations()
        {

            //Compute the number of blocks
            sys.NumBlocks = Program.selection_size / sys.BlockSize;

            //Compute the number of sampled blocks
            var sampledBlocks = sys.NumBlocks / sys.BlockFactor;

            //Compute sampled size
            Program.sample_size = sampledBlocks * sys.BlockSize;


        }

        static public async void apply()
        {

            Program.taskName = "CONFIG";
            Program.taskProgress = 0.0f;

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

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

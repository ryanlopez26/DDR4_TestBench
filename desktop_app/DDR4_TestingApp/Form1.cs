using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace DDR4_TestingApp
{
    public partial class Form1 : Form
    {

        private readonly System.Windows.Forms.Timer _uiTimer = new();

        //Binding for capture table
        private BindingSource CaptureTable_bindings = new();

        // CaptureInfos
        private List<CaptureInfo> capture_infos = new List<CaptureInfo>();

        //Selected chip button
        private Int32 selected_chip_index = 0;

        public Form1()
        {

            _uiTimer.Interval = 1000;          // milliseconds — 4x per second
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            InitializeComponent();

        }

        // On the form class:
        private InfoRsp? _lastInfo;
        private bool _infoFetchInProgress;

        private void UpdateDramPanels(byte ramOrg, byte selectedChip)
        {
            var panels = new[] { dram0, dram1, dram2, dram3, dram4, dram5, dram6, dram7 };

            int enabledCount = ramOrg switch
            {
                8 => 8,    // x8  chips: all 8 active
                16 => 4,    // x16 chips: first 4 active
                _ => 0,    // anything else: none active
            };

            for (int i = 0; i < panels.Length; i++)
            {
                panels[i].BackColor =
                    i >= enabledCount ? Color.Gray :
                    i == selectedChip ? Color.Green :
                                          Color.Red;
            }

            if (enabledCount == 8)
            {
                sideA.Enabled = true;
                sideB.Enabled = true;
            }

            if (enabledCount == 4)
            {
                sideA.Enabled = true;
                sideB.Enabled = false;
            }
        }

        private async void UiTimer_Tick(object? sender, EventArgs e)
        {
            // --- Status indicator ---
            bool connected = TcpManager.Status == TcpManager.ConnectionStatus.Connected;
            connectionState.Text = connected ? "CONNECTED" : "DISCONNECTED";
            connectionState.BackColor = connected ? Color.Green : Color.Red;

            if (!connected)
            {
                _lastInfo = null;
                ClearInfoFields();
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
                    _lastInfo = await TcpManager.SendInfoAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Info fetch failed: {ex.GetType().Name}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                    _lastInfo = null;
                }
                finally
                {
                    _infoFetchInProgress = false;
                }
            }

            // --- Refresh UI from whatever we have ---
            UpdateInfoFields(_lastInfo);
        }

        private void ClearInfoFields()
        {
            manufacturerBox.Text = "";
            modelBox.Text = "";
            uptimeBox.Text = "";
            cpuBar.Value = 0;
            cpuPercentBox.Text = "";
            ramUsageBar.Value = 0;
            ramUsageBar.Text = "";
            uplinkBox.Text = "";
            downlinkBox.Text = "";
            chipOrgBox.Text = "";
            selectedChipBox.Text = "";
            startAddrBox.Text = "";
            endAddrBox.Text = "";
        }

        private void UpdateInfoFields(InfoRsp? maybeInfo)
        {
            if (maybeInfo is not InfoRsp info)
            {
                ClearInfoFields();
                return;
            }

            // Board properties
            manufacturerBox.Text = info.Manufacturer;
            modelBox.Text = info.Model;

            // HH:MM:SS — TotalHours so it doesn't wrap at 24 h
            var ts = TimeSpan.FromSeconds(info.Uptime);
            uptimeBox.Text = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";

            // CPU — 0..100 bar + readout
            cpuBar.Maximum = 100;
            cpuBar.Value = Math.Clamp((int)Math.Round(info.CpuUsage), 0, 100);
            cpuPercentBox.Text = $"{info.CpuUsage:F1}%";

            // RAM — bar scaled to total PS RAM, plus MB readout.
            // InfoRsp doesn't report total, so adjust this for your board (ZCU104 = 2 GiB).
            const int TOTAL_RAM_MB = 2048;
            ramUsageBar.Maximum = TOTAL_RAM_MB;
            ramUsageBar.Value = Math.Clamp((int)Math.Round(info.RamUsage), 0, TOTAL_RAM_MB);
            ramUsageBox.Text = $"{info.RamUsage:F0} MB";

            // Network throughput
            uplinkBox.Text = $"{info.Uplink:F2} Mbps";
            downlinkBox.Text = $"{info.Downlink:F2} Mbps";

            // SODIMM info
            chipOrgBox.Text = $"X{info.RamOrganization.ToString()}";
            selectedChipBox.Text = info.SelectedChip.ToString();
            startAddrBox.Text = $"0x{info.StartAddr:X8}";
            endAddrBox.Text = $"0x{info.EndAddr:X8}";

            UpdateDramPanels(info.RamOrganization, info.SelectedChip);
        }


        private void button4_Click(object sender, EventArgs e)
        {
            //Open file prompt to select workspace
            String path = Tools.SelectFolder();

            //Set as the new workspace path
            FileManager.WorkspacePath = path;

            //Load the workspace
            FileManager.LoadWorkspace(path);

        }

        private void panel5_Paint(object sender, PaintEventArgs e)
        {
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status == TcpManager.ConnectionStatus.Connected)
            {
                TcpManager.Disconnect();
            }
            else
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await TcpManager.ConnectAsync(ip_address.Text, int.Parse(port.Text), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    MessageBox.Show("Could not reach the VCU server within 5 seconds.");
                }
            }
        }

        private async void writeButton_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            cmdName.Text = "WRITE";

            var cmd = new WriteCmd
            {
                Pattern = (byte)writeMode.SelectedIndex,           // 0 = zeros, 1 = ones, 2 = pseudorandom
                Seed = UInt32.Parse(prngSeed.Text),
                Delay = 0,           // per-byte delay in ms
            };

            // Progress<T> captures the current SynchronizationContext (the UI thread),
            // so the lambda runs on the UI thread even though ConnectAsync's progress
            // reports come from a background continuation. Safe to touch controls.
            var progress = new Progress<WriteRsp>(rsp =>
            {
                progressBar.Value = (int)rsp.PercentComplete;
                statusLabel.Text = $"{rsp.BytesWritten:N0} bytes  ({rsp.PercentComplete:F1}%)  {(rsp.TimeSpentMs / 1000):F0}s";
            });

            using var cts = new CancellationTokenSource();
            EventHandler cancelHandler = (_, _) => cts.Cancel();

            writeButton.Enabled = false;
            cancelButton.Enabled = true;
            cancelButton.Click += cancelHandler;

            try
            {
                WriteRsp final = await TcpManager.SendWriteAsync(cmd, progress, cts.Token);
                statusLabel.Text = $"Done. {final.BytesWritten:N0} bytes in {(final.TimeSpentMs / 1000):F0} seconds";
            }
            catch (OperationCanceledException)
            {
                statusLabel.Text = "Write cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Write failed: {ex.Message}");
            }
            finally
            {
                cancelButton.Click -= cancelHandler;   // <-- the important line
                writeButton.Enabled = true;
                cancelButton.Enabled = false;
            }
        }

        private void genSeed_Click(object sender, EventArgs e)
        {
            prngSeed.Text = Random.Shared.Next(0, 100_000_000).ToString("D8");
        }

        private void sel_dram0_Click(object sender, EventArgs e)
        {
            selected_chip_index = 0;
        }

        private void sel_dram1_Click(object sender, EventArgs e)
        {
            selected_chip_index = 1;
        }

        private void sel_dram2_Click(object sender, EventArgs e)
        {
            selected_chip_index = 2;
        }

        private void sel_dram3_Click(object sender, EventArgs e)
        {
            selected_chip_index = 3;
        }

        private void sel_dram4_Click(object sender, EventArgs e)
        {
            selected_chip_index = 4;
        }

        private void sel_dram5_Click(object sender, EventArgs e)
        {
            selected_chip_index = 5;
        }

        private void sel_dram6_Click(object sender, EventArgs e)
        {
            selected_chip_index = 6;
        }

        private void sel_dram7_Click(object sender, EventArgs e)
        {
            selected_chip_index = 7;
        }

        private async void applyConfiguration_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            if (_lastInfo is InfoRsp info)
            {


                var cmd = new ConfigCmd
                {
                    ChipIndex = (byte)selected_chip_index,      // 0..7
                    BusBytesPerChip = (byte)(info.RamOrganization / 8),  // x8 -> 1, x16 -> 2
                    BusSizeInBytes = 8,                                     // x64 bus -> 8 bytes
                    ChipSizeBytes = uint.Parse(chipSizeBox.Text) * 1024 * 1024,    // MiB -> bytes
                    enableChipSelect = Convert.ToByte(enableChipSelection.Checked),
                };

                cmdName.Text = "CONFIG";

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await TcpManager.SendConfigAsync(cmd, cts.Token);
                    statusLabel.Text = $"Config applied: chip {cmd.ChipIndex}, " +
                                       $"x{cmd.BusBytesPerChip * 8}, " +
                                       $"{cmd.ChipSizeBytes / 1024 / 1024} MiB";
                }
                catch (OperationCanceledException)
                {
                    statusLabel.Text = "Config timed out.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Config failed: {ex.Message}");

                }
            }

        }

        private void writeMode_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private async void verifyButton_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            cmdName.Text = "VERIFY";

            var cmd = new VerifyCmd
            {
                Pattern = (byte)verifyMode.SelectedIndex,    // 0 = zeros, 1 = ones, 2 = pseudorandom
                Seed = UInt32.Parse(prngSeed.Text),
                Delay = 0,
            };

            var progress = new Progress<VerifyRsp>(rsp =>
            {
                progressBar.Value = (int)rsp.PercentComplete;
                statusLabel.Text = $"{rsp.BytesVerified:N0} bytes  ({rsp.PercentComplete:F1}%)  {(rsp.TimeSpentMs / 1000):F0}s";
            });

            using var cts = new CancellationTokenSource();
            EventHandler cancelHandler = (_, _) => cts.Cancel();

            verifyButton.Enabled = false;
            cancelButton.Enabled = true;
            cancelButton.Click += cancelHandler;

            try
            {
                VerifyRsp final = await TcpManager.SendVerifyAsync(cmd, progress, cts.Token);

                // Compose summary
                uint total = final.NumCorrect + final.NumErrors;
                double corruptedPercent = total > 0 ? (final.NumErrors * 100.0 / total) : 0.0;
                double seconds = final.TimeSpentMs / 1000.0;

                verificationResults.Text =
                    $"Finished verification in {seconds:F2} seconds!\n\n" +
                    $"Correct bytes:   {final.NumCorrect:N0}\n" +
                    $"Incorrect bytes: {final.NumErrors:N0}\n\n" +
                    $"{corruptedPercent:F2}% of the bytes were corrupted.";

                statusLabel.Text = $"Verify complete in {seconds:F1}s";
            }
            catch (OperationCanceledException)
            {
                statusLabel.Text = "Verify cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Verify failed: {ex.Message}");
            }
            finally
            {
                cancelButton.Click -= cancelHandler;
                verifyButton.Enabled = true;
                cancelButton.Enabled = false;
            }
        }

    }
}

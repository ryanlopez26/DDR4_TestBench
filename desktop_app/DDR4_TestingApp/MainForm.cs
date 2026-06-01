using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using ScottPlot.WinForms;

namespace DDR4_TestingApp
{
    public partial class MainForm : Form
    {

        private readonly System.Windows.Forms.Timer _uiTimer = new();

        // Parameters
        private bool captureDiff = false;
        private bool enableSEFI = false;
        private bool enableBeamTrigger = false;

        private DateTime startTime = DateTime.Now;
        private DateTime endTime = DateTime.Now;

        enum BeamState
        {   
            Manual,
            Armed,
            Waiting,
            Triggered,
            SEFI,
            Finished
        };

        private BeamState bms = BeamState.Manual;


        public MainForm()
        {

            _uiTimer.Interval = 1000;          // milliseconds — 4x per second
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            InitializeComponent();

        }



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
           
            connectionState.BackColor = connected ? Color.Green : Color.Red;

            //Attempt to update information
            Info.update();

            //Update dynamic buttons
            if (captureDiff)
            {
                captureModeRaw.BackColor = Color.Black;
                captureModeDiff.BackColor = Color.ForestGreen;
            }
            else
            {
                captureModeDiff.BackColor = Color.Black;
                captureModeRaw.BackColor = Color.ForestGreen;
            }

            if (enableSEFI)
            {
                setSEFIOff.BackColor = Color.Black;
                SetSEFIArm.BackColor = Color.ForestGreen;
            }
            else
            {
                setSEFIOff.BackColor = Color.ForestGreen;
                SetSEFIArm.BackColor = Color.Black;
            }

            if (enableBeamTrigger)
            {
                setBeamOff.BackColor = Color.Black;
                setBeamArm.BackColor = Color.ForestGreen;
            }
            else
            {
                setBeamOff.BackColor = Color.ForestGreen;
                setBeamArm.BackColor = Color.Black;
            }

            updateTaskInfo();
            UpdateStatusBar();

            UpdateInfoFields(Info.sys);
        }

        private void updateTaskInfo()
        {

            //Update time difference calculation
            taskStart.Text = startTime.ToString();
            taskEnd.Text = endTime.ToString();
            duration.Text = $"{(endTime - startTime).TotalSeconds:F2} seconds";

            //Update arm status
            switch (bms)
            {
                case BeamState.Manual:
                    beamSyncStatus.Text = "Manual Control";
                    break;
                case BeamState.Armed:
                    beamSyncStatus.Text = "Ready for Arming";
                    break;
                case BeamState.Waiting:
                    beamSyncStatus.Text = "Waiting for beam...";
                    break;
                case BeamState.Triggered:
                    beamSyncStatus.Text = "Beam triggered";
                    break;
                case BeamState.Finished:
                    beamSyncStatus.Text = "Task finished";
                    break;
                case BeamState.SEFI:
                    beamSyncStatus.Text = "SEFI Detected";
                    break;
            }
        }
        public void UpdateStatusBar()
        {
            //Attempt to update task indicator
            taskProgress.Value = (int)Program.taskProgress;
            taskInfo.Text = Program.taskInfo;
            taskName.Text = Program.taskName;

            if (Program.beamStatus)
            {
                beamIndicator.BackColor = Color.Green;
            } else { 
                beamIndicator.BackColor = Color.Red;
            }
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

            fpga_bank.Text = "";
            fpga_rank.Text = "";
            fpga_bg.Text = "";
            fpga_capacity.Text = "";
            fpga_cas.Text = "";
            fpga_col.Text = "";
            fpga_row.Text = "";
            fpga_organization.Text = "";
            fpga_stack_height.Text = "";
        }

        private void UpdateInfoFields(InfoRsp? maybeInfo)
        {
            if (maybeInfo is not InfoRsp info)
            {
                ClearInfoFields();
                return;
            }

            //Beam status
            Program.beamStatus = info.BeamActive;

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
            fpga_bank.Text = info.PlBank.ToString();
            fpga_rank.Text = info.PlRanks.ToString();
            fpga_bg.Text = info.PlBg.ToString();
            fpga_capacity.Text = $"{info.PlCapacity.ToString()} GB";
            fpga_cas.Text = $"{info.PlCas.ToString()} CL";
            fpga_col.Text = info.PlCol.ToString();
            fpga_row.Text = info.PlRow.ToString();
            fpga_organization.Text = $"X{info.PlOrganization.ToString()}";
            fpga_stack_height.Text = info.PlStackHeight.ToString();

            UpdateDramPanels(info.PlOrganization, info.SelectedChip);
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

            Program.taskName = "WRITE";

            uint taskDelay = 0;

            //Check if we need to add delay
            if (enableDelay.Checked)
            {
                taskDelay = (uint)((delayAmount.Value * 1000) / 100);
            }

            var cmd = new WriteCmd
            {
                Pattern = (byte)writeMode.SelectedIndex,           // 0 = zeros, 1 = ones, 2 = pseudorandom
                Seed = UInt32.Parse(prngSeed.Text),
                Delay = taskDelay,           // per-byte delay in ms
                BeamTriggered = enableBeamTrigger
            };

            bms = BeamState.Waiting;
            if(!cmd.BeamTriggered) startTime = DateTime.Now;

            // Prev beam value
            bool prevBeam = false;

            // Progress<T> captures the current SynchronizationContext (the UI thread),
            // so the lambda runs on the UI thread even though ConnectAsync's progress
            // reports come from a background continuation. Safe to touch controls.
            var progress = new Progress<WriteRsp>(rsp =>
            {

                //Check to see if beam is triggered
                if (!prevBeam && rsp.BeamActive)
                {
                    //Begin time capture
                    startTime = DateTime.Now;

                    bms = BeamState.Triggered;
                }

                //Check to see if beam is inactive
                if (prevBeam && !rsp.BeamActive)
                {
                    //End time capture
                    endTime = DateTime.Now;
                }

                //Update end time
                if (!cmd.BeamTriggered) endTime = DateTime.Now;

                //Update beam value
                prevBeam = rsp.BeamActive;
                updateTaskInfo();

                //Update beam status
                Program.beamStatus = rsp.BeamActive;

                Program.taskProgress = rsp.PercentComplete;
                Program.taskInfo = $"{rsp.BytesWritten:N0} bytes  ({rsp.PercentComplete:F1}%)  {(rsp.TimeSpentMs / 1000):F0}s";
                UpdateStatusBar();
            });

            using var cts = new CancellationTokenSource();
            EventHandler cancelHandler = (_, _) => cts.Cancel();

            writeButton.Enabled = false;

            try
            {
                WriteRsp final = await TcpManager.SendWriteAsync(cmd, progress, cts.Token);
                taskInfo.Text = $"Done. {final.BytesWritten:N0} bytes in {(final.TimeSpentMs / 1000):F0} seconds";
            }
            catch (OperationCanceledException)
            {
                taskInfo.Text = "Write cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Write failed: {ex.Message}");
            }
            finally
            {
                //Update end time
                if (!cmd.BeamTriggered) endTime = DateTime.Now;

                if (prevBeam) { endTime = DateTime.Now; }
                writeButton.Enabled = true;
                bms = BeamState.Finished;
            }
        }

        private void genSeed_Click(object sender, EventArgs e)
        {
            prngSeed.Text = Random.Shared.Next(0, 100_000_000).ToString("D8");
        }

        private void sel_dram0_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 0;
        }

        private void sel_dram1_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 1;
        }

        private void sel_dram2_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 2;
        }

        private void sel_dram3_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 3;
        }

        private void sel_dram4_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 4;
        }

        private void sel_dram5_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 5;
        }

        private void sel_dram6_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 6;
        }

        private void sel_dram7_Click(object sender, EventArgs e)
        {
            Config.sys.ChipIndex = 7;
        }

        private async void verifyButton_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            Program.taskName = "VERIFY";


            uint taskDelay = 0;

            //Check if we need to add delay
            if (enableDelay.Checked)
            {
                taskDelay = (uint)((delayAmount.Value * 1000) / 100);
            }

            var cmd = new VerifyCmd
            {
                Pattern = (byte)verifyMode.SelectedIndex,    // 0 = zeros, 1 = ones, 2 = pseudorandom
                Seed = UInt32.Parse(prngSeed.Text),
                Delay = taskDelay,
                BeamTriggered = enableBeamTrigger
            };
            bms = BeamState.Armed;
            bool prevBeam = false;
            if (!cmd.BeamTriggered) startTime = DateTime.Now;

            var progress = new Progress<VerifyRsp>(rsp =>
            {
                //Check to see if beam is triggered
                if (!prevBeam && rsp.BeamActive)
                {
                    //Begin time capture
                    startTime = DateTime.Now;
                    bms = BeamState.Waiting;
                }

                //Check to see if beam is inactive
                if (prevBeam && !rsp.BeamActive)
                {
                    //End time capture
                    endTime = DateTime.Now;
                }

                //Update beam value
                prevBeam = rsp.BeamActive;

                //Update beam status
                Program.beamStatus = rsp.BeamActive;

                //Update end time
                if (!cmd.BeamTriggered) endTime = DateTime.Now;

                updateTaskInfo();

                // Compose summary
                uint total = rsp.NumCorrect + rsp.NumErrors;
                double corruptedPercent = total > 0 ? (rsp.NumErrors * 100.0 / total) : 0.0;
                double seconds = rsp.TimeSpentMs / 1000.0;

                verificationResults.Text =
                    $"Finished verification in {seconds:F2} seconds!\n\n" +
                    $"Correct bits:   {rsp.NumCorrect:N0}\n" +
                    $"Incorrect bits: {rsp.NumErrors:N0}\n\n" +
                    $"{corruptedPercent:F2}% of the bits were corrupted.";


                Program.taskProgress = (int)rsp.PercentComplete;
                Program.taskInfo = $"{rsp.BytesVerified:N0} bytes  ({rsp.PercentComplete:F1}%)  {(rsp.TimeSpentMs / 1000):F0}s";
                UpdateStatusBar();
            });

            using var cts = new CancellationTokenSource();
            EventHandler cancelHandler = (_, _) => cts.Cancel();

            verifyButton.Enabled = false;

            try
            {
                VerifyRsp final = await TcpManager.SendVerifyAsync(cmd, progress, cts.Token);

                // Compose summary
                uint total = final.NumCorrect + final.NumErrors;
                double corruptedPercent = total > 0 ? (final.NumErrors * 100.0 / total) : 0.0;
                double seconds = final.TimeSpentMs / 1000.0;

                verificationResults.Text =
                    $"Finished verification in {seconds:F2} seconds!\n\n" +
                    $"Correct bits:   {final.NumCorrect:N0}\n" +
                    $"Incorrect bits: {final.NumErrors:N0}\n\n" +
                    $"{corruptedPercent:F2}% of the bits were corrupted.";

                Program.taskInfo = $"Verify complete in {seconds:F1}s";
            }
            catch (OperationCanceledException)
            {
                Program.taskInfo = "Verify cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Verify failed: {ex.Message}");
            }
            finally
            {
                if (!cmd.BeamTriggered) endTime = DateTime.Now;

                //Mark beam inactive
                if (prevBeam)
                {
                    //Test ended with active beam
                    endTime = DateTime.Now;
                }
                bms = BeamState.Finished;
                verifyButton.Enabled = true;
            }
        }

        private void selectSaveLocation_Click(object sender, EventArgs e)
        {
            dumpPath.Text = Tools.SelectFolder(dumpPath.Text);
        }

        private async void dumpButton_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected || !Info.sys.HasValue)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            // Parse inputs — offset accepts "0x..." hex or decimal, num pages is decimal.
            uint offset, numPages;
            try
            {
                offset = 0;
                numPages = Config.sys.ChipSizeBytes / TcpManager.PAGE_SIZE;
            }
            catch (Exception)
            {
                MessageBox.Show("Offset must be hex (0x...) or decimal; pages must be a positive integer.");
                return;
            }

            if (numPages == 0)
            {
                MessageBox.Show("Page count must be at least 1.");
                return;
            }

            Program.taskName = "DUMP";

            var cmd = new DumpCmd { OffsetStart = offset, NumPages = numPages, ComparisonMode = captureDiff };

            int pagesReceived = 0;
            var progress = new Progress<DumpPage>(page =>
            {
                pagesReceived++;
                Program.taskProgress = Math.Clamp((int)(pagesReceived * 100L / numPages), 0, 100);
                Program.taskInfo = $"Page {pagesReceived}/{numPages} @ 0x{page.Address:X8}";
                UpdateStatusBar();
            });

            using var cts = new CancellationTokenSource();
            EventHandler cancelHandler = (_, _) => cts.Cancel();

            dumpButton.Enabled = false;

            try
            {
                var pages = await TcpManager.SendDumpAsync(cmd, progress, cts.Token);

                // Write the raw bytes to disk in the workspace.
                string filename = dumpFileName.Text + ".bin";
                string path = Path.Combine(dumpPath.Text, filename);

                using (var fs = File.Create(path))
                {
                    foreach (var page in pages)
                        fs.Write(page.Data, 0, page.Data.Length);
                }

                // Summary + small hex preview of the first page.
                uint totalErrors = (uint)pages.Sum(p => (long)p.NumErrors);
                long totalBytes = pages.Sum(p => (long)p.Data.Length);

                if (pages.Count > 0)
                {
                    int previewLen = Math.Min(256, pages[0].Data.Length);
                }

                Program.taskInfo = $"Dumped {totalBytes:N0} bytes ({pages.Count} pages)";
            }
            catch (OperationCanceledException)
            {
                Program.taskInfo = "Dump cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dump failed: {ex.Message}");
            }
            finally
            {
                dumpButton.Enabled = true;
            }
        }

        private void enableChipSelection_CheckedChanged(object sender, EventArgs e)
        {
            Config.sys.enableChipSelect = Convert.ToByte(enableChipSelection.Checked);
        }

        private void chipSizeBox_TextChanged(object sender, EventArgs e)
        {

        }

        private void applyConfiguration_Click(object sender, EventArgs e)
        {
            Config.apply();
        }


        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void tabPage2_Click(object sender, EventArgs e)
        {

        }

        private void captureModeRaw_Click(object sender, EventArgs e)
        {
            captureDiff = false;
        }

        private void captureModeDiff_Click(object sender, EventArgs e)
        {
            captureDiff = true;
        }

        private void setBeamArm_Click(object sender, EventArgs e)
        {
            enableBeamTrigger = true;
            bms = BeamState.Armed;
        }

        private void setBeamOff_Click(object sender, EventArgs e)
        {
            enableBeamTrigger = false;
            bms = BeamState.Manual;
        }

        private void SetSEFIArm_Click(object sender, EventArgs e)
        {
            enableSEFI = true;
        }

        private void setSEFIOff_Click(object sender, EventArgs e)
        {
            enableSEFI = false;
        }

        private void chip_capacity_SelectedIndexChanged(object sender, EventArgs e)
        {
            var factor = 0.0;

            switch (chip_capacity.SelectedIndex)
            {
                case 0:
                    factor = 0.010;
                    break;
                case 1:
                    factor = 0.100;
                    break;
                case 3:
                    factor = 0.200;
                    break;
                case 4:
                    factor = 0.250;
                    break;
                case 5:
                    factor = 0.500;
                    break;
                case 6:
                    factor = 1.00;
                    break;
                case 7:
                    factor = 1.5;
                    break;
            }

            Config.sys.ChipSizeBytes = (uint)((float)factor * (float)1024 * 1024.0 * 1024.0);
        }
    }
}

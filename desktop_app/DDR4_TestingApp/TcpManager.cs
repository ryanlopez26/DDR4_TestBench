// TcpManager.cs — C# client for the Rust VCU TCP server.
//
// Protocol (matches server.rs / commands.rs / types.rs):
//
//     [SYNC u32 BE = 0xDEADBEEF]
//     [CMD  u8 ]
//     [LEN  u16 BE]
//     [PAYLOAD ... LEN bytes]
//     [TERM u32 BE = 0xCAFEBABE]
//
// Payloads are bincode-encoded with big-endian fixed-width integers, so
// every multi-byte field on the wire is plain big-endian with no length
// prefixes or padding.
//
// Commands:
//   0x01 Write   -> WriteCmd    { u8 pattern, u64 seed }                        9 bytes
//   0x02 Verify  -> VerifyCmd   { u8 pattern, u64 seed }                        9 bytes
//   0x03 Dump    -> DumpCmd     { u32 offset_start, u32 num_pages, bool cmp }   9 bytes
//   0x04 Config  -> ConfigCmd   { u8 chip_index, u8 bus_bytes_per_chip,
//                                 u32 bus_size_in_bytes, u32 chip_size_bytes,
//                                 bool enable_chip_select,
//                                 u32 address_multiplier }                    15 bytes
//   0x05 Dynamic -> DynamicCmd  { u8 pattern, u64 seed, u32 sample_size_in_bytes,
//                                 bool wait_for_beam, f32 trigger_threshold } 18 bytes
//   0x06 Info    -> (empty payload) — server replies with InfoRsp
//   0x07 Reset   -> ResetCmd    { bool fpga_reset, bool controller_reset }     2 bytes
//                   <- ResetRsp { bool success }                              1 byte
//
// Responses re-use the same framing.
//   - Write / Verify: periodic progress frames; the caller waits until
//     percent_complete reaches 100.
//   - Dump: one frame per page (fixed 16-byte header + raw page bytes).
//   - Config: a single 1-byte ACK payload (contents not meaningful).
//   - Dynamic: periodic progress frames with NO completion signal — per
//     commands.rs, the server loops for as long as the beam is asserted
//     (or forever, if wait_for_beam is false, until a SEFI is detected).
//     The caller must cancel to stop receiving frames.
//   - Reset: commands.rs's reset_command() now sends back a ResetRsp
//     { success } once the requested line pulses finish (up to ~1.5 s if
//     both fpga_reset and controller_reset are set — each pulses low/high/
//     low with 250 ms holds), so this call blocks until that completes.
//     NOTE: gpio.rs's EMIO map was recently changed to five read-only
//     channels (beam, calibration, UI clock, PL clock, FPGA-loaded status)
//     with no FPGA-reset/controller-reset output lines anymore. Unless
//     reset_command() has been repointed at some other mechanism, this
//     command currently has no hardware backing — flagging it here rather
//     than removing it, since the client can't tell which is true from
//     this side of the wire.
//   - Info: single InfoRsp reply { beam_signal, calibration_signal,
//     ui_clock_signal, pl_clock_signal, fpga_loaded_status } — one bool
//     per gpio.rs EMIO channel.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DDR4_TestingApp
{
    // ============================== Wire types ==============================

    public struct ConfigCmd
    {
        public byte ChipIndex;
        public byte BusBytesPerChip;
        public uint BusSizeInBytes;
        public uint ChipSizeBytes;
        public bool EnableChipSelect;
        public uint AddressMultiplier;
    }

    public struct WriteCmd
    {
        public byte Pattern;   // 0 = zeros, 1 = ones, 2 = pseudorandom
        public ulong Seed;
    }

    public struct VerifyCmd
    {
        public byte Pattern;
        public ulong Seed;
    }

    public struct DumpCmd
    {
        public uint OffsetStart;
        public uint NumPages;
        public bool ComparisonMode;
    }

    public struct DynamicCmd
    {
        // Pattern generation
        public byte Pattern;   // 0 = zeros, 1 = ones, 2 = pseudorandom
        public ulong Seed;

        // Test configuration
        public uint SampleSizeInBytes; // window (in bytes) over which error_rate/error_percent are computed
        public bool WaitForBeam;       // block sending test writes until BeamSignal reads high

        // SEFI threshold
        public float TriggerThreshold; // error_rate above this marks SefiDetected
    }

    public struct ResetCmd
    {
        public bool FpgaReset;
        public bool ControllerReset;
    }

    public struct ResetRsp
    {
        public bool Success;
    }

    public struct WriteRsp
    {
        public uint BytesWritten;
        public float TimeSpentMs;
        public float PercentComplete;
    }

    public struct VerifyRsp
    {
        public uint BytesVerified;
        public float TimeSpentMs;
        public float PercentComplete;
        public ulong NumErrors;
        public ulong NumCorrect;
    }

    public struct DumpPage
    {
        public float TimeSpentMs;
        public ulong NumErrors;
        public uint Address;
        public byte[] Data;     // PAGE_SIZE bytes (1024 by default)
    }

    public struct DynamicRsp
    {
        // Time statistics
        public float ExposureTimeMs;
        public float TotalTimeMs;
        public float TimeToSefi;

        // Error statistics (NOTE: despite the name, TotalBytes/TotalErrors on
        // the wire are accumulated in *bits*, not bytes — commands.rs sums
        // per-byte popcount differences directly into these fields)
        public ulong TotalBytes;
        public ulong TotalErrors;
        public float ErrorRate;
        public float ErrorPercent;

        // Capture status
        public bool ExposureStarted;
        public bool SefiDetected;

        // Signal status — one bool per gpio.rs EMIO channel (all five are
        // read-only inputs: beam, calibration, UI clock, PL clock, FPGA-loaded).
        public bool BeamSignal;
        public bool CalibrationSignal;
        public bool UiClockSignal;
        public bool PlClockSignal;
        public bool FpgaLoadedStatus;
    }

    public struct InfoRsp
    {
        public bool BeamSignal;
        public bool CalibrationSignal;
        public bool UiClockSignal;
        public bool PlClockSignal;
        public bool FpgaLoadedStatus;
    }

    // ============================== TcpManager ==============================

    internal static class TcpManager
    {
        // ---- Protocol constants ------------------------------------------------
        private const uint SYNC_MARKER = 0xDEAD_BEEF;
        private const uint TERM_MARKER = 0xCAFE_BABE;

        private const byte CMD_WRITE = 0x01;
        private const byte CMD_VERIFY = 0x02;
        private const byte CMD_DUMP = 0x03;
        private const byte CMD_CONFIG = 0x04;
        private const byte CMD_DYNAMIC = 0x05;
        private const byte CMD_INFO = 0x06;
        private const byte CMD_RESET = 0x07;

        public const int PAGE_SIZE = 1024;

        // ---- Connection state --------------------------------------------------
        private static TcpClient? client;
        private static NetworkStream? stream;

        // The Rust server is single-threaded — at most one command in flight.
        // We enforce that on the client side too so two callers can't interleave
        // bytes on the wire.
        private static readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

        public static string Host { get; private set; } = "";
        public static int Port { get; private set; } = 8080;

        public enum ConnectionStatus { Disconnected, Connected }

        public static ConnectionStatus Status =>
            (client?.Connected ?? false) ? ConnectionStatus.Connected
                                         : ConnectionStatus.Disconnected;

        /// <summary>Raised when <see cref="Status"/> changes.</summary>
        public static event Action<ConnectionStatus>? StatusChanged;

        // ============================== Connect/Disconnect ==============================

        /// <summary>Connect to the Rust VCU server. Replaces any existing connection.</summary>
        public static async Task ConnectAsync(string host, int port = 8080,
                                              CancellationToken ct = default)
        {
            Disconnect();

            var c = new TcpClient();
            await c.ConnectAsync(host, port, ct).ConfigureAwait(false);
            client = c;
            stream = c.GetStream();
            Host = host;
            Port = port;
            StatusChanged?.Invoke(ConnectionStatus.Connected);
        }

        public static void Disconnect()
        {
            bool was = Status == ConnectionStatus.Connected;
            try { stream?.Close(); } catch { /* ignore */ }
            try { client?.Close(); } catch { /* ignore */ }
            stream = null;
            client = null;
            if (was) StatusChanged?.Invoke(ConnectionStatus.Disconnected);
        }

        // ============================== High-level commands ==============================

        /// <summary>
        /// Send a Config command. Returns once the server has applied the new
        /// geometry and ACKed.
        /// </summary>
        public static async Task SendConfigAsync(ConfigCmd cfg,
                                                 CancellationToken ct = default)
        {
            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteFrameAsync(CMD_CONFIG, EncodeConfig(cfg), ct).ConfigureAwait(false);

                var (cmd, _) = await ReadFrameAsync(ct).ConfigureAwait(false);
                if (cmd != CMD_CONFIG)
                    throw new InvalidDataException(
                        $"expected Config ACK (0x04), got 0x{cmd:X2}");
            }
            finally { sendLock.Release(); }
        }

        /// <summary>
        /// Send a Write command. Streams progress to <paramref name="progress"/>
        /// (if supplied) and completes when the server reports 100%.
        /// </summary>
        public static async Task<WriteRsp> SendWriteAsync(
            WriteCmd w,
            IProgress<WriteRsp>? progress = null,
            CancellationToken ct = default)
        {
            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteFrameAsync(CMD_WRITE, EncodeWrite(w), ct).ConfigureAwait(false);

                while (true)
                {
                    var (cmd, payload) = await ReadFrameAsync(ct).ConfigureAwait(false);
                    if (cmd != CMD_WRITE)
                        throw new InvalidDataException(
                            $"unexpected response 0x{cmd:X2} during Write");

                    var rsp = DecodeWriteRsp(payload);
                    progress?.Report(rsp);
                    if (rsp.PercentComplete >= 100.0f) return rsp;
                }
            }
            finally { sendLock.Release(); }
        }

        /// <summary>
        /// Send a Verify command. Streams progress and returns the final
        /// response (including error/correct bit counts) when complete.
        /// </summary>
        public static async Task<VerifyRsp> SendVerifyAsync(
            VerifyCmd v,
            IProgress<VerifyRsp>? progress = null,
            CancellationToken ct = default)
        {
            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteFrameAsync(CMD_VERIFY, EncodeVerify(v), ct).ConfigureAwait(false);

                while (true)
                {
                    var (cmd, payload) = await ReadFrameAsync(ct).ConfigureAwait(false);
                    if (cmd != CMD_VERIFY)
                        throw new InvalidDataException(
                            $"unexpected response 0x{cmd:X2} during Verify");

                    var rsp = DecodeVerifyRsp(payload);
                    progress?.Report(rsp);
                    if (rsp.PercentComplete >= 100.0f) return rsp;
                }
            }
            finally { sendLock.Release(); }
        }

        /// <summary>
        /// Send a Dump command. One response frame is received per page;
        /// each is reported via <paramref name="onPage"/> as it arrives and
        /// also collected into the returned list.
        /// </summary>
        public static async Task<List<DumpPage>> SendDumpAsync(
            DumpCmd d,
            IProgress<DumpPage>? onPage = null,
            CancellationToken ct = default)
        {
            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteFrameAsync(CMD_DUMP, EncodeDump(d), ct).ConfigureAwait(false);

                var pages = new List<DumpPage>((int)Math.Min(d.NumPages, 1024));
                for (uint i = 0; i < d.NumPages; i++)
                {
                    var (cmd, payload) = await ReadFrameAsync(ct).ConfigureAwait(false);
                    if (cmd != CMD_DUMP)
                        throw new InvalidDataException(
                            $"unexpected response 0x{cmd:X2} during Dump");

                    var page = DecodeDumpPage(payload);
                    pages.Add(page);
                    onPage?.Report(page);
                }
                return pages;
            }
            finally { sendLock.Release(); }
        }

        /// <summary>
        /// Send a Dynamic command and stream <see cref="DynamicRsp"/> progress
        /// frames to <paramref name="progress"/> for as long as the server keeps
        /// sending them.
        ///
        /// Unlike Write/Verify, Dynamic has no percent-complete/done signal in
        /// commands.rs: with <c>d.WaitForBeam == true</c> the server keeps
        /// testing while the beam stays asserted and only returns to the idle
        /// loop once it drops; with <c>d.WaitForBeam == false</c> the server's
        /// loop is effectively unbounded. This call will therefore run until
        /// <paramref name="ct"/> is cancelled or the connection drops — it does
        /// not return a final value on its own.
        /// </summary>
        public static async Task RunDynamicAsync(
            DynamicCmd d,
            IProgress<DynamicRsp> progress,
            CancellationToken ct = default)
        {
            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteFrameAsync(CMD_DYNAMIC, EncodeDynamic(d), ct).ConfigureAwait(false);

                while (true)
                {
                    var (cmd, payload) = await ReadFrameAsync(ct).ConfigureAwait(false);
                    if (cmd != CMD_DYNAMIC)
                        throw new InvalidDataException(
                            $"unexpected response 0x{cmd:X2} during Dynamic");

                    progress.Report(DecodeDynamicRsp(payload));
                }
            }
            finally { sendLock.Release(); }
        }

        /// <summary>
        /// Send a Reset command to pulse the FPGA and/or DDR4 controller reset
        /// lines (see gpio.rs / commands.rs::reset_command), and wait for the
        /// server's <see cref="ResetRsp"/> ACK.
        ///
        /// Each requested line is pulsed low/high/low with 250 ms holds on the
        /// server side, so this can take up to ~1.5 s to return if both
        /// <see cref="ResetCmd.FpgaReset"/> and <see cref="ResetCmd.ControllerReset"/>
        /// are set.
        ///
        /// NOTE: gpio.rs's EMIO map is now five read-only channels with no
        /// FPGA-reset/controller-reset output lines. Until reset_command() is
        /// confirmed to drive reset through some other mechanism, calling this
        /// may no longer do anything on the server side even though it still
        /// returns a ResetRsp.
        /// </summary>
        public static async Task<ResetRsp> SendResetAsync(ResetCmd r, CancellationToken ct = default)
        {
            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteFrameAsync(CMD_RESET, EncodeReset(r), ct).ConfigureAwait(false);

                var (cmd, payload) = await ReadFrameAsync(ct).ConfigureAwait(false);
                if (cmd != CMD_RESET)
                    throw new InvalidDataException(
                        $"expected Reset response (0x07), got 0x{cmd:X2}");

                return DecodeResetRsp(payload);
            }
            finally { sendLock.Release(); }
        }

        /// <summary>
        /// Send an Info command and return the server's current signal-status
        /// snapshot — one bool per gpio.rs EMIO channel (beam, calibration,
        /// UI clock, PL clock, FPGA-loaded).
        /// </summary>
        public static async Task<InfoRsp> SendInfoAsync(CancellationToken ct = default)
        {
            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteFrameAsync(CMD_INFO, Array.Empty<byte>(), ct).ConfigureAwait(false);

                var (cmd, payload) = await ReadFrameAsync(ct).ConfigureAwait(false);
                if (cmd != CMD_INFO)
                    throw new InvalidDataException(
                        $"expected Info response (0x06), got 0x{cmd:X2}");

                return DecodeInfoRsp(payload);
            }
            finally { sendLock.Release(); }
        }

        // ============================== Framing ==============================

        private static async Task WriteFrameAsync(byte cmd, byte[] payload,
                                                  CancellationToken ct)
        {
            if (stream is null)
                throw new InvalidOperationException("not connected");
            if (payload.Length > ushort.MaxValue)
                throw new ArgumentException(
                    $"payload too large: {payload.Length} bytes (max {ushort.MaxValue})");

            // SYNC(4) + CMD(1) + LEN(2) + payload + TERM(4)
            var packet = new byte[11 + payload.Length];
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0, 4), SYNC_MARKER);
            packet[4] = cmd;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(5, 2),
                                                  (ushort)payload.Length);
            Buffer.BlockCopy(payload, 0, packet, 7, payload.Length);
            BinaryPrimitives.WriteUInt32BigEndian(
                packet.AsSpan(7 + payload.Length, 4), TERM_MARKER);

            await stream.WriteAsync(packet, ct).ConfigureAwait(false);
        }

        private static async Task<(byte cmd, byte[] payload)> ReadFrameAsync(
            CancellationToken ct)
        {
            if (stream is null)
                throw new InvalidOperationException("not connected");

            // SYNC + CMD + LEN
            var head = new byte[7];
            await ReadExactAsync(head, ct).ConfigureAwait(false);

            uint sync = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0, 4));
            if (sync != SYNC_MARKER)
                throw new InvalidDataException(
                    $"bad SYNC 0x{sync:X8}, expected 0x{SYNC_MARKER:X8}");

            byte cmd = head[4];
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(5, 2));

            var payload = length == 0 ? Array.Empty<byte>() : new byte[length];
            if (length > 0)
                await ReadExactAsync(payload, ct).ConfigureAwait(false);

            var tail = new byte[4];
            await ReadExactAsync(tail, ct).ConfigureAwait(false);
            uint term = BinaryPrimitives.ReadUInt32BigEndian(tail);
            if (term != TERM_MARKER)
                throw new InvalidDataException(
                    $"bad TERM 0x{term:X8}, expected 0x{TERM_MARKER:X8}");

            return (cmd, payload);
        }

        private static async Task ReadExactAsync(byte[] buf, CancellationToken ct)
        {
            int total = 0;
            while (total < buf.Length)
            {
                int n = await stream!.ReadAsync(
                    buf.AsMemory(total, buf.Length - total), ct).ConfigureAwait(false);
                if (n == 0)
                    throw new EndOfStreamException("connection closed mid-frame");
                total += n;
            }
        }

        // ============================== Encoders ==============================

        private static byte[] EncodeConfig(ConfigCmd c)
        {
            var b = new byte[15];
            b[0] = c.ChipIndex;
            b[1] = c.BusBytesPerChip;
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(2, 4), c.BusSizeInBytes);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(6, 4), c.ChipSizeBytes);
            b[10] = Convert.ToByte(c.EnableChipSelect);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(11, 4), c.AddressMultiplier);
            return b;
        }

        private static byte[] EncodeWrite(WriteCmd c)
        {
            var b = new byte[9];
            b[0] = c.Pattern;
            BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(1, 8), c.Seed);
            return b;
        }

        private static byte[] EncodeVerify(VerifyCmd c)
        {
            var b = new byte[9];
            b[0] = c.Pattern;
            BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(1, 8), c.Seed);
            return b;
        }

        private static byte[] EncodeDump(DumpCmd c)
        {
            var b = new byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(0, 4), c.OffsetStart);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4, 4), c.NumPages);
            b[8] = Convert.ToByte(c.ComparisonMode);
            return b;
        }

        private static byte[] EncodeDynamic(DynamicCmd c)
        {
            var b = new byte[18];
            b[0] = c.Pattern;
            BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(1, 8), c.Seed);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(9, 4), c.SampleSizeInBytes);
            b[13] = Convert.ToByte(c.WaitForBeam);
            BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(14, 4), c.TriggerThreshold);
            return b;
        }

        private static byte[] EncodeReset(ResetCmd c)
        {
            return new byte[] { Convert.ToByte(c.FpgaReset), Convert.ToByte(c.ControllerReset) };
        }

        // ============================== Decoders ==============================

        private static WriteRsp DecodeWriteRsp(byte[] p)
        {
            if (p.Length < 12)
                throw new InvalidDataException(
                    $"short WriteRsp: {p.Length} bytes (need 12)");
            return new WriteRsp
            {
                BytesWritten = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(0, 4)),
                TimeSpentMs = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(4, 4)),
                PercentComplete = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(8, 4)),
            };
        }

        private static VerifyRsp DecodeVerifyRsp(byte[] p)
        {
            if (p.Length < 28)
                throw new InvalidDataException(
                    $"short VerifyRsp: {p.Length} bytes (need 28)");
            return new VerifyRsp
            {
                BytesVerified = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(0, 4)),
                TimeSpentMs = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(4, 4)),
                PercentComplete = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(8, 4)),
                NumErrors = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(12, 8)),
                NumCorrect = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(20, 8)),
            };
        }

        private static DumpPage DecodeDumpPage(byte[] p)
        {
            if (p.Length < 16)
                throw new InvalidDataException(
                    $"short DumpRsp: {p.Length} bytes (need >= 16)");
            var data = new byte[p.Length - 16];
            Buffer.BlockCopy(p, 16, data, 0, data.Length);
            return new DumpPage
            {
                TimeSpentMs = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(0, 4)),
                NumErrors = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(4, 8)),
                Address = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(12, 4)),
                Data = data,
            };
        }

        private static DynamicRsp DecodeDynamicRsp(byte[] p)
        {
            if (p.Length < 43)
                throw new InvalidDataException(
                    $"short DynamicRsp: {p.Length} bytes (need 43)");
            return new DynamicRsp
            {
                ExposureTimeMs = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(0, 4)),
                TotalTimeMs = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(4, 4)),
                TimeToSefi = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(8, 4)),
                TotalBytes = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(12, 8)),
                TotalErrors = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(20, 8)),
                ErrorRate = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(28, 4)),
                ErrorPercent = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(32, 4)),
                ExposureStarted = p[36] != 0,
                SefiDetected = p[37] != 0,
                BeamSignal = p[38] != 0,
                CalibrationSignal = p[39] != 0,
                UiClockSignal = p[40] != 0,
                PlClockSignal = p[41] != 0,
                FpgaLoadedStatus = p[42] != 0,
            };
        }

        private static ResetRsp DecodeResetRsp(byte[] p)
        {
            if (p.Length < 1)
                throw new InvalidDataException(
                    $"short ResetRsp: {p.Length} bytes (need 1)");
            return new ResetRsp { Success = p[0] != 0 };
        }

        private static InfoRsp DecodeInfoRsp(byte[] p)
        {
            if (p.Length < 5)
                throw new InvalidDataException(
                    $"short InfoRsp: {p.Length} bytes (need 5)");
            return new InfoRsp
            {
                BeamSignal = p[0] != 0,
                CalibrationSignal = p[1] != 0,
                UiClockSignal = p[2] != 0,
                PlClockSignal = p[3] != 0,
                FpgaLoadedStatus = p[4] != 0,
            };
        }
    }
}
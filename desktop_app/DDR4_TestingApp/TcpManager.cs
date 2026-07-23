// TcpManager.cs — C# client for the Rust ZCU104 DDR4-tester TCP server.
//
// Rewritten to match the current server (types.rs / commands.rs / server.rs /
// config.rs). Changes vs. the previous client:
//   * ConfigCmd is now { bool enable_logging, u32 num_blocks, u32 block_size,
//     u32 block_factor } = 13 bytes (was 20). The chip-geometry fields
//     (chip_index, bus_bytes_per_chip, bus_size_in_bytes, enable_chip_select)
//     are GONE from the command — config_command only writes enable_logging +
//     the three sampling fields into CONFIG. The server still keeps
//     chip_size_bytes / address_multiplier / etc. in its CONFIG, but this client
//     can no longer set them.
//   * DumpCmd is now { u32 block_offset, u32 num_blocks, bool comparison_mode }
//     = 9 bytes. dump_command loops (block_offset..block_offset + num_blocks)
//     stepped by CONFIG.block_factor — so num_blocks is a COUNT of blocks from
//     block_offset (NOT an end index); block_size also comes from CONFIG. It sends
//     NO end-of-dump sentinel, so the client derives the frame count from the
//     command's block count + the applied block_size/block_factor (see
//     ExpectedDumpPages / SendDumpAsync).
//   * DynamicRsp.pass_counter and current/start/end address ARE now populated by
//     the server (start=0, end=num_blocks*block_size, current tracks progress,
//     pass_counter counts completed full sweeps). The old "always 0" note is gone.
//
// Response structs (WriteRsp/VerifyRsp/DumpRsp/DynamicRsp/InfoRsp/UUIDRsp) are
// unchanged on the wire, so their decoders are identical to the previous client.
//
// Protocol framing (all framing integers big-endian / network byte order):
//
//     [SYNC u32 BE = 0xDEADBEEF]
//     [CMD  u8 ]
//     [LEN  u16 BE]
//     [PAYLOAD ... LEN bytes]
//     [TERM u32 BE = 0xCAFEBABE]
//
// Payloads are bincode with big-endian fixed-width integers (fixint), so every
// multi-byte field is plain big-endian with no length prefixes or padding.
// bool -> 1 byte (0/1); u16 -> 2 bytes; [u64;8] -> 64 bytes. The server uses
// reject_trailing_bytes(), so payload length must match the struct EXACTLY.
//
// Commands (values from config.rs — assumed unchanged; config.rs not provided):
//   0x01 Write   -> WriteCmd   { u8 pattern, u64 seed }                        9 bytes
//   0x02 Verify  -> VerifyCmd  { u16 uuid, u8 pattern, u64 seed }             11 bytes
//   0x03 Dump    -> DumpCmd    { u32 block_offset, u32 num_blocks,
//                                bool comparison_mode }                        9 bytes
//   0x04 Config  -> ConfigCmd  { bool enable_logging, u32 num_blocks,
//                                u32 block_size, u32 block_factor }           13 bytes
//   0x05 Dynamic -> DynamicCmd { u16 uuid, u8 pattern, u64 seed,
//                                u32 sample_size_in_bytes, bool wait_for_beam,
//                                f32 trigger_threshold }                      20 bytes
//   0x06 Info    -> (empty payload) — server replies with InfoRsp
//   0x07 UUID    -> UUIDCmd    { u16 uuid }                                    2 bytes
//                   <- UUIDRsp { bool success }                               1 byte
//
// Sampling geometry (drives Write/Verify/Dynamic/Dump on the server):
//     for blk in (0..NumBlocks).step_by(BlockFactor):
//         for addr in blk*BlockSize .. (blk+1)*BlockSize: <test byte>
//   so BlockFactor is a STRIDE over blocks (coverage ~= 1/BlockFactor) and the
//   logical byte extent is NumBlocks*BlockSize. BlockFactor MUST be >= 1 — the
//   server calls step_by(BlockFactor) and step_by(0) panics.
//
// Response streaming (from commands.rs):
//   - Config : one CMD_CONFIG frame, 1-byte ACK payload (contents ignored).
//   - Write  : periodic CMD_WRITE progress frames; a final frame is sent with
//              percent_complete == 100. Caller waits for percent >= 100.
//   - Verify : periodic CMD_VERIFY progress frames; a final frame is sent when
//              the sweep finishes (percent >= 100). Caller waits for percent >= 100.
//   - Dump   : one CMD_DUMP frame per flushed page — a 16-byte DumpRsp header
//              (time_spent_ms, num_errors, address) followed by up to PAGE_SIZE
//              (1024) raw bytes. The server loops
//              (BlockOffset..BlockOffset + NumBlocks) FROM THE COMMAND — NumBlocks
//              is a COUNT of blocks from BlockOffset — stepped by the CONFIG
//              block_factor, and sends NO terminator, so the client expects exactly
//              ExpectedDumpPages(NumBlocks, BlockSize, BlockFactor) frames. Pages
//              are NOT address-contiguous when block_factor > 1; each page carries
//              its own start address.
//   - Dynamic: periodic CMD_DYNAMIC frames. Termination: with wait_for_beam == true
//              the server completes when the beam drops; with wait_for_beam == false
//              it completes when a SEFI is detected (error_rate_percent > threshold).
//              If neither occurs it streams forever — cancel to stop.
//   - UUID   : one CMD_UUID frame, UUIDRsp { success } (success == uuid is AVAILABLE).
//   - Info   : one CMD_INFO frame, InfoRsp { five EMIO signal bits }.
//
// UUID: the uuid is a plain u16 label that names the CSV/summary log files (see
// recorder.rs). Call SendUuidAsync first to check availability — Success == true
// means the id is free (no {uuid}.csv exists yet). No character/format constraints.

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
        // Server-side logging on/off (recorder.rs writes {uuid}.csv + summary).
        public bool EnableLogging;

        // Sparse-sampling geometry (see header). BlockFactor MUST be >= 1.
        public uint NumBlocks;
        public uint BlockSize;
        public uint BlockFactor;
    }

    public struct WriteCmd
    {
        public byte Pattern;   // 0 = zeros, 1 = ones, 2 = pseudorandom
        public ulong Seed;
    }

    public struct VerifyCmd
    {
        public ushort Uuid;    // u16 log-file id (see recorder.rs)
        public byte Pattern;   // 0 = zeros, 1 = ones (0xFF), 2 = pseudorandom
        public ulong Seed;
    }

    public struct DumpCmd
    {
        // First block index to dump (inclusive). Byte address of block b is
        // b * BlockSize (BlockSize from the applied Config).
        public uint BlockOffset;

        // COUNT of blocks to dump, starting at BlockOffset: the server loops
        // (BlockOffset..BlockOffset + NumBlocks).step_by(block_factor). So the raw
        // block span visited is [BlockOffset, BlockOffset + NumBlocks) and the number
        // of blocks actually read back is ceil(NumBlocks / block_factor). To dump
        // exactly one block at BlockOffset, set NumBlocks = 1.
        public uint NumBlocks;

        // true  => each page holds expected^actual (XOR against the last Verify's
        //          pattern/seed — run a Verify first so the server has them).
        // false => each page holds the raw bytes read back.
        public bool ComparisonMode;
    }

    public struct DynamicCmd
    {
        public ushort Uuid;    // u16 log-file id (see recorder.rs)

        // Pattern generation
        public byte Pattern;   // 0 = zeros, 1 = ones, 2 = pseudorandom
        public ulong Seed;

        // Test configuration
        public uint SampleSizeInBytes; // rate window: error stats recompute every this-many bytes
        public bool WaitForBeam;       // block the exposure until BeamSignal reads high

        // SEFI threshold — compared against DynamicRsp.ErrorRatePercent, which is a
        // FRACTION (bits_errored / bits_sampled, range 0..1), NOT a 0..100 percentage.
        // e.g. 0.05f trips a SEFI at a 5% bit-error rate over the window.
        public float TriggerThreshold;
    }

    public struct UUIDCmd
    {
        public ushort Uuid;    // u16 log-file id to check for availability
    }

    public struct WriteRsp
    {
        public uint BytesWritten;
        public float TimeSpentMs;
        public float PercentComplete;
    }

    public struct VerifyRsp
    {
        public float TimeSpentMs;
        public float PercentComplete;

        // Address range of the sweep
        public uint CurrentAddress;
        public uint StartAddress;
        public uint EndAddress;

        // Per-bit tallies over the sweep; NumCorrect + NumIncorrect == 8 * bytes read
        // (server does num_correct += 8 - diff_bits; num_incorrect += diff_bits per byte).
        public ulong NumCorrect;
        public ulong NumIncorrect;

        // Histograms over bytes that differed:
        //   ErrBins[k]    = number of mismatched bytes with exactly k flipped bits
        //                   (length 9, indices 0..8; index 8 = fully-flipped byte)
        //   AdjErrBins[k] = number of runs of k adjacent flipped bits
        //                   (length 8, indices 0..7; index 0 unused)
        public ulong[] AdjErrBins; // 8 entries
        public ulong[] ErrBins;    // 9 entries
    }

    public struct DumpPage
    {
        public float TimeSpentMs;
        public ulong NumErrors;   // count of chip READ failures on this page (not bit diffs)
        public uint Address;
        public byte[] Data;       // up to PAGE_SIZE bytes (1024)
    }

    public struct DynamicRsp
    {
        // Time statistics
        public float ExposureTimeMs;
        public float TotalTimeMs;
        public float TimeToSefi;

        // Error statistics
        // NOTE: TotalBytes is a byte count (commands.rs does total_bytes += 1 per
        // byte tested). ErrorRate is the errored-bit count over the last window;
        // ErrorRatePercent is a fraction (errored/sampled bits, 0..1). See the bug
        // report re: ErrorRatePerSecond — its server-side formula looks incorrect.
        public ulong TotalBytes;
        public float ErrorRate;
        public float ErrorRatePerSecond;
        public float ErrorRatePercent;

        // Bit-error histograms over the sample window (same layout as VerifyRsp).
        // The server RESETS these to zero at the end of each sample window, so they
        // reflect the most recent window, not the whole run.
        public ulong[] AdjErrBins; // 8 entries
        public ulong[] ErrBins;    // 9 entries

        // Progress / addressing (now populated by dynamic_command):
        //   PassCounter    = completed full sweeps of the sampled range
        //   StartAddress   = 0
        //   EndAddress     = NumBlocks * BlockSize (logical extent; ignores stride)
        //   CurrentAddress = address of the byte most recently tested
        public uint PassCounter;
        public uint CurrentAddress;
        public uint StartAddress;
        public uint EndAddress;

        // Capture status
        public bool ExposureStarted;
        public bool SefiDetected;
        public bool TestCompleted;

        // Signal status — one bool per gpio.rs EMIO channel (all five read-only
        // inputs: beam, calibration/controller-calibrated, UI clock, PL clock,
        // FPGA-loaded).
        public bool BeamSignal;
        public bool ControllerCalibrated;
        public bool UiClock;
        public bool PlClock;
        public bool FpgaLoaded;
    }

    public struct InfoRsp
    {
        public bool BeamSignal;
        public bool ControllerCalibrated;
        public bool UiClock;
        public bool PlClock;
        public bool FpgaLoaded;
    }

    public struct UUIDRsp
    {
        // From uuid_command -> recorder::check_uuid: true means the uuid is AVAILABLE
        // (no {uuid}.csv exists yet), false means it's already taken.
        public bool Success;
    }

    // ============================== TcpManager ==============================

    internal static class TcpManager
    {
        // ---- Protocol constants (must match config.rs) ------------------------
        private const uint SYNC_MARKER = 0xDEAD_BEEF;
        private const uint TERM_MARKER = 0xCAFE_BABE;

        private const byte CMD_WRITE = 0x01;
        private const byte CMD_VERIFY = 0x02;
        private const byte CMD_DUMP = 0x03;
        private const byte CMD_CONFIG = 0x04;
        private const byte CMD_DYNAMIC = 0x05;
        private const byte CMD_INFO = 0x06;
        private const byte CMD_UUID = 0x07;

        // Must match config.rs PAGE_SIZE. Load-bearing: the Dump frame count is
        // derived from it (ceil(BlockSize / PAGE_SIZE) pages per sampled block).
        public const int PAGE_SIZE = 1024;

        // ---- Connection state --------------------------------------------------
        private static TcpClient? client;
        private static NetworkStream? stream;

        // Last sampling geometry applied via SendConfigAsync. The server derives the
        // number of Dump frames from its CONFIG (num_blocks / block_size /
        // block_factor) and sends NO end-of-dump sentinel, so the client must know
        // the same geometry to bound its read loop. Reset on disconnect.
        private static (uint NumBlocks, uint BlockSize, uint BlockFactor)? lastGeometry;

        /// <summary>
        /// The sampling geometry (NumBlocks / BlockSize / BlockFactor) actually sent to
        /// the server by the last successful <see cref="SendConfigAsync"/>, or null if no
        /// Config has been applied since connecting (reset on disconnect). This is the ONLY
        /// geometry the server will honor for a Dump: the live <c>Config.sys</c> can be
        /// edited in the UI without re-applying, so any caller that must stay within the
        /// block range the server actually has — e.g. the live block viewer — has to bound
        /// itself against THIS, not against <c>Config.sys</c>.
        /// </summary>
        public static (uint NumBlocks, uint BlockSize, uint BlockFactor)? CommittedGeometry => lastGeometry;

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

        /// <summary>Connect to the Rust ZCU server. Replaces any existing connection.</summary>
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
            Info._infoFetchInProgress = false;
            StatusChanged?.Invoke(ConnectionStatus.Connected);
        }

        public static void Disconnect()
        {
            bool was = Status == ConnectionStatus.Connected;
            try { stream?.Close(); } catch { /* ignore */ }
            try { client?.Close(); } catch { /* ignore */ }
            stream = null;
            client = null;

            // The server keeps its CONFIG across reconnects, but this client can't
            // know it without re-sending Config, so drop the cached geometry.
            lastGeometry = null;

            if (was) StatusChanged?.Invoke(ConnectionStatus.Disconnected);
        }

        // ============================== High-level commands ==============================

        /// <summary>
        /// Send a Config command. Returns once the server has applied the new
        /// geometry and ACKed (single 1-byte payload). The applied sampling
        /// geometry is cached so <see cref="SendDumpAsync(DumpCmd, IProgress{DumpPage}?, CancellationToken)"/>
        /// can bound its read loop.
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
                        $"expected Config ACK (0x{CMD_CONFIG:X2}), got 0x{cmd:X2}");

                // Remember the geometry the server is now running with.
                lastGeometry = (cfg.NumBlocks, cfg.BlockSize, cfg.BlockFactor);
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
        /// Send a Verify command. Streams progress and returns the final response
        /// (address range, bit counts, and the two error histograms) when complete.
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
        /// Number of CMD_DUMP frames the server will send for a dump of
        /// <paramref name="numBlocks"/> blocks (starting at the command's BlockOffset)
        /// at the given Config geometry. NumBlocks is a COUNT — the span length from
        /// BlockOffset — matching dump_command's loop
        ///   (block_offset .. block_offset + num_blocks).step_by(block_factor)
        /// The server has no end-of-dump marker, so this is how the client knows when
        /// a dump is finished:
        ///   sampledBlocks = ceil(numBlocks / blockFactor)    (the step_by stride)
        ///   pagesPerBlock = ceil(blockSize / PAGE_SIZE)       (page flush + trailing partial)
        ///   total         = sampledBlocks * pagesPerBlock
        /// BlockOffset does not affect the count — it only shifts where the span starts.
        /// </summary>
        public static long ExpectedDumpPages(uint numBlocks, uint blockSize, uint blockFactor)
        {
            if (blockFactor == 0)
                throw new ArgumentException(
                    "BlockFactor must be >= 1 — the server does step_by(BlockFactor) and step_by(0) panics.",
                    nameof(blockFactor));
            if (numBlocks == 0 || blockSize == 0)
                return 0;

            long sampledBlocks = ((long)numBlocks + blockFactor - 1) / blockFactor;
            long pagesPerBlock = ((long)blockSize + PAGE_SIZE - 1) / PAGE_SIZE;
            return sampledBlocks * pagesPerBlock;
        }

        /// <summary>
        /// Send a Dump command using the block_size / block_factor from the most
        /// recent <see cref="SendConfigAsync"/> on this connection. The starting block
        /// and block COUNT come from <paramref name="d"/> (BlockOffset, NumBlocks).
        /// Throws if no Config has been applied — use the explicit-geometry overload
        /// then.
        /// </summary>
        public static Task<List<DumpPage>> SendDumpAsync(
            DumpCmd d,
            IProgress<DumpPage>? onPage = null,
            CancellationToken ct = default)
        {
            if (lastGeometry is null)
                throw new InvalidOperationException(
                    "Dump needs block_size/block_factor to know how many frames to expect, " +
                    "and no Config has been applied on this connection. Call SendConfigAsync " +
                    "first, or use the SendDumpAsync overload that takes explicit geometry.");

            var g = lastGeometry.Value;
            return SendDumpAsync(d, g.BlockSize, g.BlockFactor, onPage, ct);
        }

        /// <summary>
        /// Send a Dump command with explicitly supplied block_size / block_factor
        /// (the starting block and block COUNT come from <paramref name="d"/>). One
        /// response frame is received per flushed page; each is reported via
        /// <paramref name="onPage"/> as it arrives and also collected into the
        /// returned list. The frame count is
        /// <see cref="ExpectedDumpPages"/>(d.NumBlocks, <paramref name="blockSize"/>,
        /// <paramref name="blockFactor"/>) — blockSize and blockFactor MUST match the
        /// server's current CONFIG or the read loop will desync.
        /// </summary>
        public static async Task<List<DumpPage>> SendDumpAsync(
            DumpCmd d,
            uint blockSize, uint blockFactor,
            IProgress<DumpPage>? onPage = null,
            CancellationToken ct = default)
        {
            long expected = ExpectedDumpPages(d.NumBlocks, blockSize, blockFactor);

            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteFrameAsync(CMD_DUMP, EncodeDump(d), ct).ConfigureAwait(false);

                var pages = new List<DumpPage>((int)Math.Min(expected, 4096));
                for (long i = 0; i < expected; i++)
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
        /// frames to <paramref name="progress"/>, returning the final frame.
        ///
        /// TERMINATION (per commands.rs): with <c>WaitForBeam == true</c> the server
        /// completes (<c>test_completed</c>) when the beam signal drops low. With
        /// <c>WaitForBeam == false</c> it completes when a SEFI is detected
        /// (<c>error_rate_percent &gt; TriggerThreshold</c>). If neither happens the
        /// server streams frames indefinitely — cancel <paramref name="ct"/> to stop
        /// (which surfaces as OperationCanceledException).
        /// </summary>
        public static async Task<DynamicRsp> RunDynamicAsync(
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

                    var rsp = DecodeDynamicRsp(payload);
                    progress.Report(rsp);
                    if (rsp.TestCompleted) return rsp;
                }
            }
            finally { sendLock.Release(); }
        }

        /// <summary>
        /// Send a UUID availability check. Returns the server's UUIDRsp;
        /// <see cref="UUIDRsp.Success"/> == true means the uuid is AVAILABLE
        /// (no {uuid}.csv exists yet), false means it's already taken.
        /// </summary>
        public static async Task<UUIDRsp> SendUuidAsync(UUIDCmd u, CancellationToken ct = default)
        {
            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteFrameAsync(CMD_UUID, EncodeUuid(u), ct).ConfigureAwait(false);

                var (cmd, payload) = await ReadFrameAsync(ct).ConfigureAwait(false);
                if (cmd != CMD_UUID)
                    throw new InvalidDataException(
                        $"expected UUID response (0x{CMD_UUID:X2}), got 0x{cmd:X2}");

                return DecodeUuidRsp(payload);
            }
            finally { sendLock.Release(); }
        }

        /// <summary>
        /// Send an Info command and return the server's current signal-status
        /// snapshot — one bool per gpio.rs EMIO channel (beam, controller-
        /// calibrated, UI clock, PL clock, FPGA-loaded).
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
                        $"expected Info response (0x{CMD_INFO:X2}), got 0x{cmd:X2}");

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
            // bool enable_logging  @ 0   (1)
            // u32  num_blocks      @ 1   (4)
            // u32  block_size      @ 5   (4)
            // u32  block_factor    @ 9   (4)
            //                      = 13 bytes
            var b = new byte[13];
            b[0] = Convert.ToByte(c.EnableLogging);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(1, 4), c.NumBlocks);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(5, 4), c.BlockSize);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(9, 4), c.BlockFactor);
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
            // uuid(u16, 2) + pattern(1) + seed(8) = 11
            var b = new byte[11];
            BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0, 2), c.Uuid);
            b[2] = c.Pattern;
            BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(3, 8), c.Seed);
            return b;
        }

        private static byte[] EncodeDump(DumpCmd c)
        {
            // u32  block_offset     @ 0  (4)
            // u32  num_blocks       @ 4  (4)   (COUNT of blocks from block_offset — see DumpCmd)
            // bool comparison_mode  @ 8  (1)
            //                       = 9 bytes
            var b = new byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(0, 4), c.BlockOffset);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4, 4), c.NumBlocks);
            b[8] = Convert.ToByte(c.ComparisonMode);
            return b;
        }

        private static byte[] EncodeDynamic(DynamicCmd c)
        {
            // uuid(u16, 2) + pattern(1) + seed(8) + sample(4) + wait(1) + threshold(4) = 20
            var b = new byte[20];
            BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0, 2), c.Uuid);
            b[2] = c.Pattern;
            BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(3, 8), c.Seed);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(11, 4), c.SampleSizeInBytes);
            b[15] = Convert.ToByte(c.WaitForBeam);
            BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(16, 4), c.TriggerThreshold);
            return b;
        }

        private static byte[] EncodeUuid(UUIDCmd c)
        {
            // uuid(u16, 2) = 2
            var b = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0, 2), c.Uuid);
            return b;
        }

        // ============================== Decoders ==============================

        private static WriteRsp DecodeWriteRsp(byte[] p)
        {
            const int need = 12;
            if (p.Length < need)
                throw new InvalidDataException($"short WriteRsp: {p.Length} bytes (need {need})");
            return new WriteRsp
            {
                BytesWritten = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(0, 4)),
                TimeSpentMs = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(4, 4)),
                PercentComplete = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(8, 4)),
            };
        }

        private static VerifyRsp DecodeVerifyRsp(byte[] p)
        {
            // f32 f32 u32 u32 u32 u64 u64 [u64;8] [u64;9]
            //  4 + 4 + 4 + 4 + 4 + 8 + 8 + 64 + 72 = 172
            const int need = 172;
            if (p.Length < need)
                throw new InvalidDataException($"short VerifyRsp: {p.Length} bytes (need {need})");

            var rsp = new VerifyRsp
            {
                TimeSpentMs = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(0, 4)),
                PercentComplete = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(4, 4)),
                CurrentAddress = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(8, 4)),
                StartAddress = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(12, 4)),
                EndAddress = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(16, 4)),
                NumCorrect = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(20, 8)),
                NumIncorrect = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(28, 8)),
                AdjErrBins = new ulong[8],
                ErrBins = new ulong[9],
            };

            int off = 36;
            for (int k = 0; k < 8; k++, off += 8)
                rsp.AdjErrBins[k] = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(off, 8));
            for (int k = 0; k < 9; k++, off += 8)
                rsp.ErrBins[k] = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(off, 8));

            return rsp;
        }

        private static DumpPage DecodeDumpPage(byte[] p)
        {
            const int header = 16;
            if (p.Length < header)
                throw new InvalidDataException(
                    $"short DumpRsp: {p.Length} bytes (need >= {header})");
            var data = new byte[p.Length - header];
            Buffer.BlockCopy(p, header, data, 0, data.Length);
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
            // f32 f32 f32 u64 f32 f32 f32 [u64;8] [u64;9] u32 u32 u32 u32 + 8 bools
            //  4 + 4 + 4 + 8 + 4 + 4 + 4 +  64  +  72  + 4 + 4 + 4 + 4 + 8 = 192
            const int need = 192;
            if (p.Length < need)
                throw new InvalidDataException($"short DynamicRsp: {p.Length} bytes (need {need})");

            var rsp = new DynamicRsp
            {
                ExposureTimeMs = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(0, 4)),
                TotalTimeMs = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(4, 4)),
                TimeToSefi = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(8, 4)),
                TotalBytes = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(12, 8)),
                ErrorRate = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(20, 4)),
                ErrorRatePerSecond = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(24, 4)),
                ErrorRatePercent = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(28, 4)),
                AdjErrBins = new ulong[8],
                ErrBins = new ulong[9],
            };

            // Histograms: adj_err_bins[8] @ 32, err_bins[9] @ 96 (off ends at 168).
            int off = 32;
            for (int k = 0; k < 8; k++, off += 8)
                rsp.AdjErrBins[k] = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(off, 8));
            for (int k = 0; k < 9; k++, off += 8)
                rsp.ErrBins[k] = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(off, 8));

            rsp.PassCounter = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(off, 4)); off += 4;      // 168
            rsp.CurrentAddress = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(off, 4)); off += 4;    // 172
            rsp.StartAddress = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(off, 4)); off += 4;      // 176
            rsp.EndAddress = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(off, 4)); off += 4;        // 180
            rsp.ExposureStarted = p[off++] != 0;      // 184
            rsp.SefiDetected = p[off++] != 0;         // 185
            rsp.TestCompleted = p[off++] != 0;        // 186
            rsp.BeamSignal = p[off++] != 0;           // 187
            rsp.ControllerCalibrated = p[off++] != 0; // 188
            rsp.UiClock = p[off++] != 0;              // 189
            rsp.PlClock = p[off++] != 0;              // 190
            rsp.FpgaLoaded = p[off++] != 0;           // 191

            return rsp;
        }

        private static InfoRsp DecodeInfoRsp(byte[] p)
        {
            const int need = 5;
            if (p.Length < need)
                throw new InvalidDataException($"short InfoRsp: {p.Length} bytes (need {need})");
            return new InfoRsp
            {
                BeamSignal = p[0] != 0,
                ControllerCalibrated = p[1] != 0,
                UiClock = p[2] != 0,
                PlClock = p[3] != 0,
                FpgaLoaded = p[4] != 0,
            };
        }

        private static UUIDRsp DecodeUuidRsp(byte[] p)
        {
            if (p.Length < 1)
                throw new InvalidDataException($"short UUIDRsp: {p.Length} bytes (need 1)");
            return new UUIDRsp { Success = p[0] != 0 };
        }
    }
}
// TcpManager.cs — C# client for the Rust ZCU104 DDR4-tester TCP server.
//
// Rewritten to match the current server (types.rs / commands.rs / server.rs /
// config.rs). Notable changes vs. the previous client:
//   * VerifyCmd and DynamicCmd now carry a 3-byte `uuid` field FIRST.
//   * VerifyRsp is a completely different, larger struct (address range,
//     num_correct/num_incorrect, and two bit-error histograms — AdjErrBins
//     x8 and ErrBins x9).
//   * DynamicRsp gained pass_counter + current/start/end address (4 x u32), and
//     now ALSO carries the AdjErrBins x8 / ErrBins x9 histograms (inserted between
//     error_rate_percent and pass_counter), making its payload 192 bytes.
//   * ConfigCmd gained a trailing `enable_logging` bool — Config payload is 16 bytes.
//   * New UUID command (0x07) with UUIDRsp { success }.
//   * The Reset command (0x07 in the old build) has been REMOVED from the
//     server — server.rs has no CMD_RESET dispatch arm, so it is dropped here.
//     (ResetRsp still exists in types.rs but nothing produces it.)
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
// bool -> 1 byte (0/1); [u8;3] -> 3 bytes; [u64;8] -> 64 bytes. The server uses
// reject_trailing_bytes(), so payload length must match the struct EXACTLY.
//
// Commands (values from config.rs):
//   0x01 Write   -> WriteCmd   { u8 pattern, u64 seed }                       9 bytes
//   0x02 Verify  -> VerifyCmd  { [u8;3] uuid, u8 pattern, u64 seed }         12 bytes
//   0x03 Dump    -> DumpCmd    { u32 offset_start, u32 num_pages, bool cmp }  9 bytes
//   0x04 Config  -> ConfigCmd  { u8 chip_index, u8 bus_bytes_per_chip,
//                                u32 bus_size_in_bytes, u32 chip_size_bytes,
//                                bool enable_chip_select, u32 address_multiplier,
//                                bool enable_logging }                       16 bytes
//   0x05 Dynamic -> DynamicCmd { [u8;3] uuid, u8 pattern, u64 seed,
//                                u32 sample_size_in_bytes, bool wait_for_beam,
//                                f32 trigger_threshold }                     21 bytes
//   0x06 Info    -> (empty payload) — server replies with InfoRsp
//   0x07 UUID    -> UUIDCmd    { [u8;3] uuid }                                3 bytes
//                   <- UUIDRsp { bool success }                              1 byte
//
// Response streaming (from commands.rs):
//   - Config : one CMD_CONFIG frame, 1-byte ACK payload (contents ignored).
//   - Write  : periodic CMD_WRITE progress frames; a final frame is sent with
//              percent_complete == 100. Caller waits for percent >= 100.
//   - Verify : periodic CMD_VERIFY progress frames; a final frame is sent when
//              the sweep finishes (percent >= 100). Caller waits for percent >= 100.
//   - Dump   : exactly num_pages CMD_DUMP frames, each a 16-byte header plus
//              PAGE_SIZE (1024) raw bytes.
//   - Dynamic: periodic CMD_DYNAMIC frames. The server sets test_completed ONLY
//              when wait_for_beam == true and the beam drops. See RunDynamicAsync
//              for the wait_for_beam == false caveat (no server-side completion).
//   - UUID   : one CMD_UUID frame, UUIDRsp { success }.
//   - Info   : one CMD_INFO frame, InfoRsp { five EMIO signal bits }.
//
// !!! UUID CONSTRAINT !!!
// utils::get_uuid() on the server only accepts a uuid whose three bytes are all
// ASCII UPPERCASE letters (A–Z); verify_command and dynamic_command call
// get_uuid(uuid).unwrap(), which PANICS (killing the server) on anything else.
// SendVerifyAsync/RunDynamicAsync therefore validate the uuid client-side and
// throw ArgumentException before sending. SendUuidAsync (the availability check)
// does NOT require uppercase, matching the server's non-panicking check_uuid path.

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
        public bool EnableLogging;   // maps to Rust ConfigCmd.enable_logging (server-side logging on/off)
    }

    public struct WriteCmd
    {
        public byte Pattern;   // 0 = zeros, 1 = ones, 2 = pseudorandom
        public ulong Seed;
    }

    public struct VerifyCmd
    {
        public byte[] Uuid;    // exactly 3 bytes; must be ASCII uppercase A–Z (see header)
        public byte Pattern;   // 0 = zeros, 1 = ones, 2 = pseudorandom
        public ulong Seed;
    }

    public struct DumpCmd
    {
        public uint OffsetStart;   // server aligns this down to a PAGE_SIZE boundary
        public uint NumPages;
        public bool ComparisonMode; // true => pages contain expected^actual (XOR of the pattern)
    }

    public struct DynamicCmd
    {
        public byte[] Uuid;    // exactly 3 bytes; must be ASCII uppercase A–Z (see header)

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
        public byte[] Uuid;    // exactly 3 bytes
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

        // NOTE: NumCorrect counts correct *bits* (+8 per fully-clean byte).
        // NumIncorrect is currently never incremented server-side (always 0 as of
        // the present commands.rs) — use ErrBins for the real error distribution.
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
        public byte[] Data;       // PAGE_SIZE bytes (1024)
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

        // Bit-error histograms over the sample window (same layout as VerifyRsp):
        //   AdjErrBins[k] = runs of k adjacent flipped bits   (length 8; index 0 unused)
        //   ErrBins[k]    = bytes with exactly k flipped bits (length 9; indices 0..8)
        public ulong[] AdjErrBins; // 8 entries
        public ulong[] ErrBins;    // 9 entries

        // Progress / addressing
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
        // From uuid_command -> recorder::check_uuid: true means the uuid is ALREADY
        // present in the log directory (i.e. taken), false means unused/invalid.
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
            if (was) StatusChanged?.Invoke(ConnectionStatus.Disconnected);
        }

        // ============================== High-level commands ==============================

        /// <summary>
        /// Send a Config command. Returns once the server has applied the new
        /// geometry and ACKed (single 1-byte payload).
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
        /// The uuid must be exactly 3 ASCII uppercase letters (A–Z) or the server
        /// will panic — validated client-side here.
        /// </summary>
        public static async Task<VerifyRsp> SendVerifyAsync(
            VerifyCmd v,
            IProgress<VerifyRsp>? progress = null,
            CancellationToken ct = default)
        {
            ValidateTestUuid(v.Uuid, "Verify");

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
        /// Send a Dump command. One response frame is received per page; each is
        /// reported via <paramref name="onPage"/> as it arrives and also collected
        /// into the returned list.
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
        /// frames to <paramref name="progress"/>, returning the final frame.
        ///
        /// TERMINATION (per commands.rs): the server sets <c>test_completed</c>
        /// ONLY when <c>WaitForBeam == true</c> and the beam signal drops low.
        /// A detected SEFI does NOT end the run. With <c>WaitForBeam == false</c>
        /// the server never sets <c>test_completed</c> at all, so this method will
        /// stream frames indefinitely — the ONLY way to stop it in that mode is to
        /// cancel <paramref name="ct"/> (which surfaces as OperationCanceledException).
        /// The uuid must be exactly 3 ASCII uppercase letters (A–Z) or the server
        /// will panic — validated client-side here.
        /// </summary>
        public static async Task<DynamicRsp> RunDynamicAsync(
            DynamicCmd d,
            IProgress<DynamicRsp> progress,
            CancellationToken ct = default)
        {
            ValidateTestUuid(d.Uuid, "Dynamic");

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
        /// Send a UUID availability check. Returns the server's UUIDRsp; note that
        /// <see cref="UUIDRsp.Success"/> == true means the uuid is ALREADY taken
        /// (present in the log directory). This command does not require uppercase
        /// bytes (the server's check_uuid path does not panic on invalid input).
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

        // ============================== UUID helpers ==============================

        /// <summary>Copy a 3-byte uuid into <paramref name="dst"/> (structural check only).</summary>
        private static void WriteUuid(Span<byte> dst, byte[] uuid)
        {
            if (uuid is null || uuid.Length != 3)
                throw new ArgumentException(
                    $"UUID must be exactly 3 bytes (got {(uuid is null ? "null" : uuid.Length.ToString())})");
            dst[0] = uuid[0];
            dst[1] = uuid[1];
            dst[2] = uuid[2];
        }

        /// <summary>
        /// Validate a uuid destined for Verify/Dynamic. The server's utils::get_uuid()
        /// only accepts three ASCII uppercase letters and then unwrap()s the result,
        /// so anything else would crash the server. Fail fast client-side instead.
        /// </summary>
        private static void ValidateTestUuid(byte[] uuid, string cmdName)
        {
            if (uuid is null || uuid.Length != 3)
                throw new ArgumentException(
                    $"{cmdName}: UUID must be exactly 3 bytes (got {(uuid is null ? "null" : uuid.Length.ToString())})");
            foreach (var b in uuid)
                if (b < (byte)'A' || b > (byte)'Z')
                    throw new ArgumentException(
                        $"{cmdName}: UUID bytes must be ASCII uppercase letters A–Z " +
                        $"(got 0x{b:X2}); the server would otherwise panic in get_uuid().unwrap()");
        }

        // ============================== Encoders ==============================

        private static byte[] EncodeConfig(ConfigCmd c)
        {
            // u8 chip_index, u8 bus_bytes_per_chip, u32 bus_size_in_bytes,
            // u32 chip_size_bytes, bool enable_chip_select, u32 address_multiplier,
            // bool enable_logging = 16 bytes
            var b = new byte[16];
            b[0] = c.ChipIndex;
            b[1] = c.BusBytesPerChip;
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(2, 4), c.BusSizeInBytes);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(6, 4), c.ChipSizeBytes);
            b[10] = Convert.ToByte(c.EnableChipSelect);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(11, 4), c.AddressMultiplier);
            b[15] = Convert.ToByte(c.EnableLogging);
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
            // uuid[3] + pattern(1) + seed(8) = 12
            var b = new byte[12];
            WriteUuid(b.AsSpan(0, 3), c.Uuid);
            b[3] = c.Pattern;
            BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(4, 8), c.Seed);
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
            // uuid[3] + pattern(1) + seed(8) + sample(4) + wait(1) + threshold(4) = 21
            var b = new byte[21];
            WriteUuid(b.AsSpan(0, 3), c.Uuid);
            b[3] = c.Pattern;
            BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(4, 8), c.Seed);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12, 4), c.SampleSizeInBytes);
            b[16] = Convert.ToByte(c.WaitForBeam);
            BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(17, 4), c.TriggerThreshold);
            return b;
        }

        private static byte[] EncodeUuid(UUIDCmd c)
        {
            var b = new byte[3];
            WriteUuid(b.AsSpan(0, 3), c.Uuid);
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
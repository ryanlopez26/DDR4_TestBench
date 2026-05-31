// TcpManager.cs — C# client for the Rust VCU TCP server.
//
// Protocol (matches server.rs):
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
//   0x01 Write   -> WriteCmd  { u8 pattern, u64 seed, u32 delay, bool beam }  14 bytes
//   0x02 Verify  -> VerifyCmd { u8 pattern, u64 seed, u32 delay, bool beam }  14 bytes
//   0x03 Dump    -> DumpCmd   { u32 offset_start, u32 num_pages, bool cmp }     9 bytes
//   0x04 Config  -> ConfigCmd { u8 chip_index, u8 bus_bytes_per_chip,
//                               u32 bus_size_in_bytes, u32 chip_size_bytes,
//                               bool enable_chip_select }                      11 bytes
//   0x05 Info    -> (empty payload) — server replies with InfoRsp
//
// Strings in InfoRsp are bincode-encoded as [u64 BE length][UTF-8 bytes]
// (fixint encoding uses u64 for length prefixes rather than a varint).
//
// Responses re-use the same framing. Long-running commands (Write, Verify)
// emit periodic progress frames; the caller waits until percent_complete
// reaches 100. Dump emits one frame per page. Config is a single ACK.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
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
        public byte enableChipSelect;
    }

    public struct WriteCmd
    {
        public byte Pattern;   // 0 = zeros, 1 = ones, 2 = pseudorandom
        public ulong Seed;
        public uint Delay;     // per-byte delay in ms
        public bool BeamTriggered;
    }

    public struct VerifyCmd
    {
        public byte Pattern;
        public ulong Seed;
        public uint Delay;
        public bool BeamTriggered;
    }

    public struct DumpCmd
    {
        public uint OffsetStart;
        public uint NumPages;
        public bool ComparisonMode;
    }

    public struct WriteRsp
    {
        public uint BytesWritten;
        public float TimeSpentMs;
        public float PercentComplete;
        public bool BeamActive;
    }

    public struct VerifyRsp
    {
        public uint BytesVerified;
        public float TimeSpentMs;
        public float PercentComplete;
        public uint NumErrors;
        public uint NumCorrect;
        public bool BeamActive;
    }

    public struct DumpPage
    {
        public float TimeSpentMs;
        public uint NumErrors;
        public uint Address;
        public byte[] Data;     // PAGE_SIZE bytes (1024 by default)
    }

    public struct InfoRsp
    {
        public string Manufacturer;
        public string Model;
        public float Uptime;            // seconds since boot
        public float CpuUsage;          // 0..100
        public float RamUsage;          // megabytes used (PS-side Linux RAM)
        public float Uplink;            // Mbps
        public float Downlink;          // Mbps
        public byte SelectedChip;
        public bool SimEnabled;
        public bool BeamActive;

        // RAM topology
        public byte PlOrganization;     // chip bit-width (e.g. 16 = x16)
        public byte PlRow;
        public byte PlCol;
        public byte PlBank;
        public byte PlRanks;
        public byte PlStackHeight;
        public byte PlBg;
        public byte PlCas;
        public byte PlCapacity;
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
        private const byte CMD_INFO = 0x05;

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
        /// response (including error/correct counts) when complete.
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
        /// Send an Info command and return the server's snapshot of board state
        /// (manufacturer, model, uptime, CPU/RAM/network usage, chip geometry).
        /// Cheap on the wire; cost on the server is ~100 ms because it samples
        /// /proc twice to compute CPU% and network throughput.
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
                        $"expected Info response (0x05), got 0x{cmd:X2}");

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
            var b = new byte[11];
            b[0] = c.ChipIndex;
            b[1] = c.BusBytesPerChip;
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(2, 4), c.BusSizeInBytes);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(6, 4), c.ChipSizeBytes);
            b[10] = Convert.ToByte(c.enableChipSelect);
            return b;
        }

        private static byte[] EncodeWrite(WriteCmd c)
        {
            var b = new byte[14];
            b[0] = c.Pattern;
            BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(1, 8), c.Seed);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(9, 4), c.Delay);
            b[13] = Convert.ToByte(c.BeamTriggered);
            return b;
        }

        private static byte[] EncodeVerify(VerifyCmd c)
        {
            var b = new byte[14];
            b[0] = c.Pattern;
            BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(1, 8), c.Seed);
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(9, 4), c.Delay);
            b[13] = Convert.ToByte(c.BeamTriggered);
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

        // ============================== Decoders ==============================

        private static WriteRsp DecodeWriteRsp(byte[] p)
        {
            if (p.Length < 13)
                throw new InvalidDataException(
                    $"short WriteRsp: {p.Length} bytes (need 13)");
            return new WriteRsp
            {
                BytesWritten = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(0, 4)),
                TimeSpentMs = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(4, 4)),
                PercentComplete = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(8, 4)),
                BeamActive = p[12] != 0,
            };
        }

        private static VerifyRsp DecodeVerifyRsp(byte[] p)
        {
            if (p.Length < 21)
                throw new InvalidDataException(
                    $"short VerifyRsp: {p.Length} bytes (need 21)");
            return new VerifyRsp
            {
                BytesVerified = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(0, 4)),
                TimeSpentMs = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(4, 4)),
                PercentComplete = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(8, 4)),
                NumErrors = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(12, 4)),
                NumCorrect = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(16, 4)),
                BeamActive = p[20] != 0,
            };
        }

        private static DumpPage DecodeDumpPage(byte[] p)
        {
            if (p.Length < 12)
                throw new InvalidDataException(
                    $"short DumpRsp: {p.Length} bytes (need >= 12)");
            var data = new byte[p.Length - 12];
            Buffer.BlockCopy(p, 12, data, 0, data.Length);
            return new DumpPage
            {
                TimeSpentMs = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(0, 4)),
                NumErrors = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(4, 4)),
                Address = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(8, 4)),
                Data = data,
            };
        }

        private static InfoRsp DecodeInfoRsp(byte[] p)
        {
            // InfoRsp has variable-width string fields, so walk a cursor through
            // the payload rather than using fixed offsets like the other decoders.
            int pos = 0;
            string manufacturer = ReadBincodeString(p, ref pos);
            string model = ReadBincodeString(p, ref pos);

            float uptime = ReadF32BE(p, ref pos);
            float cpu = ReadF32BE(p, ref pos);
            float ram = ReadF32BE(p, ref pos);
            float uplink = ReadF32BE(p, ref pos);
            float downlink = ReadF32BE(p, ref pos);

            byte chip = ReadU8(p, ref pos);
            bool sim = ReadU8(p, ref pos) != 0;
            bool beam = ReadU8(p, ref pos) != 0;

            // RAM topology block
            byte plOrg = ReadU8(p, ref pos);
            byte plRow = ReadU8(p, ref pos);
            byte plCol = ReadU8(p, ref pos);
            byte plBank = ReadU8(p, ref pos);
            byte plRanks = ReadU8(p, ref pos);
            byte plStackHeight = ReadU8(p, ref pos);
            byte plBg = ReadU8(p, ref pos);
            byte plCas = ReadU8(p, ref pos);
            byte plCapacity = ReadU8(p, ref pos);

            return new InfoRsp
            {
                Manufacturer = manufacturer,
                Model = model,
                Uptime = uptime,
                CpuUsage = cpu,
                RamUsage = ram,
                Uplink = uplink,
                Downlink = downlink,
                SelectedChip = chip,
                SimEnabled = sim,
                BeamActive = beam,
                PlOrganization = plOrg,
                PlRow = plRow,
                PlCol = plCol,
                PlBank = plBank,
                PlRanks = plRanks,
                PlStackHeight = plStackHeight,
                PlBg = plBg,
                PlCas = plCas,
                PlCapacity = plCapacity,
            };
        }

        // bincode w/ fixint + big-endian: strings are [u64 BE length][UTF-8 bytes].
        private static string ReadBincodeString(byte[] p, ref int pos)
        {
            if (p.Length - pos < 8)
                throw new InvalidDataException("truncated string length prefix");
            ulong len = BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(pos, 8));
            pos += 8;
            if (len > (ulong)(p.Length - pos))
                throw new InvalidDataException(
                    $"string length {len} exceeds remaining payload ({p.Length - pos} bytes)");
            int n = (int)len;
            string s = Encoding.UTF8.GetString(p, pos, n);
            pos += n;
            return s;
        }

        private static float ReadF32BE(byte[] p, ref int pos)
        {
            if (p.Length - pos < 4) throw new InvalidDataException("truncated f32");
            float v = BinaryPrimitives.ReadSingleBigEndian(p.AsSpan(pos, 4));
            pos += 4;
            return v;
        }

        private static byte ReadU8(byte[] p, ref int pos)
        {
            if (p.Length - pos < 1) throw new InvalidDataException("truncated u8");
            return p[pos++];
        }
    }
}
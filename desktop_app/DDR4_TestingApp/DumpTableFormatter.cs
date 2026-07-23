using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DDR4_TestingApp
{
    public static class DumpTableFormatter
    {
        /// <summary>
        /// Dumps <paramref name="numBlocks"/> blocks starting at
        /// <paramref name="blockOffset"/> via TcpManager's dump command and renders
        /// the returned bytes as a labeled hex table, e.g. (unitSize=1, rowSize=16):
        ///
        ///     ---- 0x0 0x1 0x2 0x3 0x4 0x5 0x6 0x7 0x8 0x9 0xA 0xB 0xC 0xD 0xE 0xF
        ///     0x00 0x4A 0x00 0x00 0x00 0xFF 0xFF 0xFF 0xFF 0x12 0x34 0x56 0x78 0x00 0x00 0x00 0x00
        ///     0x10 ...
        ///
        /// <paramref name="unitSize"/> is how many bytes make up one displayed
        /// value (1 = a byte like 0x00, 2 = a 16-bit word like 0x0000, 4 = a
        /// 32-bit word, ...). <paramref name="rowSize"/> is how many of those
        /// units appear per row - i.e. it's the column count directly, not a
        /// byte count. Column headers show the byte offset *within the row*
        /// (unpadded hex), not a running column index, so header + row address
        /// tells you the exact byte address of that cell. Multi-byte units are
        /// shown as their bytes concatenated in the order they appear in memory
        /// (first byte read = most-significant digits shown) - not
        /// reinterpreted as a little-endian machine word.
        ///
        /// The dump is returned WHOLE (no trimming): you get every byte of the
        /// sampled blocks. Row labels are the true byte addresses the server sent
        /// (block b starts at byte b*BlockSize). When the configured block_factor
        /// is &gt; 1 the sampled blocks aren't address-contiguous, so the table
        /// restarts its header at each address gap. NumBlocks is a COUNT of blocks
        /// to dump from blockOffset, not an end index (see TcpManager.DumpCmd).
        ///
        /// See <see cref="WriteDumpHexTableAsync"/> for a version that renders
        /// straight into a RichTextBox with the row/column labels bolded,
        /// instead of returning plain text.
        /// </summary>
        public static async Task<string> GenerateDumpHexTableAsync(
            uint blockOffset,
            uint numBlocks,
            uint unitSize,
            uint rowSize,
            bool comparisonMode = false,
            CancellationToken ct = default)
        {
            ValidateBlockRange(blockOffset, numBlocks, unitSize, rowSize);

            List<DumpPage> pages = await FetchPagesAsync(blockOffset, numBlocks, comparisonMode, ct)
                .ConfigureAwait(false);
            List<HexTableSegment> segments = BuildHexTableSegmentsForPages(pages, unitSize, rowSize);

            var sb = new StringBuilder();
            foreach (HexTableSegment seg in segments)
                sb.Append(seg.Text);
            return sb.ToString();
        }

        /// <summary>
        /// Renders bytes you already have in hand - e.g. a page handed to a live
        /// dump's IProgress&lt;DumpPage&gt; callback - as the same labeled hex table
        /// as <see cref="WriteDumpHexTableAsync"/>, but WITHOUT issuing another dump.
        /// Use this while streaming so you don't fire a second (serialized) dump at
        /// the server just to redraw a page you were already given.
        ///
        /// Same threading contract as WriteDumpHexTableAsync (safe from any thread;
        /// marshaled onto target's UI thread if needed), and target's Font must be
        /// monospaced for the columns to line up.
        /// </summary>
        public static void RenderBytesHexTable(
            RichTextBox target,
            uint startAddress,
            byte[] data,
            uint unitSize,
            uint rowSize)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (unitSize == 0) throw new ArgumentOutOfRangeException(nameof(unitSize), "must be > 0");
            if (rowSize == 0) throw new ArgumentOutOfRangeException(nameof(rowSize), "must be > 0");

            List<HexTableSegment> segments = BuildHexTableSegments(startAddress, data, unitSize, rowSize);

            if (target.InvokeRequired)
                target.Invoke(new Action(() => RenderSegments(target, segments)));
            else
                RenderSegments(target, segments);
        }

        /// <summary>
        /// Same as <see cref="GenerateDumpHexTableAsync"/>, but renders
        /// directly into <paramref name="target"/> with the header row and
        /// every row's address label shown in bold, and data cells in the
        /// control's regular weight.
        ///
        /// Safe to call from any thread: if <paramref name="target"/> wasn't
        /// created on the calling thread, the actual control update is
        /// marshaled onto its owning UI thread via Invoke. This matters here
        /// specifically because everything TcpManager awaits internally uses
        /// ConfigureAwait(false), so by the time the network fetch above
        /// completes, execution has very likely resumed on a thread-pool
        /// thread rather than the UI thread - touching a WinForms control at
        /// that point without marshaling back would throw a cross-thread
        /// operation exception.
        ///
        /// For the padding to actually line up as a grid, <paramref
        /// name="target"/>'s Font needs to be a monospaced font (e.g.
        /// Consolas, Courier New) - a proportional font like the WinForms
        /// default will render every cell a different width regardless of
        /// how carefully the text is space-padded.
        ///
        /// <paramref name="maxRenderBytes"/> caps how many bytes are actually
        /// RENDERED (0 = unlimited). This is a render-side limit only: the dump
        /// still comes over the wire whole (block dumps have a one-block
        /// minimum). It exists so a caller that redraws on a timer - e.g. the
        /// live viewer at 10 Hz - can request a whole 64 KB block and only pay
        /// to draw a small window of it, instead of building tens of thousands
        /// of RichTextBox segments on the UI thread every tick and freezing it.
        /// </summary>
        public static async Task WriteDumpHexTableAsync(
            RichTextBox target,
            uint blockOffset,
            uint numBlocks,
            uint unitSize,
            uint rowSize,
            bool comparisonMode = false,
            uint maxRenderBytes = 0,
            CancellationToken ct = default)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            ValidateBlockRange(blockOffset, numBlocks, unitSize, rowSize);

            List<DumpPage> pages = await FetchPagesAsync(blockOffset, numBlocks, comparisonMode, ct)
                .ConfigureAwait(false);
            List<HexTableSegment> segments = BuildHexTableSegmentsForPages(pages, unitSize, rowSize, maxRenderBytes);

            if (target.InvokeRequired)
                target.Invoke(new Action(() => RenderSegments(target, segments)));
            else
                RenderSegments(target, segments);
        }

        private static void ValidateBlockRange(
            uint blockOffset, uint numBlocks, uint unitSize, uint rowSize)
        {
            if (numBlocks == 0)
                throw new ArgumentOutOfRangeException(nameof(numBlocks),
                    "empty block range: numBlocks must be >= 1. numBlocks is now a COUNT of " +
                    "blocks to dump starting at blockOffset (not an end index) — the server " +
                    "loops (block_offset..block_offset + num_blocks).");
            if (unitSize == 0)
                throw new ArgumentOutOfRangeException(nameof(unitSize), "must be > 0");
            if (rowSize == 0)
                throw new ArgumentOutOfRangeException(nameof(rowSize), "must be > 0");
        }

        /// <summary>
        /// Issues the dump for <paramref name="numBlocks"/> blocks starting at
        /// <paramref name="blockOffset"/> (the server loops
        /// block_offset..block_offset + num_blocks, stepped by block_factor) and
        /// returns the pages exactly as the server sent them (each carrying its own
        /// start address). No trimming or page-alignment — a block dump comes back
        /// whole. Uses the block_size / block_factor cached by the last
        /// TcpManager.SendConfigAsync, so Config must have been applied on this
        /// connection first (otherwise SendDumpAsync throws).
        /// </summary>
        private static Task<List<DumpPage>> FetchPagesAsync(
            uint blockOffset, uint numBlocks, bool comparisonMode, CancellationToken ct)
        {
            var dumpCmd = new DumpCmd
            {
                BlockOffset = blockOffset,
                NumBlocks = numBlocks,
                ComparisonMode = comparisonMode,
            };

            return TcpManager.SendDumpAsync(dumpCmd, ct: ct);
        }

        /// <summary>
        /// Renders a set of dump pages using the same per-cell/per-row formatting as
        /// <see cref="BuildHexTableSegments"/>. Pages are ordered by address and
        /// address-contiguous pages are merged into one run; each run is emitted as
        /// its own labeled table (header + address-labeled rows). This keeps the row
        /// address labels correct across the gaps that appear when block_factor &gt; 1
        /// (non-contiguous sampled blocks) — for a contiguous dump it's a single
        /// table, identical to the old address-range renderer.
        /// </summary>
        private static List<HexTableSegment> BuildHexTableSegmentsForPages(
            List<DumpPage> pages, uint unitSize, uint rowSize, uint maxRenderBytes = 0)
        {
            var segments = new List<HexTableSegment>();
            if (pages is null || pages.Count == 0)
                return segments;

            // Defensive: the server emits pages in address order, but don't rely on it.
            var ordered = new List<DumpPage>(pages);
            ordered.Sort((a, b) => a.Address.CompareTo(b.Address));

            // Render budget: 0 == unlimited. Otherwise stop after this many bytes
            // have been laid out, trimming the run that crosses the limit. Keeps a
            // 10 Hz redraw of a whole block from building ~8000 segments per tick.
            long remaining = maxRenderBytes == 0 ? long.MaxValue : maxRenderBytes;

            bool firstRun = true;
            int i = 0;
            while (i < ordered.Count && remaining > 0)
            {
                uint runBase = ordered[i].Address;
                var runData = new List<byte>(ordered[i].Data);
                uint runEnd = runBase + (uint)ordered[i].Data.Length;
                i++;

                // Absorb following pages that begin exactly where this run ends.
                while (i < ordered.Count && ordered[i].Address == runEnd)
                {
                    runData.AddRange(ordered[i].Data);
                    runEnd += (uint)ordered[i].Data.Length;
                    i++;
                }

                // Trim this run to whatever render budget is left.
                byte[] runArr = runData.ToArray();
                if (runArr.Length > remaining)
                    Array.Resize(ref runArr, (int)remaining);
                remaining -= runArr.Length;

                // Blank line between non-contiguous runs so the tables don't abut.
                if (!firstRun)
                    segments.Add(new HexTableSegment("\n", isLabel: false));
                firstRun = false;

                segments.AddRange(
                    BuildHexTableSegments(runBase, runArr, unitSize, rowSize));
            }

            return segments;
        }

        /// <summary>One chunk of the rendered table, tagged with whether it's a label (bold) or data (regular).</summary>
        private readonly struct HexTableSegment
        {
            public HexTableSegment(string text, bool isLabel)
            {
                Text = text;
                IsLabel = isLabel;
            }

            public string Text { get; }
            public bool IsLabel { get; }
        }

        /// <summary>
        /// Builds the table as a sequence of labeled/unlabeled text chunks -
        /// the shared core both GenerateDumpHexTableAsync (which just
        /// concatenates the text) and WriteDumpHexTableAsync (which also uses
        /// the IsLabel flag to bold header/row-address text) are built on.
        /// </summary>
        private static List<HexTableSegment> BuildHexTableSegments(
            uint startAddress, byte[] data, uint unitSize, uint rowSize)
        {
            var segments = new List<HexTableSegment>();

            int unitsPerRow = (int)rowSize; // rowSize IS the column count - no division needed
            uint rowSizeInBytes = rowSize * unitSize;

            // Row labels are zero-padded to whatever even digit count covers
            // the highest address that will appear, so every row label lines
            // up (matches the "0x00, 0x10, ..." style rather than "0x0, 0x10, ...").
            uint lastAddress = startAddress + (uint)Math.Max(data.Length, 1) - 1;
            int rowLabelDigits = Math.Max(2, HexDigitCount(lastAddress));
            if (rowLabelDigits % 2 != 0)
                rowLabelDigits++;

            int dataCellDigits = (int)unitSize * 2;
            int cellWidth = 2 + dataCellDigits; // "0x" + digits; shared by header and data cells

            // ---- Header row (label) ----
            var header = new StringBuilder();
            header.Append('-', 2 + 8);
            for (int col = 0; col < unitsPerRow; col++)
            {
                uint offsetInRow = (uint)col * unitSize;
                string h = "0x" + offsetInRow.ToString("X");
                header.Append(' ').Append(h.PadLeft(cellWidth));
            }
            header.Append('\n');
            segments.Add(new HexTableSegment(header.ToString(), isLabel: true));

            // ---- Data rows ----
            for (int rowStart = 0; rowStart < data.Length; rowStart += (int)rowSizeInBytes)
            {
                uint rowAddress = startAddress + (uint)rowStart;
                string rowLabel = "0x" + rowAddress.ToString("X8");
                segments.Add(new HexTableSegment(rowLabel, isLabel: true));

                int bytesInThisRow = Math.Min((int)rowSizeInBytes, data.Length - rowStart);
                var rowData = new StringBuilder();

                // A trailing group shorter than unitSize (only possible on
                // the very last row, if numBytes isn't a multiple of
                // unitSize) is left off rather than padded with bytes that
                // were never actually read from the chip.
                for (int col = 0; col + (int)unitSize <= bytesInThisRow; col += (int)unitSize)
                {
                    // Built by concatenating each byte's own 2-digit hex
                    // representation rather than shifting bytes into a
                    // fixed-width integer accumulator - a ulong is only 8
                    // bytes wide, so for unitSize > 8 that approach would
                    // silently shift the earliest bytes out and lose them.
                    var cellHex = new StringBuilder(dataCellDigits);
                    for (int k = 0; k < unitSize; k++)
                        cellHex.Append(data[rowStart + col + k].ToString("X2"));

                    string cell = "0x" + cellHex;
                    rowData.Append(' ').Append(cell.PadLeft(cellWidth));
                }
                rowData.Append('\n');
                segments.Add(new HexTableSegment(rowData.ToString(), isLabel: false));
            }

            return segments;
        }

        /// <summary>
        /// Must run on target's own UI thread - see the threading note on
        /// WriteDumpHexTableAsync.
        /// </summary>
        private static void RenderSegments(RichTextBox target, List<HexTableSegment> segments)
        {
            Font regularFont = target.Font;
            using var boldFont = new Font(regularFont, regularFont.Style | FontStyle.Bold);

            target.Clear();
            foreach (HexTableSegment seg in segments)
            {
                target.SelectionStart = target.TextLength;
                target.SelectionLength = 0;
                target.SelectionFont = seg.IsLabel ? boldFont : regularFont;
                target.AppendText(seg.Text);
            }

            // Leave selection collapsed at the end in the regular font,
            // rather than wherever the last AppendText happened to leave it.
            target.SelectionStart = target.TextLength;
            target.SelectionLength = 0;
            target.SelectionFont = regularFont;
        }

        private static int HexDigitCount(uint value)
        {
            int digits = 1;
            while (value >= 16)
            {
                value >>= 4;
                digits++;
            }
            return digits;
        }
    }
}
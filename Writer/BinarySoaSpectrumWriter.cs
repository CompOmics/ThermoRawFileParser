using log4net;
using System;
using System.Buffers;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ThermoFisher.CommonCore.Data;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.Data.FilterEnums;
using ThermoFisher.CommonCore.Data.Interfaces;
using ThermoRawFileParser.Util;

namespace ThermoRawFileParser.Writer
{
    /// <summary>
    /// Streams every selected scan as a self-describing binary record laid out as
    /// Structure-of-Arrays: a 128-byte fixed scalar header, optional UTF-8 filter
    /// string, then packed <c>f64 mz[N]</c> + <c>f32 intensity[N]</c> arrays, and
    /// finally an optional verbatim trailer key/value dump. End-of-stream marker is
    /// a single <c>u32 = 0</c> word.
    ///
    /// Designed for high-throughput downstream consumers (Rust engines, GPU
    /// rescorers, columnar database loaders) that prefer zero-copy ingestion over
    /// portable XML.
    ///
    /// Two operating modes share a single byte format:
    /// <list type="bullet">
    /// <item><b>Streaming</b> (<c>--stdout</c>): records flow into a downstream
    /// process via a pipe. This is the primary use case; the writer wraps the
    /// pipe in a 1 MB <see cref="BufferedStream"/> to collapse small-write syscalls.</item>
    /// <item><b>File</b> (<c>--output</c>): the identical byte format is written to
    /// disk so the same downstream consumer can replay it later (sidecar caching).</item>
    /// </list>
    ///
    /// Format spec: <c>BINARY_SOA_FORMAT.md</c> at the repo root.
    /// </summary>
    public class BinarySoaSpectrumWriter : SpectrumWriter
    {
        private static readonly ILog Log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // ─── Format constants ────────────────────────────────────────────────

        /// <summary>Magic bytes at the start of every stream: ASCII "RCIASTR1".</summary>
        private static readonly byte[] FileMagic =
            { 0x52, 0x43, 0x49, 0x41, 0x53, 0x54, 0x52, 0x31 };

        /// <summary>Bumping this is a breaking change; readers must reject unknown versions.</summary>
        public const ushort FormatVersion = 1;

        /// <summary>File-level header is 32 bytes, written once at stream start.</summary>
        public const int FileHeaderSize = 32;

        /// <summary>Per-record fixed scalar header is 128 bytes (two cache lines).</summary>
        public const int RecordFixedHeaderSize = 128;

        /// <summary>Output buffer size for the BufferedStream wrapping BaseStream.</summary>
        private const int OutputBufferSize = 1 * 1024 * 1024;

        /// <summary>Caps for u16-sized variable sections; values longer are truncated with a warning.</summary>
        private const int MaxFilterStringLen = ushort.MaxValue;
        private const int MaxKeyOrValueLen   = ushort.MaxValue;

        /// <summary>Default extension used for file output mode.</summary>
        private const string OutputExtension = ".rcia.bin";

        private const uint PeakFlagSortedByMz = 0x1u;
        private const uint PeakFlagChargeArray = 0x2u;
        private const uint PeakFlagNoiseArrays = 0x4u;

        // ─── Reusable per-instance buffers (writer is single-threaded per file) ──

        /// <summary>128-byte scratch for assembling each record's fixed header.</summary>
        private readonly byte[] _hdrBuffer = new byte[RecordFixedHeaderSize];

        /// <summary>Scratch for building the optional metadata block.
        /// Reset between spectra; capacity grows monotonically as needed.</summary>
        private readonly MemoryStream _metaScratch = new MemoryStream(2048);
        private readonly BinaryWriter _metaWriter;

        /// <summary>Reusable zero-pad source; pad lengths are 0..7 so 8 bytes is always sufficient.</summary>
        private static readonly byte[] ZeroPad = new byte[8];

        // ─── Public ctor ─────────────────────────────────────────────────────

        public BinarySoaSpectrumWriter(ParseInput parseInput) : base(parseInput)
        {
            _metaWriter = new BinaryWriter(_metaScratch, Encoding.UTF8, leaveOpen: true);
        }

        // ─── Top-level write loop ────────────────────────────────────────────

        /// <inheritdoc/>
        public override void Write(IRawDataPlus rawFile, int firstScanNumber, int lastScanNumber)
        {
            if (!rawFile.HasMsData)
            {
                throw new RawFileParserException("No MS data in RAW file, no output will be produced");
            }

            ConfigureWriter(OutputExtension);

            // Bypass StreamWriter's text encoding entirely. Wrap BaseStream in a 1 MB
            // BufferedStream to coalesce many small writes into a few large pipe syscalls
            // — this dropped sys time ~22× on a 3.7 GB RAW benchmark.
            // leaveOpen:true keeps the BinaryWriter from closing BaseStream during dispose
            // chaining; the outer `using (Writer)` is responsible for the final close+flush.
            using (Writer)
            using (var buffered = new BufferedStream(Writer.BaseStream, OutputBufferSize))
            using (var bw = new BinaryWriter(buffered, Encoding.UTF8, leaveOpen: true))
            {
                WriteFileHeader(bw);

                int totalScans = lastScanNumber - firstScanNumber + 1;
                Log.Info("Processing " + totalScans + " scans");
                int lastScanProgress = 0;
                int written = 0;

                for (int scanNumber = firstScanNumber; scanNumber <= lastScanNumber; scanNumber++)
                {
                    ReportProgress(scanNumber, firstScanNumber, lastScanNumber, ref lastScanProgress);

                    try
                    {
                        // Apply MS-level filter (matches MzML/Parquet writer behavior).
                        int level = (int)rawFile.GetScanEventForScanNumber(scanNumber).MSOrder;
                        if (level > ParseInput.MaxLevel) continue;
                        if (!ParseInput.MsLevel.Contains(level)) continue;

                        WriteRecord(bw, rawFile, scanNumber, level);
                        written++;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Scan #{scanNumber} cannot be processed: {ex.Message}");
                        Log.Debug($"{ex.StackTrace}\n{ex.InnerException}");
                        ParseInput.NewError();
                    }
                }

                // End-of-stream marker (u32 record_size = 0)
                bw.Write((uint)0);
                bw.Flush();
                buffered.Flush();

                if (ParseInput.LogFormat == LogFormat.DEFAULT) Console.Error.WriteLine();
                Log.Info($"Wrote {written}/{totalScans} spectra to binary SoA stream");
            }
        }

        private void WriteFileHeader(BinaryWriter bw)
        {
            bw.Write(FileMagic);                  // 8 bytes
            bw.Write(FormatVersion);              // u16
            bw.Write((ushort)FileHeaderSize);     // u16
            bw.Write((uint)0);                    // flags = 0
            bw.Write(new byte[16]);               // reserved
        }

        private void ReportProgress(int scanNumber, int firstScanNumber, int lastScanNumber,
            ref int lastScanProgress)
        {
            if (ParseInput.LogFormat != LogFormat.DEFAULT) return;
            int scanProgress = (int)((double)scanNumber / (lastScanNumber - firstScanNumber + 1) * 100);
            if (scanProgress % ProgressPercentageStep == 0 && scanProgress != lastScanProgress)
            {
                Console.Error.Write("" + scanProgress + "% ");
                lastScanProgress = scanProgress;
            }
        }

        // ─── Per-spectrum record ─────────────────────────────────────────────

        /// <summary>
        /// Pulls all data for a single scan from RawFileReader and emits one binary record.
        /// The record layout:
        /// <code>
        /// [128-byte fixed header]
        /// [filter_string_len bytes UTF-8 filter string][pad-to-8]
        /// [f64 mz[N]][f32 intensity[N]][pad-to-8]
        /// [optional metadata block: u32 n_pairs, then (u16 klen, kbytes, u16 vlen, vbytes)*][pad-to-8]
        /// [final pad so record_size is a multiple of 8]
        /// </code>
        /// </summary>
        private void WriteRecord(BinaryWriter bw, IRawDataPlus rawFile, int scanNumber, int msLevel)
        {
            // 1. Pull per-scan data ──────────────────────────────────────────
            var scanFilter = rawFile.GetFilterForScanNumber(scanNumber);
            var scanEvent  = rawFile.GetScanEventForScanNumber(scanNumber);
            var scanStats  = rawFile.GetScanStatsForScanNumber(scanNumber);
            double retentionTimeMin = rawFile.RetentionTimeFromScanNumber(scanNumber);

            ScanTrailer trailer = LoadTrailer(rawFile, scanNumber);
            string filterString = scanEvent.ToString() ?? string.Empty;

            // Trailer-derived nullable scalars
            int? charge                   = trailer.AsPositiveInt("Charge State:");
            double? monoisotopicMz        = trailer.AsDouble("Monoisotopic M/Z:");
            double? ionInjectionTime      = trailer.AsDouble("Ion Injection Time (ms):");
            double? isolationWidthTrailer = trailer.AsDouble("MS" + msLevel + " Isolation Width:");
            int? masterScan               = trailer.AsPositiveInt("Master Scan Number:");
            double? faimsCv               = trailer.AsBool("FAIMS Voltage On:").GetValueOrDefault(false)
                                                ? trailer.AsDouble("FAIMS CV:") : null;
            double? elapsedScanTimeSec    = trailer.AsDouble("Elapsed Scan Time (sec):");

            // Reaction (only meaningful for MS2+)
            double precursorMz             = double.NaN;
            float  isolationWidth          = float.NaN;
            float  isolationLower          = float.NaN;
            float  isolationUpper          = float.NaN;
            float  collisionEnergy         = float.NaN;
            float  precursorIntensity      = 0f;
            byte   activationType          = 0;

            if (msLevel > 1)
            {
                masterScan = ResolveMasterScanNumber(filterString, scanNumber, masterScan);

                ResolveReactionData(rawFile, scanEvent, scanNumber, msLevel, monoisotopicMz,
                    isolationWidthTrailer, masterScan,
                    out precursorMz, out isolationWidth,
                    out isolationLower, out isolationUpper,
                    out collisionEnergy, out precursorIntensity, out activationType);
            }
            else if (msLevel == 1)
            {
                _precursorScanNumbers[""] = scanNumber;
            }

            // Peak data (centroid by default; respect --noPeakPicking selectively)
            bool requestCentroid = !ParseInput.NoPeakPicking.Contains(msLevel);
            MZData mzData = ReadPeakData(rawFile, scanEvent, scanNumber, requestCentroid);

            int nPeaks = mzData.masses?.Length ?? 0;
            double[] masses      = mzData.masses      ?? Array.Empty<double>();
            double[] intensities = mzData.intensities ?? Array.Empty<double>();
            byte scanDataType = (byte)(mzData.isCentroided ? 1 : 0);

            // 2. Encode filter string ────────────────────────────────────────
            byte[] filterBytes = Encoding.UTF8.GetBytes(filterString);
            int filterLen = filterBytes.Length;
            if (filterLen > MaxFilterStringLen)
            {
                Log.Warn($"Filter string for scan {scanNumber} truncated from {filterLen} to {MaxFilterStringLen} bytes");
                ParseInput.NewWarn();
                filterLen = MaxFilterStringLen;
            }
            int filterPadLen = ComputePadLen(RecordFixedHeaderSize + filterLen, 8);

            // 3. Compute peak section sizes ─────────────────────────────────
            bool hasChargeArray = nPeaks > 0 && mzData.charges != null && mzData.charges.Length == nPeaks;
            int noiseCount = GetNoiseArrayCount(mzData);
            bool hasNoiseArrays = noiseCount > 0;

            int requiredArrayLength = nPeaks * 8 + nPeaks * 4;
            int optionalArrayPadLen = hasChargeArray || hasNoiseArrays
                ? ComputePadLen(requiredArrayLength, 8)
                : 0;
            int optionalArrayLength =
                (hasChargeArray ? nPeaks * 8 : 0)
                + (hasNoiseArrays ? noiseCount * 8 * 3 : 0);
            int peakSection = requiredArrayLength + optionalArrayPadLen + optionalArrayLength;
            int peakPadLen = ComputePadLen(peakSection, 8);
            uint peakFlags = PeakFlagSortedByMz
                | (hasChargeArray ? PeakFlagChargeArray : 0u)
                | (hasNoiseArrays ? PeakFlagNoiseArrays : 0u);

            // 4. Build metadata block (full trailer dump) into _metaScratch ──
            int metadataLength = BuildMetadataBlock(trailer);
            int metadataPadLen = metadataLength > 0 ? ComputePadLen(metadataLength, 8) : 0;

            // 5. Compute offsets and total record size ───────────────────────
            uint arraysOffset = (uint)(RecordFixedHeaderSize + filterLen + filterPadLen);
            uint metadataOffset = metadataLength > 0
                ? (uint)(arraysOffset + peakSection + peakPadLen)
                : 0;

            int totalSize =
                RecordFixedHeaderSize + filterLen + filterPadLen
                + peakSection + peakPadLen
                + metadataLength + metadataPadLen;
            int finalPadLen = ComputePadLen(totalSize, 8);
            totalSize += finalPadLen;

            // 6. Assemble fixed header into _hdrBuffer ───────────────────────
            FillRecordHeader(_hdrBuffer,
                totalSize, scanNumber, msLevel, scanFilter.Polarity, scanDataType, activationType,
                nPeaks, retentionTimeMin * 60.0,
                precursorMz, monoisotopicMz ?? double.NaN, mzData.basePeakMass ?? double.NaN,
                isolationLower, isolationUpper, isolationWidth,
                precursorIntensity,
                (float)(mzData.basePeakIntensity ?? double.NaN),
                (float)scanStats.TIC,
                ToFloatOrNaN(ionInjectionTime), collisionEnergy,
                ToFloatOrNaN(faimsCv),
                elapsedScanTimeSec.HasValue ? (float)(elapsedScanTimeSec.Value * 1000.0) : float.NaN,
                (float)scanStats.LowMass, (float)scanStats.HighMass,
                charge ?? -1, masterScan ?? -1,
                peakFlags, noiseCount,
                filterLen, arraysOffset, metadataOffset, (uint)metadataLength);

            // 7. Emit ───────────────────────────────────────────────────────
            bw.Write(_hdrBuffer, 0, RecordFixedHeaderSize);

            if (filterLen > 0) bw.Write(filterBytes, 0, filterLen);
            WritePad(bw, filterPadLen);

            WritePeakArrays(bw, mzData, masses, intensities, nPeaks, hasChargeArray, hasNoiseArrays,
                optionalArrayPadLen);
            WritePad(bw, peakPadLen);

            if (metadataLength > 0)
            {
                bw.Write(_metaScratch.GetBuffer(), 0, metadataLength);
                WritePad(bw, metadataPadLen);
            }

            WritePad(bw, finalPadLen);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        /// <summary>Read trailer with graceful fallback if vendor metadata is unavailable.</summary>
        private ScanTrailer LoadTrailer(IRawDataPlus rawFile, int scanNumber)
        {
            try
            {
                return new ScanTrailer(rawFile.GetTrailerExtraInformation(scanNumber));
            }
            catch (Exception ex)
            {
                Log.WarnFormat("Cannot load trailer for scan {0}: {1}", scanNumber, ex.Message);
                ParseInput.NewWarn();
                return new ScanTrailer();
            }
        }

        /// <summary>Read mz/intensity arrays with graceful fallback to empty arrays on failure.</summary>
        private MZData ReadPeakData(IRawDataPlus rawFile, IScanEvent scanEvent, int scanNumber, bool centroid)
        {
            try
            {
                return ReadMZData(rawFile, scanEvent, scanNumber, centroid,
                    ParseInput.ChargeData, ParseInput.NoiseData);
            }
            catch (Exception ex)
            {
                Log.WarnFormat("Cannot read peaks for scan {0}: {1}", scanNumber, ex.Message);
                ParseInput.NewWarn();
                return new MZData
                {
                    masses = Array.Empty<double>(),
                    intensities = Array.Empty<double>(),
                    isCentroided = false,
                };
            }
        }

        /// <summary>
        /// Resolve precursor and reaction info for an MSn scan, accounting for EThcD
        /// and ETciD (ETD/ECD followed by HCD/CID supplemental activation). The primary reaction's
        /// activation type is encoded; the supplemental activation code (5) is set when the
        /// instrument's <c>SupplementalActivation</c> flag is on AND the sequential
        /// reaction pattern matches.
        /// </summary>
        private void ResolveReactionData(
            IRawDataPlus rawFile, IScanEvent scanEvent, int scanNumber, int msLevel,
            double? monoisotopicMz, double? isolationWidthTrailer, int? masterScan,
            out double precursorMz, out float isolationWidth,
            out float isolationLower, out float isolationUpper,
            out float collisionEnergy, out float precursorIntensity, out byte activationType)
        {
            precursorMz = double.NaN;
            isolationWidth = float.NaN;
            isolationLower = float.NaN;
            isolationUpper = float.NaN;
            collisionEnergy = float.NaN;
            precursorIntensity = 0f;
            activationType = 0;

            // Determine the primary reaction index. FindLastReaction (defined on the base class)
            // walks the reaction chain and accounts for supplemental activation; calling it
            // ensures EThcD/ETciD-style spectra report the ETD/ECD reaction as primary, not the
            // supplemental HCD/CID.
            int primaryReactionIndex;
            try
            {
                primaryReactionIndex = FindLastReaction(scanEvent, msLevel);
            }
            catch
            {
                // Fall back to the conventional last reaction
                IReaction fallback = GetReaction(scanEvent, scanNumber);
                if (fallback != null)
                {
                    SetReactionFields(rawFile, scanNumber, msLevel, fallback,
                        monoisotopicMz, isolationWidthTrailer, masterScan,
                        out precursorMz, out isolationWidth,
                        out isolationLower, out isolationUpper,
                        out collisionEnergy, out precursorIntensity);
                    activationType = EncodeActivationType(fallback.ActivationType);
                }
                return;
            }

            IReaction primaryReaction;
            try { primaryReaction = scanEvent.GetReaction(primaryReactionIndex); }
            catch
            {
                Log.Warn($"Cannot get primary reaction for scan {scanNumber}");
                return;
            }

            SetReactionFields(rawFile, scanNumber, msLevel, primaryReaction,
                monoisotopicMz, isolationWidthTrailer, masterScan,
                out precursorMz, out isolationWidth,
                out isolationLower, out isolationUpper,
                out collisionEnergy, out precursorIntensity);

            activationType = EncodeActivationType(primaryReaction.ActivationType);

            // EThcD/ETciD detection: supplemental activation flag is on AND primary reaction is
            // ETD/ECD AND a HCD/CID reaction follows it.
            if (scanEvent.SupplementalActivation == TriState.On
                && (primaryReaction.ActivationType == ActivationType.ElectronTransferDissociation
                 || primaryReaction.ActivationType == ActivationType.ElectronCaptureDissociation))
            {
                try
                {
                    var supplemental = scanEvent.GetReaction(primaryReactionIndex + 1);
                    if (supplemental.ActivationType == ActivationType.HigherEnergyCollisionalDissociation
                     || supplemental.ActivationType == ActivationType.CollisionInducedDissociation)
                    {
                        activationType = 5; // EThcD/ETciD
                    }
                }
                catch { /* no supplemental — keep primary encoding */ }
            }
        }

        private void SetReactionFields(
            IRawDataPlus rawFile, int scanNumber, int msLevel, IReaction reaction,
            double? monoisotopicMz, double? isolationWidthTrailer, int? masterScan,
            out double precursorMz, out float isolationWidth,
            out float isolationLower, out float isolationUpper,
            out float collisionEnergy, out float precursorIntensity)
        {
            precursorMz = CalculateSelectedIonMz(reaction, monoisotopicMz, isolationWidthTrailer);
            collisionEnergy = (float)reaction.CollisionEnergy;

            double? iw = isolationWidthTrailer ?? reaction.IsolationWidth;
            if (iw.HasValue && iw.Value >= 0)
            {
                double offset = iw.Value / 2.0 + reaction.IsolationWidthOffset;
                isolationWidth = (float)iw.Value;
                isolationLower = (float)(reaction.PrecursorMass - iw.Value + offset);
                isolationUpper = (float)(reaction.PrecursorMass + offset);
            }
            else
            {
                isolationWidth = float.NaN;
                isolationLower = float.NaN;
                isolationUpper = float.NaN;
            }

            precursorIntensity = 0f;
            if (masterScan.HasValue && masterScan.Value > 0 && precursorMz > 0)
            {
                try
                {
                    precursorIntensity = (float)CalculatePrecursorPeakIntensity(
                        rawFile, masterScan.Value, reaction.PrecursorMass, isolationWidthTrailer,
                        ParseInput.NoPeakPicking.Contains(msLevel - 1));
                }
                catch { /* graceful degradation: leave at 0 */ }
            }
        }

        private int? ResolveMasterScanNumber(string filterString, int scanNumber, int? trailerMasterScan)
        {
            TrackPrecursorFilter(filterString, scanNumber, out var precursorFilter);

            if (trailerMasterScan.HasValue)
            {
                return trailerMasterScan.Value;
            }

            int precursorScan = GetParentFromScanString(precursorFilter);
            if (precursorScan == -2)
            {
                Log.Warn($"Cannot find precursor scan for scan# {scanNumber}");
                ParseInput.NewWarn();
                return null;
            }

            return precursorScan > 0 ? (int?)precursorScan : null;
        }

        private void TrackPrecursorFilter(string filterString, int scanNumber, out string precursorFilter)
        {
            precursorFilter = "";

            var match = _filterStringIsolationMzPattern.Match(filterString);
            if (match == null || !match.Success)
            {
                return;
            }

            precursorFilter = match.Groups[1].Value;
            _precursorScanNumbers[precursorFilter] = scanNumber;
        }

        /// <summary>
        /// Bulk-emit the SoA peak arrays. The mz array is a zero-copy reinterpret of the
        /// existing <c>double[]</c>; the intensity array requires an f64→f32 narrowing pass
        /// over a pooled <c>float[]</c>. The narrowing loop is auto-vectorized by RyuJIT
        /// (AVX2/AVX-512/NEON).
        /// </summary>
        private static void WritePeakArrays(BinaryWriter bw, MZData mzData, double[] masses, double[] intensities,
            int nPeaks, bool hasChargeArray, bool hasNoiseArrays, int optionalArrayPadLen)
        {
            if (nPeaks > 0)
            {
                WriteDoubleArray(bw, masses, nPeaks);

                // intensity: f64 → f32 narrow into pooled buffer, emit as bytes
                float[] intBuf = ArrayPool<float>.Shared.Rent(nPeaks);
                try
                {
                    for (int i = 0; i < nPeaks; i++) intBuf[i] = (float)intensities[i];
                    ReadOnlySpan<byte> intSpan = MemoryMarshal.AsBytes(intBuf.AsSpan(0, nPeaks));
                    bw.Write(intSpan);
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(intBuf);
                }
            }

            WritePad(bw, optionalArrayPadLen);
            if (hasChargeArray) WriteDoubleArray(bw, mzData.charges, nPeaks);
            if (hasNoiseArrays)
            {
                int noiseCount = mzData.noiseData.Length;
                WriteDoubleArray(bw, mzData.massData, noiseCount);
                WriteDoubleArray(bw, mzData.noiseData, noiseCount);
                WriteDoubleArray(bw, mzData.baselineData, noiseCount);
            }
        }

        private static void WriteDoubleArray(BinaryWriter bw, double[] values, int count)
        {
            bw.Write(MemoryMarshal.AsBytes(values.AsSpan(0, count)));
        }

        private static int GetNoiseArrayCount(MZData mzData)
        {
            if (mzData.massData == null || mzData.noiseData == null || mzData.baselineData == null) return 0;
            int count = mzData.noiseData.Length;
            return mzData.massData.Length == count && mzData.baselineData.Length == count ? count : 0;
        }

        /// <summary>
        /// Build the optional metadata block into the reusable scratch buffer.
        /// Returns the number of valid bytes; callers should slice <c>_metaScratch.GetBuffer()</c>
        /// to that length.
        /// Format: <c>u32 n_pairs</c> followed by <c>(u16 klen, kbytes, u16 vlen, vbytes)</c> entries.
        /// </summary>
        private int BuildMetadataBlock(ScanTrailer trailer)
        {
            if (trailer.Length == 0) return 0;

            _metaScratch.SetLength(0);
            _metaScratch.Position = 0;
            _metaWriter.Write((uint)trailer.Length);
            var labels = trailer.Labels;
            var values = trailer.Values;
            bool truncated = false;
            for (int i = 0; i < labels.Length; i++)
            {
                truncated |= WriteLengthPrefixed(_metaWriter, labels[i] ?? "");
                truncated |= WriteLengthPrefixed(_metaWriter, values[i] ?? "");
            }
            if (truncated)
            {
                Log.Warn($"Trailer metadata field truncated to {MaxKeyOrValueLen} bytes");
                ParseInput.NewWarn();
            }
            _metaWriter.Flush();
            return (int)_metaScratch.Length;
        }

        private static bool WriteLengthPrefixed(BinaryWriter bw, string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            int len = Math.Min(bytes.Length, MaxKeyOrValueLen);
            bw.Write((ushort)len);
            if (len > 0) bw.Write(bytes, 0, len);
            return bytes.Length > MaxKeyOrValueLen;
        }

        /// <summary>
        /// Pack the 128-byte fixed scalar header into <paramref name="dest"/> at offset 0.
        /// All offsets are documented in BINARY_SOA_FORMAT.md and must stay in sync.
        /// </summary>
        private static void FillRecordHeader(byte[] dest,
            int recordSize, int scanId, int msLevel, PolarityType polarity,
            byte scanDataType, byte activationType, int nPeaks,
            double retentionTimeSeconds,
            double precursorMz, double precursorMzMonoisotopic, double basePeakMz,
            float isolationLower, float isolationUpper, float isolationWidth,
            float precursorIntensity, float basePeakIntensity, float totalIonCurrent,
            float ionInjectionTimeMs, float collisionEnergy, float faimsCv,
            float elapsedScanTimeMs, float lowMass, float highMass,
            int precursorCharge, int masterScanNumber,
            uint peakFlags, int auxiliaryArrayCount,
            int filterStringLen, uint arraysOffset,
            uint metadataOffset, uint metadataLength)
        {
            var span = dest.AsSpan(0, RecordFixedHeaderSize);

            // Block 1 — identity & shape (16 bytes)
            BinaryPrimitives_WriteU32(span, 0,  (uint)recordSize);
            BinaryPrimitives_WriteU32(span, 4,  (uint)scanId);
            span[8]  = unchecked((byte)(sbyte)msLevel);
            span[9]  = EncodePolarity(polarity);
            span[10] = scanDataType;
            span[11] = activationType;
            BinaryPrimitives_WriteU32(span, 12, (uint)nPeaks);

            // Block 2 — doubles (32 bytes)
            BinaryPrimitives_WriteF64(span, 16, retentionTimeSeconds);
            BinaryPrimitives_WriteF64(span, 24, precursorMz);
            BinaryPrimitives_WriteF64(span, 32, precursorMzMonoisotopic);
            BinaryPrimitives_WriteF64(span, 40, basePeakMz);

            // Block 3 — floats (48 bytes)
            BinaryPrimitives_WriteF32(span, 48, isolationLower);
            BinaryPrimitives_WriteF32(span, 52, isolationUpper);
            BinaryPrimitives_WriteF32(span, 56, isolationWidth);
            BinaryPrimitives_WriteF32(span, 60, precursorIntensity);
            BinaryPrimitives_WriteF32(span, 64, basePeakIntensity);
            BinaryPrimitives_WriteF32(span, 68, totalIonCurrent);
            BinaryPrimitives_WriteF32(span, 72, ionInjectionTimeMs);
            BinaryPrimitives_WriteF32(span, 76, collisionEnergy);
            BinaryPrimitives_WriteF32(span, 80, faimsCv);
            BinaryPrimitives_WriteF32(span, 84, elapsedScanTimeMs);
            BinaryPrimitives_WriteF32(span, 88, lowMass);
            BinaryPrimitives_WriteF32(span, 92, highMass);

            // Block 4 — ints & flags (16 bytes)
            BinaryPrimitives_WriteI32(span, 96,  precursorCharge);
            BinaryPrimitives_WriteI32(span, 100, masterScanNumber);
            BinaryPrimitives_WriteU32(span, 104, peakFlags);
            BinaryPrimitives_WriteU32(span, 108, (uint)auxiliaryArrayCount);

            // Block 5 — variable-section pointers (16 bytes)
            BinaryPrimitives_WriteU16(span, 112, (ushort)filterStringLen);
            BinaryPrimitives_WriteU16(span, 114, 0);     // reserved2
            BinaryPrimitives_WriteU32(span, 116, arraysOffset);
            BinaryPrimitives_WriteU32(span, 120, metadataOffset);
            BinaryPrimitives_WriteU32(span, 124, metadataLength);
        }

        // ─── Encoding helpers ────────────────────────────────────────────────

        /// <summary>Map RawFileReader's polarity enum to the on-wire byte encoding.</summary>
        public static byte EncodePolarity(PolarityType p) => p switch
        {
            PolarityType.Negative => 0,
            PolarityType.Positive => 1,
            _                     => 255,
        };

        /// <summary>
        /// Map a single reaction's <see cref="ActivationType"/> to the on-wire byte encoding.
        /// EThcD/ETciD (5) is set by the caller when the supplemental-activation pattern is detected;
        /// this helper returns the primary type only.
        /// </summary>
        public static byte EncodeActivationType(ActivationType t) => t switch
        {
            ActivationType.CollisionInducedDissociation        => 1,
            ActivationType.HigherEnergyCollisionalDissociation => 2,
            ActivationType.ElectronTransferDissociation        => 3,
            ActivationType.ElectronCaptureDissociation         => 4,
            // 5 reserved for caller-detected EThcD/ETciD
            ActivationType.UltraVioletPhotoDissociation        => 6,
            ActivationType.NegativeElectronTransferDissociation => 7,
            ActivationType.MultiPhotonDissociation             => 8,
            ActivationType.PQD                                 => 9,
            ActivationType.ProtonTransferReaction              => 10,
            ActivationType.NegativeProtonTransferReaction      => 11,
            _                                                  => 255,
        };

        // ─── Pad helpers ─────────────────────────────────────────────────────

        /// <summary>Number of zero pad bytes to align <paramref name="currentLen"/> to <paramref name="alignment"/>.</summary>
        public static int ComputePadLen(int currentLen, int alignment)
        {
            int remainder = currentLen % alignment;
            return remainder == 0 ? 0 : alignment - remainder;
        }

        private static void WritePad(BinaryWriter bw, int n)
        {
            if (n > 0) bw.Write(ZeroPad, 0, n);
        }

        private static float ToFloatOrNaN(double? v) => v.HasValue ? (float)v.Value : float.NaN;

        // ─── Inline little-endian writers ────────────────────────────────────
        // We avoid the BCL System.Buffers.Binary.BinaryPrimitives namespace import to keep
        // the writer's dependencies minimal and self-documenting. These match its semantics
        // exactly (little-endian, no allocation, JIT-inlinable).

        private static void BinaryPrimitives_WriteU16(Span<byte> dest, int off, ushort v)
        {
            dest[off]     = (byte)(v & 0xFF);
            dest[off + 1] = (byte)((v >> 8) & 0xFF);
        }
        private static void BinaryPrimitives_WriteU32(Span<byte> dest, int off, uint v)
        {
            dest[off]     = (byte)(v & 0xFF);
            dest[off + 1] = (byte)((v >> 8)  & 0xFF);
            dest[off + 2] = (byte)((v >> 16) & 0xFF);
            dest[off + 3] = (byte)((v >> 24) & 0xFF);
        }
        private static void BinaryPrimitives_WriteI32(Span<byte> dest, int off, int v)
            => BinaryPrimitives_WriteU32(dest, off, unchecked((uint)v));
        private static void BinaryPrimitives_WriteF32(Span<byte> dest, int off, float v)
        {
            uint bits = BitConverter.SingleToUInt32Bits(v);
            BinaryPrimitives_WriteU32(dest, off, bits);
        }
        private static void BinaryPrimitives_WriteF64(Span<byte> dest, int off, double v)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(v);
            dest[off]     = (byte)(bits & 0xFF);
            dest[off + 1] = (byte)((bits >> 8)  & 0xFF);
            dest[off + 2] = (byte)((bits >> 16) & 0xFF);
            dest[off + 3] = (byte)((bits >> 24) & 0xFF);
            dest[off + 4] = (byte)((bits >> 32) & 0xFF);
            dest[off + 5] = (byte)((bits >> 40) & 0xFF);
            dest[off + 6] = (byte)((bits >> 48) & 0xFF);
            dest[off + 7] = (byte)((bits >> 56) & 0xFF);
        }
    }
}

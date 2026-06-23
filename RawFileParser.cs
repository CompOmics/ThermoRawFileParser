using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using log4net;
using ThermoFisher.CommonCore.Data;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.Data.Interfaces;
using ThermoRawFileParser.Writer;
using ThermoRawFileParser.Util;

namespace ThermoRawFileParser
{
    public static class RawFileParser
    {
        private static readonly ILog Log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Process and extract the RAW file(s). 
        /// </summary>
        /// <param name="parseInput">the parse input object</param>
        public static void Parse(ParseInput parseInput)
        {
            // Input raw folder mode
            if (parseInput.RawDirectoryPath != null)
            {
                Log.Info("Started analyzing folder " + parseInput.RawDirectoryPath);

                var rawFilesPath = Directory
                    .EnumerateFiles(parseInput.RawDirectoryPath, "*", SearchOption.TopDirectoryOnly)
                    .Where(s => s.ToLower().EndsWith("raw")).ToArray();
                Log.Info(String.Format("The folder contains {0} RAW files", rawFilesPath.Length));

                if (rawFilesPath.Length == 0)
                {
                    Log.Debug("No raw files found in folder");
                    throw new RawFileParserException("No raw files found in folder!");
                }

                foreach (var filePath in rawFilesPath)
                {
                    parseInput.RawFilePath = filePath;
                    Log.Info("Started parsing " + parseInput.RawFilePath);
                    TryProcessFile(parseInput);
                }
            }
            // Input raw file mode
            else
            {
                Log.Info("Started parsing " + parseInput.RawFilePath);

                TryProcessFile(parseInput);
            }
        }

        /// <summary>
        /// Process and extract the given RAW file and catch IO exceptions.
        /// </summary>
        /// <param name="parseInput">the parse input object</param>
        private static void TryProcessFile(ParseInput parseInput)
        {
            try
            {
                ProcessFile(parseInput);
            }
            
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException)
                {
                    Log.Error(!ex.Message.IsNullOrEmpty()
                        ? ex.Message
                        : "Attempting to write to an unauthorized location.");
                    parseInput.NewError();
                }
                else if (ex is RawFileParserException)
                {
                    Log.Error(ex.Message);
                    parseInput.NewError();
                }
                else
                {
                    Log.Error("An unexpected error occurred (see below)");
                    Log.Error(ex.ToString());
                    parseInput.NewError();
                }
            }
        }

        /// <summary>
        /// Process and extract the given RAW file. 
        /// </summary>
        /// <param name="parseInput">the parse input object</param>
        private static void ProcessFile(ParseInput parseInput)
        {
            // Create the IRawDataPlus object for accessing the RAW file
            IRawDataPlus rawFile;

            //checking for symlinks
            var fileInfo = new FileInfo(parseInput.RawFilePath);

            if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) //detected path is a symlink
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var realPath = NativeMethods.GetFinalPathName(parseInput.RawFilePath);
                    Log.DebugFormat("Detected reparse point, real path: {0}", realPath);
                    parseInput.UpdateRealPath(realPath);
                }
                else //Mono should handle all non-windows platforms
                {
                    var realPath = Path.Combine(Path.GetDirectoryName(parseInput.RawFilePath), Mono.Unix.UnixPath.ReadLink(parseInput.RawFilePath));
                    Log.DebugFormat("Detected reparse point, real path: {0}", realPath);
                    parseInput.UpdateRealPath(realPath);
                }
            }
            
            using (rawFile = RawFileReaderFactory.ReadFile(parseInput.RawFilePath))
            {
                if (!rawFile.IsOpen)
                {
                    throw new RawFileParserException("Unable to access the RAW file using the native Thermo API");
                }

                // Check for any errors in the RAW file
                if (rawFile.IsError)
                {
                    throw new RawFileParserException($"Native Thermo API reported the following error - RAW file is likely corrupted\n{rawFile.FileError.ErrorMessage}");
                }

                // Check if the RAW file is being acquired
                if (rawFile.InAcquisition)
                {
                    throw new RawFileParserException("RAW file cannot be processed since it is still being acquired");
                }

                // Get the number of instruments (controllers) present in the RAW file and set the 
                // selected instrument to the MS instrument, first instance of it
                var firstScanNumber = -1;
                var lastScanNumber = -1;

                if (rawFile.GetInstrumentCountOfType(Device.MS) != 0)
                {
                    rawFile.SelectInstrument(Device.MS, 1);
                    rawFile.IncludeReferenceAndExceptionData = !parseInput.ExData;

                    // Get the first and last scan from the RAW file
                    firstScanNumber = rawFile.RunHeaderEx.FirstSpectrum;
                    lastScanNumber = rawFile.RunHeaderEx.LastSpectrum;

                    // Check for empty file
                    if (lastScanNumber < 1)
                    {
                        throw new RawFileParserException("Empty RAW file, no output will be produced");
                    }
                }

                if (parseInput.MetadataFormat != MetadataFormat.None)
                {
                    MetadataWriter metadataWriter = new MetadataWriter(parseInput);
                    metadataWriter.WriteMetadata(rawFile, firstScanNumber, lastScanNumber);
                }

                if (parseInput.OutputFormat != OutputFormat.None)
                {
                    SpectrumWriter spectrumWriter;
                    switch (parseInput.OutputFormat)
                    {
                        case OutputFormat.MGF:
                            spectrumWriter = new MgfSpectrumWriter(parseInput);
                            spectrumWriter.Write(rawFile, firstScanNumber, lastScanNumber);
                            break;
                        case OutputFormat.MzML:
                        case OutputFormat.IndexMzML:
                            spectrumWriter = new MzMlSpectrumWriter(parseInput);
                            spectrumWriter.Write(rawFile, firstScanNumber, lastScanNumber);
                            break;
                        case OutputFormat.Parquet:
                            spectrumWriter = new ParquetSpectrumWriter(parseInput);
                            spectrumWriter.Write(rawFile, firstScanNumber, lastScanNumber);
                            break;
                    }
                }

                Log.Info("Finished parsing " + parseInput.UserProvidedPath);
            }
        }
    }
}
using System;
using System.Reflection;
using System.IO;
using log4net;
using log4net.Core;
using Mono.Options;
using ThermoFisher.CommonCore.Data;
using System.Linq;
using ThermoRawFileParser.Query;
using ThermoRawFileParser.XIC;
using System.Globalization;
using System.Collections.Generic;
using System.Data;
using ThermoRawFileParser.Util;

[assembly: log4net.Config.XmlConfigurator()]

namespace ThermoRawFileParser
{
    public static class MainClass
    {
        private static readonly ILog Log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        public static void Main(string[] args)
        {
            // Set Invariant culture as default for all further processing
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            // Introduce subcommand for xics and spectra query
            if (args.Length > 0)
            {
                switch (args[0])
                {
                    case "xic":
                        XicParametersParsing(args.Skip(1).ToArray()); // skip first command
                        break;
                    case "query":
                        SpectrumQueryParametersParsing(args.Skip(1).ToArray());
                        break;
                    default:
                        RegularParametersParsing(args);
                        break;
                }
            }
            else
            {
                RegularParametersParsing(args);
            }
        }

        private static void XicParametersParsing(string[] args)
        {
            XicParameters parameters = new XicParameters();
            string singleFile = null;
            string fileDirectory = null;
            string outputFile = null;
            string outputDirectory = null;
            string logFormatString = null;

            var optionSet = new OptionSet
            {
                {
                    "h|help", "Prints out the options.",
                    h => parameters.help = h != null
                },
                {
                    "i=|input=", "The raw file input (Required). Specify this or an input directory -d",
                    v => singleFile = v
                },
                {
                    "d=|input_directory=",
                    "The directory containing the raw files (Required). Specify this or an input file -i.",
                    v => fileDirectory = v
                },
                {
                    "j=|json=",
                    "The json input file (Required).",
                    v => parameters.jsonFilePath = v
                },
                {
                    "p|print_example",
                    "Show a json input file example.",
                    v => parameters.printJsonExample = v != null
                },
                {
                    "b=|output=",
                    "The output file. Specify this or an output directory. Specifying neither writes to the input directory.",
                    v => outputFile = v
                },
                {
                    "o=|output_directory=",
                    "The output directory. Specify this or an output file. Specifying neither writes to the input directory.",
                    v => outputDirectory = v
                },
                {
                    "6|base64",
                    "Encodes the content of the xic vectors as base 64 encoded string.",
                    v => parameters.base64 = v != null
                },
                {
                    "s|stdout",
                    "Pipes the output into standard output. Logging is being turned off.",
                    v => parameters.stdout = v != null
                },
                {
                    "w|warningsAreErrors", "Return non-zero exit code for warnings; default only for errors",
                    v => parameters.Vigilant = v != null
                },
                {
                    "l=|logging=", "Optional logging level: 0 for silent, 1 for verbose, 2 for default, 3 for warning, 4 for error; both numeric and text (case insensitive) value recognized.",
                    v => logFormatString = v
                }
            };

            try
            {
                // parse the command line
                var extra = optionSet.Parse(args);

                if (!extra.IsNullOrEmpty())
                {
                    throw new OptionException("unexpected extra arguments", null);
                }


                if (parameters.help)
                {
                    ShowHelp("usage is:", new OptionException(),
                        optionSet);
                    return;
                }

                if (parameters.printJsonExample)
                {
                    var exampleJson =
                        "[\n  {\n    \"mz\":673.363,\n    \"tolerance\":10,\n    \"tolerance_unit\": \"ppm\",\n  },\n  {\n    \"mz\":867.345,\n    \"tolerance\": 0.02,\n    \"tolerance_unit\": \"da\",\n    \"rt_start\":87.56,\n    \"rt_end\":99.56\n  }\n]";

                    Console.WriteLine(exampleJson);
                    return;
                }


                if (singleFile != null && !File.Exists(singleFile))
                {
                    throw new OptionException(
                        "specify a valid RAW file location",
                        "-i, --input");
                }

                if (fileDirectory != null && !Directory.Exists(fileDirectory))
                {
                    throw new OptionException(
                        "specify a valid input directory",
                        "-d, --input_directory");
                }


                if (parameters.jsonFilePath == null)
                {
                    throw new OptionException(
                        "specify an json input file. If you are not sure about the structure of the json file, use -p for printing an examplarily json input file",
                        "-j, --json");
                }


                if (parameters.jsonFilePath != null && !File.Exists(parameters.jsonFilePath))
                {
                    throw new OptionException(
                        "specify a valid json file location",
                        "-j, --json");
                }


                if (outputDirectory != null && !Directory.Exists(outputDirectory))
                {
                    throw new OptionException(
                        "specify a valid output location",
                        "-o, --output");
                }

                if ((singleFile == null && fileDirectory == null) || (singleFile != null && fileDirectory != null))
                {
                    throw new OptionException(
                        "specify either an input file or an input directory",
                        "-i, --input xor -d, --input_directory");
                }

                if (outputFile != null && outputDirectory != null)
                {
                    throw new OptionException(
                        "cannot use an output file and an output directory simultaneously",
                        "-b, --output_file; -o, --output");
                }

                if (singleFile != null)
                {
                    parameters.rawFileList.Add(Path.GetFullPath(singleFile));
                    
                    if (outputFile != null)
                    {
                        parameters.outputFileList.Add(Path.GetFullPath(outputFile));
                    }
                    else if (outputDirectory != null)
                    {
                        parameters.outputFileList.Add(Path.Combine(outputDirectory ?? throw new NoNullAllowedException("Output directory cannot be null"),
                                                        Path.GetFileNameWithoutExtension(singleFile) + ".json"));
                    }
                    else
                    {
                        parameters.outputFileList.Add(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(singleFile)),
                                                        Path.GetFileNameWithoutExtension(singleFile) + ".json"));
                    }
                }
                else
                {
                    if (outputFile != null)
                    {
                        throw new OptionException("Cannot use single output file to proceess a directory, use directory output instead", "-o, --output");
                    }
                    else if (outputDirectory != null)
                    {
                        outputDirectory = Path.GetFullPath(outputDirectory);
                    }
                    else
                    {
                        outputDirectory = Path.GetFullPath(fileDirectory);
                    }

                    var directoryInfo = new DirectoryInfo(Path.GetFullPath(fileDirectory));
                    var files = directoryInfo.GetFiles("*", SearchOption.TopDirectoryOnly)
                        .Where(f => f.Extension.ToLower() == ".raw").ToArray<FileInfo>();
                    foreach (var file in files)
                    {
                        parameters.rawFileList.Add(file.FullName);
                        parameters.outputFileList.Add(Path.Combine(outputDirectory ?? throw new NoNullAllowedException("Output directory cannot be null"),
                                                    Path.GetFileNameWithoutExtension(file.Name) + ".json"));
                    }
                }

                if (logFormatString != null)
                {
                    parameters.LogFormat = (LogFormat)ParseToEnum(typeof(LogFormat), logFormatString, "-l, --logging");
                }

                if (parameters.stdout) parameters.LogFormat = LogFormat.SILENT; //switch off logging in stdout
            }
            catch (OptionException optionException)
            {
                ShowHelp("Error - usage is:", optionException,
                    optionSet);
            }
            catch (ArgumentNullException)
            {
                if (parameters.help)
                {
                    ShowHelp("usage is:", new OptionException(),
                        optionSet);
                }
                else
                {
                    ShowHelp("Error - usage is:", new OptionException(),
                        optionSet);
                }
            }

            var exitCode = 1;
            try
            {
                switch (parameters.LogFormat)
                {
                    case LogFormat.VERBOSE:
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository()).Root.Level =
                            Level.Debug;
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository())
                            .RaiseConfigurationChanged(EventArgs.Empty);
                        break;
                    case LogFormat.SILENT:
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository()).Root.Level =
                            Level.Off;
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository())
                            .RaiseConfigurationChanged(EventArgs.Empty);
                        break;
                    case LogFormat.WARNING:
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository()).Root.Level =
                            Level.Warn;
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository())
                            .RaiseConfigurationChanged(EventArgs.Empty);
                        break;
                    case LogFormat.ERROR:
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository()).Root.Level =
                            Level.Error;
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository())
                            .RaiseConfigurationChanged(EventArgs.Empty);
                        break;
                }

                XicExecutor.Run(parameters);

                Log.Info($"Processing completed {parameters.Errors} errors, {parameters.Warnings} warnings");

                exitCode = parameters.Vigilant ? parameters.Errors + parameters.Warnings : parameters.Errors;
            }
            catch (Exception ex)
            {
                if (ex is RawFileParserException)
                {
                    Log.Error(ex.Message);
                }
                else
                {
                    Log.Error("An unexpected error occured:");
                    Log.Error(ex.ToString());
                }
            }
            finally
            {
                Environment.Exit(exitCode);
            }
        }

        private static void SpectrumQueryParametersParsing(string[] args)
        {
            string logFormatString = null;
            QueryParameters parameters = new QueryParameters();
            var optionSet = new OptionSet
            {
                {
                    "h|help", "Prints out the options.",
                    h => parameters.help = h != null
                },
                {
                    "i=|input=", "The raw file input (Required).",
                    v => parameters.rawFilePath = v
                },
                {
                    "n=|scans=",
                    "The scan numbers. e.g. \"1-5, 20, 25-30\"",
                    v => parameters.scans = v
                },
                {
                    "b=|output=",
                    "The output file. Specifying none writes the output file to the input file parent directory.",
                    v => parameters.outputFile = v
                },
                {
                    "p|noPeakPicking",
                    "Don't use the peak picking provided by the native Thermo library. By default peak picking is enabled.",
                    v => parameters.noPeakPicking = v != null
                },
                {
                    "s|stdout",
                    "Pipes the output into standard output. Logging is being turned off",
                    v => parameters.stdout = v != null
                },
                {
                   "w|warningsAreErrors", "Return non-zero exit code for warnings; default only for errors",
                    v => parameters.Vigilant = v != null
                },
                {
                    "l=|logging=", "Optional logging level: 0 for silent, 1 for verbose, 2 for default, 3 for warning, 4 for error; both numeric and text (case insensitive) value recognized.",
                    v => logFormatString = v
                }
            };

            try
            {
                // parse the command line
                var extra = optionSet.Parse(args);

                if (!extra.IsNullOrEmpty())
                {
                    throw new OptionException("unexpected extra arguments", null);
                }

                if (parameters.help)
                {
                    ShowHelp("usage is:", new OptionException(),
                        optionSet);
                    return;
                }

                if (parameters.rawFilePath == null)
                {
                    throw new OptionException(
                        "specify an input file",
                        "-i, --input ");
                }

                if (parameters.rawFilePath != null && !File.Exists(parameters.rawFilePath))
                {
                    throw new OptionException(
                        "specify a valid RAW file location",
                        "-i, --input");
                }

                if (parameters.scans.IsNullOrEmpty())
                {
                    throw new OptionException(
                        "specify a valid scan range",
                        "-s, --scans");
                }

                if (logFormatString != null)
                {
                    parameters.LogFormat = (LogFormat)ParseToEnum(typeof(LogFormat), logFormatString, "-l, --logging");
                }

                if (parameters.stdout) parameters.LogFormat = LogFormat.SILENT; //switch off logging in stdout
            }
            catch (OptionException optionException)
            {
                ShowHelp("Error - usage is:", optionException,
                    optionSet);
            }
            catch (ArgumentNullException)
            {
                if (parameters.help)
                {
                    ShowHelp("usage is:", new OptionException(),
                        optionSet);
                }
                else
                {
                    ShowHelp("Error - usage is:", new OptionException(),
                        optionSet);
                }
            }

            var exitCode = 1;
            try
            {
                switch (parameters.LogFormat)
                {
                    case LogFormat.VERBOSE:
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository()).Root.Level =
                            Level.Debug;
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository())
                            .RaiseConfigurationChanged(EventArgs.Empty);
                        break;
                    case LogFormat.SILENT:
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository()).Root.Level =
                            Level.Off;
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository())
                            .RaiseConfigurationChanged(EventArgs.Empty);
                        break;
                    case LogFormat.WARNING:
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository()).Root.Level =
                            Level.Warn;
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository())
                            .RaiseConfigurationChanged(EventArgs.Empty);
                        break;
                    case LogFormat.ERROR:
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository()).Root.Level =
                            Level.Error;
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository())
                            .RaiseConfigurationChanged(EventArgs.Empty);
                        break;
                }

                QueryExecutor.Run(parameters);

                Log.Info($"Processing completed {parameters.Errors} errors, {parameters.Warnings} warnings");

                exitCode = parameters.Vigilant ? parameters.Errors + parameters.Warnings : parameters.Errors;
            }
            catch (Exception ex)
            {
                if (ex is RawFileParserException)
                {
                    Log.Error(ex.Message);
                }
                else
                {
                    Log.Error("An unexpected error occured:");
                    Log.Error(ex.ToString());
                }
            }
            finally
            {
                Environment.Exit(exitCode);
            }
        }

        private static void RegularParametersParsing(string[] args)
        {
            var help = false;
            var version = false;
            string outputFormatString = null;
            string metadataFormatString = null;
            string logFormatString = null;
            var parseInput = new ParseInput();

            var optionSet = new OptionSet
            {
                {
                    "h|help", "Prints out the options.",
                    h => help = h != null
                },
                {
                    "v|version", "Prints out the version of the executable.",
                    v => version = v != null
                },
                {
                    "i=|input=", "The raw file input (Required). Specify this or an input directory -d.",
                    v => parseInput.RawFilePath = v
                },
                {
                    "d=|input_directory=",
                    "The directory containing the raw files (Required). Specify this or an input raw file -i.",
                    v => parseInput.RawDirectoryPath = v
                },
                {
                    "b=|output=",
                    "The output file. Specify this or an output directory -o. Specifying neither writes to the input directory.",
                    v => parseInput.OutputFile = v
                },
                {
                    "o=|output_directory=",
                    "The output directory. Specify this or an output file -b. Specifying neither writes to the input directory.",
                    v => parseInput.OutputDirectory = v
                },
                {
                    "s|stdout",
                    "Write to standard output. Cannot be combined with file or directory output. Implies silent logging, i.e. logging level 0",
                    v => parseInput.StdOut = v != null
                },
                {
                    "f=|format=",
                    "The spectra output format: 0 for MGF, 1 for mzML, 2 for indexed mzML, 3 for Parquet, 4 for BinarySoa, 5 for None (no output); both numeric and text (case insensitive) value recognized. Defaults to indexed mzML if no format is specified.",
                    v => outputFormatString = v
                },
                {
                    "m=|metadata=", "The metadata output format: 0 for JSON, 1 for TXT, 2 for None (no output); both numeric and text (case insensitive) value recognized. Defaults to None",
                    v => metadataFormatString = v
                },
                {
                    "c=|metadata_output_file",
                    "The metadata output file. By default the metadata file is written to the output directory.",
                    v => parseInput.MetadataOutputFile = v
                },
                {
                    "g|gzip", "GZip the output file.",
                    v => parseInput.Gzip = v != null
                },
                {
                    "p:|noPeakPicking:",
                    "Don't use the peak picking provided by the native Thermo library. By default peak picking is enabled. Optional argument allows disabling peak peaking only for selected MS levels and should be a comma-separated list of integers (1,2,3) and/or intervals (1-3), open-end intervals (1-) are allowed",
                    v => parseInput.NoPeakPicking = v is null ? ParseInput.AllLevels : ParseMsLevel(v)
                },
                {
                    "z|noZlibCompression",
                    "Don't use zlib compression for the m/z ratios and intensities. By default zlib compression is enabled.",
                    v => parseInput.NoZlibCompression = v != null
                },
                {
                    "a|allDetectors",
                    "Extract additional detector data: UV/PDA etc",
                    v => parseInput.AllDetectors = v != null
                },
                {
                    "l=|logging=", "Optional logging level: 0 for silent, 1 for verbose, 2 for default, 3 for warning, 4 for error; both numeric and text (case insensitive) value recognized.",
                    v => logFormatString = v
                },
                {
                    "e|ignoreInstrumentErrors", "Ignore missing properties by the instrument.",
                    v => parseInput.IgnoreInstrumentErrors = v != null
                },
                {
                    "x|excludeExceptionData", "Exclude reference and exception data",
                    v => parseInput.ExData = v != null
                },
                {
                    "L=|msLevel=",
                    "Select MS levels (MS1, MS2, etc) included in the output, should be a comma-separated list of integers (1,2,3) and/or intervals (1-3), open-end intervals (1-) are allowed",
                    v => parseInput.MsLevel = ParseMsLevel(v)
                },
                {
                    "P|mgfPrecursor",
                    "Include precursor scan number in MGF file TITLE",
                    v => parseInput.MgfPrecursor = v != null
                },
                {
                    "N|noiseData", "Include noise data in mzML and BinarySoa output",
                    v => parseInput.NoiseData = v != null
                },
                {
                    "C|chargeData", "Include instrument detected charge states in mzML and BinarySoa output (only for high resolution centroided data)",
                    v => parseInput.ChargeData = v != null
                },
                {
                  "w|warningsAreErrors", "Return non-zero exit code for warnings; default only for errors",
                    v => parseInput.Vigilant = v != null 
                },
                {
                    "u:|s3_url:",
                    "Optional property to write directly the data into S3 Storage.",
                    v => parseInput.S3Url = v
                },
                {
                    "k:|s3_accesskeyid:",
                    "Optional key for the S3 bucket to write the file output.",
                    v => parseInput.S3AccessKeyId = v
                },
                {
                    "t:|s3_secretaccesskey:",
                    "Optional key for the S3 bucket to write the file output.",
                    v => parseInput.S3SecretAccessKey = v
                },
                {
                    "n:|s3_bucketName:",
                    "S3 bucket name",
                    v => parseInput.BucketName = v
                }
            };

            try
            {
                // parse the command line
                var extra = optionSet.Parse(args);

                if (!extra.IsNullOrEmpty())
                {
                    throw new OptionException("Unexpected extra arguments", null);
                }

                if (help)
                {
                    var helpMessage =
                        $"Usage is {Assembly.GetExecutingAssembly().GetName().Name}.exe [subcommand] [options]\noptional subcommands are xic|query (use [subcommand] -h for more info]):";
                    ShowHelp(helpMessage, null, optionSet);
                    return;
                }

                if (version)
                {
                    Console.WriteLine(Version);
                    return;
                }

                if (parseInput.RawFilePath == null && parseInput.RawDirectoryPath == null)
                {
                    throw new OptionException(
                        "specify an input file or an input directory",
                        "-i, --input or -d, --input_directory");
                }

                if (parseInput.RawFilePath != null)
                {
                    if (parseInput.RawDirectoryPath != null)
                    {
                        throw new OptionException(
                            "specify an input file or an input directory, not both",
                            "-i, --input or -d, --input_directory");
                    }

                    if (!File.Exists(parseInput.RawFilePath))
                    {
                        throw new OptionException(
                            "specify a valid RAW file location",
                            "-i, --input");
                    }
                }

                if (parseInput.RawDirectoryPath != null && !Directory.Exists(parseInput.RawDirectoryPath))
                {
                    throw new OptionException(
                        "specify a valid input directory",
                        "-d, --input_directory");
                }

                if (parseInput.StdOut && (parseInput.OutputFile != null || parseInput.OutputDirectory != null))
                {
                    throw new OptionException(
                        "standard output cannot be combined with file or directory output",
                        "-s, --stdout");
                }

                if (parseInput.OutputFile == null && parseInput.OutputDirectory == null)
                {
                    if (parseInput.RawFilePath != null)
                    {
                        parseInput.OutputDirectory = Path.GetDirectoryName(Path.GetFullPath(parseInput.RawFilePath));
                    }
                    else if (parseInput.RawDirectoryPath != null)
                    {
                        parseInput.OutputDirectory = parseInput.RawDirectoryPath;
                    }
                }

                if (parseInput.OutputFile != null)
                {
                    if (parseInput.OutputDirectory == null)
                    {
                        parseInput.OutputDirectory = Path.GetDirectoryName(parseInput.OutputFile);
                    }
                    else
                    {
                        throw new OptionException(
                            "specify an output directory or an output file, not both",
                            "-o, --output or -b, --output_file");
                    }

                    if (Directory.Exists(parseInput.OutputFile))
                    {
                        throw new OptionException(
                            "specify a valid output file, not a directory",
                            "-b, --output_file");
                    }

                    if (parseInput.RawDirectoryPath != null)
                    {
                        throw new OptionException(
                            "when using an input directory, specify an output directory instead of an output file",
                            "-o, --output instead of -b, --output_file");
                    }
                }

                if (!parseInput.OutputDirectory.IsNullOrEmpty() && !Directory.Exists(parseInput.OutputDirectory))
                {
                    throw new OptionException(
                        "specify a valid output directory",
                        "-o, --output");
                }

                if (metadataFormatString == null && outputFormatString == null)
                {
                    parseInput.OutputFormat = OutputFormat.IndexMzML;
                }

                if (outputFormatString != null)
                {
                    parseInput.OutputFormat = (OutputFormat)ParseToEnum(typeof(OutputFormat), outputFormatString, "-f, --format");
                }

                if (metadataFormatString != null)
                {
                    parseInput.MetadataFormat = (MetadataFormat)ParseToEnum(typeof(MetadataFormat), metadataFormatString, "-m, --metadata");
                }

                if (parseInput.MetadataOutputFile != null && Directory.Exists(parseInput.MetadataOutputFile))
                {
                    throw new OptionException(
                        "specify a valid metadata output file, not a directory",
                        "-c, --metadata_output_file");
                }

                if (parseInput.MetadataOutputFile != null && parseInput.MetadataFormat == MetadataFormat.None)
                {
                    throw new OptionException("specify a metadata format (0 for JSON, 1 for TXT)",
                        "-m, --metadata");
                }

                if (parseInput.MetadataOutputFile != null && parseInput.RawDirectoryPath != null)
                {
                    throw new OptionException(
                        "when using an input directory, specify an output directory instead of a metadata output file",
                        "-o, --output instead of -c, --metadata_output_file");
                }

                if (logFormatString != null)
                {
                    parseInput.LogFormat = (LogFormat)ParseToEnum(typeof(LogFormat), logFormatString, "-l, --logging");
                }

                if (parseInput.StdOut)
                {
                    parseInput.LogFormat = LogFormat.SILENT;

                    //use non-indexed mzML with stdout
                    if (parseInput.OutputFormat == OutputFormat.IndexMzML) parseInput.OutputFormat = OutputFormat.MzML;
                }

                // Switch off gzip compression for Parquet
                if (parseInput.OutputFormat == OutputFormat.Parquet) parseInput.Gzip = false;

                parseInput.MaxLevel = parseInput.MsLevel.Max();

                if (parseInput.S3Url != null && parseInput.S3AccessKeyId != null &&
                    parseInput.S3SecretAccessKey != null && parseInput.BucketName != null)
                    if (Uri.IsWellFormedUriString(parseInput.S3Url, UriKind.Absolute))
                    {
                        parseInput.InitializeS3Bucket();
                    }
                    else
                    {
                        throw new RawFileParserException("Invalid S3 url: " + parseInput.S3Url);
                    }
            }
            catch (OptionException optionException)
            {
                ShowHelp("Error - usage is:", optionException,
                    optionSet);
            }
            catch (ArgumentNullException)
            {
                if (help)
                {
                    ShowHelp("usage is:", new OptionException(),
                        optionSet);
                }
                else
                {
                    ShowHelp("Error - usage is:", new OptionException(),
                        optionSet);
                }
            }

            var exitCode = 1;
            try
            {
                switch (parseInput.LogFormat)
                {
                    case LogFormat.VERBOSE:
                        ((log4net.Repository.Hierarchy.Hierarchy) LogManager.GetRepository()).Root.Level =
                            Level.Debug;
                        ((log4net.Repository.Hierarchy.Hierarchy) LogManager.GetRepository())
                            .RaiseConfigurationChanged(EventArgs.Empty);
                        break;
                    case LogFormat.SILENT:
                        ((log4net.Repository.Hierarchy.Hierarchy) LogManager.GetRepository()).Root.Level =
                            Level.Off;
                        ((log4net.Repository.Hierarchy.Hierarchy) LogManager.GetRepository())
                            .RaiseConfigurationChanged(EventArgs.Empty);
                        break;
                    case LogFormat.WARNING:
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository()).Root.Level =
                            Level.Warn;
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository())
                            .RaiseConfigurationChanged(EventArgs.Empty);
                        break;
                    case LogFormat.ERROR:
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository()).Root.Level =
                            Level.Error;
                        ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository())
                            .RaiseConfigurationChanged(EventArgs.Empty);
                        break;
                }

                RawFileParser.Parse(parseInput);

                Log.Info($"Processing completed {parseInput.Errors} errors, {parseInput.Warnings} warnings");

                exitCode = parseInput.Vigilant ? parseInput.Errors + parseInput.Warnings: parseInput.Errors;
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Error(!ex.Message.IsNullOrEmpty()
                    ? ex.Message
                    : "Attempting to write to an unauthorized location.");
            }
            catch (Amazon.S3.AmazonS3Exception ex)
            {
                Log.Error(!ex.Message.IsNullOrEmpty()
                    ? "An Amazon S3 exception occured: " + ex.Message
                    : "An Amazon S3 exception occured: " + ex);
            }
            catch (Exception ex)
            {
                if (ex is RawFileParserException)
                {
                    Log.Error(ex.Message);
                }
                else
                {
                    Log.Error("An unexpected error occured:");
                    Log.Error(ex.ToString());
                }
            }
            finally
            {
                Environment.Exit(exitCode);
            }
        }

        /// <summary>
        /// Show the help message
        /// </summary>
        /// <param name="message">the help message</param>
        /// <param name="optionException">the option exception, can be null</param>
        /// <param name="optionSet">the option set object</param>
        private static void ShowHelp(string message, OptionException optionException, OptionSet optionSet)
        {
            if (optionException != null)
            {
                if (!optionException.OptionName.IsNullOrEmpty())
                {
                    Console.Error.Write(optionException.OptionName + ": ");
                }

                Console.Error.WriteLine(optionException.Message);
            }

            Console.Error.WriteLine(message);
            optionSet.WriteOptionDescriptions(Console.Error);
            Environment.Exit(-1);
        }

        private static string GetValidEnumLevels(Type enumType)
        {
            List<string> output = new List<string>();
            foreach (int v in Enum.GetValues(enumType))
            {
                output.Add(String.Format("{0} ({1})", Enum.GetName(enumType, v), v));
            }

            return String.Join("\n", output);
        }

        private static int ParseToEnum(Type enumType, string formatString, string keyName)
        {
            if (int.TryParse(formatString, out var formatInt)) //can be parsed as int
            {
                if (Enum.IsDefined(enumType, formatInt))
                {
                    return formatInt;
                }
                else
                {
                    throw new OptionException(
                    String.Format("unknown format value, the following values recognized (case insensitive)\n{0}", GetValidEnumLevels(enumType)),
                    keyName);
                }

            }
            else //try parse as a string
            {
                try
                {
                    return (int)Enum.Parse(enumType, formatString, true);
                }

                catch (Exception)
                {
                    throw new OptionException(
                    String.Format("unknown format value, the following values recognized (case insensitive)\n{0}", GetValidEnumLevels(enumType)),
                    keyName);
                }
            }
        }

        private static HashSet<int> ParseMsLevel(string inputString)
        {
            try
            {
                var levelIterator = new NumberIterator(inputString, 1, 10);
                return new HashSet<int>(levelIterator.IterateScans());
            }
            catch (Exception ex)
            {
                throw new OptionException($"Cannot parse MS level from {inputString} - {ex.Message}", "msLevel");
            }
        }
    }
}

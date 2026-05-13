# ThermoRawFileParser

A tool allowing reading Thermo RAW mass spectrometer files and converting to common open formats on all platforms supporting .NET Core.

Supported formats:
* MGF
* mzML and indexed mzML
* Apache Parquet
* BinarySoa

Version before 2.0.0 require Mono to run on Linux and Mac.

As of version 1.2.0, 2 subcommands are available (shoutout to the [eubic 2020 developers meeting](https://eubic-ms.org/events/2020-developers-meeting/), see [usage](#usage) for examples):
* query: returns one or more spectra in JSON PROXI by scan number(s)
* xic: returns chromatogram data based on JSON filter input

RawFileReader reading tool. Copyright © 2016 by Thermo Fisher Scientific, Inc. All rights reserved

## ThermoRawFileParser Publication:
  * Hulstaert N, Shofstahl J, Sachsenberg T, Walzer M, Barsnes H, Martens L, Perez-Riverol Y: _ThermoRawFileParser: Modular, Scalable, and Cross-Platform RAW File Conversion_ [[PMID 31755270](https://www.ncbi.nlm.nih.gov/pubmed/31755270)].
  * If you use ThermoRawFileParser as part of a publication, please include this reference.

## Requirements

### Current version
Release page provide self-contained releases for OSX, Linux, and Windows and framework-based release. Framework-based release requires [.NET 8 runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime).

For developers: [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) is required to build the tool.

### Prior to 1.5.0
[Mono](https://www.mono-project.com/download/stable/#download-lin) (install mono-complete if you encounter "assembly not found" errors).

## Download

Click [here](https://github.com/compomics/ThermoRawFileParser/releases) to go to the release page (with [release notes](https://github.com/compomics/ThermoRawFileParser/wiki/ReleaseNotes) starting from v1.1.7).

You can find the ThermoRawFileParserGUI [here](https://github.com/compomics/ThermoRawFileParserGUI).

## Release Notes

You can read release notes (starting from version 1.1.7) in the [wiki page](https://github.com/compomics/ThermoRawFileParser/wiki/ReleaseNotes)

## Usage

```
ThermoRawFileParser -i=/home/user/data_input/raw_file.raw -o=/home/user/data_input/output/ -f=0 -g -m=0
```

with only the mimimal required argument `-i` or `-d` this becomes

```
ThermoRawFileParser -i=/home/user/data_input/raw_file.raw
```
or

```
ThermoRawFileParser -d=/home/user/data_input/
```

When running framework-based version use `dotnet ThermoRawFileParser.dll` instead.

 The optional parameters only work in the -option=value format. The tool can output some RAW file metadata `-m=0|1` (0 for JSON, 1 for TXT) and the spectra file `-f=0|1|2|3|4|5` (0 for MGF, 1 for mzML, 2 for indexed mzML, 3 for Parquet, 4 for BinarySoa, 5 for no output) or both. Use the `-p` flag to disable the thermo native peak picking.

```
Usage is ThermoRawFileParser.exe [subcommand] [options]
optional subcommands are xic|query (use [subcommand] -h for more info]):
  -h, --help                 Prints out the options.
  -v, --version              Prints out the version of the executable.
  -i, --input=VALUE          The raw file input (Required). Specify this or an
                               input directory -d.
  -d, --input_directory=VALUE
                             The directory containing the raw files (Required).
                               Specify this or an input raw file -i.
  -b, --output=VALUE         The output file. Specify this or an output
                               directory -o. Specifying neither writes to the
                               input directory.
  -o, --output_directory=VALUE
                             The output directory. Specify this or an output
                               file -b. Specifying neither writes to the input
                               directory.
  -s, --stdout               Write to standard output. Cannot be combined with
                               file or directory output. Implies silent logging,
                                i.e. logging level 0
  -f, --format=VALUE         The spectra output format: 0 for MGF, 1 for mzML,
                               2 for indexed mzML, 3 for Parquet, 4 for
                               BinarySoa, 5 for None (no output); both numeric
                               and text (case insensitive) value recognized.
                               Defaults to indexed mzML if no format is
                               specified.
  -m, --metadata=VALUE       The metadata output format: 0 for JSON, 1 for TXT,
                               2 for None (no output); both numeric and text (
                               case insensitive) value recognized. Defaults to
                               None
  -c, --metadata_output_file=VALUE
                             The metadata output file. By default the metadata
                               file is written to the output directory.
  -g, --gzip                 GZip the output file.
  -p, --noPeakPicking[=VALUE]
                             Don't use the peak picking provided by the native
                               Thermo library. By default peak picking is
                               enabled. Optional argument allows disabling peak
                               peaking only for selected MS levels and should
                               be a comma-separated list of integers (1,2,3)
                               and/or intervals (1-3), open-end intervals (1-)
                               are allowed
  -z, --noZlibCompression    Don't use zlib compression for the m/z ratios and
                               intensities. By default zlib compression is
                               enabled.
  -a, --allDetectors         Extract additional detector data: UV/PDA etc
  -l, --logging=VALUE        Optional logging level: 0 for silent, 1 for
                               verbose, 2 for default, 3 for warning, 4 for
                               error; both numeric and text (case insensitive)
                               value recognized.
  -e, --ignoreInstrumentErrors
                             Ignore missing properties by the instrument.
  -x, --excludeExceptionData Exclude reference and exception data
  -L, --msLevel=VALUE        Select MS levels (MS1, MS2, etc) included in the
                               output, should be a comma-separated list of
                               integers (1,2,3) and/or intervals (1-3), open-
                               end intervals (1-) are allowed
  -P, --mgfPrecursor         Include precursor scan number in MGF file TITLE
  -N, --noiseData            Include noise data in mzML and BinarySoa output
  -C, --chargeData           Include instrument detected charge states in mzML
                               and BinarySoa output (only for high resolution
                               centroided data)
  -w, --warningsAreErrors    Return non-zero exit code for warnings; default
                               only for errors
  -u, --s3_url[=VALUE]       Optional property to write directly the data into
                               S3 Storage.
  -k, --s3_accesskeyid[=VALUE]
                             Optional key for the S3 bucket to write the file
                               output.
  -t, --s3_secretaccesskey[=VALUE]
                             Optional key for the S3 bucket to write the file
                               output.
  -n, --s3_bucketName[=VALUE]
                             S3 bucket name
```

Output file extension is determined by the used output format and (optional) gzip compression, for example, if format is MGF without gzip compression, the output file will receive `.mgf` extension, if format is mzML with gzip compression the output file will have `.mzML.gz` extension. All user input will be standardized to fulfill abovementioned requirements.

A (java) graphical user interface is also available [here](https://github.com/compomics/ThermoRawFileParserGUI) that enables the selection of an input RAW directory or one ore more RAW files.

### query subcommand
Enables the retrieval spectra by (a) scan number(s) in [PROXI format](https://github.com/HUPO-PSI/proxi-schemas).

```
mono ThermoRawFileParser.exe query -i=/home/user/data_input/raw_file.raw -o=/home/user/output.json n="1-5, 20, 25-30"
```

```
ThermoRawFileParser.exe query --help
usage is:
  -h, --help                 Prints out the options.
  -i, --input=VALUE          The raw file input (Required).
  -n, --scans=VALUE          The scan numbers. e.g. "1-5, 20, 25-30"
  -b, --output=VALUE    	 The output file. Specifying none writes the output
                               file to the input file parent directory.
  -p, --noPeakPicking        Don't use the peak picking provided by the native
                               Thermo library. By default peak picking is
                               enabled.
  -s, --stdout               Pipes the output into standard output. Logging is
                               being turned off
  -w, --warningsAreErrors    Return non-zero exit code for warnings; default
                               only for errors
  -l, --logging=VALUE        Optional logging level: 0 for silent, 1 for
                               verbose, 2 for default, 3 for warning, 4 for
                               error; both numeric and text (case insensitive)
                               value recognized.
```

### xic subcommand
Return one or more chromatograms based on query JSON input.

```
mono ThermoRawFileParser.exe xic -i=/home/user/data_input/raw_file.raw -j=/home/user/xic_input.json
```

```
ThermoRawFileParser.exe xic --help
  -h, --help                 Prints out the options.
  -i, --input=VALUE          The raw file input (Required). Specify this or an
                               input directory -d
  -d, --input_directory=VALUE
                             The directory containing the raw files (Required).
                               Specify this or an input file -i.
  -j, --json=VALUE           The json input file (Required).
  -p, --print_example        Show a json input file example.
  -b, --output=VALUE    	 The output file. Specify this or an output
                               directory. Specifying neither writes to the
                               input directory.
  -o, --output_directory=VALUE         
							 The output directory. Specify this or an output
                               file. Specifying neither writes to the input
                               directory.
  -6, --base64               Encodes the content of the xic vectors as base 64
                               encoded string.
  -s, --stdout               Pipes the output into standard output. Logging is
                               being turned off.
  -w, --warningsAreErrors    Return non-zero exit code for warnings; default
                               only for errors
  -l, --logging=VALUE        Optional logging level: 0 for silent, 1 for
                               verbose, 2 for default, 3 for warning, 4 for
                               error; both numeric and text (case insensitive)
                               value recognized.
```

Provide one of the following filters:
 * M/Z and tolerance (tolerance unit optional, defaults to `ppm`)
 * M/Z start and end
 * sequence and tolerance (tolerance unit optional, defaults to `ppm`)

optionally one can define starting and ending retention times, provide filter string (defaults to `ms`, i.e. only MS1 scans), and a comment (free text) field; any valid filter string is supported,
however only basic validation is performed, see [issue #158](https://github.com/compomics/ThermoRawFileParser/issues/158) for details. Comment can contain any text and will be preserved in the output.

An example input JSON file:

```
[
        {
            "mz":488.5384,
            "tolerance":10,
            "tolerance_unit":"ppm"           
        },
        {
            "mz":575.2413,
            "tolerance":10,
            "rt_start":630,
            "rt_end":660,
            "scan_filter":"ms2"
        },
        {
            "mz_start":749.7860,
            "mz_end" : 750.4,            
            "rt_start":630,
            "rt_end":660
        },
        {
            "sequence":"TRANNEL",
            "tolerance":10
        },
        {
            "mz":1014.5099732499732,
            "rt_start":14.0600881872,
            "rt_end":14.4167198290667,
            "tolerance":5,
            "tolerance_unit":"ppm",
            "comment":"Only ion trap scans"
            "scan_filter":"ITMS"
        }
   }
]

```

[Go to top of page](#thermorawfileparser)

## Galaxy integration

ThermoRawFileParser is available in the Galaxy [ToolShed](https://toolshed.g2.bx.psu.edu/view/galaxyp/thermo_raw_file_converter/a3edda696e4d) and is deployed at the [European Galaxy Server](https://usegalaxy.eu/root?tool_id=toolshed.g2.bx.psu.edu/repos/galaxyp/thermo_raw_file_converter/thermo_raw_file_converter/).

## Logging

By default the parser only logs to console. To enable logging to file, uncomment the file appender in the `log4net.config` file.

```
<log4net>
    <root>
        <level value="INFO" />
        <appender-ref ref="console" />
        <!--<appender-ref ref="file" />-->
    </root>
    <appender name="console" type="log4net.Appender.ConsoleAppender">
        <layout type="log4net.Layout.PatternLayout">
            <conversionPattern value="%date %level %logger - %message%newline" />
        </layout>
    </appender>
    <!--<appender name="file" type="log4net.Appender.RollingFileAppender">
        <file value="ThermoRawFileParser.log" />
        <appendToFile value="true" />
        <rollingStyle value="Size" />
        <maxSizeRollBackups value="5" />
        <maximumFileSize value="10MB" />
        <staticLogFileName value="true" />
        <layout type="log4net.Layout.PatternLayout">
            <conversionPattern value="%date [%thread] %level %logger - %message%newline" />
        </layout>
    </appender>-->
</log4net>
```

## Docker

First check the latest version tag on [biocontainers/thermorawfileparser/tags](https://quay.io/repository/biocontainers/thermorawfileparser?tab=tags). Then pull and run the container with

```bash
docker run -i -t -v /home/user/raw:/data_input quay.io/biocontainers/thermorawfileparser:<tag> ThermoRawFileParser.sh --help
```

[Go to top of page](#thermorawfileparser)

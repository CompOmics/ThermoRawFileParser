using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using IO.Mgf;
using NUnit.Framework;
using Parquet;
using ThermoRawFileParser;
using ThermoRawFileParser.Writer.MzML;

namespace ThermoRawFileParserTest
{
    [TestFixture]
    public class WriterTests
    {
        [Test]
        public void TestExtensionsNull()
        {
            // Get temp path for writing the test files
            var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempFilePath);

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW");
            var parseInput = new ParseInput();
            parseInput.RawFilePath = testRawFile;
            parseInput.OutputDirectory = tempFilePath;

            //empty filename
            parseInput.OutputFormat = OutputFormat.MGF;
            RawFileParser.Parse(parseInput);
            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mgf")));

            parseInput.OutputFormat = OutputFormat.MzML;
            RawFileParser.Parse(parseInput);
            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mzML")));
            File.Delete(Path.Combine(tempFilePath, "small.mzML"));

            parseInput.OutputFormat = OutputFormat.IndexMzML;
            RawFileParser.Parse(parseInput);
            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mzML")));

            parseInput.Gzip = true;
            RawFileParser.Parse(parseInput);
            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mzML.gz")));
            File.Delete(Path.Combine(tempFilePath, "small.mzML.gz"));

            parseInput.OutputFormat = OutputFormat.MGF;
            RawFileParser.Parse(parseInput);
            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mgf.gz")));

            parseInput.OutputFormat = OutputFormat.MzML;
            RawFileParser.Parse(parseInput);
            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mzML.gz")));

            Directory.Delete(tempFilePath, true);
        }

        [Test]
        public void TestExtensionsFull()
        {
            // Get temp path for writing the test files
            var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempFilePath);

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW");
            var parseInput = new ParseInput();
            parseInput.RawFilePath = testRawFile;

            List<OutputFormat> formats = new List<OutputFormat>();
            string userInput;
            string expectedOutput;

            foreach (string line in File.ReadLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/ExtensionTest.tsv")).Skip(1))
            {
                string[] words = line.Split('\t');

                switch (words[0].ToLower())
                {
                    case "mgf": formats = new List<OutputFormat> { OutputFormat.MGF }; break;
                    case "mzml": formats = new List<OutputFormat> { OutputFormat.MzML, OutputFormat.IndexMzML }; break;
                }

                switch (words[1].ToLower())
                {
                    case "true": parseInput.Gzip = true; break;
                    default: parseInput.Gzip = false; break;
                }

                userInput = words[2];
                expectedOutput = words[3];

                parseInput.OutputFile = Path.Combine(tempFilePath, userInput);

                foreach (var format in formats)
                {
                    parseInput.OutputFormat = format;
                    RawFileParser.Parse(parseInput);
                    Assert.That(File.Exists(Path.Combine(tempFilePath, expectedOutput)));
                    File.Delete(Path.Combine(tempFilePath, expectedOutput));
                }

            }

            Directory.Delete(tempFilePath, true);
        }

        [Test]
        public void TestMgf()
        {
            // Get temp path for writing the test MGF
            var tempFilePath = Path.GetTempPath();

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW");
            var parseInput = new ParseInput(testRawFile, null, tempFilePath, OutputFormat.MGF);

            RawFileParser.Parse(parseInput);

            Assert.That(parseInput.Errors, Is.EqualTo(0));
            Assert.That(parseInput.Warnings, Is.EqualTo(0));

            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mgf")));
            var mgfData = Mgf.LoadAllStaticData(Path.Combine(tempFilePath, "small.mgf"));
            Assert.That(mgfData.NumSpectra, Is.EqualTo(34));

            File.Delete(Path.Combine(tempFilePath, "small.mgf"));
        }

        [Test]
        public void TestFolderMgfs()
        {
            // Get temp path for writing the test MGF
            var tempInPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var tempOutPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            Directory.CreateDirectory(tempInPath);
            Directory.CreateDirectory(tempOutPath);

            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW"), Path.Combine(tempInPath, "small.RAW"));
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small2.RAW"), Path.Combine(tempInPath, "small2.RAW"));
            File.Create(Path.Combine(tempInPath, "Not_raw_file")).Close(); // Create an empty file to test handling of multiple files

            var parseInput = new ParseInput(null, tempInPath, tempOutPath, OutputFormat.MGF);

            RawFileParser.Parse(parseInput);

            Assert.That(parseInput.Errors, Is.EqualTo(0));
            Assert.That(parseInput.Warnings, Is.EqualTo(0));

            var numFiles = Directory.GetFiles(tempOutPath, "*.mgf");
            Assert.That(numFiles.Length, Is.EqualTo(2));

            var mgfData = Mgf.LoadAllStaticData(Path.Combine(tempOutPath, "small.mgf"));
            Assert.That(mgfData.NumSpectra, Is.EqualTo(34));

            var mgfData2 = Mgf.LoadAllStaticData(Path.Combine(tempOutPath, "small2.mgf"));
            Assert.That(mgfData2.NumSpectra, Is.EqualTo(49));

            Directory.Delete(tempInPath, true);
            Directory.Delete(tempOutPath, true);
        }

        [Test]
        public void TestMzml()
        {
            // Get temp path for writing the test mzML
            var tempFilePath = Path.GetTempPath();

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW");
            var parseInput = new ParseInput(testRawFile, null, tempFilePath, OutputFormat.MzML);

            RawFileParser.Parse(parseInput);

            Assert.That(parseInput.Errors, Is.EqualTo(0));
            Assert.That(parseInput.Warnings, Is.EqualTo(0));

            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mzML")));
            // Deserialize the mzML file
            using (var fs = new FileStream(Path.Combine(tempFilePath, "small.mzML"), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            {
                var xmlSerializer = new XmlSerializer(typeof(mzMLType));
                var testMzMl = (mzMLType)xmlSerializer.Deserialize(fs);

                Assert.That(testMzMl.run.spectrumList.count, Is.EqualTo("48"));
                Assert.That(testMzMl.run.spectrumList.spectrum.Length, Is.EqualTo(48));

                Assert.That(testMzMl.run.chromatogramList.count, Is.EqualTo("1"));
                Assert.That(testMzMl.run.chromatogramList.chromatogram.Length, Is.EqualTo(1));

                Assert.That(testMzMl.run.chromatogramList.chromatogram[0].defaultArrayLength, Is.EqualTo(48));

                Assert.That(testMzMl.run.spectrumList.spectrum[0].binaryDataArrayList.count, Is.EqualTo("2"));
                Assert.That(testMzMl.run.spectrumList.spectrum[0].binaryDataArrayList.binaryDataArray.Length, Is.EqualTo(2));
                //test that the m/z array is present
                Assert.That(testMzMl.run.spectrumList.spectrum[0].binaryDataArrayList.binaryDataArray.Any(a => a.cvParam.Any(c => c.accession == "MS:1000514")));
                //test that the intensity array is present
                Assert.That(testMzMl.run.spectrumList.spectrum[0].binaryDataArrayList.binaryDataArray.Any(a => a.cvParam.Any(c => c.accession == "MS:1000515")));
            }

            File.Delete(Path.Combine(tempFilePath, "small.mzML"));
        }

        [Test]
        public void TestMzml_Charge()
        {
            // Get temp path for writing the test mzML
            var tempFilePath = Path.GetTempPath();

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW");
            var parseInput = new ParseInput(testRawFile, null, tempFilePath, OutputFormat.MzML);
            parseInput.ChargeData = true;

            RawFileParser.Parse(parseInput);
            Assert.That(parseInput.Errors, Is.EqualTo(0));
            Assert.That(parseInput.Warnings, Is.EqualTo(0));

            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mzML")));
            // Deserialize the mzML file
            using (var fs = new FileStream(Path.Combine(tempFilePath, "small.mzML"), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            {
                var xmlSerializer = new XmlSerializer(typeof(mzMLType));
                var testMzMl = (mzMLType)xmlSerializer.Deserialize(fs);

                //check that the charge array is present
                Assert.That(testMzMl.run.spectrumList.spectrum[0].binaryDataArrayList.count, Is.EqualTo("3"));
                Assert.That(testMzMl.run.spectrumList.spectrum[0].binaryDataArrayList.binaryDataArray.Length, Is.EqualTo(3));
                Assert.That(testMzMl.run.spectrumList.spectrum[0].binaryDataArrayList.binaryDataArray.Any(a => a.cvParam.Any(c => c.accession == "MS:1000516")));
            }

            File.Delete(Path.Combine(tempFilePath, "small.mzML"));
        }

        [Test]
        public void TestMzml_Noise()
        {
            // Get temp path for writing the test mzML
            var tempFilePath = Path.GetTempPath();

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW");
            var parseInput = new ParseInput(testRawFile, null, tempFilePath, OutputFormat.MzML);
            parseInput.NoiseData = true;

            RawFileParser.Parse(parseInput);
            Assert.That(parseInput.Errors, Is.EqualTo(0));
            Assert.That(parseInput.Warnings, Is.EqualTo(0));

            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mzML")));
            // Deserialize the mzML file
            using (var fs = new FileStream(Path.Combine(tempFilePath, "small.mzML"), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            {
                var xmlSerializer = new XmlSerializer(typeof(mzMLType));
                var testMzMl = (mzMLType)xmlSerializer.Deserialize(fs);

                //check that the charge array is present
                Assert.That(testMzMl.run.spectrumList.spectrum[0].binaryDataArrayList.count, Is.EqualTo("5"));
                Assert.That(testMzMl.run.spectrumList.spectrum[0].binaryDataArrayList.binaryDataArray.Length, Is.EqualTo(5));
                // Check that the noise arrays are present
                Assert.That(testMzMl.run.spectrumList.spectrum[0].binaryDataArrayList.binaryDataArray.Any(a => a.cvParam.Any(c => c.accession == "MS:1002745")));
                Assert.That(testMzMl.run.spectrumList.spectrum[0].binaryDataArrayList.binaryDataArray.Any(a => a.cvParam.Any(c => c.accession == "MS:1002744")));
                Assert.That(testMzMl.run.spectrumList.spectrum[0].binaryDataArrayList.binaryDataArray.Any(a => a.cvParam.Any(c => c.accession == "MS:1002743")));
            }

            File.Delete(Path.Combine(tempFilePath, "small.mzML"));
        }

        [Test]
        public void TestProfileMzml()
        {
            // Get temp path for writing the test mzML
            var tempFilePath = Path.GetTempPath();

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW");
            var parseInput = new ParseInput(testRawFile, null, tempFilePath, OutputFormat.MzML);

            parseInput.NoPeakPicking = new HashSet<int> { 1, 2 };

            RawFileParser.Parse(parseInput);
            Assert.That(parseInput.Errors, Is.EqualTo(0));
            Assert.That(parseInput.Warnings, Is.EqualTo(0));

            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mzML")));
            // Deserialize the mzML file
            using (var fs = new FileStream(Path.Combine(tempFilePath, "small.mzML"), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            {
                var xmlSerializer = new XmlSerializer(typeof(mzMLType));
                var testMzMl = (mzMLType)xmlSerializer.Deserialize(fs);

                Assert.That(testMzMl.run.spectrumList.count, Is.EqualTo("48"));
                Assert.That(testMzMl.run.spectrumList.spectrum.Length, Is.EqualTo(48));
                // Check that the spectrum is profile
                Assert.That(testMzMl.run.spectrumList.spectrum[0].cvParam.Any(c => c.accession == "MS:1000128"));

                Assert.That(testMzMl.run.chromatogramList.count, Is.EqualTo("1"));
                Assert.That(testMzMl.run.chromatogramList.chromatogram.Length, Is.EqualTo(1));

                Assert.That(testMzMl.run.chromatogramList.chromatogram[0].defaultArrayLength, Is.EqualTo(48));
            }

            File.Delete(Path.Combine(tempFilePath, "small.mzML"));
        }

        [Test]
        public void TestMSLevels()
        {
            // Get temp path for writing the test mzML
            var tempFilePath = Path.GetTempPath();

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW");
            var parseInput = new ParseInput(testRawFile, null, tempFilePath, OutputFormat.MzML);

            parseInput.MsLevel = new HashSet<int> { 1 };

            RawFileParser.Parse(parseInput);
            Assert.That(parseInput.Errors, Is.EqualTo(0));
            Assert.That(parseInput.Warnings, Is.EqualTo(0));

            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mzML")));
            // Deserialize the mzML file
            using (var fs = new FileStream(Path.Combine(tempFilePath, "small.mzML"), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            {
                var xmlSerializer = new XmlSerializer(typeof(mzMLType));
                var testMzMl = (mzMLType)xmlSerializer.Deserialize(fs);

                Assert.That(testMzMl.run.spectrumList.count, Is.EqualTo("14"));
                Assert.That(testMzMl.run.spectrumList.spectrum.Length, Is.EqualTo(14));

                Assert.That(testMzMl.run.chromatogramList.chromatogram[0].defaultArrayLength, Is.EqualTo(48));
            }

            File.Delete(Path.Combine(tempFilePath, "small.mzML"));
        }

        [Test]
        public void TestIndexedMzML()
        {
            // Get temp path for writing the test mzML
            var tempFilePath = Path.GetTempPath();

            Console.WriteLine(tempFilePath);

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW");
            var parseInput = new ParseInput(testRawFile, null, tempFilePath, OutputFormat.IndexMzML);

            RawFileParser.Parse(parseInput);
            Assert.That(parseInput.Errors, Is.EqualTo(0));
            Assert.That(parseInput.Warnings, Is.EqualTo(0));

            Assert.That(File.Exists(Path.Combine(tempFilePath, "small.mzML")));
            // Deserialize the mzML file
            using (var fs = new FileStream(Path.Combine(tempFilePath, "small.mzML"), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            {
                var xmlSerializer = new XmlSerializer(typeof(indexedmzML));
                var testMzMl = (indexedmzML)xmlSerializer.Deserialize(fs);

                Assert.That(testMzMl.mzML.run.spectrumList.count, Is.EqualTo("48"));
                Assert.That(testMzMl.mzML.run.spectrumList.spectrum.Length, Is.EqualTo(48));

                Assert.That(testMzMl.mzML.run.chromatogramList.count, Is.EqualTo("1"));
                Assert.That(testMzMl.mzML.run.chromatogramList.chromatogram.Length, Is.EqualTo(1));

                Assert.That(testMzMl.indexList.index.Length, Is.EqualTo(2));
                Assert.That(testMzMl.indexList.index[0].name.ToString(), Is.EqualTo("spectrum"));
                Assert.That(testMzMl.indexList.index[0].offset.Length, Is.EqualTo(48));
                Assert.That(testMzMl.indexList.index[1].name.ToString(), Is.EqualTo("chromatogram"));
                Assert.That(testMzMl.indexList.index[1].offset.Length, Is.EqualTo(1));
            }

            File.Delete(Path.Combine(tempFilePath, "small.mzML"));
        }

        [Test]
        public void TestMzML_MS2()
        {
            // Get temp path for writing the test mzML
            var tempFilePath = Path.GetTempPath();

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small2.RAW");
            var parseInput = new ParseInput(testRawFile, null, tempFilePath, OutputFormat.MzML);

            RawFileParser.Parse(parseInput);
            Assert.That(parseInput.Errors, Is.EqualTo(0));
            Assert.That(parseInput.Warnings, Is.EqualTo(0));

            Assert.That(File.Exists(Path.Combine(tempFilePath, "small2.mzML")));
            // Deserialize the mzML file
            using (var fs = new FileStream(Path.Combine(tempFilePath, "small2.mzML"), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            {
                var xmlSerializer = new XmlSerializer(typeof(mzMLType));
                var testMzMl = (mzMLType)xmlSerializer.Deserialize(fs);

                Assert.That(testMzMl.run.spectrumList.spectrum.Length, Is.EqualTo(95));

                var precursor = testMzMl.run.spectrumList.spectrum[16].precursorList.precursor[0].selectedIonList.selectedIon[0];
                var selectedMz = Double.Parse(precursor.cvParam.Where(cv => cv.accession == "MS:1000744").First().value);
                Assert.That(selectedMz - 604.7592 < 0.001);

                var selectedZ = int.Parse(precursor.cvParam.Where(cv => cv.accession == "MS:1000041").First().value);
                Assert.That(selectedZ, Is.EqualTo(2));

                var selectedI = Double.Parse(precursor.cvParam.Where(cv => cv.accession == "MS:1000042").First().value);
                Assert.That(selectedI - 17044 < 1);

                Assert.That(testMzMl.run.chromatogramList.chromatogram[0].defaultArrayLength, Is.EqualTo(95));
            }

            File.Delete(Path.Combine(tempFilePath, "small2.mzML"));
        }

        [Test]
        public void TestParquetCentroid()
        {
            // Get temp path for writing the test output
            var tempFilePath = Path.GetTempPath();

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW");
            var parseInput = new ParseInput(testRawFile, null, tempFilePath, OutputFormat.Parquet);

            RawFileParser.Parse(parseInput);
            Assert.That(parseInput.Errors, Is.EqualTo(0));
            Assert.That(parseInput.Warnings, Is.EqualTo(0));

            // Actual test
            var parquetFilePath = Path.Combine(tempFilePath, "small.mzparquet");

            Assert.That(File.Exists(parquetFilePath));
            using (var parquetReader = ParquetReader.CreateAsync(parquetFilePath).Result)
            {
                var groupReader = parquetReader.OpenRowGroupReader(0);
                var schema = parquetReader.Schema;
                var scanColumn = groupReader.ReadColumnAsync(schema.FindDataField("scan")).Result;

                Assert.That(scanColumn.NumValues, Is.EqualTo(48520));
                Assert.That(scanColumn.Statistics.DistinctCount, Is.EqualTo(48));
                Assert.That((from int p in scanColumn.Data where p == 22 select p).Count(), Is.EqualTo(1632));
            }

            File.Delete(parquetFilePath);
        }

        [Test]
        public void TestParquetProfile()
        {
            // Get temp path for writing the test mzML
            var tempFilePath = Path.GetTempPath();

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW");
            var parseInput = new ParseInput(testRawFile, null, tempFilePath, OutputFormat.Parquet);
            parseInput.NoPeakPicking = new HashSet<int> { 1, 2 };

            RawFileParser.Parse(parseInput);
            Assert.That(parseInput.Errors, Is.EqualTo(0));
            Assert.That(parseInput.Warnings, Is.EqualTo(0));

            // Actual test
            var parquetFilePath = Path.Combine(tempFilePath, "small.mzparquet");

            Assert.That(File.Exists(parquetFilePath));
            using (var parquetReader = ParquetReader.CreateAsync(parquetFilePath).Result)
            {
                var groupReader = parquetReader.OpenRowGroupReader(0);
                var schema = parquetReader.Schema;
                var scanColumn = groupReader.ReadColumnAsync(schema.FindDataField("scan")).Result;

                Assert.That(scanColumn.NumValues, Is.EqualTo(305213));
                Assert.That(scanColumn.Statistics.DistinctCount, Is.EqualTo(48));
                Assert.That((from int p in scanColumn.Data where p == 22 select p).Count(), Is.EqualTo(17758));
            }

            File.Delete(parquetFilePath);
        }

        [TestCase(false, 48520, 1632)]
        [TestCase(true, 305213, 17758)]
        public void TestBinarySoa(bool profile, int expectedPeakCount, int expectedScan22PeakCount)
        {
            var tempFilePath = Path.GetTempPath();

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Data/small.RAW");
            var parseInput = new ParseInput(testRawFile, null, tempFilePath, OutputFormat.BinarySoa);
            if (profile)
            {
                parseInput.NoPeakPicking = new HashSet<int> { 1, 2 };
            }

            RawFileParser.Parse(parseInput);
            Assert.That(parseInput.Errors, Is.EqualTo(0));
            Assert.That(parseInput.Warnings, Is.EqualTo(0));

            var binaryFilePath = Path.Combine(tempFilePath, "small.rcia.bin");
            Assert.That(File.Exists(binaryFilePath));

            var records = ReadBinarySoaRecords(binaryFilePath);
            Assert.That(records.Count, Is.EqualTo(48));
            Assert.That(records.Sum(r => r.PeakCount), Is.EqualTo(expectedPeakCount));
            Assert.That(records.Single(r => r.ScanId == 22).PeakCount, Is.EqualTo(expectedScan22PeakCount));
            Assert.That(records.Any(r => r.MsOrder > 1 &&
                                         r.MasterScanNumber > 0 &&
                                         !float.IsNaN(r.IsolationLower) &&
                                         !float.IsNaN(r.IsolationUpper) &&
                                         r.IsolationLower < r.IsolationUpper));

            File.Delete(binaryFilePath);
        }

        private struct BinarySoaRecord
        {
            public int ScanId;
            public int MsOrder;
            public int PeakCount;
            public float IsolationLower;
            public float IsolationUpper;
            public int MasterScanNumber;
        }

        private static List<BinarySoaRecord> ReadBinarySoaRecords(string path)
        {
            var records = new List<BinarySoaRecord>();

            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                Assert.That(br.ReadBytes(8), Is.EqualTo(new byte[] { 0x52, 0x43, 0x49, 0x41, 0x53, 0x54, 0x52, 0x31 }));
                Assert.That(br.ReadUInt16(), Is.EqualTo(1));
                Assert.That(br.ReadUInt16(), Is.EqualTo(32));
                br.ReadBytes(20);

                while (true)
                {
                    var header = br.ReadBytes(128);
                    var recordSize = BitConverter.ToUInt32(header, 0);
                    if (recordSize == 0)
                    {
                        break;
                    }

                    records.Add(new BinarySoaRecord
                    {
                        ScanId = (int)BitConverter.ToUInt32(header, 4),
                        MsOrder = (sbyte)header[8],
                        PeakCount = (int)BitConverter.ToUInt32(header, 12),
                        IsolationLower = BitConverter.ToSingle(header, 48),
                        IsolationUpper = BitConverter.ToSingle(header, 52),
                        MasterScanNumber = BitConverter.ToInt32(header, 100)
                    });

                    fs.Seek(recordSize - 128, SeekOrigin.Current);
                }
            }

            return records;
        }
    }
}

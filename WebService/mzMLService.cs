/*******************************************************************************
  Copyright 2015-2017 Yaroslav Lyutvinskiy <Yaroslav.Lyutvinskiy@ki.se> and 
  Roland Nilsson <Roland.Nilsson@ki.se>
 
  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

  http://www.apache.org/licenses/LICENSE-2.0

  Unless required by applicable law or agreed to in writing, software
  distributed under the License is distributed on an "AS IS" BASIS,
  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
  See the License for the specific language governing permissions and
  limitations under the License.
 
 *******************************************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ionic.Zlib;
using System.Text;
using System.Xml;


namespace mzAccess {
    public class mzMLFile : IMSFile {

        class mzMLSpectrum {
            public int ScanNumber;
            public double RT;
            public int MSOrder;
            public double ParentMass;
            public long Offset;
            public long mzBinaryOffset;
            public long mzBinaryLen;
            public long intBinaryOffset;
            public long intBinaryLen;
            public bool Profile;
            public bool mzBit64;
            public bool mzGziped;
            public bool intBit64;
            public bool intGziped;
            public string ID = null;
            public string Filter = null;

            public mzMLSpectrum() { }

            public mzMLSpectrum(int SN, double RT) {
                this.ScanNumber = SN;
                this.RT = RT;
            }

            public class byRT:IComparer<mzMLSpectrum>{
                public int Compare(mzMLSpectrum x, mzMLSpectrum y){
                    if (x.RT > y.RT) { return 1;} 
                    if (x.RT < y.RT) { return -1;} 
                    return 0;
                }
            }
            public class bySN:IComparer<mzMLSpectrum>{
                public int Compare(mzMLSpectrum x, mzMLSpectrum y){
                    if (x.ScanNumber > y.ScanNumber) { return 1;} 
                    if (x.ScanNumber < y.ScanNumber) { return -1;} 
                    return 0;
                }
            }

        }

        List<mzMLSpectrum> Spectra = new List<mzMLSpectrum>();

        string FileName;
        XmlTextReader Reader;
        FileStream MLStream;

        double LowestMass = double.PositiveInfinity;
        double HighestMass = 0.0;
        bool AllProfile = true;
        bool AllCentroided = true;
        

        public mzMLFile(string FileName) {
            this.FileName = FileName;
            MLStream = File.OpenRead(FileName);
            Reader = new XmlTextReader(FileName);
            Reader.WhitespaceHandling = System.Xml.WhitespaceHandling.None;
            bool Indexed = false;
            int SpectraCount = 0;
            string str = null;
            long IndexOffset = 0;

            //Check for index and Number of spectra 
            while(Reader.Read()) {
                if(Reader.Name == null)
                    continue;
                if(Reader.Name == "indexedmzML") {
                    Indexed = true;
                    break;
                }
                if(Reader.Name == "spectrumList" ) { //break condition
                    //while(Reader.MoveToNextAttribute()) {
                    //    if(Reader.Name == "count") {
                    //        SpectraCount = int.Parse(Reader.Value);
                    //        break;
                    //    }
                    //}
                    break;
                }
            }
            //if indexed - trying to read index
            if(Indexed) {
                Byte[] ReadBuffer = new Byte[512];
                MLStream.Seek(-512, SeekOrigin.End);
                MemoryStream MStream = new MemoryStream();
                MLStream.Read(ReadBuffer, 0, 512);
                MStream.Write(ReadBuffer, 0, 512);
                MStream.Position = 0;
                StreamReader MReader = new StreamReader(MStream);
                do { //does it make sence here to use temporary XMLReader?
                    str = MReader.ReadLine();
                    if(str.Contains("<indexListOffset>")) {
                        str = str.Substring(str.IndexOf(">") + 1);
                        str = str.Substring(0, str.IndexOf("<"));
                        IndexOffset = long.Parse(str);
                        break;
                    }
                } while(!MReader.EndOfStream);
                MLStream.Seek(IndexOffset, SeekOrigin.Begin);
                ReadBuffer = new Byte[MLStream.Length - IndexOffset];
                MLStream.Read(ReadBuffer, 0, Convert.ToInt32(MLStream.Length - IndexOffset));
                MStream = new MemoryStream();
                MStream.Write(ReadBuffer, 0, ReadBuffer.Length);
                MStream.Position = 0;
                MReader = new StreamReader(MStream);
                MStream.Position = 0;
                Reader = new XmlTextReader(MStream);
                Reader.Read();
                bool IndexList = false;
                bool SpectrumIndex = false;
                mzMLSpectrum S = null;
                do {
                    if(Reader.NodeType == XmlNodeType.Element) {
                        if(Reader.Name == "indexList") {
                            IndexList = true;
                        }
                        if(IndexList && Reader.Name == "index") {
                            if(Reader.GetAttribute("name") == "spectrum") {
                                SpectrumIndex = true;
                            } else {
                                SpectrumIndex = false;
                            }
                        }
                        if(IndexList && SpectrumIndex && Reader.Name == "offset") {
                            S = new mzMLSpectrum();
                            S.ID = Reader.GetAttribute("idRef");
                        }
                    }
                    if(Reader.NodeType == XmlNodeType.Text && S != null) {
                        S.Offset = long.Parse(Reader.Value);
                    }
                    if (Reader.NodeType == XmlNodeType.EndElement) {
                        if(S != null) {
                            Spectra.Add(S);
                            S = null;
                        }
                        if(Reader.Name == "indexList")
                            break;
                    }
                } while(Reader.Read());
                SpectraCount = Spectra.Count;
            }

            if(!Indexed) {
                MLStream.Seek(0, SeekOrigin.Begin);
                long Pos = 0;
                while(Spectra.Count < SpectraCount) {
                    mzMLSpectrum S = new mzMLSpectrum();
                    S.Offset = NextTagPosition(MLStream, "<spectrum", Pos);
                    Pos = S.Offset;
                    if(Pos < 0)
                        throw (new RawFileException("Inconsistant mzML"));
                    Spectra.Add(S);
                }
            }

            MLStream.Seek(0, SeekOrigin.Begin);
            for (int i = 0 ; i < SpectraCount ; i++) {
                // Read chunk of XML
                byte[] Buffer = null; 
                MLStream.Seek(Spectra[i].Offset, SeekOrigin.Begin);
                int Len = Convert.ToInt32((i + 1 < SpectraCount) ? Spectra[i + 1].Offset - Spectra[i].Offset : IndexOffset - Spectra[i].Offset);
                Buffer = new byte[Len];
                MLStream.Read(Buffer, 0, Len);
                //Make XML reader of this chunk
                MemoryStream MStream = new MemoryStream();
                MStream.Write(Buffer, 0, Buffer.Length);
                MStream.Position = 0;
                MemoryStream MBStream = new MemoryStream();
                MBStream.Write(Buffer, 0, Buffer.Length);
                MBStream.Position = 0;
                Reader = new XmlTextReader(MStream);
                bool DataArrayFlag = false;
                bool Gzipped = false;
                bool Bit64 = false;
                bool intArray = false;
                bool mzArray = false;
                long arrayOffset = 0;

                while(Reader.Read()) {
                    if(Reader.NodeType == XmlNodeType.Element && Reader.Name == "spectrum") {
                        Spectra[i].ScanNumber = Convert.ToInt32(Reader.GetAttribute("index"));
                        Spectra[i].ID = Reader.GetAttribute("id");
                    }
                    if(Reader.NodeType == XmlNodeType.Element && Reader.Name == "precursorList" && Convert.ToInt32(Reader.GetAttribute("count"))>1) {
                        throw (new UnsupportedFeatureException(String.Format("Multiple precursors in mzML are not supported. Check Spectra ID: {0}",Spectra[i].ID)));
                    }
                    if(Reader.NodeType == XmlNodeType.Element && Reader.Name == "binaryDataArray") {
                        DataArrayFlag = true;
                        arrayOffset = NextTagPosition(MBStream,"<binary>",arrayOffset);
                    }
                    if(Reader.NodeType == XmlNodeType.EndElement && Reader.Name == "binaryDataArray") {
                        DataArrayFlag = false;
                        long StartOffset = arrayOffset;
                        arrayOffset = NextTagPosition(MBStream,"</binary>",arrayOffset);
                        if(!intArray && !mzArray)
                            continue;
                        //read array boundaries here 
                        if(intArray) {
                            Spectra[i].intGziped = Gzipped;
                            Spectra[i].intBit64 = Bit64;
                            Spectra[i].intBinaryOffset = Spectra[i].Offset + StartOffset + 8;
                            Spectra[i].intBinaryLen = arrayOffset - StartOffset - 8;
                            Gzipped = false; Bit64 = false; intArray = false;
                        }
                        if(mzArray) {
                            Spectra[i].mzGziped = Gzipped;
                            Spectra[i].mzBit64 = Bit64;
                            Spectra[i].mzBinaryOffset = Spectra[i].Offset + StartOffset + 8;
                            Spectra[i].mzBinaryLen = arrayOffset - StartOffset - 8;
                            Gzipped = false; Bit64 = false; mzArray = false;
                        }
                    }
                    if(Reader.NodeType == XmlNodeType.Element && Reader.Name == "cvParam") {
                        switch(Reader.GetAttribute("accession")) {
                        case "MS:1000579": { //Ms-only spectrum
                                Spectra[i].MSOrder = 1;
                                break;
                            }
                        case "MS:1000511": { //MS Level
                                Spectra[i].MSOrder = Convert.ToInt32(Reader.GetAttribute("value"));
                                break;
                            }
                        case "MS:1000128": { //Profile scan
                                Spectra[i].Profile = true;
                                break;
                            }
                        case "MS:1000127": { //Centroided scan
                                Spectra[i].Profile = false;
                                break;
                            }
                        case "MS:1000501": { //Lowest mass
                                LowestMass = Math.Min(LowestMass, Convert.ToDouble(Reader.GetAttribute("value")));
                                break;
                            }
                        case "MS:1000500": { //Highest mass
                                HighestMass = Math.Max(HighestMass, Convert.ToDouble(Reader.GetAttribute("value")));
                                break;
                            }
                        case "MS:1000512": { //Filter string
                                Spectra[i].Filter = Reader.GetAttribute("value");
                                break;
                            }
                        case "MS:1000016": { //Retention Time
                                if(Reader.GetAttribute("unitName") == "minute") {
                                    Spectra[i].RT = Convert.ToDouble(Reader.GetAttribute("value"));
                                    break;
                                }
                                if(Reader.GetAttribute("unitName") == "second") {
                                    Spectra[i].RT = Convert.ToDouble(Reader.GetAttribute("value"))/60.0;
                                    break;
                                }
                                throw (new RawFileException(String.Format("Inconsistant mzML. Unknown unit \"{0}\" for retention time",Reader.GetAttribute("unitName"))));
                            }
                        case "MS:1000744": { //precusor mass
                                Spectra[i].ParentMass = Convert.ToDouble(Reader.GetAttribute("value"));
                                break;
                            }
                        case "MS:1000521": { //32-bit data array 
                                if(DataArrayFlag) {
                                    Bit64 = false;
                                }
                                break;
                            }
                        case "MS:1000523": { //64-bit data array 
                                if(DataArrayFlag) {
                                    Bit64 = true;
                                }
                                break;
                            }
                        case "MS:1000574": { //zlib compression
                                if(DataArrayFlag) {
                                    Gzipped = true;
                                }
                                break;
                            }
                        case "MS:1000576": { //no compression
                                if(DataArrayFlag) {
                                    Gzipped = false;
                                }
                                break;
                            }
                        case "MS:1000514": { //mz binary array
                                if(DataArrayFlag) {
                                    mzArray = true;
                                }
                                break;
                            }
                        case "MS:1000515": { //intensity binary array
                                if(DataArrayFlag) {
                                    intArray = true;
                                }
                                break;
                            }
                        }
                    }
                    if(Reader.NodeType == XmlNodeType.EndElement && Reader.Name == "spectrum") {
                        if(Spectra[i].MSOrder == 1) {
                            AllProfile &= Spectra[i].Profile;
                            AllCentroided &= !Spectra[i].Profile;
                        }
                        break;
                    }
                }
            }
            Spectra.Sort((a, b) => a.RT.CompareTo(b.RT));
        }

        List<DataPoint> GetSpectraPoints(mzMLSpectrum S, double MinMZ, double MaxMZ) {
            //Convert base64 string (optionally gzipped) from mzML to binary stream of floating point numbers (for mz values)
            //read string from mzML
            MLStream.Seek(S.mzBinaryOffset,SeekOrigin.Begin);
            Byte[] mzBuffer = new Byte[S.mzBinaryLen];
            MLStream.Read(mzBuffer, 0, Convert.ToInt32(S.mzBinaryLen));
            //convert base 64 to binary bytes array
            mzBuffer = Convert.FromBase64String(Encoding.UTF8.GetString(mzBuffer));
            //stream will address binary numbers
            Stream mzStream = null;
            MemoryStream mmzStream = new MemoryStream(mzBuffer);
            mmzStream.Position = 0;
            if(S.mzGziped) {
                //it can be gzip stream based on memory stream
                mzStream = new ZlibStream(mmzStream,CompressionMode.Decompress);
            } else {
                //or just a memory stream
                mzStream = mmzStream;
            }

            //Convert base64 string (optionally gzipped) from mzML to binary stream of floating point numbers (for intensity values)
            MLStream.Seek(S.intBinaryOffset,SeekOrigin.Begin);
            Byte[] intBuffer = new Byte[S.intBinaryLen];
            MLStream.Read(intBuffer, 0, Convert.ToInt32(S.intBinaryLen));
            intBuffer = Convert.FromBase64String(Encoding.UTF8.GetString(intBuffer));
            Stream intStream = null;
            MemoryStream mintStream = new MemoryStream(intBuffer);
            mintStream.Position = 0;
            if(S.intGziped) {
                intStream = new ZlibStream(mintStream,CompressionMode.Decompress);
            } else {
                intStream = mintStream;
            }

            List<DataPoint> Res = new List<DataPoint>();
            Byte[] buf = new Byte[8];
            int mzLen = S.mzBit64 ? 8 : 4;
            int intLen = S.intBit64 ? 8 : 4;
            while(true) {
                if(mzStream.Read(buf, 0, mzLen) < mzLen) break;
                double MZ = S.mzBit64 ? BitConverter.ToDouble(buf, 0) : BitConverter.ToSingle(buf, 0);
                if(intStream.Read(buf, 0, intLen) < intLen) break;
                double Int = S.intBit64 ? BitConverter.ToDouble(buf, 0) : BitConverter.ToSingle(buf, 0);
                if (MZ>=MinMZ && MZ <= MaxMZ) {
                    DataPoint DP = new DataPoint();
                    DP.Mass = MZ;
                    DP.Intensity = (float)Int;
                    Res.Add(DP);
                }
            }
            return Res;
        }

        public void CloseFile() {
            Spectra.Clear();
            Reader.Close();
            MLStream.Close();
        }

        public double[] GetArea(double MZLow, double MZHigh, double StartRT, double EndRT, bool Profile = false) {
            if(AllCentroided && Profile) {
                throw new DataUnavailableException(String.Format("File {0} does not contain profile data", FileName));
            }
            if(AllProfile && !Profile) {
                throw new DataUnavailableException(String.Format("File {0} does not contain centroided data", FileName));
            }
            List<double> Res = new List<double>();
            foreach(mzMLSpectrum S in Spectra) {
                if(S.RT < StartRT || S.RT > EndRT || S.MSOrder > 1) continue;
                if(!S.Profile && Profile) {
                    throw new DataUnavailableException(String.Format("File: Spectrum: Spectrom contains only centroided data.",FileName,S.ID));
                }
                if(S.Profile && !Profile) {
                    throw new DataUnavailableException(String.Format("File: Spectrum: Spectrom contains only profile data.",FileName,S.ID));
                }
                List<DataPoint> DPs = GetSpectraPoints(S, MZLow, MZHigh);
                foreach(DataPoint DP in DPs) {
                    Res.Add(DP.Mass);
                    Res.Add(S.RT);
                    Res.Add(DP.Intensity);
                }
            }
            return Res.ToArray();
        }

        public double[] GetAveSpectrum(double MZLow, double MZHigh, double StartRT, double EndRT, bool Profile = false) {
            throw new UnsupportedFeatureException("mzML does not support averaging of spectra");
        }

        public FragmentationInfo[] GetFragmentationEvents(double MZLow, double MZHigh, double StartRT, double EndRT) {
            int StartScan = GetScanNumberFromRT(StartRT);
            int EndScan = GetScanNumberFromRT(EndRT);
            List<FragmentationInfo> ResList = new List<FragmentationInfo>();
            for (int i = StartScan ; i <=EndScan ; i++) {
                if(Spectra[i].MSOrder > 1 ) continue;
                if(Spectra[i].ParentMass < MZLow || Spectra[i].ParentMass > MZHigh ) continue;
                FragmentationInfo Info = new FragmentationInfo();
                Info.MSOrder = Spectra[i].MSOrder;
                Info.ParentMZ = Spectra[i].ParentMass;
                Info.RT = Spectra[i].RT;
                Info.Description = Spectra[i].ID == null ? "" : String.Format("ID = \"{0}\" ", Spectra[i].ID); //ID, Filter, Precursors
                Info.Description += Spectra[i].Filter == null ? "" : String.Format("Filter: \"{0}\" ", Spectra[i].Filter);
                Info.Description += "Parent mass: " + Spectra[i].ParentMass.ToString();
                Info.ScanNumber = Spectra[i].ScanNumber;
            }
            if(ResList.Count > 0) {
                return ResList.ToArray();
            } else {
                return null;
            }
        }

        public double[] GetMassRange() {
            return new double[] {
                LowestMass, HighestMass
            };
        }

        public double GetRTFromScanNumber(int ScanNumber) {
            return Spectra[Spectra.BinarySearch(new mzMLSpectrum(ScanNumber,0.0), new mzMLSpectrum.bySN())].RT;
        }

        public double[] GetRTRange() {
            return new double[] {
                Spectra[0].RT,
                Spectra[Spectra.Count-1].RT
            };
        }

        public int GetScanNumberFromRT(double RT) {
            int Index = Spectra.BinarySearch(new mzMLSpectrum(0, RT), new mzMLSpectrum.byRT());
            if (Index < 0) Index = ~Index;
            if (Index == Spectra.Count) Index = Spectra.Count - 1;
            while(Spectra[Index].MSOrder > 1) Index--;
            return Spectra[Index].ScanNumber;
        }

        public double[] GetSpectrum(double MZLow, double MZHigh, int ScanNumber, bool Profile = false) {
            List<double> Res = new List<double>();
            mzMLSpectrum S = Spectra[Spectra.BinarySearch(new mzMLSpectrum(ScanNumber, 0.0), new mzMLSpectrum.bySN())];
            if(!S.Profile && Profile) {
                throw new DataUnavailableException(String.Format("File: Spectrum: Spectrom contains only centroided data.",FileName,S.ID));
            }
            if(S.Profile && !Profile) {
                throw new DataUnavailableException(String.Format("File: Spectrum: Spectrom contains only profile data.",FileName,S.ID));
            }
            List<DataPoint> DPs = GetSpectraPoints(S, MZLow, MZHigh);
            foreach(DataPoint DP in DPs) {
                Res.Add(DP.Mass);
                Res.Add(DP.Intensity);
            }
            return Res.ToArray();
        }

        public double[] GetTrace(double MZLow, double MZHigh, double RTLow, double RTHigh) {
            List<double> Res = new List<double>();
            double Margin = 0.0;
            foreach(mzMLSpectrum S in Spectra) {
                if(S.RT < RTLow || S.RT > RTHigh || S.MSOrder > 1)
                    continue;
                Margin = S.Profile ? 1.0 : 0.0;
                List<DataPoint> DPs = GetSpectraPoints(S, MZLow-Margin, MZHigh+Margin);
                if(DPs.Count == 0) {
                    Res.Add(S.RT);
                    Res.Add(0.0);
                    continue;
                }
                double Max = 0.0;
                foreach(DataPoint DP in DPs) {
                    if(DP.Intensity > Max && DP.Mass >= MZLow && DP.Mass <= MZHigh) {
                        Max = DP.Intensity;
                    }
                }
                if (Max == 0.0 && S.Profile) { //approximation
                    for (int i = 0 ; i < DPs.Count-1 ; i++) {
                        if (DPs[i].Mass<MZLow && DPs[i+1].Mass > MZHigh) {
                            double LowInt = DPs[i].Intensity + (DPs[i + 1].Intensity - DPs[i].Intensity) * ((MZLow - DPs[i].Mass) / (DPs[i + 1].Mass - DPs[i].Mass));
                            double HighInt = DPs[i].Intensity + (DPs[i + 1].Intensity - DPs[i].Intensity) * ((MZHigh - DPs[i].Mass) / (DPs[i + 1].Mass - DPs[i].Mass));
                            Max = Math.Max(LowInt, HighInt);
                            break;
                        }
                    }
                }
                Res.Add(S.RT);
                Res.Add(Max);
            }
            UtilityFunctions.ClearZeroes(Res, 0.0, 0.0, false);
            return Res.ToArray();
        }

        public long NextTagPosition(Stream S, string Tag, long Offset) {
            S.Position = Offset;
            int Counter = 0;
            int OffsetLen = Tag.Length;
            while(Counter<OffsetLen) {
                int b = S.ReadByte();
                if(b < 0)
                    return -1;
                char ch = Convert.ToChar(b);
                if(ch == Tag[Counter])
                    Counter++;
                else
                    Counter = 0;
            }
            return S.Position - Counter;
        }

    }
}

//TO DO: for spectra description string
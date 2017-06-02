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
using Agilent.MassSpectrometry.DataAnalysis;


namespace mzAccess
{
    /// <summary>
    /// Agilent MHDAC does not provide fast RT to ScanNumber interface 
    /// therefore scan information need to be stored inside of AgilentMSFile class 
    /// Scan Info class provides such a storage for cached RT to Scan information 
    /// </summary>
    struct ScanInfo{
        public int ScanNumber;
        public double RT;
        public double MinMass;
        public double MaxMass;
        public bool MSOnly;
        public ScanInfo(int _Scan, double _RT, bool _MSOnly){
            ScanNumber = _Scan;
            RT = _RT;
            MSOnly = _MSOnly;
            MinMass = 0.0;
            MaxMass = 0.0;
        }
        public ScanInfo(int _Scan, double _RT){
            ScanNumber = _Scan;
            RT = _RT;
            MSOnly = false;
            MinMass = 0.0;
            MaxMass = 0.0;
        }

        public class byRT:IComparer<ScanInfo>{
            public int Compare(ScanInfo x, ScanInfo y){
                if (x.RT > y.RT) { return 1;} 
                if (x.RT < y.RT) { return -1;} 
                return 0;
            }
        }
        public class bySN:IComparer<ScanInfo>{
            public int Compare(ScanInfo x, ScanInfo y){
                if (x.ScanNumber > y.ScanNumber) { return 1;} 
                if (x.ScanNumber < y.ScanNumber) { return -1;} 
                return 0;
            }
        }
    }

    /// <summary>
    /// IMSFile implementation for Agilent .d files
    /// </summary>
    public class AgilentMSFile : IMSFile{
        //full file name 
        public string FileName;
        // MHDAC data reader interface to keep it open
        IMsdrDataReader MSReader;
        List<ScanInfo> Scans = new List<ScanInfo>();

        public AgilentMSFile(string FileName){
            this.FileName = FileName;
            BDADataAccess DA = new BDADataAccess();
            IBDADataAccess IDA = DA;
            IDA.OpenDataFile(FileName);
            IBdaMsScanRecordCollection SRList = IDA.GetScanRecordsInfo(MSScanType.AllMS);
            MassSpecDataReader Reader = new MassSpecDataReader();
            MSReader = Reader;
            MSReader.OpenDataFile(FileName);
            //collect scan info
            for (int i = 0 ; i < MSReader.MSScanFileInformation.TotalScansPresent; i++){
                IMSScanRecord ScanRecord =  MSReader.GetScanRecord(i);
                ScanInfo SI = new ScanInfo(i, ScanRecord.RetentionTime,ScanRecord.MSLevel!=MSLevel.MSMS);
                Scans.Add(SI);
                IDA.GetScanRecord(ScanRecord.ScanID);
                IBdaMsScanRecInfo ScanRecInfo;
                if (SRList.TryGetValue(ScanRecord.ScanID, out ScanRecInfo)){;
                    SI.MinMass = ScanRecInfo.MeasuredMassRange.Start;
                    SI.MaxMass = ScanRecInfo.MeasuredMassRange.End;
                }
            }
            IDA.CloseDataFile();
        }

        //IMSFile.GetTrace Implementation
        public double[] GetTrace(double MZLow, double MZHigh, double RTLow, double RTHigh){
            IBDAChromFilter Filter = new BDAChromFilter();
            Filter.ChromatogramType = ChromType.ExtractedIon;
            Filter.DoCycleSum = false;
            Filter.IncludeMassRanges = new IRange[1];
            Filter.IncludeMassRanges[0] = new MinMaxRange(MZLow,MZHigh);
            Filter.ScanRange = new MinMaxRange(RTLow, RTHigh);
            Filter.MSLevelFilter = MSLevel.MS;
            IBDAChromData XIC = MSReader.GetChromatogram(Filter)[0]; //20 msec
            double[] Res = new double[XIC.TotalDataPoints*2];
            for (int i = 0 ; i<XIC.TotalDataPoints ; i++){
                Res[i * 2] = XIC.XArray[i];
                Res[i * 2 + 1] = XIC.YArray[i];
            }
            return Res;
        }

        //IMSFile.GetSpectrum Implementation
        public double[] GetSpectrum(double MZLow, double MZHigh, int ScanNumber, bool Profile = false){
            IBDASpecData SpecData = MSReader.GetSpectrum(ScanNumber,null,null,Profile?DesiredMSStorageType.Profile:DesiredMSStorageType.Peak); 
            return TrimAndFormat(SpecData,MZLow,MZHigh);
        }

        //IMSFile.GetAveSpectrum Implementation
        public double[] GetAveSpectrum(double MZLow, double MZHigh, double StartRT, double EndRT, bool Profile = false){
            IBDASpecFilter Filter = new BDASpecFilter();
            Filter.AverageSpectrum = true;
            Filter.MassRange = new MinMaxRange(MZLow,MZHigh);
            IRange ScanRange = new MinMaxRange(StartRT,EndRT);
            Filter.ScanRange = new IRange[1];
            Filter.ScanRange[0] = ScanRange;
            Filter.DesiredMSStorageType = Profile ? DesiredMSStorageType.Profile : DesiredMSStorageType.Peak;
            Filter.SpectrumType = SpecType.TofMassSpectrum;
            Filter.MSLevelFilter = MSLevel.MS;
            Filter.MSScanTypeFilter = MSScanType.AllMS;
            IBDASpecData[] SpecData = MSReader.GetSpectrum(Filter); 
            return TrimAndFormat(SpecData[0],MZLow,MZHigh);
        }

        //IMSFile.GetArea Implementation
        public double[] GetArea(double MZLow, double MZHigh, double StartRT, double EndRT, bool Profile = false){
            int StartScan = GetScanNumberFromRT(StartRT);
            int EndScan = GetScanNumberFromRT(EndRT);
            List<IBDASpecData> Specs = new List<IBDASpecData>();
            IRange[] MassRange = new IRange[1];
            MassRange[0] = new MinMaxRange(MZLow, MZHigh);
            for (int i = StartScan; i <= EndScan; i++){
                IBDASpecData SpecData = MSReader.GetSpectrum(i, null, null, Profile ? DesiredMSStorageType.Profile : DesiredMSStorageType.Peak);
                if(SpecData.MSLevelInfo == MSLevel.MS) {
                    SpecData.TrimXRange(MassRange, false);
                    Specs.Add(SpecData);
                }
            }
            //clear zeroes
            List<List<double>> Cleaned = new List<List<double>>();
            for(int i = 0 ; i < Specs.Count ; i++){
                List<double> SData = new List<double>();
                for (int j = 0; j < Specs[i].XArray.Length; j++){
                    SData.Add(Specs[i].XArray[j]);
                    SData.Add(Specs[i].YArray[j]);
                }
                ClearZeroes(SData, MZLow, MZHigh, Profile);
                Cleaned.Add(SData);
            }
            //number of spectra
            int PointCount=0;
            for(int i = 0 ; i < Specs.Count ; i++){
                PointCount += Cleaned[i].Count;
            }
            double[] Res = new double[(PointCount/2)*3];
            int Counter = 0;
            for (int i = 0; i < Cleaned.Count ; i++){
                double RT = GetRTFromScanNumber(i+StartScan);
                for (int j = 0; j < Cleaned[i].Count; j+=2){
                    Res[Counter * 3] = Cleaned[i][j];
                    Res[Counter * 3 + 1] = RT;
                    Res[Counter * 3 + 2] = Cleaned[i][j+1];
                    Counter++;
                }
            }
            return Res;
        }


        //IMSFile.GetScanNumberFromRT Implementation
        public int GetScanNumberFromRT(double RT)
        {
            int Index = Scans.BinarySearch(new ScanInfo(0, RT), new ScanInfo.byRT());
            if (Index < 0) Index = ~Index;
            if (Index == Scans.Count) Index = Scans.Count - 1;
            while(!Scans[Index].MSOnly) Index--;
            return Scans[Index].ScanNumber;
        }
            
        //IMSFile.GetRTFromScanNumber Implementation
        public double GetRTFromScanNumber(int ScanNumber){
            return Scans[Scans.BinarySearch(new ScanInfo(ScanNumber,0.0),new ScanInfo.bySN())].RT;
        }

        //IMSFile.CloseFile Implementation
        public void CloseFile(){
            MSReader.CloseDataFile();
        }

        //IMSFile.GetMassRange Implementation
        public double[] GetMassRange(){
            return new double[] {
                MSReader.FileInformation.MSScanFileInformation.MzScanRangeMinimum,
                MSReader.FileInformation.MSScanFileInformation.MzScanRangeMaximum
            };
        }

        //IMSFile.GetFragmentationEvents Implementation
        public FragmentationInfo[] GetFragmentationEvents(double MZLow, double MZHigh, double StartRT, double EndRT) {
            int StartScan = GetScanNumberFromRT(StartRT);
            int EndScan = GetScanNumberFromRT(EndRT);
            List<FragmentationInfo> ResList = new List<FragmentationInfo>();
            for (int i = StartScan ; i <=EndScan ; i++) {
                IMSScanRecord ScanRecord =  MSReader.GetScanRecord(i);
                if(ScanRecord.MSLevel != MSLevel.MSMS) continue;
                FragmentationInfo Info = new FragmentationInfo();
                Info.MSOrder = 2;
                Info.ParentMZ = ScanRecord.MZOfInterest;
                Info.RT = ScanRecord.RetentionTime;
                Info.Description = String.Format("MSMS Spectrum to Mass = {0}; RT = {1}",Info.ParentMZ,Info.RT);
                Info.ScanNumber = i;
                if (Info.ParentMZ >= MZLow && Info.ParentMZ<=MZHigh)
                    ResList.Add(Info);
            }
            if(ResList.Count > 0) {
                return ResList.ToArray();
            } else {
                return null;
            }
        }


        //IMSFile.GetRTRange Implementation
        public double[] GetRTRange(){
            return new double[] {
                Scans[0].RT,
                Scans[Scans.Count-1].RT
            };
        }

        //Service functions

        //This function convert given spectral data IBDASpecData SpecData to double[] expected by IMSFile definition 
        //if neccessary it can trim data to [MZLow;MZHigh] boundaries
        static double[] TrimAndFormat(IBDASpecData SpecData, double MZLow, double MZHigh){
            int StartIndex = 0; 
            int EndIndex = 0;
            for(int i = 0; i<SpecData.TotalDataPoints;i++){
                if (SpecData.XArray[i]<MZLow){
                    StartIndex = i;
                }
                if (SpecData.XArray[i]>MZHigh){
                    EndIndex = i-1;
                    break;
                }
            }
            if (EndIndex == 0){
                EndIndex = SpecData.TotalDataPoints - 1;
            }
            StartIndex++;
            double[] Res = new double[(EndIndex-StartIndex+1)*2];
            for (int i = StartIndex; i<=EndIndex;i++){
                Res[(i - StartIndex) * 2] = SpecData.XArray[i];
                Res[(i - StartIndex) * 2 + 1] = SpecData.YArray[i];
            }
            return Res;
        }

        //Clean list out of sequencial zeroes (used for chromatograms)
        //if necessary (SizeZeroes==true) it can add leading and trailing zeroes to array at Low and High mass points 
        static public int ClearZeroes(List<double> DList, double Low, double High, bool SizeZeroes){
            if (SizeZeroes){
                if(DList[0]>Low && DList[1] == 0.0){
                    DList.Insert(0, Low);
                    DList.Insert(1, 0.0);
                }
                if(DList[DList.Count-2]<High && DList[DList.Count-1] == 0.0){
                    DList.Add(High);
                    DList.Add(0.0);
                }
            }
            for (int i=DList.Count - 2 ; i > 0 ; i-=2){
                if (DList[i+1] == 0.0 && DList[i - 1] == 0.0 && DList[i + 3] == 0.0) {
                    DList.RemoveAt(i+1);
                    DList.RemoveAt(i);
                }
            }
            return DList.Count;
        }


    }

}
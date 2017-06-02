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
 /*
 
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace mzAccess {

    //class represents cache file built for complete folder
    //it keeps folder cache infomation and performs reading operations for RCH1MSFile class
    //it fabriques RCH1MSFile and RFEntry classes for folder cache
    //also it serves directly pooled requests
    public class DirCache {

        //Global mass index
        List<double> _MassIndex = null;
        //mass index accessor - load mass index on demand
        public List<double> MassIndex {
            get {
                lock (MassLock) {
                    if(_MassIndex == null) {
                        BuildMassIndex();
                    }
                    return _MassIndex;
                }
            }
        }

        object MassLock = new object();
        //FileName/FileID pairs

        //single file record from folder.cache
        public class FileRec {
            public string FileName; //origanal file name
            public int FileID;      //file number inside of folder cache
            public long RTOffset;   //file offset in folder cahe for spectra RT records
            public int SpectraCount;//number of spectra
        }

        //file records - always filled 
        public List<FileRec> FileRecs = new List<FileRec>();

        //full file name to read
        private string FileName;
        //file offset for mass index
        private long StartMassPosition;
        //file offset for first data points page 
        private long StartPagePosition;
        //Last access time
        public DateTime LastUsed = DateTime.Now;

        //constructor - loads global information on folder cache
        public DirCache(string FileName) {
            this.FileName = FileName;
            BinaryReader sr = 
               new BinaryReader(File.Open(FileName,FileMode.Open,FileAccess.Read,FileShare.Read),new ASCIIEncoding());
            //Read signature - currently RCH1
            string sig = sr.ReadString();
            int FileCount = sr.ReadInt32();
            for (int i = 0 ; i < FileCount ; i++) {
                FileRec FR = new FileRec();
                FR.FileID = sr.ReadInt32();
                FR.FileName = sr.ReadString();
                FR.FileName = Path.GetFileNameWithoutExtension(FR.FileName);
                FR.SpectraCount = sr.ReadInt32();
                FR.RTOffset = sr.BaseStream.Position;
                sr.BaseStream.Seek(FR.SpectraCount * 12,SeekOrigin.Current);
                FileRecs.Add(FR);
            }
            StartMassPosition = sr.BaseStream.Position;
            int PageCount = sr.ReadInt32();
            sr.BaseStream.Seek(PageCount * 8, SeekOrigin.Current);
            StartPagePosition = sr.BaseStream.Position;
            sr.Close();
        }

        //on deactivate - free mass index (resource release)
        public void Deactivate() {
            lock (MassLock) {
                _MassIndex = null;
            }
        }

        //Get dictionary of RT and ScanNumber for spectra of particular file
        public SortedList<int, double> GetRTs(string FileName) {
            FileRec FR = FileRecs.First(REC => REC.FileName == FileName);
            SortedList<int, double> Res = new SortedList<int, double>();
            BinaryReader sr = 
               new BinaryReader(File.Open(this.FileName,FileMode.Open,FileAccess.Read,FileShare.Read),new ASCIIEncoding());
            sr.BaseStream.Seek(FR.RTOffset, SeekOrigin.Begin);
            for(int i = 0 ; i < FR.SpectraCount ; i++) {
                Res.Add(sr.ReadInt32(), sr.ReadDouble());
            }
            sr.Close();
            return Res;
        }

        //Accessor for RCHEntry List originated from folder cache
        public List<RCHEntry> Files {
            get {
                List<RCHEntry> Ents = new List<RCHEntry>();
                foreach(FileRec FR in FileRecs) {
                    RCHEntry Ent = new RCHEntry(FileName);
                    Ent.Cache = this;
                    Ent.FileName = FR.FileName;
                    Ents.Add(Ent);
                }
                return Ents;
            }
        }

        //creates MS service file for one of the files combined in folder cache 
        //RCH1MSFile object then provide full interface IMSFile
        public RCH1MSFile GetFile(string FileName) {
            RCH1MSFile RCH = new RCH1MSFile();
            RCH.JoinedCache = this;
            RCH.sr = new BinaryReader(File.Open(this.FileName,FileMode.Open,FileAccess.Read,FileShare.Read),new ASCIIEncoding());
            RCH.RTs = GetRTs(FileName);
            RCH.FileName = FileName;
            return RCH;
        }

        //Loads mass index to RAM
        private void BuildMassIndex() {
            BinaryReader sr = 
               new BinaryReader(File.Open(FileName,FileMode.Open,FileAccess.Read,FileShare.Read),new ASCIIEncoding());
            sr.BaseStream.Seek(StartMassPosition, SeekOrigin.Begin);
            int PageCount = sr.ReadInt32();
            _MassIndex = new List<double>();
            for (int i = 0 ; i < PageCount ; i++) {
                _MassIndex.Add(sr.ReadDouble());
            }
            sr.Close();
        }


        //Gets data points from file 
        //this function is a basis for IMSFile functions implemented in RCH1MSFile class
        //RCH1MSFile have to provide its own stream reader opened to folder cache since there can be multiple reading operation in paralel 
        public List<DataPoint> GetPoints(string FileName, BinaryReader Reader, double MZLow, double MZHigh, int StartScan, int EndScan) {
            FileRec FR = FileRecs.First(REC => REC.FileName == FileName);
            long LowPos = MassIndex.BinarySearch(MZLow);
            if(LowPos < 0)
                LowPos = (~LowPos) - 1;
            //for the first page 
            if(LowPos < 0)
                LowPos = 0;
            long HighPos = MassIndex.BinarySearch(MZHigh);
            if(HighPos < 0)
                HighPos = (~HighPos) - 1;
            //for the first page 
            if(HighPos < 0)
                HighPos = 0;
            List<DataPoint> Points = new List<DataPoint>();
            Reader.BaseStream.Seek(StartPagePosition + LowPos * 64000, SeekOrigin.Begin);
            long FileLen = Reader.BaseStream.Length;

            for(long i = LowPos ; i <= HighPos ; i++) {
                long Limit = (Math.Min(64000, (FileLen - StartPagePosition) - i * 64000)) / 20;
                byte[] Pbyte = Reader.ReadBytes((int)(Limit * 20)); 
                DataPoint P = new DataPoint();
                for(int j = 0 ; j < Limit ; j++) {
                    P.Mass = BitConverter.ToDouble(Pbyte,j*20);
                    P.Intensity = BitConverter.ToSingle(Pbyte,(j*20)+8);
                    P.Scan = BitConverter.ToInt32(Pbyte,(j*20)+12);
                    int FileID = BitConverter.ToInt32(Pbyte,(j*20)+16);
                    //P.RT = RTs[P.Scan]; - there is no RTs locally
                    if(FileID == FR.FileID && P.Scan >= StartScan && P.Scan <= EndScan && P.Mass >= MZLow && P.Mass <= MZHigh) {
                        Points.Add(P);
                        P = new DataPoint();
                    }
                }
            }
            Points.Sort();
            return Points;
        }


        //Gets data points from file when serves for multiple pooled requests 
        //DirCache have to provide datapoints for incapsulated files and do nothing with a rest 
        public void GetPointsPooled(List<MSDataService.PoolAreaReq> Pool) {
            List<int> PoolIndexes = new List<int>();
            List<double> LowMZs = new List<double>();
            List<double> HighMZs = new List<double>();
            //Merge lists
            for(int i = 0 ; i < Pool.Count ; i++) {
                if(Pool[i].Completed)
                    continue;
               //List can contain queries to multiple folders
               //here queries to current folders are selected 
                FileRec PR =FileRecs.FirstOrDefault(fr => fr.FileName == Pool[i].RFEntry.FileName);
                if (PR != null) {
                    PoolIndexes.Add(i);
                    int j = 0;
                    //if mass intervals are the same for different queries they will be count only once
                    for(; j < LowMZs.Count ; j++) {
                        if(Pool[i].MZHigh < LowMZs[j] || Pool[i].MZLow > HighMZs[j])
                            continue;
                        if(Pool[i].MZLow < LowMZs[j]) 
                            LowMZs[j] = Pool[i].MZLow;
                        if(Pool[i].MZHigh > HighMZs[j])
                            HighMZs[j] = Pool[i].MZHigh;
                        break;
                    }
                    if (j == LowMZs.Count) {
                        LowMZs.Add(Pool[i].MZLow);
                        HighMZs.Add(Pool[i].MZHigh);
                    }
                    //Calc FileIDs and Scans for Pool
                    //note RFEntry is a complete RCH1MSFile with all functions avialable
                    Pool[i].StartScan = Pool[i].RFEntry.MSFile.GetScanNumberFromRT(Pool[i].RTLow);
                    Pool[i].EndScan = Pool[i].RFEntry.MSFile.GetScanNumberFromRT(Pool[i].RTHigh);
                    Pool[i].FileID = PR.FileID;
                    Pool[i].Points = new List<DataPoint>();
                }
            }
            if(LowMZs.Count == 0)
                return;
            //join intersecting mass intervals
            LowMZs.Sort();
            HighMZs.Sort();
            for(int i = LowMZs.Count - 1 ; i > 0 ; i--) {
                if(LowMZs[i] > HighMZs[i - 1])
                    continue;
                LowMZs[i - 1] = Math.Min(LowMZs[i], LowMZs[i - 1]);
                HighMZs[i - 1] = Math.Max(HighMZs[i], HighMZs[i - 1]);
                LowMZs.RemoveAt(i);
                HighMZs.RemoveAt(i);
            }
            //Calc FileIDs and Scans for Pool
            //2D List AreaPools Outer list is grouped and sorted by mass interval, internal list is sorted by FileIDs
            List<List<MSDataService.PoolAreaReq>> AreaPools = new List<List<MSDataService.PoolAreaReq>>();
            MSDataService.PoolAreaReq.byFileID PoolCompaprer = new MSDataService.PoolAreaReq.byFileID();
            MSDataService.PoolAreaReq toSearch = new MSDataService.PoolAreaReq();
            for(int i = 0 ; i < LowMZs.Count ; i++) {
                List<MSDataService.PoolAreaReq> AP = new List<MSDataService.PoolAreaReq>();
                AreaPools.Add(AP);
                foreach(MSDataService.PoolAreaReq PR in Pool) {
                    if (PR.MZHigh<=HighMZs[i] && PR.MZLow >= LowMZs[i]) {
                        AP.Add(PR);
                    }
                }
                AP.Sort(PoolCompaprer);
            }

            //get data for mass intervals found 
            BinaryReader Reader = 
               new BinaryReader(File.Open(FileName,FileMode.Open,FileAccess.Read,FileShare.Read),new ASCIIEncoding());
            
            for(int i = 0 ; i < LowMZs.Count ; i++) {
                //get data point page index for lower mass
                long LowPos = MassIndex.BinarySearch(LowMZs[i]);
                if(LowPos < 0)
                    LowPos = (~LowPos) - 1;
                //for the first page 
                if(LowPos < 0)
                    LowPos = 0;
                //get data point page index for higher mass
                long HighPos = MassIndex.BinarySearch(HighMZs[i]);
                if(HighPos < 0)
                    HighPos = (~HighPos) - 1;
                //for the first page 
                if(HighPos < 0)
                    HighPos = 0;
                //move to the first selected page
                Reader.BaseStream.Seek(StartPagePosition + LowPos * 64000, SeekOrigin.Begin);
                long FileLen = Reader.BaseStream.Length;
                for(long j = LowPos ; j <= HighPos ; j++) {
                    //Last page can be shorter than fixed 64000 bytes
                    long Limit = (Math.Min(64000, (FileLen - StartPagePosition) - j * 64000)) / 20;
                    //read binary data
                    byte[] Pbyte = Reader.ReadBytes((int)(Limit * 20));
                    DataPoint P = new DataPoint();
                    for(int k = 0 ; k < Limit ; k++) {
                        P.Mass = BitConverter.ToDouble(Pbyte, k * 20);
                        P.Intensity = BitConverter.ToSingle(Pbyte, (k * 20) + 8);
                        P.Scan = BitConverter.ToInt32(Pbyte, (k * 20) + 12);
                        int FileID = BitConverter.ToInt32(Pbyte, (k * 20) + 16);
                        if(P.Mass < LowMZs[i])
                            continue;
                        if(P.Mass > HighMZs[i])
                            break;
                        //push points to related requests
                        //if there is a file for data point
                        toSearch.FileID = FileID; 
                        int APIndex = AreaPools[i].BinarySearch(toSearch,PoolCompaprer);
                        if(APIndex < 0)
                            continue;
                        //if data point is in scan interval
                        if( P.Scan >= AreaPools[i][APIndex].StartScan &&  P.Scan <= AreaPools[i][APIndex].EndScan) {
                            AreaPools[i][APIndex].Points.Add(P);
                            P = new DataPoint();
                        }
                    }
                }
            }
            Reader.Close();

            //Fill RTs
            for(int i = 0 ; i < Pool.Count ; i++) {
                if(Pool[i].Points != null) {
                    for(int j = 0 ; j < Pool[i].Points.Count ; j++) {
                        Pool[i].Points[j].RT = Pool[i].RFEntry.MSFile.GetRTFromScanNumber(Pool[i].Points[j].Scan);
                    }
                }
            }
            //Format
            for(int i = 0 ; i < Pool.Count ; i++) {
                if(Pool[i].Points == null)
                    continue;
                Pool[i].FileID = -1;
                Pool[i].Points.Sort();
                double[] Res = null;
                //two double for chromatogram point (mz - discarded)
                if (Pool[i].Type == MSDataService.MSDataType.Chromatogram) {
                    (Pool[i].RFEntry.MSFile as RCH1MSFile).ChromatogramFormat(Pool[i].Points, (Pool[i].MZLow + Pool[i].MZHigh) / 2.0);
                    Res = new double[Pool[i].Points.Count * 2];
                    for(int j = 0 ; j < Pool[i].Points.Count ; j++) {
                        Res[j * 2] = Pool[i].Points[j].RT;
                        Res[j * 2 + 1] = Pool[i].Points[j].Intensity;
                    }
                }
                //three doubles for slice point
                if (Pool[i].Type == MSDataService.MSDataType.Slice) {
                    Res = new double[Pool[i].Points.Count * 3];
                    for(int j = 0 ; j < Pool[i].Points.Count ; j++) {
                        Res[j * 3] = Pool[i].Points[j].Mass;
                        Res[j * 3 + 1] = Pool[i].Points[j].RT;
                        Res[j * 3 + 2] = Pool[i].Points[j].Intensity;
                    }
                }
                Pool[i].Res = Res;
                Pool[i].Points = null;
                Pool[i].Completed = true;
            }
        }
    }


    //class RCH1MSFile defines GetPoint as forwarding actual reading operations to DirCache class
    public class RCH1MSFile : RCHMSFile {

        public string FileName;
        public DirCache JoinedCache;
        //inherited RTs, sr

        //Forward data points reading to DirCache
        protected override List<DataPoint> GetPoints(double MZLow, double MZHigh, double StartRT, double EndRT) {
            int StartScan = GetScanNumberFromRT(StartRT);
            int EndScan = GetScanNumberFromRT(EndRT);
            List<DataPoint> Points = JoinedCache.GetPoints(FileName, sr, MZLow, MZHigh, StartScan, EndScan);
            foreach(DataPoint P in Points) {
                P.RT = RTs[P.Scan];
            }
            Points.Sort();
            return Points;
        }
    }



}
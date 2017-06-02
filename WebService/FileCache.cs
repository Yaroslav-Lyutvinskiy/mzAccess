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

namespace mzAccess {

    //actual data reading procedures for single file cache
    public class RCH0MSFile : RCHMSFile {

        //file offset for first data points page
        public long StartPagePosition;
        public long FileLen;
        public List<double> MassIndex = new List<double>();

        //reads heading info for RCH0 files
        public RCH0MSFile(string FileName) {
            fs = new FileStream(FileName,FileMode.Open,FileAccess.Read);
            sr = new BinaryReader(fs);
            //Read signature - currently RCH0
            string sig = sr.ReadString();
            int SpectraCount = sr.ReadInt32();
            for(int i = 0 ; i < SpectraCount ; i++) {
                RTs.Add(sr.ReadInt32(), sr.ReadSingle());
            }
            MinRT = RTs.Min(m => m.Value);
            MaxRT = RTs.Max(m => m.Value);
            int PageCount = sr.ReadInt32();
            for(int i = 0 ; i < PageCount ; i++) {
                MassIndex.Add(sr.ReadDouble());
            }
            StartPagePosition = fs.Position;
            FileLen = fs.Length;
            MinMZ = MassIndex[0];
            fs.Seek(-16,SeekOrigin.End);
            MaxMZ = sr.ReadDouble();
        }


        //reads datapoints for RCH0 files
        protected override List<DataPoint> GetPoints(double MZLow, double MZHigh, double StartRT, double EndRT) {
            int LowPos = MassIndex.BinarySearch(MZLow);
            if(LowPos < 0)
                LowPos = (~LowPos) - 1;
            //for the first page 
            if(LowPos < 0)
                LowPos = 0;
            int HighPos = MassIndex.BinarySearch(MZHigh);
            if(HighPos < 0)
                HighPos = (~HighPos) - 1;
            //for the first page 
            if(HighPos < 0)
                HighPos = 0;
            List<DataPoint> Points = new List<DataPoint>();
            fs.Seek(StartPagePosition + LowPos * 65536, SeekOrigin.Begin);
            for(long i = LowPos ; i <= HighPos ; i++) {
                long Limit = (Math.Min(65536, (FileLen - StartPagePosition) - i * 65536)) / 16;
                byte[] Pbyte = sr.ReadBytes((int)(Limit << 4)); 
                for(int j = 0 ; j < Limit ; j++) {
                    DataPoint P = new DataPoint();
                    P.Mass = BitConverter.ToDouble(Pbyte,j<<4);
                    P.Intensity = BitConverter.ToSingle(Pbyte,(j<<4)+8);
                    P.Scan = Convert.ToInt32(BitConverter.ToSingle(Pbyte,(j<<4)+12));
                    P.RT = RTs[P.Scan];
                    if(P.Mass >= MZLow && P.Mass <= MZHigh && P.RT >= StartRT && P.RT <= EndRT) {
                        Points.Add(P);
                    }
                }
            }
            Points.Sort();
            return Points;
        }

        public override void CloseFile() {
            base.CloseFile();
            MassIndex = null;
        }


    }
}
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

    //basic class RCHMSFile provides IMSFile implementation for both file and folder cache 
    //actual data points reading is to be made in abstract function GetPoints
    public abstract class RCHMSFile : IMSFile {

        public FileStream fs;
        public BinaryReader sr;

        public SortedList<int, double> RTs = new SortedList<int, double>();

        protected double MinMZ;
        protected double MaxMZ;
        protected double MinRT;
        protected double MaxRT;

        public virtual void CloseFile() {
            sr.Close();
            if(fs != null) {
                fs.Close();
                fs = null;
            }
            RTs = null;
        }

        protected abstract List<DataPoint> GetPoints(double MZLow, double MZHigh, double StartRT, double EndRT);

        public double[] GetArea(double MZLow, double MZHigh, double StartRT, double EndRT, bool Profile = false) {
            List<DataPoint> Points = GetPoints(MZLow, MZHigh, StartRT, EndRT);
            double[] Res = new double[Points.Count * 3];
            for(int i = 0 ; i < Points.Count ; i++) {
                Res[i * 3] = Points[i].Mass;
                Res[i * 3 + 1] = Points[i].RT;
                Res[i * 3 + 2] = Points[i].Intensity;
            }
            return Res;
        }

        //cannot be implemented on RCH files
        public double[] GetAveSpectrum(double MZLow, double MZHigh, double StartRT, double EndRT, bool Profile = false) {
            throw new UnsupportedFeatureException("Cache does not support averaging of spectra");
        }

        public double[] GetMassRange() {
            return new[] { MinMZ, MaxMZ };
        }

        public double GetRTFromScanNumber(int ScanNumber) {
            if(RTs.Count == 0)
                throw new DataUnavailableException("There is no data in cache file. Probably, centroid data was not available when caching");
            return RTs.Last(r => r.Key <= ScanNumber).Value;
        }

        public double[] GetRTRange() {
            return new[] { MinRT, MaxRT };
        }

        public int GetScanNumberFromRT(double RT) {
            if(RTs.Count == 0)
                throw new DataUnavailableException("There is no data in cache file. Probably, centroid data was not available when cacheing");
            if (RTs.Values[RTs.Count-1] < RT) {
                return RTs.Keys[RTs.Count - 1];
            } else {
                if (RT < RTs.Values[0]) {
                    return RTs.Keys[0];
                }
                return RTs.Last(r => r.Value <= RT).Key;
            }
        }

        public double[] GetSpectrum(double MZLow, double MZHigh, int ScanNumber, bool Profile = false) {
            double RT = GetRTFromScanNumber(ScanNumber);
            if (GetScanNumberFromRT(RT) != ScanNumber) {
                throw (new DataUnavailableException("There is no MSMS in cach, Use Cache = False"));
            }
            List<DataPoint> Points = GetPoints(MZLow, MZHigh, RT, RT);
            double[] Res = new double[Points.Count * 2];
            for(int i = 0 ; i < Points.Count ; i++) {
                Res[i * 2] = Points[i].Mass;
                Res[i * 2 + 1] = Points[i].Intensity;
            }
            return Res;
        }

        public void ChromatogramFormat(List<DataPoint> Points,double FillingMass) {

            //Remove duplicates - only maximum intensity points will be returned
            for(int i = Points.Count - 1 ; i > 0 ; i--) {
                if(Points[i].RT == Points[i - 1].RT) {
                    if(Points[i].Intensity > Points[i - 1].Intensity) {
                        Points.RemoveAt(i - 1);
                    } else {
                        Points.RemoveAt(i);
                    }
                }
            }
            //Add Zeroes
            if(Points.Count > 0) {
                int StartScanIndex = RTs.IndexOfKey(Points[0].Scan);
                //leading zero
                if(StartScanIndex > 0)
                    StartScanIndex--;
                //trailing zero
                int EndScanIndex = RTs.IndexOfKey(Points[Points.Count - 1].Scan);
                if(EndScanIndex < RTs.Count - 1) 
                    EndScanIndex++;

                int PointCounter = 0;
                for(int i = StartScanIndex ; i <= EndScanIndex ; i++) {
                    //insert missed values
                    if (i==EndScanIndex || Points[PointCounter].Scan > RTs.Keys[i]) {
                        DataPoint P = new DataPoint();
                        P.Scan = RTs.Keys[i];
                        P.RT = RTs.Values[i];
                        P.Mass = FillingMass;
                        Points.Insert(PointCounter, P);
                    }
                    PointCounter++;
                }
                //excessive zeroes
                for(int i = Points.Count-2 ; i > 0 ; i--) {
                    if (Points[i].Intensity == 0.0 && 
                        Points[i-1].Intensity == 0.0 && 
                        Points[i+1].Intensity == 0.0) {
                        Points.RemoveAt(i);
                    }
                }
            }
        }

        public double[] GetTrace(double MZLow, double MZHigh, double RTLow, double RTHigh) {

            List<DataPoint> Points = GetPoints(MZLow, MZHigh, RTLow, RTHigh);

            ChromatogramFormat(Points, (MZLow + MZHigh) / 2.0);

            double[] Res = new double[Points.Count * 2];
            for(int i = 0 ; i < Points.Count ; i++) {
                Res[i * 2] = Points[i].RT;
                Res[i * 2 + 1] = Points[i].Intensity;
            }
            return Res;
        }

        //GetFragmentationEvents - fragmentation data is not cashed
        public FragmentationInfo[] GetFragmentationEvents(double MZLow, double MZHigh, double StartRT, double EndRT) {
            return null;
        }

    }

}
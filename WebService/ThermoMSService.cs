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
using System.Runtime.InteropServices;
using System.IO;
using MSFileReaderLib;

namespace mzAccess
{
    /// <summary>
    /// IMSFile implementation for Thermo .raw files
    /// </summary>
    public class ThermoMSFile:IMSFile{

        //full file name 
        public string FileName;
        // MSFileReader raw file interface to keep it open
        MSFileReader_XRawfile RawFile;
        double MaxRT = 0.0;
        int MaxScan = 0;

        public ThermoMSFile(string FileName){
            this.FileName = FileName;
            RawFile = new MSFileReader_XRawfile();
            RawFile.Open(FileName);
            RawFile.SetCurrentController(0, 1);
            int Error=0;
            string ErrorStr = null;
            RawFile.IsError(ref Error);
            RawFile.GetErrorMessage(ref ErrorStr);
            RawFile.GetNumSpectra(ref MaxScan);
            while(MaxRT == 0) {
                MaxScan--;
                RawFile.RTFromScanNum(MaxScan, ref MaxRT);
            }
            if(Error != 0) {
                MSDataService.Log(String.Format("Error = {0},Error Message: {1}, File Exists:{2}", Error, ErrorStr, File.Exists(FileName)));
            }
        }

        //check scan filter  if mass in the scan range of that scan
        bool InMassRange(string Filter,double Mass){
            string MR = Filter.Substring(Filter.IndexOf("[") + 1, Filter.LastIndexOf("]") - Filter.IndexOf("[") - 1);
            double Low = Convert.ToDouble(MR.Substring(0, MR.IndexOf("-") - 1));
            double High = Convert.ToDouble(MR.Substring(MR.IndexOf("-") + 1));
            return (Mass > Low && Mass < High);
        }

        //IMSFile.GetTrace Implementation
        public double[] GetTrace(double MZLow, double MZHigh, double RTLow, double RTHigh){
            object ChroData = null;
            object PeakFlags = null;
            int ArraySize = 0;
            string MassRange = String.Format("{0:F5}-{1:F5}",MZLow,MZHigh); //like "430.90-430.95" string
            string FilterToRead = "Full ms";
            RawFile.GetNumSpectra(ref MaxScan);
            //call for chromatogram
            RawFile.GetChroData(0, 0, 0, FilterToRead, MassRange, null, 0.0, ref RTLow, ref RTHigh, 0, 0, ref ChroData, ref PeakFlags, ref ArraySize); //15msec
            int Error=0;
            string ErrorStr = null;
            RawFile.IsError(ref Error);
            RawFile.GetErrorMessage(ref ErrorStr);
            List<double> Res = new List<double>(); 
            //Data reading
            for (int k = 0 ; k<ArraySize ; k++ ){
                double RT = (double)(ChroData as Array).GetValue(0, k);
                double Int = (double)(ChroData as Array).GetValue(1, k);
                if (Int == 0.0){
                    string Filter = null;
                    RawFile.GetFilterForScanRT(RT, ref Filter);
                    if (!InMassRange(Filter, MZHigh) || !InMassRange(Filter, MZLow))
                        continue;
                }
                Res.Add(RT);
                Res.Add(Int);
            }
            //Zero cleaning
            for (int i=Res.Count - 4 ; i > 0 ; i-=2){
                if (Res[i+1] == 0.0 && Res[i - 1] == 0.0 && Res[i + 3] == 0.0) {
                    Res.RemoveAt(i+1);
                    Res.RemoveAt(i);
                }
            }
            if (Res.Count != 0){
                return Res.ToArray();
            }else{
                return new double[0];
            }
        }

        //IMSFile.GetSpectrum Implementation
        public double[] GetSpectrum(double MZLow, double MZHigh, int ScanNumber, bool Profile = false){
            object MassData = null;
            object PeakFlags = null;
            int ArraySize = 0;
            double PeakWidth = 0.0; //??
            int Centroided = Profile ? 0 : 1;
            List<double> ResList = new List<double>();
            if (Profile){
                int isProfile = 0;
                (RawFile as IXRawfile2).IsProfileScanForScanNum(ScanNumber, ref isProfile);
                if (isProfile == 0) {
                    throw (new DataUnavailableException(String.Format("Profile data is not available for file: {1} Scan {0} ", ScanNumber, FileName)));
                }
                string MassRange = String.Format("{0:F3}-{1:F3}",MZLow,MZHigh); //"430.90-430.95";
                (RawFile as IXRawfile3).GetMassListRangeFromScanNum(
                    ref ScanNumber, null, 0, 0, 0, Centroided, ref PeakWidth,ref MassData , ref PeakFlags, MassRange, ref ArraySize);
                for (int k = 0 ; k<ArraySize ; k++ ){
                    ResList.Add((double)(MassData as Array).GetValue(0, k));
                    ResList.Add((double)(MassData as Array).GetValue(1, k));
                }
            }else{
                (RawFile as IXRawfile2).GetLabelData(ref MassData, ref PeakFlags, ref  ScanNumber);
                ArraySize = (MassData as Array).GetLength(1); 
                int k;
                for ( k = 0 ; k < ArraySize ; k++){
                    if ((double)(MassData as Array).GetValue(0, k) >= MZLow) break;
                }
                if (k == ArraySize) return null;
                for ( ; k < ArraySize ; k++){
                    if ((double)(MassData as Array).GetValue(0, k) > MZHigh) break;
                    ResList.Add((double)(MassData as Array).GetValue(0, k));
                    ResList.Add((double)(MassData as Array).GetValue(1, k));
                }
            }
            if (ResList.Count == 0) return null;
            double[] Res = ResList.ToArray();
            return Res;
        }

        //IMSFile.GetAveSpectrum Implementation
        public double[] GetAveSpectrum(double MZLow, double MZHigh, double StartRT, double EndRT, bool Profile = false){
            object MassData = null;
            object PeakFlags = null;
            int ArraySize = 0;
            double PeakWidth = 0.0; 
            int StartScan = 0;
            RawFile.ScanNumFromRT(StartRT, ref StartScan );
            if(EndRT > MaxRT)
                EndRT = MaxRT;
            int EndScan = 0;
            RawFile.ScanNumFromRT(EndRT, ref EndScan);
            List<double> ResList = new List<double>();
            int BKScan1 = 0, BKScan2 = 0, BKScan3 = 0, BKScan4 = 0;
            (RawFile as IXRawfile4).GetAverageMassList(
                ref StartScan, ref EndScan, ref BKScan1, ref BKScan2, ref BKScan3, ref BKScan4, null, 0, 0, 0, 0, ref PeakWidth, ref MassData, ref PeakFlags, ref ArraySize);
            if(!Profile) {
                MZLow -= 1.0;
                MZHigh += 1.0;
            }
            for(int k = 0 ; k < ArraySize ; k++) {
                if((double)(MassData as Array).GetValue(0, k) >= MZLow && (double)(MassData as Array).GetValue(0, k) <= MZHigh) {
                    ResList.Add((double)(MassData as Array).GetValue(0, k));
                    ResList.Add((double)(MassData as Array).GetValue(1, k));
                }
            }
            if (!Profile){
                //There is also (RawFile as IXRawfile4).GetAveragedLabelData function but I failed to make proper marshaling 
                //int* pnScanNumbers argument has been recognized as ref int where actually it is int[] 
                //changing Interop assembly does not help - it continues marshaling 8 bytes of pointer instead of marshaling array by value
                //therefore I have here some custom implementation of peak centroiding
                List<double> Centroids = Centroid(ResList);
                ResList.Clear();
                for (int k = 0 ; k<Centroids.Count/2 ; k++ ){
                    if (Centroids[k*2] >= MZLow+1.0 && Centroids[k*2] <= MZHigh-1.0 ) {
                        ResList.Add(Centroids[k*2]);
                        ResList.Add(Centroids[k*2+1]);
                    }
                }
            }
            double[] Res = ResList.ToArray();
            return Res;
        }

        //IMSFile.GetArea Implementation
        public double[] GetArea(double MZLow, double MZHigh, double StartRT, double EndRT, bool Profile = false){
            object MassData = null;
            object PeakFlags = null;
            int ArraySize = 0;
            double PeakWidth = 0.0; //??
            int Centroided = Profile ? 0 : 1;
            string MassRange = String.Format("{0:F3}-{1:F3}",MZLow,MZHigh); //"430.90-430.95";
            int StartScan = 0;
            RawFile.ScanNumFromRT(StartRT, ref StartScan );
            if(EndRT > MaxRT)
                EndRT = MaxRT;
            int EndScan = 0;
            RawFile.ScanNumFromRT(EndRT, ref EndScan);
            List<double> ResList = new List<double>();
            for( int i = StartScan; i <= EndScan ; i++){
                int Order = 0;
                (RawFile as IXRawfile5).GetMSOrderForScanNum(i, ref Order);
                if(Order > 1) {
                    continue;
                }
                MassData = null;
                PeakFlags = null;
                ArraySize = 0;
                PeakWidth = 0.0;
                if (Profile){
                    //Check if profile info avialable 
                    int isProfile = 0;
                    (RawFile as IXRawfile2).IsProfileScanForScanNum(i, ref isProfile);
                    if (isProfile == 0) {
                        throw (new DataUnavailableException(String.Format("Profile data is not available for file: {1} Scan {0} ", i, FileName)));
                    }
                    (RawFile as IXRawfile3).GetMassListRangeFromScanNum(
                        ref i, null, 0, 0, 0, Centroided, ref PeakWidth,ref MassData , ref PeakFlags, MassRange, ref ArraySize);
                    double CurRT = GetRTFromScanNumber(i);
                    for (int k = 0 ; k<ArraySize ; k++ ){
                        ResList.Add((double)(MassData as Array).GetValue(0, k));
                        ResList.Add(CurRT);
                        ResList.Add((double)(MassData as Array).GetValue(1, k));
                    }
                }else{
                    (RawFile as IXRawfile2).GetLabelData(ref MassData, ref PeakFlags, ref i);
                    ArraySize = (MassData as Array).GetLength(1); 
                    //here can be binary search for performance optimization 
                    int k;
                    for ( k = 0 ; k < ArraySize ; k++){
                        if ((double)(MassData as Array).GetValue(0, k) >= MZLow) break;
                    }
                    if (k == ArraySize) continue;
                    double CurRT = GetRTFromScanNumber(i);
                    for ( ; k < ArraySize ; k++){
                        if ((double)(MassData as Array).GetValue(0, k) > MZHigh) break;
                        ResList.Add((double)(MassData as Array).GetValue(0, k));
                        ResList.Add(CurRT);
                        ResList.Add((double)(MassData as Array).GetValue(1, k));
                    }
                }
            }
            double[] Res = ResList.ToArray();
            return Res;
        }

        //IMSFile.GetFragmentationEvents Implementation
        public FragmentationInfo[] GetFragmentationEvents(double MZLow, double MZHigh, double StartRT, double EndRT) {
            int StartScan = 0;
            RawFile.ScanNumFromRT(StartRT, ref StartScan);
            int EndScan = 0;
            RawFile.ScanNumFromRT(EndRT, ref EndScan);
            List<FragmentationInfo> ResList = new List<FragmentationInfo>();
            for (int i = StartScan ; i <= EndScan ; i++) {
                string Filter = null;
                IntPtr Infos = IntPtr.Zero;
                int ArraySize = 0;
                FragmentationInfo Info = new FragmentationInfo();
                RawFile.GetFilterForScanNum(i, ref Filter);
                Info.Description = Filter;
                Info.RT = GetRTFromScanNumber(i);
                Info.ScanNumber = i;
                //First chanse
                if(Filter.Contains("ms2")) {
                    Info.MSOrder = 2;
                    string  part= Filter.Substring(0,Filter.IndexOf('@'));
                    Info.ParentMZ = Convert.ToDouble(part.Substring(part.LastIndexOf(' ')));
                }
                //Second chanse
                object Labels = null;
                object Values = null;
                ArraySize = 0;
                RawFile.GetTrailerExtraForScanNum(i, ref Labels, ref Values, ref ArraySize);
                for (int k = 0 ; k < ArraySize ; k++ ){
                    if ((Labels as Array).GetValue(k).ToString().Contains("Mono")){
                        double M = Convert.ToDouble((Values as Array).GetValue(k).ToString());
                        if(M != 0.0)
                            Info.ParentMZ = M;
                    }
                }
                double PMass = 0.0;
                int Order = 2;
                (RawFile as IXRawfile5).GetPrecursorMassForScanNum(i,Order,ref PMass);
                if (PMass != 0.0)
                    Info.ParentMZ = PMass;
                ArraySize = 0;
                Order = 0;
                (RawFile as IXRawfile5).GetMSOrderForScanNum(i, ref Order);
                if(Order != 0)
                    Info.MSOrder = Order;
                (RawFile as IXRawfile5).GetNumberOfMSOrdersFromScanNum(i, ref ArraySize);
                double Isolation = 0.0;
                (RawFile as IXRawfile5).GetIsolationWidthForScanNum(i, ArraySize, ref Isolation);
                if (Info.MSOrder>1 && Info.ParentMZ >= MZLow && Info.ParentMZ<=MZHigh)
                    ResList.Add(Info);
            }
            if(ResList.Count > 0) {
                return ResList.ToArray();
            } else {
                return null;
            }
        }

        //IMSFile.GetScanNumberFromRT Implementation
        public int GetScanNumberFromRT(double RT){
            if (RT < 0) return 1;
            int ScanNum = 0;
            RawFile.ScanNumFromRT(RT, ref ScanNum);
            int Order = 0;
            do {
                (RawFile as IXRawfile5).GetMSOrderForScanNum(ScanNum, ref Order);
                ScanNum--;
            } while(Order != 1);
            return ScanNum++;
        }

        //IMSFile.GetRTFromScanNumber Implementation
        public double GetRTFromScanNumber(int ScanNumber){
            double RT = 0.0;
            RawFile.RTFromScanNum(ScanNumber, ref RT);
            return RT;
        }

        //IMSFile.GetMassRange Implementation
        public double[] GetMassRange(){
            double MinMass = 0.0;
            RawFile.GetLowMass(ref MinMass);
            double MaxMass = 0.0;
            RawFile.GetHighMass(ref MaxMass);
            return new double[] {MinMass,MaxMass};
        }

        //IMSFile.GetRTRange Implementation
        public double[] GetRTRange(){
            double MinRT = 0.0;
            RawFile.GetStartTime(ref MinRT);
            double MaxRT = 0.0;
            RawFile.GetEndTime(ref MaxRT);
            return new double[] {MinRT,MaxRT};
        }

        //IMSFile.CloseFile Implementation
        public void CloseFile(){
            RawFile.Close();
        }

        public List<double> Centroid(List<double> Data)
        {
            List<double> OutData = new List<double>();

            int i = 1; //i follows odd numbers - for intensity
            //pass leadiing zeroes
            while(i < Data.Count && Data[i] == 0.0) i+=2;

            //peaks are considered as a solid area above zero (with no local minima) or area above zero splitted by valleys (with local minima)
            while(i < Data.Count) {
                double sumI = Data[i-2] / 2.0; //half of the valley point
                double Last = Data[i-2] / 2.0;
                double sumIi = 0.0;
                bool Down = false;
                int LowPoint = i - 3; //points to mass of zero peak point
                int PointsPassed = 0;
                while(i < Data.Count && Data[i] != 0.0) { //starts at first point completely belong to peak
                    //if valley passed 
                    if(Down && Last < Data[i]) {
                        //leave loop 
                        break;
                    }
                    //if going down
                    if(Last > Data[i]) {
                        Down = true;
                    }
                    //for center of mass calc
                    PointsPassed++;
                    sumIi += Data[i] * PointsPassed;
                    sumI += Data[i];
                    Last = Data[i];
                    i+=2;
                }

                if (Data[i] > 0.0) { // subtract half of last point if it was valley
                    sumIi -= (Last/2.0) * PointsPassed;
                    sumI -= Last/2.0;
                }

                int u = Convert.ToInt32(Math.Floor(sumIi / sumI));
                double du = sumIi / sumI - (double)u;
                if((LowPoint + u*2 + 2) < Data.Count) {
                    //Mass as a center of mass 
                    OutData.Add(Data[LowPoint + u*2] * (1 - du) + Data[LowPoint + u*2 + 2] * du);
                    //Intensity as an area under shape of peak 
                    OutData.Add(sumI);
                } else {
                    break;
                }
                while(i < Data.Count && Data[i] == 0.0) i+=2;
            }
            return OutData;
        }
    }
}
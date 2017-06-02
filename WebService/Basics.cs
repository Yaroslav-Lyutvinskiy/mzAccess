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

namespace mzAccess {

    //structure for GetFragmentationEvents calls 
    public class FragmentationInfo {
        public double RT; //retention time
        public double ParentMZ; //mz of ion of interest in MSOnly spectrum
        public int ScanNumber; //Scan Number - access key to access MS2 spectra by funcuin GetSpectrabyScanNumber
        public int MSOrder; //Order event 2 for MSMS, 3 for MS3, etc.
        public string Description; //Free form description of event - vendor API dependent, for thermo it is a Filter string 
    }

    //MS data point, used internally, mainly for RCH files
    public class DataPoint : IComparable<DataPoint> {
        public double Mass;
        public float Intensity;
        public int Scan;
        public double RT;
        public int CompareTo(DataPoint p) {
            int Res = RT.CompareTo(p.RT);
            if(Res != 0) {
                return Res;
            } else {
                return Mass.CompareTo(p.Mass);
            }
        }
    }


    //Inerface to be implemented by any MS data source
    //functions correspond to exported functions of service
    public interface IMSFile {
        double[] GetTrace(double MZLow, double MZHigh, double RTLow, double RTHigh);
        double[] GetSpectrum(double MZLow, double MZHigh, int ScanNumber, bool Profile = false);
        double[] GetAveSpectrum(double MZLow, double MZHigh, double StartRT, double EndRT, bool Profile = false);
        double[] GetArea(double MZLow, double MZHigh, double StartRT, double EndRT, bool Profile = false);
        FragmentationInfo[] GetFragmentationEvents(double MZLow, double MZHigh, double StartRT, double EndRT);
        int GetScanNumberFromRT(double RT);
        double GetRTFromScanNumber(int ScanNumber);
        double[] GetMassRange();
        double[] GetRTRange();
        void CloseFile();
    }
}
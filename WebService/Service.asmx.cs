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
using System.Web.Services;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;


namespace mzAccess
{
    /// <summary>
    /// Summary description for WebServiceSample
    /// </summary>
    [WebService(Namespace = "http://mzaccess.org/DataService",
    //[WebService(Namespace = "http://tempuri.org/",
        Description = "<h3>Service provides access to mass spectrometry raw data. <br/> Look for detailed documentation to <a href = \"http://mzaccess.org/\">mzAccess.org</a> </h3>")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    // To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
    // [System.Web.Script.Services.ScriptService]

    public class MSDataService : System.Web.Services.WebService
    {

        [WebMethod(Description = @"Gets a chromatogram for the specified MZ-RT area.")]
        public double[] GetChromatogram(string FileName, double MZLow, double MZHigh, double RTLow, double RTHigh, out string ErrorMessage, bool Cache = true){
            try{
                //Get access key as Filename with no path or extention
                FileName = Path.GetFileNameWithoutExtension(FileName);
                //Get Raw or Cache entry depending of Cache parameter
                Entry Cached = Cache ? Global.RCHCache[FileName] : Global.FileCashe[FileName];
                //Get IMSFile from entry 
                IMSFile MSFile = Cached.MSFile;
                Cached.Lock();
                double[] Res;
                try {
                    //call interface function 
                    Res = MSFile.GetTrace(MZLow,MZHigh,RTLow,RTHigh);
                }
                finally {
                    //unlock entry anycase 
                    Cached.Unlock();
                }
                ErrorMessage = null;
                return Res;
            }
            catch(KeyNotFoundException){
                //if error then return Error message with out ErrorMessage parameter
                ErrorMessage = String.Format("GetChromatogram file exception; File not found {0}",FileName);;
                return null;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetChromatogram uncategorized exception; Message: {0}; Stack: {1} \n "+
                    "Parameters: FileName - \"{2}\", MZLow - {3:f5}, MZHigh - {4:f5}, RTLow - {5:f3}, RTHigh - {6:f3}, Cache - {7} ",
                    e.Message,e.StackTrace,FileName, MZLow, MZHigh, RTLow, RTHigh, Cache );
                Log(ErrorMessage);
                return null;
            }
        }

        [WebMethod(Description = @"Gets a spectrum identified by the ScanNumber parameter")]
        //Can access both MSOnly and MSMS spectra
        public double[] GetSpectrumByScanNumber(string FileName, double MZLow, double MZHigh, int ScanNumber, out string ErrorMessage, bool Cache = false, bool Profile = false){
            try{
                if (Cache && Profile) {
                    Cache = false;
                }
                FileName = Path.GetFileNameWithoutExtension(FileName);
                Entry Cached = Cache ? Global.RCHCache[FileName] : Global.FileCashe[FileName];
                IMSFile MSFile = Cached.MSFile;
                Cached.Lock();
                double[] Res;
                try {
                    Res = MSFile.GetSpectrum(MZLow, MZHigh, ScanNumber, Profile);
                }
                finally {
                    Cached.Unlock();
                }
                ErrorMessage = null;
                return Res;
            }
            catch(KeyNotFoundException){
                ErrorMessage = String.Format("GetSpectrumbyScanNumber file exception; File not found {0}",FileName);
                return null;
            }
            catch(ArgumentException e){
                ErrorMessage = e.Message;
                return null;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetSpectrumbyScanNumber uncategorized exception; Message: {0}; Stack: {1} \n "+
                    "Parameters: FileName - \"{2}\", MZLow - {3:f5}, MZHigh - {4:f5}, ScanNumber - {5}, Cache - {6}, Profile - {7} ",
                    e.Message,e.StackTrace,FileName, MZLow, MZHigh, ScanNumber, Cache, Profile );
                Log(ErrorMessage);
                return null;
            }
        }

        [WebMethod(Description = @"Gets Mass/Intensity pairs for particular MSOnly spectra identified by Retention Time.")]
        //is to be MSOnly spectrum access
        public double[] GetSpectrumByRT(string FileName, double MZLow, double MZHigh, double RT, out string ErrorMessage, bool Cache = false, bool Profile = false){
            try{
                if (Cache && Profile) {
                    Cache = false;
                }
                FileName = Path.GetFileNameWithoutExtension(FileName);
                Entry Cached = Cache ? Global.RCHCache[FileName] : Global.FileCashe[FileName];
                IMSFile MSFile = Cached.MSFile;
                Cached.Lock();
                double[] Res;
                try {
                    int ScanNumber = MSFile.GetScanNumberFromRT(RT);
                    Res = MSFile.GetSpectrum(MZLow, MZHigh, ScanNumber, Profile);
                }
                finally {
                    Cached.Unlock();
                }
                ErrorMessage = null;
                return Res;
            }
            catch(KeyNotFoundException){
                ErrorMessage = String.Format("GetSpectrumbyRT file exception; File not found {0}",FileName);
                return null;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetSpectrumbyRT uncategorized exception; Message: {0}; Stack: {1} \n "+
                    "Parameters: FileName - \"{2}\", MZLow - {3:f5}, MZHigh - {4:f5}, RT - {5:f3}, Cache - {6}, Profile - {7} ",
                    e.Message,e.StackTrace,FileName, MZLow, MZHigh, RT, Cache, Profile );
                Log(ErrorMessage);
                return null;
            }
        }

        [WebMethod(Description = @"Gets scan number for of nearest MS1 spectrum with less or equal retention time.")]
        public int GetScanNumberFromRT(string FileName, double RT, out string ErrorMessage, bool Cache = false)
        {
            try{
                FileName = Path.GetFileNameWithoutExtension(FileName);
                Entry Cached = Cache ? Global.RCHCache[FileName] : Global.FileCashe[FileName];
                IMSFile MSFile = Cached.MSFile;
                Cached.Lock();
                try {
                    int ScanNumber = MSFile.GetScanNumberFromRT(RT);
                    ErrorMessage = null;
                    return ScanNumber;
                }
                finally {
                    Cached.Unlock();
                }
            }
            catch(KeyNotFoundException){
                ErrorMessage = String.Format("GetScanNumberFromRT file exception; File not found {0}",FileName);
                return -1;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetScanNumberFromRT uncategorized exception; Message: {0}; Stack: {1} \n "+
                    "Parameters: FileName - \"{2}\", RT - {3:f3}, Cache - {4}",
                    e.Message, e.StackTrace, FileName, RT, Cache );
                Log(ErrorMessage);
                return -1;
            }
        }
            
        [WebMethod(Description = @"Gets exact retention time for particular scan number.")]
        public double GetRTFromScanNumber(string FileName, int ScanNumber, out string ErrorMessage, bool Cache = false){
            try{
                FileName = Path.GetFileNameWithoutExtension(FileName);
                Entry Cached = Cache ? Global.RCHCache[FileName] : Global.FileCashe[FileName];
                IMSFile MSFile = Cached.MSFile;
                Cached.Lock();
                try {
                    double RT = MSFile.GetRTFromScanNumber(ScanNumber);
                    ErrorMessage = null;
                    return RT; 
                }
                finally {
                    Cached.Unlock();
                }
            }
            catch(KeyNotFoundException){
                ErrorMessage = String.Format("GetScanNumberFromRT file exception; File not found {0}",FileName);
                return -1.0;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetScanNumberFromRT uncategorized exception; Message: {0}; Stack: {1} \n "+
                    "Parameters: FileName - \"{2}\", ScanNumber - {3}, Cache - {4} ",
                    e.Message,e.StackTrace,FileName, ScanNumber, Cache);
                Log(ErrorMessage);
                return -1.0;
            }
        }

        [WebMethod(Description = @"Gets Mass/Intensity pairs for averaged (summed) spectra for Retention Time range")]
        public double[] GetAverageSpectrum(string FileName, double MZLow, double MZHigh, double RTLow, double RTHigh, out string ErrorMessage, bool Profile = false){
            try{
                FileName = Path.GetFileNameWithoutExtension(FileName);
                Entry Cached = Global.FileCashe[FileName];
                IMSFile MSFile = Cached.MSFile;
                Cached.Lock();
                double[] Res;
                try {
                    Res = MSFile.GetAveSpectrum(MZLow,MZHigh,RTLow,RTHigh,Profile);
                }
                finally {
                    Cached.Unlock();
                }
                ErrorMessage = null;
                return Res;
            }
            catch(KeyNotFoundException){
                ErrorMessage = String.Format("GetAverageSpectrum file exception; File not found {0}",FileName);;
                return null;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetAverageSpectrum uncategorized exception; Message: {0}; Stack: {1} \n "+
                    "Parameters: FileName - \"{2}\", MZLow - {3:f5}, MZHigh - {4:f5}, RTLow - {5:f3}, RTHigh - {6:f3}, Profile - {7} ",
                    e.Message,e.StackTrace,FileName, MZLow, MZHigh, RTLow, RTHigh, Profile );
                Log(ErrorMessage);
                return null;
            }
        }

        [WebMethod(Description = "Gets all intensity data for the specified MZ-RT area.")]
        public double[] GetArea(string FileName, double MZLow, double MZHigh, double RTLow, double RTHigh, out string ErrorMessage, bool Cache = true, bool Profile = false){
            try{
                if (Cache && Profile) {
                    Cache = false;
                }
                FileName = Path.GetFileNameWithoutExtension(FileName);
                Entry Cached = Cache ? Global.RCHCache[FileName] : Global.FileCashe[FileName];
                IMSFile MSFile = Cached.MSFile;
                Cached.Lock();
                double[] Res;
                try {
                    Res = MSFile.GetArea(MZLow,MZHigh,RTLow,RTHigh,Profile);
                }
                finally {
                    Cached.Unlock();
                }
                ErrorMessage = null;
                return Res;
            }
            catch(KeyNotFoundException){
                ErrorMessage = String.Format("GetArea file exception; File not found {0}",FileName);;
                return null;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetArea uncategorized exception; Message: {0}; Stack: {1} \n "+
                    "Parameters: FileName - \"{2}\", MZLow - {3:f5}, MZHigh - {4:f5}, RTLow - {5:f3}, RTHigh - {6:f3}, Profile - {7}, Cache - {8} ",
                    e.Message,e.StackTrace,FileName, MZLow, MZHigh, RTLow, RTHigh, Profile, Cache );
                Log(ErrorMessage);
                return null;
            }
        }

        [WebMethod(Description = "Describe fragmentation events, which occur in requested LC-MS area.")]
        public FragmentationInfo[] GetFragmentationEvents(string FileName, double MZLow, double MZHigh, double RTLow, double RTHigh, out string ErrorMessage) {
            try{
                FileName = Path.GetFileNameWithoutExtension(FileName);
                Entry Cached = Global.FileCashe[FileName];
                IMSFile MSFile = Cached.MSFile;
                Cached.Lock();
                FragmentationInfo[] Res;
                try {
                    Res = MSFile.GetFragmentationEvents(MZLow,MZHigh,RTLow,RTHigh);
                }
                finally {
                    Cached.Unlock();
                }
                ErrorMessage = null;
                return Res;
            }
            catch(KeyNotFoundException){
                ErrorMessage = String.Format("GetExtraSpectraInfo file exception; File not found {0}",FileName);;
                return null;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetExtraSpectraInfo uncategorized exception; Message: {0}; Stack: {1} \n "+
                    "Parameters: FileName - \"{2}\", MZLow - {3:f5}, MZHigh - {4:f5}, RTLow - {5:f3}, RTHigh - {6:f3} ",
                    e.Message,e.StackTrace,FileName, MZLow, MZHigh, RTLow, RTHigh);
                Log(ErrorMessage);
                return null;
            }

        }


        [WebMethod(Description = "Get full mass range for specified file name.")]
        public double[] GetMZRange(string FileName, out string ErrorMessage, bool Cache = false){
            try{
                FileName = Path.GetFileNameWithoutExtension(FileName);
                Entry Cached = Cache ? Global.RCHCache[FileName] : Global.FileCashe[FileName];
                IMSFile MSFile = Cached.MSFile;
                Cached.Lock();
                try {
                    double[] RTR = MSFile.GetMassRange();
                    ErrorMessage = null;
                    return RTR; 
                }
                finally {
                    Cached.Unlock();
                }
            }
            catch(KeyNotFoundException){
                ErrorMessage = String.Format("GetMassRange file exception; File not found {0}",FileName);
                return null;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetMassRange uncategorized exception; Message: {0}; Stack: {1} \n "+
                    "Parameters: FileName - \"{2}\", Cache - {3} ",
                    e.Message,e.StackTrace,FileName, Cache);
                Log(ErrorMessage);
                return null;
            }
        }

        [WebMethod(Description = "Get full retention time range for specified file name")]
        public double[] GetRTRange(string FileName, out string ErrorMessage, bool Cache = false){
            try{
                FileName = Path.GetFileNameWithoutExtension(FileName);
                Entry Cached = Cache ? Global.RCHCache[FileName] : Global.FileCashe[FileName];
                IMSFile MSFile = Cached.MSFile;
                Cached.Lock();
                try {
                    double[] RTR = MSFile.GetRTRange();
                    ErrorMessage = null;
                    return RTR; 
                }
                finally {
                    Cached.Unlock();
                }
            }
            catch(KeyNotFoundException){
                ErrorMessage = String.Format("GetRTRange file exception; File not found {0}",FileName);
                return null;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetRTRange uncategorized exception; Message: {0}; Stack: {1} \n "+
                    "Parameters: FileName - \"{2}\", Cache - {3} ",
                    e.Message,e.StackTrace,FileName, Cache);
                Log(ErrorMessage);
                return null;
            }
        }

        [WebMethod(Description = "List files available with a service. Use dos-style asterisk templates")]
        public string[] FileList(string FileMask, out string ErrorMessage) {
            try {
                List<string> AvailableFiles = new List<string>();
                ErrorMessage = null;
                //thanks to Michael Sorens - http://stackoverflow.com/questions/725341/how-to-determine-if-a-file-matches-a-file-mask
                string Pattern =
                         '^' +
                         Regex.Escape(FileMask.Replace(".", "__DOT__")
                                         .Replace("*", "__STAR__")
                                         .Replace("?", "__QM__"))
                             .Replace("__DOT__", "[.]")
                             .Replace("__STAR__", ".*")
                             .Replace("__QM__", ".")
                         + '$';
                Regex Template = new Regex(Pattern, RegexOptions.IgnoreCase);
                foreach(string FileName in Global.FileCashe.Keys) {
                    if(Template.IsMatch(FileName)) {
                        AvailableFiles.Add(FileName);
                    }
                }
                return AvailableFiles.ToArray();
            } catch(Exception e) {
                ErrorMessage = String.Format("GetRTRange uncategorized exception; Message: {0}; Stack: {1} \n " +
                    "Parameters: FileMask - \"{2}\" ",
                    e.Message, e.StackTrace, FileMask);
                Log(ErrorMessage);
                return null;
            }
        }



        [WebMethod(Description = "Batch analog of GetChromatogram function. Gets Retention time/Intensity pairs for particular parameter sets")]
        public double[][] GetChromatogramArray(string[] FileNames, double[] MZLow, double[] MZHigh, double[] RTLow, double[] RTHigh, out string ErrorMessage, bool Cache = true){
            try{
                //Array family of functions implemented by RunPool function
                double[][] Res = RunPool(FileNames, MZLow, MZHigh, RTLow, RTHigh, MSDataType.Chromatogram, out ErrorMessage, Cache, false);
                return Res;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetChromArray uncategorized general exception; Message: {0}; Stack: {1} \n ",
                    e.Message, e.StackTrace);
                Log(ErrorMessage);
                return null;
            }
        }

        [WebMethod(Description = "Batch analog of GetAverageSpectrum function.")]
        public double[][] GetSpectrumArray(string[] FileNames, double[] MZLow, double[] MZHigh, double[] RTLow, double[] RTHigh, out string ErrorMessage, bool Profile = false){
            try{
                //Array family of functions implemented by RunPool function
                double[][] Res = RunPool(FileNames, MZLow, MZHigh,RTLow,RTHigh,MSDataType.Spectrum, out ErrorMessage, false, Profile);
                return Res;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetSpectraArray uncategorized general exception; Message: {0}; Stack: {1} \n ",
                    e.Message, e.StackTrace);
                Log(ErrorMessage);
                return null;
            }

        }

        [WebMethod(Description = "Batch analog of GetArea function.")]
        public double[][] GetAreaArray(string[] FileNames, double[] MZLow, double[] MZHigh, double[] RTLow, double[] RTHigh, out string ErrorMessage, bool Cache = true , bool Profile = false){
            if(Cache && Profile)
                Cache = false;
            try{
                //Array family of functions implemented by RunPool function
                double[][] Res = RunPool(FileNames, MZLow, MZHigh, RTLow, RTHigh, MSDataType.Slice, out ErrorMessage, Cache, Profile);
                return Res;
            }
            catch(Exception e){
                ErrorMessage = String.Format("GetSliceArray uncategorized general exception; Message: {0}; Stack: {1} \n ",
                    e.Message, e.StackTrace);
                Log(ErrorMessage);
                return null;
            }
        }

        [WebMethod(Description = "Forces service to rescan directories to actualize list of files accessible with a service")]
        public int ServiceRescan(){
            Global.Rescan();
            return Global.FileCashe.Count;
        }


        public enum MSDataType {Chromatogram,Spectrum,Slice,Failed};
        //Since Array requests are being processed in parralel information about calls have to be delivered to 
        //working threads with single pointer to structure. Likewise, processing results after call is to be 
        //returned by the same structure
        //PoolAreaReq incapsulates all necessary information to call function from working thread
        //Also it serves DirCache.GetPointsPooled function which is optimized for speed for cache pooled requests 
        public class PoolAreaReq{
            public Entry RFEntry;                   //File to be queried
            public double MZLow;                    //RT-MZ area - low mz boundary
            public double MZHigh;                   //RT-MZ area - high mz boundary
            public double RTLow;                    //RT-MZ area - low RT boundary    
            public double RTHigh;                   //RT-MZ area - high RT boundary
            public int StartScan;                   //low RT boundary converted to scanNumber
            public int EndScan;                     //high RT boundary converted to scanNumber
            public int FileID = -1;                 //FileID inside of folder cache (if applicable)
            public MSDataType Type;                 //Type of information requested
            public bool Profile;                    //Profile parameter
            public double[] Res;                    //Query result to be returned for callee
            public List<DataPoint> Points = null;   //Intermediate query result - used in DirCache.GetPointsPooled 
            public string ErrorMessage = null;      //ErrorMessage - if there is error appeared in processing
            public bool Completed = false;          //flag if request is completed

            public class byFileID:IComparer<PoolAreaReq>{
                public int Compare(PoolAreaReq x, PoolAreaReq y){
                    if (x.FileID > y.FileID) { return 1;} 
                    if (x.FileID < y.FileID) { return -1;} 
                    return 0;
                }
            }
        }


        //only one pool operation at the time
        static object PoolLock = new object();

        double[][] RunPool(string[] FileName, double[] MZLow, double[] MZHigh, double[] RTLow, double[] RTHigh, MSDataType Type, out string ErrorMessage, bool Cache = true,  bool Profile = false){
            List<double[]> ResList = new List<double[]>();
            List<PoolAreaReq> Pool = new List<PoolAreaReq>();
            int CountCompleted = 0;
            object LockForCount = new object();
            Dictionary<string, Entry> Files =  Cache ? Global.RCHCache : Global.FileCashe;
            //Check for Array Length equality
            if (FileName.Length != MZLow.Length || 
                FileName.Length != MZHigh.Length || 
                FileName.Length != RTLow.Length || 
                FileName.Length != RTHigh.Length
                ) {
                ErrorMessage = "Argument exception: Incoming arrays are not of equal length";
                return null;
            }

            //check if request can be processed by DirCache class
            List<DirCache> toAddress = new List<DirCache>();
            for (int i = 0 ; i < FileName.Length ; i++){
                PoolAreaReq PA = new PoolAreaReq();
                string ShortFileName = Path.GetFileNameWithoutExtension(FileName[i]);
                if (Files.TryGetValue(ShortFileName,out PA.RFEntry)){
                    PA.MZLow = MZLow[i];
                    PA.MZHigh = MZHigh[i];
                    PA.RTHigh = RTHigh[i];
                    PA.RTLow = RTLow[i];
                    PA.Profile = Profile;
                    PA.Type = Type;
                    IMSFile F = PA.RFEntry.MSFile;
                    //check if request can be processed by DirCache class
                    if ((PA.RFEntry is RCHEntry) && 
                        ((PA.RFEntry as RCHEntry).Cache != null) &&
                        (!toAddress.Contains(((PA.RFEntry as RCHEntry).Cache)))) {
                        //if can be processed - collect DirCaches which will take part in process
                        toAddress.Add((PA.RFEntry as RCHEntry).Cache);
                    }
                }else{
                    PA.ErrorMessage = String.Format("Array request argument exception: File {0} has not found",FileName[i]);
                    PA.Type = MSDataType.Failed;
                    PA.Completed = true;
                    CountCompleted++;
                }
                PA.Res = null;
                Pool.Add(PA);
            }

            //Apply requests to selected DirCache objects
            if(Cache && Type != MSDataType.Spectrum) {  
                for(int i = 0 ; i < toAddress.Count ; i++) {
                    toAddress[i].GetPointsPooled(Pool);
                }

                for(int i = 0 ; i < Pool.Count ; i++) {
                    if (Pool[i].Res != null) {
                        CountCompleted++;
                    }
                }
            }

            //if not all requests were processed in DirCache objects
            //then make processsing threads for the rest of them
            if(CountCompleted < Pool.Count) {
                for(int i = 0 ; i < Pool.Count ; i++) {
                    if(Pool[i].Type != MSDataType.Failed && Pool[i].Res == null) {
                        //Pool[i].Th = new Thread(ThreadProc);
                        //Pool[i].Th.Start(Pool[i]);
                        ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadProc), Pool[i]);
                        Thread.Sleep(0);
                    }
                }
                int LocalCount;
                //wait until all the threads will finish processing
                while(true) {
                    Thread.Sleep(0);
                    LocalCount = 0;
                    foreach(PoolAreaReq P in Pool) {
                        if(P.Completed)
                            LocalCount++;                                 
                    }
                    if(LocalCount == Pool.Count)
                        break;
                }
            }

            double[][] Res = new double[Pool.Count][];
            ErrorMessage = "";
            for(int i = 0 ; i < Pool.Count ; i++){
                Res[i] = Pool[i].Res;
                if (Pool[i].ErrorMessage != null) {
                    ErrorMessage = ErrorMessage + Pool[i].ErrorMessage + "\n";
                }
            }
            if(ErrorMessage == "")
                ErrorMessage = null;
            return Res;
        }

        //Thread procedure for RunPool function 
        //it just select and run appropriate function from MSFile object 
        public void ThreadProc(Object stateInfo) {
            PoolAreaReq Req = stateInfo as PoolAreaReq;
            try{
                Req.RFEntry.Lock();
                switch(Req.Type){
                    case MSDataType.Chromatogram:{
                        Req.Res = Req.RFEntry.MSFile.GetTrace(Req.MZLow,Req.MZHigh,Req.RTLow,Req.RTHigh);
                        break;
                    }
                    case MSDataType.Spectrum:{
                        Req.Res = Req.RFEntry.MSFile.GetAveSpectrum(Req.MZLow,Req.MZHigh,Req.RTLow,Req.RTHigh,Req.Profile);
                        break;
                    }
                    case MSDataType.Slice:{
                        Req.Res = Req.RFEntry.MSFile.GetArea(Req.MZLow,Req.MZHigh,Req.RTLow,Req.RTHigh,Req.Profile);
                        break;
                    }
                }
            }
            catch(Exception e){
                Req.ErrorMessage = String.Format("Thread procedure uncategorized general exception; Message: {0}; Stack: {1} \n Parameters: MSData: {2}; "+
                    " MZLow - {3}; MZHigh - {4}; RTLow - {5}; RTHigh - {6}. ",
                    e.Message, e.StackTrace,Req.Type,Req.MZLow,Req.MZHigh,Req.RTLow,Req.RTHigh);
                Log(Req.ErrorMessage);
                Req.Res = null;
                Req.Type = MSDataType.Failed;
            }
            finally {
                Req.Completed = true;
                Req.RFEntry.Unlock();
            }
        }

        static object LogLock = new object();

        //Write message to System Application Event Log
        public static void Log(string Message){
            lock(LogLock){
                EventLog appLog = 
                    new EventLog("Application");
                appLog.Source = "Application";
                appLog.WriteEntry(Message);
            }
        }
    }
}

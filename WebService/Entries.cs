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
using System.Threading;
using System.Timers;
using ZetaLongPaths;


namespace mzAccess {

    //class which incapsulate access to lazily inialiazed data access interface for MS files
    //predessor of all data access entries contained in a service
    abstract public class Entry {
        //full path to source file - used for factual opening of the file
        protected string FullPath;
        //file name with no path or extension - key for file access
        public string FileName;
        //when fale was accessed last time
        protected  DateTime LastUsed;
        //factual interface to serve data access operations 
        protected IMSFile File_ = null;
        //property providing access to interface with lazy initialization
        abstract public IMSFile MSFile{ get; }

        public Entry(string FullPath){
            this.FullPath = FullPath;
            FileName = ZlpPathHelper.GetFileNameWithoutExtension(FullPath);
            LastUsed = DateTime.Now;
        }

        //deinitialize factual interface and free associated resources
        public void Close(){
            Lock();
            if (File_ != null){
                File_.CloseFile();
                File_ = null;
            }
            Unlock();
        }

        //Object lock to prevent simultaneous access 
        public void Lock() {
            Monitor.Enter(this);
        }

        public void Unlock() {
            Monitor.Exit(this);
        }
    }

    public class RawEntry:Entry{

        public RawEntry(string FullPath):base(FullPath) {
        }

        static object CountLock = new object();

        //ThermoActives and AgilentActives lists controls number of opened thermo and Agilent files
        //Since number of files allowed to be opened stated separately in Settings here are two separate lists
        //List contains all activated thermo files where File represent active reference to thermo file
        protected static List<RawEntry> ThermoActives = new List<RawEntry>();
        //List contains all activated agilent files where File represent active reference to agilent file
        protected static List<RawEntry> AgilentActives = new List<RawEntry>();


        public override IMSFile MSFile{
            get{
                //when someone asks for interface last use time is to be reset
                LastUsed = DateTime.Now;
                if (File_!=null) return File_;
                //if there is no active reference - file need to be activated
                lock (CountLock) {  //Lists manipulations have to be locked for single thread
                    if(FullPath.ToUpper().Contains(".RAW")) {
                        //if limit of open files is reached - before activation of new file 
                        //most old files is to be deactivated - here for thermo files
                        while(Global.Settings.ThermoFiles > 0 && ThermoActives.Count >= Global.Settings.ThermoFiles) {
                            DateTime Last = DateTime.Now;
                            RawEntry toClose = null;
                            foreach(RawEntry R in ThermoActives) {
                                if(R.LastUsed < Last)
                                    toClose = R;
                            }
                            toClose.Close();
                            ThermoActives.Remove(toClose);
                        }
                    }
                    if(FullPath.ToUpper().Contains(".D")) {
                        //if limit of open files is reached - before activation of new file 
                        //most old files is to be deactivated - here for agilent files
                        while(Global.Settings.AgilentFiles > 0 && AgilentActives.Count >= Global.Settings.AgilentFiles) {
                            DateTime Last = DateTime.Now;
                            RawEntry toClose = null;
                            foreach(RawEntry R in AgilentActives) {
                                if(R.LastUsed < Last)
                                    toClose = R;
                            }
                            toClose.Close();
                            AgilentActives.Remove(toClose);
                        }
                    }
                }
                //actual data access objects creation
                if(FullPath.ToUpper().Contains(".RAW")) {
                    ThermoMSFile TF = new ThermoMSFile(FullPath);
                    File_ = TF;
                    ThermoActives.Add(this);
                }
                if(FullPath.ToUpper().Contains(".D")) {
                    AgilentMSFile AF = new AgilentMSFile(FullPath);
                    File_ = AF;
                    AgilentActives.Add(this);
                }
                return File_;
            }
        }

        //on timer event (like once a minute) - files have not been used for long time is to be deactivated
        public static void CloseOnTimeOut(object sender, ElapsedEventArgs e){
            lock (CountLock) {
                for(int i = ThermoActives.Count-1 ; i >= 0 ; i-- ) {
                    if(Global.Settings.FileTimeOut > 0 && (DateTime.Now - ThermoActives[i].LastUsed ).TotalMinutes > Global.Settings.FileTimeOut) {
                        RawEntry R = ThermoActives[i];
                        R.Lock();
                        R.Close();
                        ThermoActives.RemoveAt(i);
                        R.Unlock();
                    }
                }
                for(int i = AgilentActives.Count-1 ; i >= 0 ; i-- ) {
                    if(Global.Settings.FileTimeOut > 0 && (DateTime.Now - AgilentActives[i].LastUsed).TotalMinutes > Global.Settings.FileTimeOut) {
                        RawEntry R = AgilentActives[i];
                        R.Lock();
                        R.Close();
                        AgilentActives.RemoveAt(i);
                        R.Unlock();
                    }
                }
            }
        }

        //closes all active files and free associated resources
        public static void CloseAll() {
            foreach(RawEntry A in AgilentActives) {
                A.Close();
            }
            AgilentActives.Clear();
            foreach(RawEntry T in ThermoActives) {
                T.Close();
            }
            ThermoActives.Clear();
        }
             
    }

    public class RCHEntry:Entry{

        public RCHEntry(string FullPath) : base(FullPath) {}

        //RCHEntry can be related to individual .rch file or to folder cache files 
        //DirCache Cache is a referrence to folder cache object
        public DirCache Cache = null;

        static object CountLock = new object();

        //List contains all activated RCH files where File represent active reference to RCH file or folder.cache
        protected static List<RCHEntry> RCHActives = new List<RCHEntry>();

        public override IMSFile MSFile{
            get{
                if(File_ == null) {
                    //if there is no active reference - file need to be activated
                    //if limit of open files is reached - before activation of new file 
                    //most old files is to be deactivated - here for rch files
                    while(Global.Settings.RCHFiles > 0 && RCHActives.Count >= Global.Settings.RCHFiles) {
                        DateTime Last = DateTime.Now;
                        RCHEntry toClose = null;
                        foreach(RCHEntry R in RCHActives) {
                            if(R.LastUsed < Last)
                                toClose = R;
                        }
                        toClose.Lock();
                        toClose.Close();
                        RCHActives.Remove(toClose);
                        toClose.Unlock();
                    }
                    if(Cache == null) {
                        //single file cache
                        File_ = new RCH0MSFile(FullPath);
                    } else {
                        //folder cache - Cache.GetFile creates and returns object of RCH1MSFile class
                        File_ = Cache.GetFile(FileName);
                    }
                    RCHActives.Add(this);
                } 
                LastUsed = DateTime.Now;
                return File_;
            }
        }

        //on timer event (like once a minute) - files have not been used for long time is to be deactivated
        public static void CloseOnTimeOut(object sender, ElapsedEventArgs e){
            lock (CountLock) {
                for(int i = RCHActives.Count-1 ; i >= 0 ; i-- ) {
                    if(Global.Settings.FileTimeOut > 0 && (DateTime.Now - RCHActives[i].LastUsed).TotalMinutes > Global.Settings.FileTimeOut) {
                        RCHEntry C = RCHActives[i];
                        C.Lock();
                        C.Close();
                        RCHActives.RemoveAt(i);
                        C.Unlock();
                    }
                }
            }
        }

        //closes all active files and free associated resources
        public static void CloseAll() {
            foreach(RCHEntry R in RCHActives) {
                R.Close();
            }
            RCHActives.Clear();
        }

        //deactivate (free resources) all folder cashes where no active references present
        public static void ClearFolderCache(List<DirCache> Caches){
            List<DirCache> ActiveFolders = new List<DirCache>();
            foreach(RCHEntry R in RCHActives) {
                if (R.Cache != null) {
                    if(!ActiveFolders.Contains(R.Cache)) {
                        ActiveFolders.Add(R.Cache);
                    }
                }
            }
            foreach(DirCache D in Caches) {
                if(!ActiveFolders.Contains(D)) {
                    D.Deactivate();
                }
            }
        }


    }
}
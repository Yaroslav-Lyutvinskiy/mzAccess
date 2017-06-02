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
using System.Web.SessionState;
using System.IO;
using System.Configuration;
using System.Timers;
using ZetaLongPaths;
using Microsoft.Web.Administration;

namespace mzAccess
{
    public class Global : System.Web.HttpApplication
    {
    // Declare signatures for Win32 LogonUser and CloseHandle APIs

        //Settings loaded from web.config - described in Configuration note
        public class Settings {
            static public int ThermoFiles=0;
            static public int AgilentFiles = 0;
            static public bool ThermoEnabled = true;
            static public bool AgilentEnabled = true;
            static public bool CacheEnabled = true;
            static public int RCHFiles = 0;
            static public int FileTimeOut = 120;
            static public List<string> RootPaths = new List<string>();
            static public void Load() {
                ThermoFiles = Convert.ToInt32(ConfigurationManager.AppSettings["ThermoFiles"]);
                ThermoEnabled = Convert.ToBoolean(ConfigurationManager.AppSettings["ThermoEnabled"]);
                AgilentFiles = Convert.ToInt32(ConfigurationManager.AppSettings["AgilentFiles"]);
                AgilentEnabled = Convert.ToBoolean(ConfigurationManager.AppSettings["AgilentEnabled"]);
                RCHFiles = Convert.ToInt32(ConfigurationManager.AppSettings["RCHFiles"]);
                CacheEnabled = Convert.ToBoolean(ConfigurationManager.AppSettings["CacheEnabled"]);
                FileTimeOut = Convert.ToInt32(ConfigurationManager.AppSettings["FileTimeOut"]);
                foreach(string Key in ConfigurationManager.AppSettings.AllKeys) {
                    if (Key.Substring(0,4) == "Root") {
                        RootPaths.Add(ConfigurationManager.AppSettings[Key]);
                    }
                }
            }
        }


        //All available raw ms data files entries
        public static Dictionary<string, Entry> FileCashe = new Dictionary<string, Entry>();
        //All available cache ms data files entries
        public static Dictionary<string, Entry> RCHCache = new Dictionary<string, Entry>();
        //All available directory caches 
        //Directory cache - file where all peak information about files in decent directory is collected
        public static List<DirCache> DirCaches = new List<DirCache>();

        //Function traverse subfolder tree of "root" folder and populate global enries dictionaries with found ms files
        //Function is to be used in inital setup or rescan of the service
        static public void TraverseTree(string root)
        {
            // Data structure to hold names of subfolders to be
            // examined for files.
            if (root == "") return;
            //stack of subfolders to search
            Stack<string> dirs = new Stack<string>(20);

            if (!ZlpIOHelper.DirectoryExists(root)){
                throw new ArgumentException();
            }
            dirs.Push(root);
            while (dirs.Count > 0){
                string currentDir = dirs.Pop();
                ZlpDirectoryInfo  di = new ZlpDirectoryInfo(currentDir);
                ZlpDirectoryInfo[] subDirs;
                try{
                    subDirs = ZlpIOHelper.GetDirectories(currentDir);
                }
                catch (Exception e){
                    MSDataService.Log("TraverseTree: "+e.Message);
                    continue;
                }

                ZlpFileInfo[] files = null;
                try{
                    files = ZlpIOHelper.GetFiles(currentDir);
                }
                catch (Exception e){
                    MSDataService.Log("TraverseTree: "+e.Message);
                    continue;
                }

                foreach (ZlpFileInfo file in files){
                    try{
                        // .raw files for thermo files
                        string Ext = file.Extension;
                        if(Ext == ".raw") {
                            if(Settings.ThermoEnabled) {
                                RawEntry FC = new RawEntry(file.FullName);
                                try {
                                    FileCashe.Add(FC.FileName, FC);
                                }
                                catch(Exception e) {
                                    MSDataService.Log("TraverseTree-AddFiles: " + FC.FileName + e.Message);
                                }
                            }
                        }
                        if(Settings.CacheEnabled) {
                            //RCH files for single file cache
                            if(Ext == ".rch") {
                                RCHEntry FC = new RCHEntry(file.FullName);
                                RCHCache.Add(Path.GetFileNameWithoutExtension(file.Name), FC);
                            }
                            //folder.cache - name for combined cache for whole folder
                            if(file.Name == "folder.cache") {
                                DirCache JCache = new DirCache(file.FullName);
                                DirCaches.Add(JCache);
                                List<RCHEntry> Ents = JCache.Files;
                                foreach(RCHEntry Ent in Ents) {
                                    RCHCache.Add(Ent.FileName, Ent);
                                }
                            }
                        }
                    }
                    catch (Exception e){
                        MSDataService.Log("TraverseTree: "+e.Message);
                        continue;
                    }
                }
                foreach (ZlpDirectoryInfo dir in subDirs){
                    try{
                        //agilent raw files are actually folders with .d extension
                        if (dir.FullName.IndexOf(".d")==dir.FullName.Length-2){
                            if(Settings.AgilentEnabled) {
                                RawEntry FC = new RawEntry(dir.FullName);
                                FileCashe.Add(FC.FileName, FC);
                            }
                        }else{
                            dirs.Push(dir.FullName);
                        }
                    }catch(Exception e){
                        MSDataService.Log("TraverseTree - AddDir: "+dir.FullName+e.Message);
                        continue;
                    }
                }
            }
        }

        //deactivate (free resources) all excessive folder cashes 
        static void ClearFolderCache(object sender, ElapsedEventArgs e){
            RCHEntry.ClearFolderCache(DirCaches);
        }

        //(re)initialization of service
        static public void Rescan(){
            FileCashe.Clear();
            RCHCache.Clear();
            Settings.Load();
            RawEntry.CloseAll();
            RCHEntry.CloseAll();
            foreach(DirCache D in DirCaches) {
                D.Deactivate();
            }
            DirCaches.Clear();
            foreach(string R in Settings.RootPaths) {
                TraverseTree(R);
            }
        }

        static public void ServiceRecycle()
        {
            ServerManager serverManager = new ServerManager();
            ApplicationPoolCollection applicationPoolCollection = serverManager.ApplicationPools;
            foreach (ApplicationPool applicationPool in applicationPoolCollection)
            {
                if (applicationPool.Name == "DefaultAppPool")
                {
                    applicationPool.Recycle();
                }
            }
            // CommitChanges to persist the changes to the ApplicationHost.config.
            serverManager.CommitChanges();
            return;
        }


        static System.Timers.Timer TimeoutTimer;

        void Application_Start(object sender, EventArgs m){
            TimeoutTimer = new System.Timers.Timer(60000);//once a minute
            //onTimer event clears resources of files which has not been used for a long time (>Settings FileTimeout)
            TimeoutTimer.Elapsed += RawEntry.CloseOnTimeOut;
            TimeoutTimer.Elapsed += RCHEntry.CloseOnTimeOut;
            TimeoutTimer.Elapsed += ClearFolderCache;
            TimeoutTimer.Start();
            Rescan();
        }

        void Application_End(object sender, EventArgs e)
        {
            foreach( KeyValuePair<string, Entry> FC in FileCashe){
                FC.Value.Close();
            }
            foreach( KeyValuePair<string, Entry> FC in RCHCache){
                FC.Value.Close();
            }
            //  Code that runs on application shutdown
        }

        void Application_Error(object sender, EventArgs e){
            // Code that runs when an unhandled error occurs
            MSDataService.Log(e.ToString());
        }

        void Session_Start(object sender, EventArgs e)
        {
            HttpSessionState S = Session;
            // Code that runs when a new session is started
        }

        void Session_End(object sender, EventArgs e)
        {
            //finalisation code here 
            // Code that runs when a session ends. 
            // Note: The Session_End event is raised only when the sessionstate mode
            // is set to InProc in the Web.config file. If session mode is set to StateServer 
            // or SQLServer, the event is not raised.
        }
    }
}

﻿using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.ComponentModel;
using System.Text;
using GalaSoft.MvvmLight.Messaging;
using SimpleCPUMiner.Messages;
using System.Collections.Generic;
using GalaSoft.MvvmLight.Threading;
using SimpleCPUMiner.View;
using System.Windows.Threading;
using System.Runtime;
using SimpleCPUMiner.Model;
using static SimpleCPUMiner.Consts;
using System.Linq;
using Microsoft.Win32;

namespace SimpleCPUMiner.ViewModel
{
    public class MainViewModel : ViewModelBase
    {
        public RelayCommand<int> startMiningCommand { get; private set; }
        public RelayCommand<int> ShowAboutCommand { get; private set; }
        public ObservableCollection<MinerSettings> MinerSettingsList { get; set; }
        private UserConfiguration userConfiguration;
        public MinerSettings SelectedMinerSettings { get; set;}
        public RelayCommand<Window> CloseWindowCommand { get; private set; }
        public RelayCommand<string> CpuAffinityCommand { get; private set; }
        public RelayCommand<int> LaunchOnWindowsStartup { get; private set; }
        public RelayCommand<string> PoolAddCommand { get; private set; }
        public RelayCommand<string> PoolRemoveCommand { get; private set; }
        public RelayCommand<string> PoolModifyCommand { get; private set; }
        public Thread MinerThread { get; set; }
        public ObservableCollection<string> MinerOutputString { get; set;}
        public List<PoolSettings> Pools { get; set; }
        public int SelectedPoolIndex { get; set; }
        public bool IsIdle { get; set; }
        public bool isEnabledCPUThreadAuto { get; set; }
        private PoolSettings _selectedPool;
        private string _threadNumber;
        private bool isAutostartMining;
        private int startingDelayInSec;
        private string startMiningButtonContent { get; set; }
        private int tempDelayInSec;
        private bool isCountDown = false;
        private DispatcherTimer timer;

        public PoolSettings SelectedPool {
            get
            {
                return _selectedPool;
            }
            set
            {
                _selectedPool = value;
            }
        }

        public string ThreadNumber
        {
            get { return _threadNumber; }
            set
            {
                _threadNumber = value;
                SelectedMinerSettings.NumberOfThreads = value;

                if (value.Equals("0") && IsIdle)
                    isEnabledCPUThreadAuto = true;
                else
                    isEnabledCPUThreadAuto = false;

                RaisePropertyChanged(nameof(IsEnabledCPUThreadAuto));
            }
        }

        public bool IsEnabledCPUThreadAuto
        {
            get
            {
                if (!IsIdle) return false;
                else return isEnabledCPUThreadAuto;
            }
        }

        public bool IsAutoStartMining
        {
            get
            {
                return isAutostartMining;
            }
            set
            {
                isAutostartMining = value;
                SelectedMinerSettings.IsAutostartMining = value;
                saveMinerSettings();
            }
        }

        public int StartingDelayInSec
        {
            get {
                
                return startingDelayInSec;
            }
            set
            {
                startingDelayInSec = value;
                SelectedMinerSettings.StartingDelayInSec = value;
                RaisePropertyChanged(nameof(StartingDelayInSec));
            }
        }

        public string MainWindowTitle
        {
            get
            {
#if DEBUG
                return "Simple Miner " + Consts.VersionNumber + " - DEBUG MODE!!!";
#else
                return "Simple Miner " + Consts.VersionNumber;
#endif
            }
        }

        public string StartMiningButtonContent
        {
            get
            {
                if (IsIdle == true)
                {
                    isCountDown = false;
                    RaisePropertyChanged(nameof(StartingDelayInSec));
                    return "Start mining";
                }
                else
                {
                    if (isCountDown == true)
                    {
                        return $"Stop mining ({tempDelayInSec})";
                    }
                    else
                    {
                        return $"Stop mining";
                    }
                }
            }
        
            set
            {
                StartMiningButtonContent = (IsIdle == true) ? "Start mining" : "Stop mining";
                RaisePropertyChanged(nameof(StartMiningButtonContent));
            }
        }

        public Action RefreshPools { get; internal set; }

        ExeManager MyManager;

        public MainViewModel()
        {
            Pools = new List<PoolSettings>();
            terminateProcess();
            loadConfigFile();
            IsIdle = true;
            MinerOutputString = new ObservableCollection<string>();
            SelectedMinerSettings = userConfiguration.SettingsList[userConfiguration.SelectedConfigIndex];
            ThreadNumber = SelectedMinerSettings.NumberOfThreads;
            isAutostartMining = SelectedMinerSettings.IsAutostartMining;
            StartingDelayInSec = SelectedMinerSettings.StartingDelayInSec;
            SetCommands();
            MinerSettingsList = new ObservableCollection<MinerSettings>();
            Messenger.Default.Register<MinerOutputMessage>(this, msg => { minerOutputReceived(msg); });
            Messenger.Default.Register<CpuAffinityMessage>(this, msg => { CpuAffinityReceived(msg); });
            tempDelayInSec = SelectedMinerSettings.StartingDelayInSec;
            SelectedMinerSettings.IsLaunchOnWindowsStartup = Utils.IsStartupItem();
            if (SelectedMinerSettings.IsAutostartMining) autoStartMiningWithDelay();
            SavePools(null);
        }

        private void SetCommands()
        {
            startMiningCommand = new RelayCommand<int>(startMining);
            CloseWindowCommand = new RelayCommand<Window>(CloseWindow);
            LaunchOnWindowsStartup = new RelayCommand<int>(setStartup);
            ShowAboutCommand = new RelayCommand<int>(ShowAbout);
            CpuAffinityCommand = new RelayCommand<string>(ShowCpuAffinity);
            PoolAddCommand = new RelayCommand<string>(AddPool);
            PoolModifyCommand = new RelayCommand<string>(ModifyPool);
            PoolRemoveCommand = new RelayCommand<string>(RemovePool);
        }

        private void AddPool(string obj)
        {
            var poolSettingWindow = new PoolForm();
            var poolVM = new PoolFormViewModel()
            {
                Pool = new PoolSettings() { IsRemoveable = true, IsGPUPool = true, IsCPUPool = true, CoinType = CoinTypes.OTHER }
            };

            poolVM.AddPool = AddPool;
            poolVM.UpdateCoinType();
            poolSettingWindow.DataContext = poolVM;
            poolSettingWindow.ShowDialog();
        }

        private void ModifyPool(string obj)
        {
            var poolSettingWindow = new PoolForm();
            var poolVM = new PoolFormViewModel() {
                Pool = new PoolSettings() {
                    CoinType = SelectedPool.CoinType,
                    FailOverPriority = SelectedPool.FailOverPriority,
                    IsCPUPool = SelectedPool.IsCPUPool,
                    IsGPUPool = SelectedPool.IsGPUPool,
                    IsFailOver = SelectedPool.IsFailOver,
                    IsMain = SelectedPool.IsMain,
                    IsRemoveable = SelectedPool.IsRemoveable,
                    Password = SelectedPool.Password,
                    Port = SelectedPool.Port,
                    URL = SelectedPool.URL,
                    Username = SelectedPool.Username
                }
            };

            poolVM.UpdatePoolList = SavePools;
            poolVM.UpdateCoinType();
            poolSettingWindow.DataContext = poolVM;
            poolSettingWindow.ShowDialog();
        }

        private void SavePools(PoolSettings ps)
        {
            if(ps!= null)
            {
                if (ps.IsMain)
                {
                    var mainPool = Pools.Where(x => x.IsMain).FirstOrDefault();
                    if (mainPool != null)
                    {
                        mainPool.IsMain = false;
                    }
                }

                SelectedPool.CoinType = ps.CoinType;
                SelectedPool.URL = ps.URL;
                SelectedPool.FailOverPriority = ps.FailOverPriority;
                SelectedPool.IsCPUPool = ps.IsCPUPool;
                SelectedPool.IsFailOver = ps.IsFailOver;
                SelectedPool.IsGPUPool = ps.IsGPUPool;
                SelectedPool.IsMain = ps.IsMain;
                SelectedPool.Password = ps.Password;
                SelectedPool.Port = ps.Port;
                SelectedPool.Username = ps.Username;
            }

            var poolzToSave = Pools.ToList();
            Utils.SerializeObject(Consts.PoolFilePath, poolzToSave);
            Pools = Pools.OrderByDescending(x=>x.IsMain).ThenByDescending(x=>x.IsFailOver).ThenBy(x=>x.FailOverPriority).ToList();
            RaisePropertyChanged(nameof(Pools));
            RefreshPools?.Invoke();
        }

        private void RemovePool(string obj)
        {

            MessageBoxResult messageBoxResult = MessageBox.Show("Are you sure?", "Delete Confirmation", MessageBoxButton.YesNo);
            if (messageBoxResult == MessageBoxResult.Yes)
            {
                if (SelectedPool.IsRemoveable)
                    Pools.Remove(SelectedPool);
                else
                    MessageBox.Show("The selected pool can't be removed.");

                SavePools(null);
            }
        }

        private void AddPool(PoolSettings ps)
        {
            if (ps.IsMain)
            {
                var mainPool = Pools.Where(x => x.IsMain).FirstOrDefault();
                if (mainPool != null)
                {
                    mainPool.IsMain = false;
                }
            }

            Pools.Add(ps);
            SavePools(null);
        }

        private void CpuAffinityReceived(object msg)
        {
            var mm = msg as CpuAffinityMessage;
            if (mm != null)
            {
                SelectedMinerSettings.CPUAffinity = mm.CpuAffinity;
                RaisePropertyChanged(nameof(SelectedMinerSettings));
            }
        }

        private void ShowCpuAffinity(string pThreadNumber)
        {
            CpuAffinity ca = new CpuAffinity();
            var vm = new CpuAffinityViewModel();

            if (String.IsNullOrEmpty(_threadNumber))
                vm.ThreadNumber = "0";
            else
                vm.ThreadNumber = _threadNumber;

            vm.CalcCpuAffinity();
            ca.DataContext = vm;
            ca.ShowDialog();
        }

        private void ShowAbout(int obj)
        {
            var window = new About();
            window.ShowDialog();
        }

        //Automatikusan elindítja a bányászást
        private void autoStartMiningWithDelay()
        {
            if (SelectedMinerSettings.StartingDelayInSec > 0)
            {
                isCountDown = true;
                IsIdle = false;
                timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Tick += Timer_Elapsed;
                timer.Start();
            }
        }

        private void Timer_Elapsed(object sender, EventArgs e)
        {
            if (IsIdle == false && isCountDown == true)
            {
                tempDelayInSec--;
                RaisePropertyChanged(nameof(StartingDelayInSec));
                RaisePropertyChanged(nameof(StartMiningButtonContent));

                if (tempDelayInSec == 0)
                {
                    timer.Stop();
                    isCountDown = false;
                    IsIdle = true;
                    startMining(0);
                }
            }
        }

        /// <summary>
        /// Beállítja, hogy induljon-e a Windowssal a program
        /// </summary>
        private void setStartup(int obj)
        {
            if (SelectedMinerSettings.IsLaunchOnWindowsStartup == true) Utils.AddProgramToStartup();
            else Utils.RemoveProgramFromStartup();
        }

        private void minerOutputReceived(MinerOutputMessage msg)
        {
            DispatcherHelper.CheckBeginInvokeOnUI(() => {
                if (MinerOutputString.Count > 100)
                    MinerOutputString.RemoveAt(0);

                MinerOutputString.Add(msg.OutputText);
                if (msg.IsError)
                {
                    IsIdle = true;
                    RaisePropertyChanged(nameof(IsIdle));
                    RaisePropertyChanged(nameof(StartMiningButtonContent));
                }
            });
        }

        internal CancelEventHandler ApplicationClosing()
        {
            var ce = new CancelEventHandler((object sender, System.ComponentModel.CancelEventArgs e) => {
                terminateProcess();
            });
            return ce;
        }

        private void terminateProcess()
        {
            try
            {
                if (MinerThread != null && MinerThread.IsAlive)
                {
                    MinerThread.Abort();

                    Utils.TryKillProcess(Consts.ProcessName);

                    MinerThread.Join();
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                MessageBox.Show(ex.Message);
#endif
            }
        }

        /// <summary>
        /// A miner program elindítása a megadott paraméterekkel.
        /// </summary>
        private void startMining(int window)
        {
            if (Utils.CheckInstallation() != CheckDetails.Installed)
            {
                if (Utils.InstallMiners() != InstallDetail.Installed)
                {
                    MessageBoxResult mResult = MessageBox.Show(String.Format("Simple Miner is corrupted, may the antivirus application blocked it. \nIf you already add it as an exception in your antivirus application, please try to download the miner again.\nWould you like to navigate to Simple Miner download page?"), "Miner error", MessageBoxButton.YesNo, MessageBoxImage.Stop);
                    if (mResult == MessageBoxResult.No)
                    {
                        return;
                    }
                    else
                    {
                        Process.Start(Consts.MinerDownload);
                        return;
                    }
                }
            }

            //talán csökkenti a memória fragmentációt
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            if (IsIdle)
            {
#if DEBUG
                MessageBox.Show(generateMinerCall());
#endif
                MinerOutputString.Clear();
                saveMinerSettings();
                MyManager = new ExeManager(Consts.ApplicationPath);

                MinerThread = new Thread(() => MyManager.ExecuteResource(generateMinerCall()));
                //MinerThread.Priority = ThreadPriority.Highest;
                MinerThread.Start();
                IsIdle = false;
            }
            else
            {
                terminateProcess();
                IsIdle = true;
                if (!isCountDown){ Messenger.Default.Send(new MinerOutputMessage() { OutputText = "Mining finished!" }); }
            }
            RaisePropertyChanged(nameof(IsIdle));
            RaisePropertyChanged(nameof(StartMiningButtonContent));
            RaisePropertyChanged(nameof(IsEnabledCPUThreadAuto));
        }

        //Betölti a konfigurációt a bin fájlból vagy ha nem találja vagy nem beolvasható, akkor beállítja a default értékeket 
        private void loadConfigFile()
        {
            if (!File.Exists(Consts.ConfigFilePath))
            {
                setDefaultConfigValues();
                //Ha még nincs hozzáadva a Defender kivételekhez az aktuális mappa és nincs meg a config file sem, akkor hozzáadjuk
                if (!isExcludedThisFolder())
                {
                    MessageBoxResult mResult = MessageBox.Show("Do you want to add this folder to Defender exlusions?", "Defender exclusion", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (mResult == MessageBoxResult.Yes)
                    {
                        addToDefenderExclusions();
                    }
                }
            }
            else
            {
                userConfiguration = Utils.DeSerializeObject<UserConfiguration>(Consts.ConfigFilePath);

                if (userConfiguration == null)
                {
                    setDefaultConfigValues();
                }
            }

            if(!File.Exists(Consts.PoolFilePath))
            {
                setDefaultPool();
            }
            else
            {
                var poolzToLoad = Utils.DeSerializeObject<List<PoolSettings>>(Consts.PoolFilePath);

                if(poolzToLoad != null)
                    poolzToLoad.ForEach(Pools.Add);              

                if (Pools == null || Pools.Count == 0)
                    setDefaultPool();
            }

            SelectedPool = Pools[0];
            SelectedPoolIndex = 0;       
        }

        /// <summary>
        /// Sets the default pool.
        /// </summary>
        private void setDefaultPool()
        {
            if (Pools == null)
                Pools = new List<PoolSettings>();

            Pools.Add(new PoolSettings()
            {
                ID = 0,
                CoinType = CoinTypes.XMR,
                IsCPUPool = true,
                IsGPUPool = true,
                IsFailOver = false,
                IsMain = true,
                IsRemoveable = true,
                URL = "pool.supportxmr.com",
                Port = 5555,
                Username = "4BrL51JCc9NGQ71kWhnYoDRffsDZy7m1HUU7MRU4nUMXAHNFBEJhkTZV9HdaL4gfuNBxLPc3BeMkLGaPbF5vWtANQrGBNaWKx1HPVM1Y7t",
                Password = Consts.DefaultSettings.Password
            });

            Pools.Add(new PoolSettings()
            {
                ID = 1,
                CoinType = CoinTypes.ETN,
                IsCPUPool = true,
                IsGPUPool = true,
                IsFailOver = false,
                IsMain = false,
                IsRemoveable = true,
                URL = "pool.etn.spacepools.org",
                Port = Consts.DefaultSettings.Port,
                Username = "etnkEKwVnTfcwuBnSKuQgaQetJ7SiqnH3c6TU1HXBgFkSrtwaviEkBijMVrMhGi1aP4hPKJwaaKp5Rqhxi4pyP9i26A9dRJEhW",
                Password = Consts.DefaultSettings.Password
            });

            Pools.Add(new PoolSettings() {
               ID = 2,
               CoinType = CoinTypes.SUMO,
               IsCPUPool = true,
               IsGPUPool = true,
               IsFailOver = false,
               IsMain = false,
               IsRemoveable = true,
               URL = "mine.sumo.fairpool.xyz",
               Port = 5555,
               Username = "Sumoo2rnbqd1QtcNsu15T18VTr96wpHPuECGFEsn39pEXj6zPhZA5CWZdPJA5PHWz64akcbi184QZG9ago6no9ZvZbrwwxk8E6b",
               Password = Consts.DefaultSettings.Password
            });

            Pools.Add(new PoolSettings()
            {
                ID = 3,
                CoinType = CoinTypes.BCN,
                IsCPUPool = true,
                IsGPUPool = true,
                IsFailOver = false,
                IsMain = false,
                IsRemoveable = true,
                URL = "bytecoin.uk",
                Port = 7777,
                Username = "29msgPgmFVySsENM7VvAFB6oim2zzUDWtThgjFWqoWuaVvTYjB2wi6hfNCezqRpKfLJf5dmANoy6uA2bGtZ3uT5fJLRUfqe",
                Password = Consts.DefaultSettings.Password
            });

            Pools.Add(new PoolSettings()
            {
                ID = 4,
                CoinType = CoinTypes.KRB,
                IsCPUPool = true,
                IsGPUPool = true,
                IsFailOver = false,
                IsMain = false,
                IsRemoveable = true,
                URL = "krb.crypto-coins.club",
                Port = 6666,
                Username = "KeHZfRMcpaAhqU9apvRj5eXwPjAKmR69agvKK42mYmzL9W79mW7QcDPKwQG5ouAoEY2hqTwRagnMEKXVvi6uzw3Y5nHEnrw",
                Password = Consts.DefaultSettings.Password
            });
        }


        /// <summary>
        /// Alapértékek beállítása
        /// </summary>
        private void setDefaultConfigValues()
        {
            userConfiguration = new UserConfiguration();
            userConfiguration.SelectedConfigIndex = 0;
            userConfiguration.SettingsList.Add(new MinerSettings()
            {
                URL = Consts.DefaultSettings.URL,
                Port = Consts.DefaultSettings.Port,
                Username = Consts.DefaultSettings.UserName,
                Password = Consts.DefaultSettings.Password,
                DonateLevel = Consts.DefaultSettings.DonateLevel,
                IsKeepalive = Consts.DefaultSettings.IsKeepalive,
                NoColor = Consts.DefaultSettings.IsNoColor,
                IsLogging = Consts.DefaultSettings.IsLogging,
                IsNicehashSupport = Consts.DefaultSettings.IsNicehashSupport,
                IsBackgroundMining = Consts.DefaultSettings.IsBackgroundMining,
                IsLaunchOnWindowsStartup = Consts.DefaultSettings.IsLaunchOnWindowsStartup,
                IsAutostartMining = Consts.DefaultSettings.IsAutostartMining,
                StartingDelayInSec = Consts.DefaultSettings.StartingDelayInSec,
                IsMinimizeToTray = Consts.DefaultSettings.IsMinimizeToTray,
                NumberOfThreads = Consts.DefaultSettings.NumberOfThreads,
                MaxCPUUsage = Consts.DefaultSettings.MaxCpuUsage,
                RetryPause = Consts.DefaultSettings.RetryPause,
                NumOfRetries = Consts.DefaultSettings.NumOfRetries
            });
        }

        /// <summary>
        /// Legenerálja a felparaméterezett miner hívást.
        /// </summary>
        /// <returns>Az exe paraméterezett hívása</returns>
        private string generateMinerCall()
        {
            var mainPool = Pools.Where(x => x.IsMain).FirstOrDefault();
            var listPool = Pools.Where(x => !x.IsMain).OrderBy(y => y.FailOverPriority).Reverse().ToList();
            

            StringBuilder sb = new StringBuilder();
            if (mainPool != null)
                sb.Append($" -o {mainPool.URL}:{mainPool.Port} -u {mainPool.Username} -p {mainPool.Password}");

            sb.Append((SelectedMinerSettings.IsKeepalive == true) ? " -k" : "");

            foreach(var item in listPool.Where(x => x.IsFailOver))
            {
                sb.Append($" -o {item.URL}:{item.Port} -u {item.Username} -p {item.Password}");
            }

            sb.Append((SelectedMinerSettings.Algo == null) ? "" : " -a " + SelectedMinerSettings.Algo);
            sb.Append((SelectedMinerSettings.NumberOfThreads.Equals("0") || SelectedMinerSettings.NumberOfThreads.Equals("")) ? "" : $" -t {SelectedMinerSettings.NumberOfThreads}");
            sb.Append((SelectedMinerSettings.AlgorithmVariation == null) ? "" : $" -v {SelectedMinerSettings.AlgorithmVariation}");          
            sb.Append((SelectedMinerSettings.NumOfRetries == 0) ? " -r 3" : $" -r {SelectedMinerSettings.NumOfRetries}");
            sb.Append((SelectedMinerSettings.RetryPause == 0) ? " -R 10" : $" -R {SelectedMinerSettings.RetryPause}");
            sb.Append(!String.IsNullOrEmpty(SelectedMinerSettings.CPUAffinity) ? $" --cpu-affinity {SelectedMinerSettings.CPUAffinity}" : "");
            sb.Append((SelectedMinerSettings.NoHugePages == true) ? " --no-huge-pages" : "");
            sb.Append((SelectedMinerSettings.NoColor == true) ? " --no-color" : "");
            sb.Append($" --donate-level={SelectedMinerSettings.DonateLevel}");
            sb.Append((SelectedMinerSettings.UserAgent == true) ? " --user-agent" : "");
            sb.Append((SelectedMinerSettings.IsBackgroundMining == true) ? " -B" : "");
            sb.Append((SelectedMinerSettings.JSONConfigfile == null) ? "" : $" -c {SelectedMinerSettings.JSONConfigfile}");
            sb.Append((SelectedMinerSettings.IsLogging == true) ? $" -l {DateTime.Now.Year}{DateTime.Now.Month}{DateTime.Now.Day}.log" : "");
            sb.Append((SelectedMinerSettings.MaxCPUUsage <= 1 || !SelectedMinerSettings.NumberOfThreads.Equals("0")) ? "" : $" --max-cpu-usage={SelectedMinerSettings.MaxCPUUsage}");
            sb.Append((SelectedMinerSettings.Safe == true) ? " --safe" : "");
            sb.Append((SelectedMinerSettings.IsNicehashSupport == true) ? " --nicehash" : "");
            sb.Append((SelectedMinerSettings.PrintHashRate == 0) ? "" : $" --print-time={SelectedMinerSettings.PrintHashRate}");

            return sb.ToString();
        }

        private void CloseWindow(Window window)
        {
            saveMinerSettings();

            if (window != null && !IsIdle)
            {
                MessageBoxResult mResult = MessageBox.Show("Simple Miner is actively working.\nDo you really want to exit?","Exit confirmation",MessageBoxButton.YesNo,MessageBoxImage.Warning);
                if (mResult == MessageBoxResult.No)
                {
                    return;
                }

                window.Close();
            }
            else if (window != null)
            {
                window.Close();
            }
        }

        /// <summary>
        /// Elmenti a felhasználó által megadott beállításokat
        /// </summary>
        private void saveMinerSettings()
        {
            Utils.SerializeObject(Consts.ConfigFilePath,userConfiguration);
        }

        /// <summary>
        /// Megvizsgálja, hogy hozzá van-e adva a program mappája a Defender kivételekhez
        /// </summary>
        /// <returns></returns>
        private bool isExcludedThisFolder()
        {
            RegistryKey OurKey = Registry.LocalMachine;
            OurKey = OurKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths");

            foreach (string Keyname in OurKey.GetValueNames())
            {
                Console.WriteLine(Keyname);

                //Ha már hozzá van adva
                if (Keyname == Consts.ApplicationPath) return true;
            }

            return false;
        }

        private void addToDefenderExclusions()
        {
            var psi = new ProcessStartInfo();
            psi.CreateNoWindow = false;
            psi.FileName = Consts.addToDefenderExclusionBatchFilePath;
            psi.Verb = "runas"; //this is what actually runs the command as administrator
            psi.UseShellExecute = true;
            var process = new Process();

            try
            {
                // Delete the file if it exists.
                if (File.Exists(Consts.addToDefenderExclusionBatchFilePath))
                {
                    File.Delete(Consts.addToDefenderExclusionBatchFilePath);
                }

                // Create the file.
                using (StreamWriter writer = new StreamWriter(Consts.addToDefenderExclusionBatchFilePath))
                {
                    writer.Write("powershell -Command \" & {Add-MpPreference -ExclusionPath '" + Consts.ApplicationPath + "';}\"");
                }

                process.StartInfo = psi;
                process.Start();
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                Debug.Write(ex.Message);
            }
            finally
            {
                if (File.Exists(Consts.addToDefenderExclusionBatchFilePath))
                {
                    File.Delete(Consts.addToDefenderExclusionBatchFilePath);
                }
            }
        }
    }

    class ExeManager
    {
        string rPath;

        public ExeManager(string DestinationPath)
        {
            rPath = Path.Combine(DestinationPath, Consts.ExeFileName);
        }

        /// <summary>
        /// Kiírja a resourcesben lévő exe fájlt, majd futtatja.
        /// </summary>
        public void ExecuteResource(string _parameters)
        {
            Process x = null;
            try
            {
                var startInfo = new ProcessStartInfo() {
                    Arguments = _parameters,
                    FileName = rPath,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                x = Process.Start(startInfo);
               // x.PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
                x.EnableRaisingEvents = true;
                x.OutputDataReceived += X_OutputDataReceived;
                x.BeginOutputReadLine();
                x.WaitForExit();

                Messenger.Default.Send<MinerOutputMessage>(new MinerOutputMessage() { OutputText = "Something went wrong! Check miner parameters!", IsError = true });
            }
            catch (ThreadAbortException)
            {
                if (x != null && !x.HasExited)
                {
                    x.Kill();
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error");
            }
        }

        private void X_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Messenger.Default.Send<MinerOutputMessage>(new MinerOutputMessage() { OutputText = e.Data });
        }

        private void sendOutputData(object sender, DataReceivedEventArgs e)
        {
            MessageBox.Show(e.Data);
            //Messenger.Default.Send(new MinerOutputMessage() { OutputText = "Itt az üzenet!" });
        }
    }
}
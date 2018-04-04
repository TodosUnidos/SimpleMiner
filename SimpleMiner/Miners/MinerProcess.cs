﻿using GalaSoft.MvvmLight.Messaging;
using SimpleCPUMiner.Hardware;
using SimpleCPUMiner.Messages;
using SimpleCPUMiner.Miners.Stratum;
using SimpleCPUMiner.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static SimpleCPUMiner.Consts;

namespace SimpleCPUMiner.Miners
{
    public class MinerProcess
    {
        public OpenCLDevice[] Devices { get; set; }
        public MinerType MinerType { get; set; }
        public Algorithm Algorithm { get; set; }
        public List<Miner> Miners { get; set; }
        public Thread MinerThread { get; set; }
        public SimpleMinerSettings Settings { get; set; }
        private List<PoolSettingsXml> _pools = new List<PoolSettingsXml>();
        ExeManager exeMinerManager;

        public bool InitalizeMiner(List<PoolSettingsXml> pools)
        {
            bool mehetAMenet = true;
            Miners = new List<Miner>();
            switch (Algorithm)
            {
                case Algorithm.CryptoNight:
                default:
                    var main = pools.Where(y => y.IsMain).FirstOrDefault();
                    if(main != null && string.IsNullOrEmpty(main.Username))
                    {
                        string msg = $"ERROR: Wallet address is empty on the main pool ({main.URL}). {Environment.NewLine}Add your wallet address and try again!";
                        MessageBox.Show(msg,"Error during start",MessageBoxButton.OK,MessageBoxImage.Error);
                        Messenger.Default.Send<MinerOutputMessage>(new MinerOutputMessage() { OutputText = msg, IsError = true });
                        mehetAMenet = false;
                    }

                    _pools.AddRange(pools.OrderByDescending(y => y.IsMain).ThenBy(y => y.FailOverPriority));
                    _pools.Where(x => string.IsNullOrEmpty(x.Username) && !x.IsMain).ToList().ForEach(x => Messenger.Default.Send<MinerOutputMessage>(new MinerOutputMessage() { OutputText = $"WARNING: Wallet empty for failover pool: {x.URL}:{x.Port}", IsError = true }));
                    _pools.ForEach(x => Messenger.Default.Send<MinerOutputMessage>(new MinerOutputMessage() { OutputText = $"Pool added to miner: {x.URL}:{x.Port}, Main:{x.IsMain}, Failover order: {x.FailOverPriority}" }) );
                    break;
            }

            return mehetAMenet;
        }

        /// <summary>
        /// Miner indítása
        /// </summary>
        /// <returns></returns>
        public bool StartMiner()
        {
            bool success = false;

            if (_pools != null && _pools.Count>0)
            {
                if (MinerType == MinerType.GPU)
                {
                    switch (Algorithm)
                    {
                        case Algorithm.CryptoNight:
                        default:
                            Stratum.Stratum mainPool = createStratumFromPoolSettings(_pools, Algorithm.CryptoNight);
                            if (mainPool != null && mainPool is CryptoNightStratum)
                            {
                                foreach (var device in Devices.Where(x => x.IsEnabled))
                                {
                                    for (int i = 0; i < device.Threads; ++i)
                                    {
                                        OpenCLCryptoNightMiner miner = null;
                                        try
                                        {
                                            miner = new OpenCLCryptoNightMiner(device);
                                            ThreadPool.QueueUserWorkItem(delegate
                                            {
                                                miner.Start(mainPool as CryptoNightStratum, device.Intensity, device.WorkSize, true);
                                            });
                                            Miners.Add(miner);
                                        }
                                        catch(Exception ex)
                                        {
                                            Messenger.Default.Send<MinerOutputMessage>(new MinerOutputMessage() { OutputText = $"Faild to start GPU miner on Device# {device.ADLAdapterIndex} Thread# {i+1}", Exception = ex, IsError = true });
                                        }
                                    }
                                }
                                success = true;
                            }
                            break;
                    }
                }
                else if (MinerType == MinerType.CPU)
                {
                    exeMinerManager = new ExeManager(Consts.ApplicationPath, Consts.ExeFileName);
                    string minerParameter = generateMinerCall();
                    MinerThread = new Thread(() => exeMinerManager.ExecuteResource(minerParameter));
                    MinerThread.Start();
                    success = true;
                }
            }
            else
            {
                Messenger.Default.Send<MinerOutputMessage>(new MinerOutputMessage() { OutputText = "WARNING: No main pool selected." });
            }

            return success;
        }

        private Stratum.Stratum createStratumFromPoolSettings(List<PoolSettingsXml> pPoolSettings, Algorithm pAlgorithm)
        {
            Stratum.Stratum result = null;
            switch (pAlgorithm)
            {
                case Algorithm.CryptoNight:
                default:
                    result = new CryptoNightStratum(pPoolSettings);
                    break;
            }
            return result;
        }

        /// <summary>
        /// Miner leállítása
        /// </summary>
        public void StopMiner()
        {
            if (MinerType == MinerType.GPU)
                Messenger.Default.Send<StopMinerThreadsMessage>(new StopMinerThreadsMessage());

            if (MinerType==MinerType.CPU)
                terminateProcess();

            Miners.Clear();
        }

        /// <summary>
        /// cpu szál leállítása.
        /// </summary>
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
        /// Legenerálja a felparaméterezett miner hívást.
        /// </summary>
        /// <returns>Az exe paraméterezett hívása</returns>
        private string generateMinerCall()
        {
            var mainPool = _pools.Where(x => x.IsMain).FirstOrDefault();
            var listPool = _pools.Where(x => !x.IsMain).OrderBy(y => y.FailOverPriority).ToList();


            StringBuilder sb = new StringBuilder();
            if (mainPool != null)
                sb.Append($" -o {mainPool.URL}:{mainPool.Port} -u {mainPool.Username} -p {mainPool.Password}");

            sb.Append(" -k");

            foreach (var item in listPool.Where(x => x.IsFailOver))
            {
                if(String.IsNullOrEmpty(item.Username))
                    continue;

                sb.Append($" -o {item.URL}:{item.Port} -u {item.Username} -p {item.Password}");
            }

            sb.Append((Settings.NumberOfThreads.Equals("0") || Settings.NumberOfThreads.Equals("")) ? "" : $" -t {Settings.NumberOfThreads}");
            sb.Append(" -r 3");
            sb.Append(" -R 10");
            sb.Append(!String.IsNullOrEmpty(Settings.CPUAffinity) ? $" --cpu-affinity {Settings.CPUAffinity}" : "");
            sb.Append($" --donate-level=1");
            sb.Append((Settings.MaxCPUUsage <= 1 || !Settings.NumberOfThreads.Equals("0")) ? "" : $" --max-cpu-usage={Settings.MaxCPUUsage}");
            sb.Append(" --nicehash");

            if (Settings.ApplicationMode.Equals(ApplicationMode.Normal))
                sb.Append(" --api-port=54321");

#if DEBUG
            Messenger.Default.Send<MinerOutputMessage>(new MinerOutputMessage() { OutputText = sb.ToString() });
#endif

            return sb.ToString();
        }
    }
}

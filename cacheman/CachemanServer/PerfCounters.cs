using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using CachemanCommon;

namespace CachemanServer {
    public class PerfCounters {

        private static Dictionary<string, PerformanceCounter> _counters;
        
        private  const string CATEGORY = "Cacheman";

        private  const string GETS_PER_SECOND = "Gets/second";

        private  const string GETS_TOTAL = "Gets - Total";

        private  const string GETS_MISSES ="Gets - Misses";

        private  const string OPS_TOTAL = "Operations - Total";
        private  const string OPS_PER_SECOND = "Operations/Second";

        private  const string ITEMS_CURRENT = "Items - Current";
        private  const string BYTES_STORED_CURRENT = "KB stored - Current";

        private const string CACHE_EXPIRY = "Cache Expirations - Total";

        private static CounterCreationData[] _counterData = new CounterCreationData[] 
                {
                    new CounterCreationData(GETS_PER_SECOND, GETS_PER_SECOND, PerformanceCounterType.RateOfCountsPerSecond64),
                    new CounterCreationData(GETS_TOTAL, GETS_TOTAL, PerformanceCounterType.NumberOfItems64),
                    new CounterCreationData(GETS_MISSES, GETS_MISSES, PerformanceCounterType.NumberOfItems64),
                    new CounterCreationData(OPS_TOTAL, OPS_TOTAL, PerformanceCounterType.NumberOfItems64),
                    new CounterCreationData(OPS_PER_SECOND, OPS_PER_SECOND, PerformanceCounterType.RateOfCountsPerSecond64),

                    new CounterCreationData(ITEMS_CURRENT, ITEMS_CURRENT, PerformanceCounterType.NumberOfItems64),
                    new CounterCreationData(BYTES_STORED_CURRENT, BYTES_STORED_CURRENT, PerformanceCounterType.NumberOfItems64),

                    new CounterCreationData(CACHE_EXPIRY, CACHE_EXPIRY, PerformanceCounterType.NumberOfItems64)
                };


        private static bool _shouldUsePerfCounters;
        private static string _instance;
        
        public static void InitializeCounters(string instance) {

            _instance = instance;
      

            _shouldUsePerfCounters = true;
            //
            // Do a token check to see whether perf counters have been installed by this machine - if not,
            // try installing them
            //
            
             if(!PerformanceCounterCategory.Exists(CATEGORY)) {
                    InstallPerfCounters();
             }


             _counters = new Dictionary<string, PerformanceCounter>();

             foreach (CounterCreationData perfCounterData in _counterData) {
                 _counters[perfCounterData.CounterName] = new PerformanceCounter(CATEGORY, perfCounterData.CounterName, _instance, false);
                 _counters[perfCounterData.CounterName].RawValue = 0;
             }

            
        }

        public static void RemovePerfCounters() {
            if (PerformanceCounterCategory.Exists(CATEGORY)) {
                PerformanceCounterCategory.Delete(CATEGORY);
            }
        }


        public static void LogGet(bool successful) {

            if (!_shouldUsePerfCounters) {
                return;
            }

            _counters[GETS_PER_SECOND].Increment();
            _counters[GETS_TOTAL].Increment();
            if (!successful) {
                _counters[GETS_MISSES].Increment();
            }

            _counters[OPS_PER_SECOND].Increment();
            _counters[OPS_TOTAL].Increment();

        }

        public static void LogSet() {

            if (!_shouldUsePerfCounters) {
                return;
            }
            _counters[OPS_PER_SECOND].Increment();
            _counters[OPS_TOTAL].Increment();
        }

        public static void LogDelete() {

            if (!_shouldUsePerfCounters) {
                return;
            }
            _counters[OPS_PER_SECOND].Increment();
            _counters[OPS_TOTAL].Increment();
        }

        public static void LogStoreSizeChange(long newNumItems, long newSizeBytes) {

            if (!_shouldUsePerfCounters) {
                return;
            }
            _counters[ITEMS_CURRENT].RawValue = newNumItems;
            _counters[BYTES_STORED_CURRENT].RawValue =  (long)(newSizeBytes/1024.00);
        }

        public static void LogCacheExpiry() {

            if (!_shouldUsePerfCounters) {
                return;
            }
            _counters[CACHE_EXPIRY].Increment();
        }

        public static void InstallPerfCounters() {

            try {

                
                PerformanceCounterCategory.Create(CATEGORY, "Cacheman Server", PerformanceCounterCategoryType.MultiInstance,
                    new CounterCreationDataCollection(_counterData));
            } catch (Exception ex) {
                Log.LogError("Error while creating perf counters" + ex.Message);
                _shouldUsePerfCounters = false;
            }

        }


    }
}

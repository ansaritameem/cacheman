using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Diagnostics;
using CachemanCommon;

namespace CachemanServer {
    public sealed class Store {

        private static Dictionary<string, Value> _values = new Dictionary<string, Value>();
        private static Int64 _storedBytes;

        private Store() { }
        
        public static byte[] GetValue(string key, Client currentClient) {

                //
                // Read a value from the store.  If the value has lived past its TTL, we'll expire it.
                //
                Value value;
                using(TimeLock.Acquire(_values)) {
                    bool foundValue = _values.TryGetValue(key, out value);
                    if (!foundValue || value == null) {
                        return null;
                    }

                    if (value.TTL > 0) {
                        //
                        // Honour expiry TTL on object if any
                        //
                        if (value.TimeAdded.AddSeconds(value.TTL).CompareTo(DateTime.Now) <= 0) {
                            RemoveValue(key);
                            return null;

                        }
                    }

                    //
                    //Do some book-keeping with the value 
                    //
                    value.LastAccessed = DateTime.Now;
                    Interlocked.Increment(ref value.NumAccesses);
                    return value.Data;         
                }

        }
         
        
         private static void CacheExpiry(long minSizeNeeded){
            
             //
             // First remove all expired elements
             //

             
             long _beforeExpiryBytes = _storedBytes;
             long _valueCount = _values.Count;

             Stopwatch timer = Stopwatch.StartNew();

             List<KeyValuePair<string,Value>> deleteList = new List<KeyValuePair<string,Value>>();

             //
             // Construct a 'to-delete' list of elements to be expired
             //
             Value value;
             foreach (string key in _values.Keys) {
                 value = _values[key];
                 if (value.TTL>0 && value.TimeAdded.AddSeconds(value.TTL).CompareTo(DateTime.Now) <= 0){
                     deleteList.Add(new KeyValuePair<string,Value>(key,value));
                 }
             }

             //
             // Delete them!
             //
             foreach (KeyValuePair<string, Value> keyValue in deleteList)
             {
                 _storedBytes -= keyValue.Value.Data.Length;
                 _values.Remove(keyValue.Key);
             }

             long _sizeEvictedForExpiration = _beforeExpiryBytes - _storedBytes;
             long _valuesEvictedForExpiration = _valueCount - _values.Count;


             //
             // We keep removing items per our cache expiration strategy until we hit the cache fill ratio *and*
             // we have enough space to squeeze in minSizeNeeded (the current object we're trying to put into the
             // cache)
             //

             List<KeyValuePair<string, Value>> sortedList = null;
             while (
                        ((_storedBytes *1.0) / Global.MAX_CACHE_MEMORY) > Global.CACHE_FILL_RATIO ||
                        (_storedBytes + minSizeNeeded)> Global.MAX_CACHE_MEMORY ){

                 if (sortedList == null) {
                     sortedList = GetSortedValues();
                 }
                 _values.Remove(sortedList[0].Key);
                 _storedBytes -= sortedList[0].Value.Data.Length;
                 sortedList.RemoveAt(0);
             }

             GC.WaitForPendingFinalizers();
             GC.Collect();

             timer.Stop();

             Log.LogMessage(String.Format("CacheExpiry \n Bytes beforeCacheExpiry: {0} \n Bytes evicted for expiration: {1} \n Bytes evicted from LRU list {2} \n" +
                            "Values before CacheExpiry: {3} \n Values evicted for expiration: {4} \n Values evicted from LRU list {5} \n" +
                            "Time taken: {6} milliseconds",
                            _beforeExpiryBytes, _sizeEvictedForExpiration, (_beforeExpiryBytes -_sizeEvictedForExpiration)-_storedBytes,
                            _valueCount, _valuesEvictedForExpiration, (_valueCount -_valuesEvictedForExpiration) -_values.Count,
                            timer.ElapsedMilliseconds));

             PerfCounters.LogCacheExpiry();
             PerfCounters.LogStoreSizeChange(_values.Count, _storedBytes);
         }

         private static List<KeyValuePair<string, Value>>  GetSortedValues() {

             // TODO: Implement a configurable cache expiration strategy rather than a hard-coded LRU.

             List<KeyValuePair<string, Value>> sortedList = new List<KeyValuePair<string,Value>>();
             //
             // Make a LRU list
             //
             foreach (KeyValuePair<string, Value> keyValue in _values) {
                 sortedList.Add(keyValue);
             }
             sortedList.Sort(delegate(KeyValuePair<string, Value> x, KeyValuePair<string, Value> y) {
                 return x.Value.LastAccessed.CompareTo(y.Value.LastAccessed);
             });

             return sortedList;
         }

        public static void SetValue(string key, byte[] data, long ttl) {

            Value value = new Value { Data = data, TimeAdded = DateTime.Now, TTL = ttl,  LastAccessed = DateTime.Now };

            //
            // Remove the value if it already exists
            //

            
            
            using(TimeLock.Acquire(_values)) {

                RemoveValue(key);

                if ((_storedBytes + data.Length) > (Global.MAX_CACHE_MEMORY )){
                    //
                    // We dont have enough memory - we need to CacheExpiry
                    // TODO: Should we CacheExpiry if we go beyond fill ratio?
                    //
                    CacheExpiry(data.Length);
                }

                _values[key] = value;
                _storedBytes += data.Length;

            }
            PerfCounters.LogStoreSizeChange(_values.Count, _storedBytes);
        }

        public static bool RemoveValue(string key)
        {
            Value value;

            //
            // Take a lock on the store and remove the value. Update stats when we're done
            //
            using (TimeLock.Acquire(_values)){
                bool foundValue = _values.TryGetValue(key, out value);

                if (!foundValue){
                    return false;
                }

                _values.Remove(key);
                _storedBytes -= value.Data.Length;
                PerfCounters.LogStoreSizeChange(_values.Count, _storedBytes);
                
                value = null;
                return true;

            }
            

        }
    }


    public class Value {

        public byte[] Data;

        public long TTL;

        public DateTime TimeAdded ;

        //
        // Use the below values to expire items either through LRU or LFU
        //
        public DateTime LastAccessed ;

        public long NumAccesses;

    }
}


using System;
using System.Threading;
using System.Text;

namespace CachemanServer {

    //
    // A simple wrapper around the .NET monitor which wont block forever, thus letting us recover from a deadlock.
    // Of course, we'll need to debug and figure out why - hence, on debug builds, you'll get a warning message
    // The usage model would be like this
    //  using(TimeLock.Acquire( resource) ){
    //   ... your code goes here
    //   }

    struct TimeLock:IDisposable {

        Object _resource;

        private TimeLock(Object resource) {
            this._resource = resource;
        }

        public static TimeLock Acquire(object resource) {

            TimeLock timedLock = new TimeLock(resource);
            
            //
            // Try and acquire our lock in a reasonable amount of time. If we can't, we error out
            // instead of risking deadlocking the entire process
            //
            
            if (Monitor.TryEnter(resource, Global.DEFAULT_LOCK_CONTENTION_TIME)) {

                return timedLock;
            } else {

                //
                // Couldn't acquire lock! Possible deadlock! If we have debugging enabled, break into the debugger
                //
                System.Diagnostics.Debug.Assert(false, "Possible deadlock detected while trying to acquire a lock on " + resource.ToString());
                throw new Exception("Possible deadlock detected while trying to acquire a lock on " + resource.ToString());
                
            }

        }


        public void Dispose() {
            //
            // Unlock the resource
            //
            Monitor.Exit(_resource);
            GC.SuppressFinalize(this);
        }
       
    }
}

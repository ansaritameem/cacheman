using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace CachemanServer
{
    public class Global
    {

        //
        // Default IPv4 address to bind to
        //
        public static IPAddress IPV4ADDR = Utils.FindSafestIPAddrBind();

        //
        // Default port we listen on 
        //
        public  static int  PORT = 16180;

        //
        // Default max memory for cache
        //
        public static long MAX_CACHE_MEMORY = 500*1000*1000;

        //
        // Fill ratio. When we fill up memory, CacheExpiry will bring usage down to this percentage
        //
        public static double CACHE_FILL_RATIO = 0.7;

        //
        // The maximum amount of time we'll wait to try and acquire a lock before timing out and erroring out
        //
        public static TimeSpan DEFAULT_LOCK_CONTENTION_TIME = TimeSpan.FromSeconds(10);

        //
        // Service name (will show up in list of running services)
        //
        public static string SERVICE_NAME = "Cacheman";

        //
        // Whether the user wants to run it as an interactive process
        //
        public static bool IS_INTERACTIVE = false;
    }
}

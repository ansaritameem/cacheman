using System;
using System.Collections.Generic;
using System.Text;
using CachemanCommon;
using System.Net;

namespace CachemanServer {
    public class Utils {

        public static void ParseAndExecCmdLine(string[] args) {

            foreach (string arg in args) {

                if (arg == "/?" || arg == "/h") {
                    PrintUsage();
                    Environment.Exit(0);
                } else if (arg.StartsWith("/port:")) {

                    string port = arg.Split(':')[1];
                    Global.PORT = Convert.ToInt32(port);
                } else if (arg.StartsWith("/maxcachesize:")) {

                    string maxcachesize = arg.Split(':')[1];
                    Global.MAX_CACHE_MEMORY = Convert.ToInt64(maxcachesize) * 1024 * 1024;
                } else if (arg.StartsWith("/ip:")) {

                    string ipaddress = arg.Split(':')[1];
                    if (ipaddress.ToLowerInvariant() == "any") {
                        Global.IPV4ADDR = IPAddress.Any;
                    } else {
                        Global.IPV4ADDR = System.Net.IPAddress.Parse(ipaddress);
                    }

                } else if (arg.StartsWith("/deletecounters")) {

                    PerfCounters.RemovePerfCounters();
                    Log.LogMessage("Counters deleted.");
                    Environment.Exit(0);

                } else if (arg.StartsWith("/interactive")) {

                    Global.IS_INTERACTIVE = true;
                } else {
                    Log.LogMessage("Unknown argument: " + arg);
                    Environment.Exit(-1);
                }



            }
        }


        static void PrintUsage() {

            Log.LogMessage("/? or /h  \t\t This message");
            Log.LogMessage("/port:<port>   \t\t Bind to specific port");
            Log.LogMessage("/maxcachesize:<size_in_MB>   \t\t Max cache size in MB");
            Log.LogMessage("/ip:<ipaddress> \t\t IP address to bind to. If you want to bind to all interfaces, pass the text 'any' without quotes");
            Log.LogMessage("/deletecounters \t\t Deletes all Cacheman perf counters installed");
            Log.LogMessage("/interactive \t\t Runs the process directly instead of launching the service");


        }

        public static IPAddress FindSafestIPAddrBind() {

            //
            // The goal here is to bind to a local subnet if possible. This will get overridden by any
            // command line options passed,
            // We get all listed IP addresses on the current machine and try and bind to the
            // first 10.*.*.* or 192.*.*.* address we can find. if we dont find either, fall back
            // to listening on IPADDR_ANY
            //

            
            IPAddress[] hostAddrs = Dns.GetHostAddresses(Dns.GetHostName());

            foreach (IPAddress candidateAddr in hostAddrs) {

                if (candidateAddr.ToString().StartsWith("10.") ||
                    candidateAddr.ToString().StartsWith("192.") || 
                    candidateAddr.ToString().StartsWith("157.") ) {
                    return candidateAddr;
                }
            }

            return IPAddress.Any;

        }
    }
}

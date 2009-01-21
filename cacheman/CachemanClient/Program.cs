using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using System.Diagnostics;
using CachemanAPI;
namespace CachemanConsole
{
    class Program
    {
        CachemanClient _client;

        static void Main(string[] args) {

            Console.WriteLine("Cacheman Client Console - Version " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString());
            Console.WriteLine("Bugs and suggestions - mail@sriramkrishnan.com");

            Program p = new Program();
            while (true) {
                p.REPL();
            }
           
        }

        public void REPL() {
            try {
                if (_client != null) {
                    Console.Write("\n>");
                } else {
                    Console.Write("Type 'connect <ipaddress>' to connect to a server\n>");
                }
                string cmd = Console.ReadLine();
                string[] components = (new Regex(@"\s+")).Split(cmd);

                string command = components[0].ToLowerInvariant();

                if (command != "quit" && command != "help" && components.Length < 2) {
                    Console.WriteLine("Error: Every command needs atleast a parameter");
                    return;
                }
                
                if(_client == null && !command.StartsWith("connect")) {
                    Console.WriteLine("Connect to a server first");
                    return;
                }


                if (command == "quit") {
                    Environment.Exit(0);
                    return;
                }

                if (command == "stress") {
                    if (components.Length < 3) {
                        Console.WriteLine("Enter number of stress iterations and size of each value to send");
                        return;
                    }
                    Stress(Convert.ToInt32(components[1]), Convert.ToInt32(components[2]));
                    return;
                }

                if (command == "stressgc") {
                    if (components.Length < 3) {
                        Console.WriteLine("Enter number of stress iterations and size of each value to send");
                        return;
                    }
                    StressGC(Convert.ToInt32(components[1]), Convert.ToInt32(components[2]));
                    return;
                }

                if (command == "connect") {
                    if (components.Length < 2) {
                        Console.WriteLine("Enter ip address of server");
                        return;
                    }
                    if (components.Length == 2) {
                        _client = new CachemanClient(new IPEndPoint(IPAddress.Parse(components[1]), 16180));
                        _client.Connect();
                        Console.WriteLine("Connected to " + components[1]);
                    }

                    if (components.Length >2) {

                        IPEndPoint[] endpoints = new IPEndPoint[components.Length -1];
                        string servers = "";
                        for(int i=1;i<components.Length;i++ ) {
                            endpoints[i-1] = new IPEndPoint(IPAddress.Parse(components[i]), 16180);
                            servers += endpoints[i - 1].ToString() + " ";
                        }
                        _client = new CachemanClient(endpoints);
                        Console.Write("Connected to " + servers);
                        
                    }
                    
                    return;
                }

                if (command == "help") {

                    Console.WriteLine("Commands:\n set key value\n get key\n delete key\n stress iterations size-of-value" +
                               "\n stressgc iterations size-of-value");
                    return;
                }

                string key = components[1];

                if (command == "get") {
                    Stopwatch s = Stopwatch.StartNew();
                    object ret = _client.Get(key);
                    s.Stop();
                    Console.WriteLine(ret == null ? "Key not found" : (string)ret);
                    Console.WriteLine("Executed in " + s.ElapsedMilliseconds.ToString() + " milliseconds");
                }

                if (command == "delete") {
                    Stopwatch s = Stopwatch.StartNew();
                    Console.WriteLine(_client.Delete(key) ? "Deleted" : "Not deleted!");
                    s.Stop();
                    Console.WriteLine("Executed in " + s.ElapsedMilliseconds.ToString() + " milliseconds");
                }

                if (command == "set") {

                    if (components.Length < 3) {
                        Console.WriteLine("Need 2 parameters for set command");
                        return;
                    }
                    Stopwatch s = Stopwatch.StartNew();
                    Console.WriteLine(_client.Set(key, components[2], -1) ? "Set" : "Not set!");
                    s.Stop();
                    Console.WriteLine("Executed in " + s.ElapsedMilliseconds.ToString() + " milliseconds");
                }
            } catch (Exception ex) { Console.WriteLine(ex); }

        }


        private bool CompareHashEquality(byte[] a, byte[] b) {
            if (a.Length != b.Length) {
                return false;
            }

            for (int i = 0; i < a.Length; i++) {

                if (a[i] != b[i]) {
                    return false;
                }
            }

            return true;
        }
        public void Stress(int times, int size) {

            Console.WriteLine("Starting stress. 1 iteration = 3 ops (1 set + 1 get + 1 delete)");
            DateTime start = DateTime.Now;
                 for(int i=0;i<times;i++) {

                     
                     Stopwatch s = Stopwatch.StartNew();
                    int numTimes = 1000;

                    byte[] data = new byte[size];
                    for (int k = 0; k < size; k++) {
                        data[k] = (byte)k;
                    }
                    MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();
                    byte[] hash = md5.ComputeHash(data);
                    for(int j=0;j<numTimes;j++) {
                        _client.Set("testkey" + start.Ticks, data, -1);
                        byte[] response = (byte[])_client.Get("testkey" + start.Ticks);

                        if (response == null) {
                            Console.WriteLine("Didnt get response from server");
                            continue;
                        }
                        if (!CompareHashEquality(md5.ComputeHash(response), hash)) {
                            Console.WriteLine("Error! Got incorrect data from server");
                        }
                        _client.Delete("testkey" + start.Ticks);
                    
                    }
                    s.Stop();
                    Console.WriteLine("Speed: " + ((numTimes) / (s.ElapsedMilliseconds / 1000.0)) + " iterations per second");
                 }

             
        }

        public void StressGC(int times, int size) {

            string key = "testkey" + DateTime.Now.Ticks.ToString();
            byte[] data = new byte[size];
            byte[] response;
            for(int j =0; j<data.Length; j++ ) {
                data[j] = (byte)j;
            }
            
            Stopwatch s = Stopwatch.StartNew();
            for (int i = 0; i < times; i++) {
                //
                // Set something different for the data so that we can detect if we get incorrect data
                //
                data[0] = (byte)i;
                _client.Set(key + "_" + i.ToString(), data, -1);
            }
            s.Stop();
            Console.WriteLine("Speed: " + times/(s.ElapsedMilliseconds/1000.0) + " SETs per second");
            s = Stopwatch.StartNew();

            long nullEntries=0;
            for(int i=0; i<times;i++) {
                response = (byte[])_client.Get(key + "_" + i.ToString());
                if (response == null) {
                    nullEntries++;
                    continue;
                }
                if (response[0] != (byte)i) {
                    Console.WriteLine("Incorrect data from server for " + key + "_" + i.ToString());
                    continue;
                }
            }

            s.Stop();
            Console.WriteLine("Speed: " + times / (s.ElapsedMilliseconds / 1000.0) + " GETs per second");
            Console.WriteLine("Retrieved entries: " +( times-nullEntries) + " (Loss " + (nullEntries *1.0/ times * 1.0) * 100 + " %)");

        }
    }
}
